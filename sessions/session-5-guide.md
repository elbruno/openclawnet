# Session 5: Channels, Browser & Events

**Duration:** 50 minutes | **Level:** Intermediate .NET

---

## Overview

OpenClawNet is production-ready. Now it's time to **take it everywhere**. In this session, we extend the platform in three directions:

1. **Bot Channels** — Connect the agent to Microsoft Teams using Bot Framework, so users interact through chat apps they already use.
2. **Browser Control** — Add Playwright-powered web automation: navigate pages, extract content, take screenshots, fill forms.
3. **Event-Driven Triggers** — Move beyond scheduled polling to event-driven webhook execution: GitHub pushes, monitoring alerts, and custom events fire agent runs instantly.

By the end of this session, your agent receives messages from Teams, browses the web like a human, and responds to real-world events — completing the journey from local chatbot to a full, connected AI platform.

---

## Before the Session

### Prerequisites

- Session 4 complete and working
- .NET 10 SDK, VS Code or Visual Studio
- Local LLM running (Ollama with `llama3.2` or Foundry Local)
- **Teams:** Microsoft 365 developer account for Teams testing (or use Bot Framework Emulator)
- **Browser tool:** Run `playwright install chromium` after `dotnet build`

### Starting Point

- The `session-4-complete` code
- Cloud providers, scheduling, health checks, and testing all working
- 24 passing tests

### Git Checkpoint

**Starting tag:** `session-5-start` (alias: `session-4-complete`)  
**Ending tag:** `session-5-complete`

---

## Stage 1: Bot Channels — Teams Integration (15 min)

### Concepts

**The channel problem.**
Until now, users can only talk to OpenClawNet through the web UI or the REST API. But most enterprise users live in Teams. Bringing the agent to Teams means zero friction — no new tab, no new account, just chat.

**Bot Framework + IBotAdapter.**
Microsoft Bot Framework provides the wire protocol for Teams, Slack, and other channels. We wrap it in our own `IBotAdapter` abstraction so the agent runtime stays channel-agnostic:

```csharp
public interface IBotAdapter
{
    string Platform { get; }  // "teams", "slack", ...
    Task HandleRequestAsync(HttpContext httpContext, CancellationToken ct = default);
}
```

**How Teams bots work.**
1. User sends a message in Teams.
2. Teams calls our `/api/messages` webhook (HTTP POST).
3. Bot Framework validates the JWT token and parses the `Activity`.
4. Our `OpenClawNetBot : TeamsActivityHandler` receives the turn.
5. We call `IAgentOrchestrator.ProcessAsync` — same runtime used by the web UI.
6. Response goes back to Teams via `turnContext.SendActivityAsync`.

**Session continuity.**
Each Teams conversation is mapped to an OpenClawNet session ID using an in-memory `ConcurrentDictionary<string, Guid>`. Message history and memory persist across messages in the same Teams thread.

### Code Walkthrough

#### IBotAdapter + TeamsAdapter

```csharp
// src/OpenClawNet.Adapters.Teams/IBotAdapter.cs
public interface IBotAdapter
{
    string Platform { get; }
    Task HandleRequestAsync(HttpContext httpContext, CancellationToken ct = default);
}

// src/OpenClawNet.Adapters.Teams/TeamsAdapter.cs
public sealed class TeamsAdapter : IBotAdapter
{
    public string Platform => "teams";

    public async Task HandleRequestAsync(HttpContext httpContext, CancellationToken ct = default)
        => await _cloudAdapter.ProcessAsync(httpContext.Request, httpContext.Response, _bot, ct);
}
```

#### OpenClawNetBot — routing to the agent

```csharp
protected override async Task OnMessageActivityAsync(
    ITurnContext<IMessageActivity> turnContext, CancellationToken ct)
{
    var sessionId = _sessionMap.GetOrAdd(turnContext.Activity.Conversation.Id, _ => Guid.NewGuid());
    var request = new AgentRequest { SessionId = sessionId, UserMessage = turnContext.Activity.Text! };
    var response = await _orchestrator.ProcessAsync(request, ct);
    await turnContext.SendActivityAsync(MessageFactory.Text(response.Content), ct);
}
```

**Key points:**
- `TeamsActivityHandler` handles Teams-specific lifecycle events (member added, etc.)
- `ConcurrentDictionary` maps Teams conversation IDs to OpenClawNet session IDs
- The agent accesses all its tools and skills — no feature degradation in Teams

#### DI Registration

```csharp
// Teams adapter — enabled via appsettings.json "Teams:Enabled": true
if (builder.Configuration.GetValue<bool>("Teams:Enabled"))
{
    builder.Services.Configure<TeamsOptions>(builder.Configuration.GetSection("Teams"));
    builder.Services.AddTeamsAdapter();
}

// Webhook endpoint
app.MapPost("/api/messages", async (HttpContext ctx, IBotAdapter adapter) =>
    await adapter.HandleRequestAsync(ctx));
```

**appsettings.json configuration:**
```json
"Teams": {
  "Enabled": true,
  "MicrosoftAppId": "<your-bot-app-id>",
  "MicrosoftAppPassword": "<your-client-secret>",
  "MicrosoftAppTenantId": ""
}
```

