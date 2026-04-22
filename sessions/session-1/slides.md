---
marp: true
title: "OpenClawNet — Session 1: Foundation + Local Chat"
theme: default
paginate: true
size: 16:9
---

<!-- _class: lead -->

# OpenClawNet
## Session 1 — Foundation & Local Chat

**Microsoft Reactor Series · 75–90 min · Intermediate .NET**

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

---

## Why this series exists

- Build a real **agentic** .NET app, not a toy chatbot
- 100% **open source**, runs **locally** by default
- Five sessions, four sessions delivered live, one bonus
- Every line of code is in [`elbruno/openclawnet`](https://github.com/elbruno/openclawnet)

> Today: chat with a model. Next time: give it tools. Then jobs, MCP, multi-agent.

---

## What you'll have at the end of session 1

A working **Aspire** distributed app with:

- 🧠 A pluggable model provider (`IAgentProvider`)
- 🌐 A Blazor chat UI streaming tokens via **HTTP NDJSON**
- 💾 EF Core persistence (SQLite) for conversations
- 🔌 5 providers wired: **Ollama, Azure OpenAI, Foundry Local, Microsoft Foundry, GitHub Copilot SDK**
- 📊 Aspire dashboard with logs, metrics, traces

---

## Prerequisites (recap)

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0+ | `dot.net/download` |
| Aspire workload | latest | `dotnet workload install aspire` |
| Ollama (or Foundry Local) | latest | `ollama pull llama3.2` |
| VS Code / Visual Studio | current | + GitHub Copilot |

> Hardware: **16 GB RAM minimum** for local LLMs. 32 GB recommended.

---

# 🏗️  Stage 1 — Architecture

---

## 27 projects, 4 layers

```
┌──────────────────────────────────────────────┐
│           Blazor Web (chat UI)               │
├──────────────────────────────────────────────┤
│   HTTP NDJSON + Minimal APIs (Gateway)       │
├──────────────────────────────────────────────┤
│       RuntimeAgentProvider (router)          │
├────────┬────────┬────────┬────────┬──────────┤
│ Ollama │ Azure  │Foundry │Foundry │ GitHub   │
│        │ OpenAI │        │ Local  │ Copilot  │
├────────┴────────┴────────┴────────┴──────────┤
│        Storage (EF Core, SQLite)             │
└──────────────────────────────────────────────┘
```

---

## The contract: `IAgentProvider`

```csharp
public interface IAgentProvider
{
    string Name { get; }
    IChatClient CreateChatClient(AgentProfile profile);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
```

- `IChatClient` is the **Microsoft.Extensions.AI** standard
- Every provider in the box implements one interface
- Swap providers in **one DI line** — no app code changes

---

## Vertical-slice project layout

| Slice | Project | LOC |
|-------|---------|-----|
| Abstractions | `Models.Abstractions` | 93 |
| Provider | `Models.Ollama` | 181 |
| Provider | `Models.AzureOpenAI` | 185 |
| Provider | `Models.GitHubCopilot` | 142 |
| Storage | `Storage` | 275 |
| Gateway | `Gateway` | 625 |
| UI | `Web` | 28 |
| Aspire | `AppHost` | 18 |

---

# 🔌  Stage 2 — Providers

---

## Ollama in 8 lines

```csharp
services.Configure<OllamaOptions>(o =>
{
    o.Endpoint = "http://localhost:11434";
    o.Model    = "llama3.2";
});
services.AddSingleton<IAgentProvider, OllamaAgentProvider>();

var provider = sp.GetRequiredService<IAgentProvider>();
var client   = provider.CreateChatClient(profile);
await foreach (var update in client.GetStreamingResponseAsync(messages))
    Console.Write(update.Text);
```

---

## Azure OpenAI — 3 auth modes

| Mode | When to use |
|------|-------------|
| **API Key** | Local dev, demos, CI secrets |
| **Integrated** | Azure-hosted with managed identity |
| **Federated** | GitHub Actions OIDC → Azure |

The provider picks the right credential based on `AzureOpenAIOptions.AuthMode`.

---

## GitHub Copilot SDK provider

```csharp
services.Configure<GitHubCopilotOptions>(o =>
{
    o.Model = "gpt-5-mini"; // or claude-sonnet-4.5, gpt-5, ...
});
services.AddSingleton<IAgentProvider, GitHubCopilotAgentProvider>();
```

Auth: `gh auth login` (uses host config) or `GitHubCopilot:GitHubToken` user-secret.
Requires an active **GitHub Copilot subscription** (free tier exists).

---

# 🌐  Stage 3 — Gateway + Streaming

---

## HTTP NDJSON, not SignalR

We migrated from `ChatHub` (SignalR) to **POST /api/chat/stream** returning `application/x-ndjson`.

Why?
- Simpler client code (`HttpClient` + line reader)
- Works behind any reverse proxy
- No sticky sessions
- One round-trip per turn

---

## The streaming endpoint

```csharp
group.MapPost("/api/chat/stream", async (
    ChatStreamRequest req, IAgentRuntime runtime, HttpContext ctx) =>
{
    ctx.Response.ContentType = "application/x-ndjson";
    await foreach (var ev in runtime.ExecuteStreamAsync(ctx))
    {
        var line = JsonSerializer.Serialize(ev) + "\n";
        await ctx.Response.WriteAsync(line);
        await ctx.Response.Body.FlushAsync();
    }
});
```

---

## Blazor consumer

```csharp
using var resp = await Http.PostAsJsonAsync(
    "/api/chat/stream", request,
    HttpCompletionOption.ResponseHeadersRead);

using var stream = await resp.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

while (!reader.EndOfStream)
{
    var line = await reader.ReadLineAsync();
    var ev   = JsonSerializer.Deserialize<StreamEvent>(line!);
    AppendToken(ev.Delta);     // re-renders Blazor cell
    StateHasChanged();
}
```

---

# 💾  Stage 4 — Storage

---

## EF Core entities (curated)

| Entity | Purpose |
|--------|---------|
| `ChatSession` | One conversation thread |
| `ChatMessageEntity` | Each user/assistant turn (FK → session) |
| `AgentProfile` | Named bundle: provider + model + instructions |
| `ScheduledJob` | Recurring/one-shot job (session 3) |
| `JobRun` + `JobRunEvent` | Persisted execution timeline |

---

## Schema migration without EF migrations

We use `EnsureCreatedAsync` + a hand-written `SchemaMigrator`:

```csharp
await db.Database.EnsureCreatedAsync();
await SchemaMigrator.UpgradeAsync(db);
```

Reasons:
- One SQLite file, no need for full migration history
- Adds new tables/columns idempotently
- Makes "delete the .db and start over" a valid recovery story

---

# 🚀  Stage 5 — Run it

---

## `aspire start` and you're chatting

```pwsh
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"
aspire start src\OpenClawNet.AppHost
```

Then:
- 📊 Aspire dashboard → http://localhost:15100
- 🌐 Web UI → http://localhost:5010
- 🔌 Gateway → http://localhost:5000

---

## Demo recap

1. **Demo 1** — Console: switch providers in 1 line (`code/demo1`)
2. **Demo 2** — "Bug injection" with Copilot: explain & fix
3. **Demo 3** — Drop in a custom agent persona (pirate / chef / robot)

---

# 🎯  Where we go next

- **Session 2** — Tools: file system, shell, web, image, audio, scheduler
- **Session 3** — Long-running jobs + run-event timeline
- **Session 4** — MCP servers (in-process + remote)
- **Bonus** — Multi-agent orchestration

---

<!-- _class: lead -->

# Questions?

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

<br>

`elbruno/openclawnet` · MIT licensed · contributions welcome