### Live Demo

1. **With Bot Framework Emulator** (no Azure required):
   - Run the app locally
   - Open Bot Framework Emulator → connect to `http://localhost:5000/api/messages`
   - Chat with the agent through the emulator
   - Show tools working (e.g., "list files in the current directory")
2. **With Teams** (if ngrok/dev tunnel available):
   - Set up a dev tunnel: `devtunnel host -p 5000 --allow-anonymous`
   - Register the bot in Azure and point the messaging endpoint to the tunnel URL
   - Chat directly in Teams

---

## Stage 2: Browser Control with Playwright (15 min)

### Concepts

**Why browser automation?**
Many tasks require real web interaction: reading content that requires JavaScript, filling forms, extracting structured data from dynamic pages. `web_fetch` (our HTTP-based tool) only works for static content. Playwright controls a real browser.

**BrowserTool as an ITool.**
The browser tool plugs into the existing tool registry — zero changes to the agent runtime:

```csharp
public sealed class BrowserTool : ITool
{
    public string Name => "browser";
    // Actions: navigate | extract-text | screenshot | click | fill
}
```

**Headless Chromium.**
Playwright uses Chromium in headless mode — no window, no display required. Runs perfectly in CI and Docker.

**Setup:**
```bash
dotnet build
playwright install chromium   # one-time setup
```

### Code Walkthrough

#### BrowserTool — five actions

```csharp
public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
{
    var action = input.GetStringArgument("action");

    return action?.ToLowerInvariant() switch
    {
        "navigate"     => await NavigateAsync(input, sw, ct),
        "extract-text" => await ExtractTextAsync(input, sw, ct),
        "screenshot"   => await ScreenshotAsync(input, sw, ct),
        "click"        => await ClickAsync(input, sw, ct),
        "fill"         => await FillAsync(input, sw, ct),
        _ => ToolResult.Fail(Name, "Unknown action", sw.Elapsed)
    };
}
```

**extract-text** — the agent's research superpower:

```csharp
private async Task<ToolResult> ExtractTextAsync(ToolInput input, Stopwatch sw, CancellationToken ct)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    var page = await browser.NewPageAsync();

    await page.GotoAsync(url, new() { Timeout = 30_000 });

    // Optional: scope to a CSS selector (e.g., "article", "#main-content")
    var text = selector is not null
        ? await page.Locator(selector).InnerTextAsync()
        : await page.InnerTextAsync("body");

    return ToolResult.Ok(Name, text[..Math.Min(text.Length, 5_000)], sw.Elapsed);
}
```

**screenshot** — visual debugging and reporting:

```csharp
private async Task<ToolResult> ScreenshotAsync(...)
{
    var path = Path.Combine(Path.GetTempPath(), $"browser-{Guid.NewGuid():N}.png");
    await page.ScreenshotAsync(new() { Path = path, FullPage = false });
    return ToolResult.Ok(Name, $"Screenshot saved: {path}", sw.Elapsed);
}
```

#### DI Registration

```csharp
// No HttpClient needed — Playwright manages its own browser process
builder.Services.AddTool<BrowserTool>();
```

### Live Demo

1. Ask the agent: *"Extract the main content from https://devblogs.microsoft.com/dotnet/"*
2. Show the `browser` tool being called in the Aspire Dashboard traces
3. Show extracted text returned to the agent, then summarized
4. Take a screenshot: *"Take a screenshot of https://learn.microsoft.com/dotnet/"*
5. Show the PNG path returned by the tool

**Copilot Moment:**
> *"Add a `wait-for-selector` action to BrowserTool that waits up to 5 seconds for a CSS selector to appear before extracting text. Use Playwright's `WaitForSelectorAsync`."*

---

## Stage 3: Event-Driven Webhooks (10 min)

### Concepts

**Scheduling vs. events.**
Session 4 introduced cron-based scheduling (poll every N seconds/minutes). Events are more powerful: the agent reacts *instantly* when something happens — a GitHub push, an alert firing, a payment received.

**The webhook pattern.**
Any system that supports webhooks (GitHub, Azure Monitor, Stripe, PagerDuty, etc.) can call `POST /api/webhooks/{eventType}` on the Gateway. The agent runs immediately with the payload as context.

```
External System → POST /api/webhooks/github-push
                  { "message": "PR #42 merged", "data": { "branch": "main", ... } }
                        ↓
              OpenClawNet Agent runs with full tool access
                        ↓
              Response returned (+ saved in DB as a new session)
```

**Each webhook creates a session.**
Every webhook trigger creates a new `ChatSession` with `Provider = "webhook"` for a clean audit trail. The agent has full tool access — it could read files, browse the web, call APIs, or schedule follow-up jobs.

### Code Walkthrough

#### WebhookEndpoints

```csharp
group.MapPost("/{eventType}", async (
    string eventType,
    WebhookPayload payload,
    IAgentOrchestrator orchestrator,
    IDbContextFactory<OpenClawDbContext> dbFactory) =>
{
    // Create audit session
    var session = new ChatSession { Title = $"Webhook: {eventType}", Provider = "webhook" };
    db.Sessions.Add(session); await db.SaveChangesAsync();

    // Build context-rich message for the agent
    var userMessage = $"A '{eventType}' webhook event was received.\n\nPayload:\n{contextJson}";

    var response = await orchestrator.ProcessAsync(new AgentRequest
    {
        SessionId = session.Id,
        UserMessage = userMessage
    });

    return Results.Ok(new { session.Id, response.Content, response.ToolCallCount });
});
```

### Live Demo

1. Trigger a webhook manually with curl or Scalar/Swagger:
   ```bash
   curl -X POST http://localhost:5000/api/webhooks/github-push \
     -H "Content-Type: application/json" \
     -d '{"message": "PR #42 merged to main", "data": {"branch": "main", "author": "alice"}}'
   ```
2. Show the agent processing the event in the Aspire Dashboard
3. Check `GET /api/webhooks` — list recent webhook sessions
4. Show the full conversation in the web UI (session was created)

---

## Closing (10 min)

### Full Series Recap

| Session | Topic | What We Built |
|---------|-------|--------------|
| **1** | Scaffolding + Local Chat | Aspire, local LLM, HTTP SSE streaming, chat UI |
| **2** | Tools + Agent Workflows | Tool loop, registry, executor, approval policies |
| **3** | Skills + Memory | Markdown skills, summarization, semantic search |
| **4** | Automation + Cloud | Cloud providers, scheduling, health checks, 24 tests |
| **5** | Channels + Browser + Events | Teams adapter, Playwright browser tool, webhooks |

### Architecture — Complete Platform

```
┌──────────────────────────────────────────────────────────────────┐
│                       OpenClawNet Platform                       │
├────────────┬────────────┬──────────────┬─────────────────────────┤
│  Web UI    │  Teams Bot │  REST API    │    Webhooks             │
│ (Blazor)   │ /api/msgs  │ /api/chat    │  /api/webhooks/{event}  │
├────────────┴────────────┴──────────────┴─────────────────────────┤
│                      Agent Orchestrator                          │
│         ┌────────────────────────────────────────┐               │
│         │          Prompt Composer               │               │
│         │    (System + Skills + Memory)          │               │
│         └────────────────────────────────────────┘               │
├────────┬──────────┬─────────┬─────────┬──────────────────────────┤
│ Tools  │  Skills  │ Memory  │Scheduler│    New in Session 5      │
│ FS/Web │  Loader  │  Store  │ BackSvc │ Browser | Teams Adapter  │
├────────┴──────────┴─────────┴─────────┴──────────────────────────┤
│                     Model Abstraction                            │
│      ┌─────────┬──────────────┬──────────┬──────────┐           │
│      │ Ollama  │ FoundryLocal │ AzureAI  │ Foundry  │           │
│      └─────────┴──────────────┴──────────┴──────────┘           │
├────────────────────────────────────────────────────────────────────┤
│                 Storage (EF Core + SQLite)                        │
└────────────────────────────────────────────────────────────────────┘
```

### Where to Go from Here

- **More channels:** Slack (`SlackNet`), Discord (`Discord.Net`), WhatsApp (Twilio)
- **Advanced browser:** Multi-step form automation, authenticated browsing, PDF generation
- **Webhook security:** HMAC signature verification for GitHub/Stripe webhooks
- **Durable workflows:** Hangfire or Azure Durable Functions for complex multi-step automation
- **Multi-agent:** Agent-to-agent communication, orchestrator patterns

### Thank You + Q&A

- Repository: `github.com/elbruno/openclawnet`
- Series: Microsoft Reactor — OpenClawNet
- Built with: .NET 10, Aspire, GitHub Copilot, Local LLMs

---

## After the Session

### What Now Works

- ✅ Microsoft Teams integration via Bot Framework + `IBotAdapter`
- ✅ Playwright browser tool with 5 actions (navigate, extract-text, screenshot, click, fill)
- ✅ Event-driven webhooks at `POST /api/webhooks/{eventType}`
- ✅ Webhook session audit trail in the database
- ✅ `Teams:Enabled` feature flag — opt-in configuration
- ✅ Full platform with 5 input channels and 6 tool types

### Key Concepts Covered

1. `IBotAdapter` — channel abstraction over Bot Framework CloudAdapter
2. `TeamsActivityHandler` — Teams-specific message routing
3. Session continuity via conversation ID mapping
4. Microsoft Playwright — headless Chromium from .NET
5. `ITool` extensibility — dropping in a new capability with zero agent changes
6. Event-driven architecture — webhooks vs. cron scheduling
7. Audit trail pattern — `Provider = "webhook"` sessions

### Git Checkpoint

**Tag:** `session-5-complete`

**Files added:**
- `src/OpenClawNet.Tools.Browser/` — Playwright BrowserTool
- `src/OpenClawNet.Adapters.Teams/` — IBotAdapter, TeamsAdapter, OpenClawNetBot
- `src/OpenClawNet.Gateway/Endpoints/WebhookEndpoints.cs` — event-driven triggers
- `src/OpenClawNet.Gateway/Program.cs` — updated registrations
