---
marp: true
title: "OpenClawNet — Session 2: Tools & Agent Workflows"
description: "From chatbot to agent: in-process ITool + MCP, one approval gate, one runtime"
theme: openclaw
paginate: true
size: 16:9
footer: "OpenClawNet · Session 2 · Tools & Agent Workflows"
---

<!-- _class: lead -->

# OpenClawNet
## Session 2 — Tools & Agent Workflows

**Microsoft Reactor Series · ~75 min · Intermediate .NET**

> *Tools turn a chatbot into a coworker.*

<br>

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

<!--
SPEAKER NOTES — title slide.
Hi everyone, welcome back. Session 1 we got a real Blazor + Aspire app talking to local and cloud LLMs. Today we close the gap between "chatbot" and "agent": we give the model the ability to actually DO things — read files, run shells, call APIs, even launch other agents. We'll do it twice: with our own ITool framework AND with MCP servers. Same agent. Same approval gate. One mental model.
Promise: by the end you'll have written a custom tool, swapped an approval policy, attached an MCP server, and watched a real agent loop pick the right tool for the job.
-->

---

## Where Session 1 left off

- A real **Aspire** app, 27 projects, 4 layers
- `IAgentProvider` abstracts 5 model providers
- HTTP **NDJSON streaming** to a Blazor UI
- EF Core / SQLite persistence + agent profiles
- `aspire start` → chat with `llama3.2` in 30 seconds

> Today we add the missing ingredient: **action**.

<!--
SPEAKER NOTES — recap.
Quick recap of Session 1 in 30 seconds. We have a working Aspire stack, five providers behind one IAgentProvider interface, a Blazor UI streaming over NDJSON, and SQLite-backed conversations. What we DON'T have yet is action — a chatbot that just talks isn't an agent. Today we fix that.
If anyone joined late and missed Session 1, the recording and code are at github.com/elbruno/openclawnet.
-->

---

## Chatbot vs. agent

|              | Chatbot                       | **Agent**                                    |
|--------------|-------------------------------|----------------------------------------------|
| Inputs       | text                          | text **+ tool results**                      |
| Outputs      | text                          | text **+ tool calls**                        |
| Loop         | one turn                      | **multi-turn until done**                    |
| Side effects | none                          | **filesystem, network, shells, schedules**   |
| Risk profile | low                           | **needs an approval gate**                   |

<!--
SPEAKER NOTES — chatbot vs agent.
This is the only theory slide of the day, so let's make it count. A chatbot is a request/response thing — you send text, you get text. An agent does that PLUS it can decide "I need more information" or "I need to make a change", emit a tool call, get a result back, and use that result to decide what to do next. That's the loop. The big architectural consequence is that side effects enter the picture — and the moment side effects exist, you need a story for "who approved this?" That's why approval policies are a first-class citizen in OpenClawNet.
-->

---

## Two tool surfaces, one agent

<div class="cols">
<div>

### `ITool` (in-process)
- 100% C#, in your process
- Microsecond overhead
- Total schema control
- Ideal for: hot path, secrets, custom logic

</div>
<div>

### MCP (Model Context Protocol)
- Open protocol, language-agnostic
- Stdio or in-process transport
- Reuse any community server
- Ideal for: 3rd-party integrations, polyglot teams

</div>
</div>

> Same agent loop. Same `IToolApprovalPolicy`. Same NDJSON events.

<!--
SPEAKER NOTES — two surfaces.
This is the slide that sets up the whole session. OpenClawNet ships TWO ways to give an agent tools, and people often think they have to pick one religion. They don't. Use ITool when speed and control matter — your own code, your own process, no marshalling, no protocol overhead. Use MCP when you want to plug in something the community already wrote — GitHub MCP server, Notion, a vendor's database connector. The crucial design choice we made is that the agent itself, the runtime, and the approval gate don't care which surface a tool came from. From the model's point of view, they're all just tools in the manifest.
-->

---

## Today's build, by the numbers

- **`OpenClawNet.Tools.Abstractions`** — `ITool`, `ToolMetadata`, `ToolResult`
- **`OpenClawNet.Tools.Core`** — registry, executor, approval policies
- **5** in-process tools — FileSystem, Shell, Web, Image, Scheduler
- **5** bundled MCP servers — FileSystem, Web, Browser, Shell, Abstractions
- **1** unified `IToolApprovalPolicy` covering both
- **5** runnable demos at `docs/sessions/session-2/code/`

<!--
SPEAKER NOTES — by the numbers.
Quick lay of the land before we dive into code. There are two project pairs to know: Tools.Abstractions (interfaces) and Tools.Core (registry, executor, policies). On top of those, five built-in in-process tools and five bundled MCP servers. The whole tool surface is governed by one approval policy interface, and we have five console demos in the repo you can run on the train home.
-->

---

# 🔧  Stage 1 — In-process Tools (`ITool`)

<!--
SPEAKER NOTES — Stage 1 divider.
First half-hour: in-process tools. We'll look at the contract, the registry/executor split, the approval gate, and a custom tool from scratch.
-->

---

## `ITool` — the contract

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolMetadata Metadata { get; }

    Task<ToolResult> ExecuteAsync(
        ToolInput input,
        CancellationToken ct = default);
}
```

- One method, one return type — easy to mock, easy to test
- `ToolInput` carries arguments + caller identity
- `ToolResult` is `Ok(...)` or `Fail(...)` (rich error info)

<!--
SPEAKER NOTES — ITool contract.
Five members total. Name is the identifier the model uses when emitting a tool call. Description is what the model READS to decide whether to use this tool — write it like a docstring, not like a code comment. Metadata is the structured side: parameter schema, approval requirement, tags, category. ExecuteAsync gets the parsed arguments and a cancellation token. ToolResult is a discriminated record — Ok or Fail — so callers never have to choose between "throw an exception" and "return null". Both branches carry duration so observability is free.
-->

---

## `ToolMetadata` — what the LLM sees

```csharp
public sealed record ToolMetadata(
    JsonDocument ParameterSchema, // JSON Schema draft 2020-12
    bool RequiresApproval,        // does this need a human?
    string Category,              // "fs" | "shell" | "web" | ...
    string[] Tags);
```

- **Parameter schema** = the contract with the model
- **`RequiresApproval`** = the contract with the human
- **Category + Tags** drive the UI's "tool picker" filters

<!--
SPEAKER NOTES — ToolMetadata.
The metadata record is small but every field carries weight. Parameter schema is JSON Schema 2020-12 — that's what gets converted into a function definition when we hand the manifest to the model. Get the schema right and the model will call the tool with the right arguments first time; get it wrong and you'll waste tokens on retry loops. RequiresApproval is the safest guard rail in the whole system: when true, the executor will pause and emit a ToolApprovalRequest event before doing anything destructive. Category and Tags don't affect runtime behaviour — they're for the Tools page in the UI, so users can filter by surface or by capability.
-->

---

## Registry vs. Executor

<div class="cols">
<div>

### `IToolRegistry`
- **Discovery** (global, singleton)
- "What tools exist?"
- "Hand me one by name"
- Merges in-process **and** MCP tools

</div>
<div>

### `IToolExecutor`
- **Execution** (scoped, per request)
- Approval gate
- Stopwatch + try/catch
- Emits NDJSON events

</div>
</div>

> Separation = you can swap one without breaking the other.

<!--
SPEAKER NOTES — registry vs executor.
This split looks bureaucratic but it's load-bearing. The registry is global, scoped as a singleton — it's just "the catalogue of tools currently loaded". The executor is scoped per request, because the approval policy may need to know who's calling, in which conversation, with what budget left. By keeping discovery and execution apart we can do things like: load an MCP server at runtime and have it appear in the registry without restarting the executor; or wrap the executor with telemetry without touching the registry. And later in the session you'll see this pays off when MCP tools just slot into the same registry.
-->

---

## The approval gate, in 6 lines

```csharp
if (await _policy.RequiresApprovalAsync(tool, input, ct)
 && !await _policy.IsApprovedAsync(tool, input, ct))
{
    return ToolResult.Fail(
        tool.Name,
        "approval required",
        TimeSpan.Zero);
}
```

- Default policy: **`AlwaysApprovePolicy`** (great for demos, terrible for prod)
- Production: replace with one that asks the user (UI prompt, Teams card, ...)
- Same gate covers `ITool` **and** MCP tools

<!--
SPEAKER NOTES — approval gate.
Six lines. That's the entire approval gate. The interface has two methods: RequiresApprovalAsync (does this combination of tool + arguments need a human?) and IsApprovedAsync (has a human already said yes?). The default implementation says "yes, always approved" which is fine for a demo and a disaster for production. The pattern we recommend in production is: RequiresApprovalAsync looks at the tool metadata AND the arguments — for example "any shell command containing rm -rf needs approval, anything else is fine" — and IsApprovedAsync checks an in-memory map populated by a UI callback or a Teams adaptive card. The crucial property of this design: when we add MCP support later, the same six lines run for MCP tools too. One gate, two surfaces.
-->

---

## Wiring it up — one extension method

```csharp
services.AddToolFramework();   // registry + executor + AlwaysApprovePolicy
services.AddTool<MyAwesomeTool>();
services.AddTool<AnotherTool>();
```

To swap the approval policy:

```csharp
services.RemoveAll<IToolApprovalPolicy>();
services.AddSingleton<IToolApprovalPolicy, MyHumanInTheLoopPolicy>();
```

<!--
SPEAKER NOTES — wiring.
Two-line wiring is the goal. AddToolFramework registers the registry, the executor, and a default approval policy. AddTool<T> adds your tool as a singleton ITool — the registry picks it up automatically because it's IEnumerable&lt;ITool&gt;-injected. To swap the policy in production you remove the default and add your own. We'll see this exact pattern in Demo 2.
-->

---

## 🤖 Copilot moment — scaffold a tool

> "Generate an `ITool` implementation called `WeatherTool` that takes a `city` string parameter and returns a fake forecast. Include `ToolMetadata` with a JSON Schema. Use the same patterns as `FileSystemTool` in this repo."

- Copilot reads sibling tools as examples
- Generates schema, metadata, and a `Fail` path
- Saves you ~20 minutes per tool

<!--
SPEAKER NOTES — Copilot moment.
Quick demo if time permits, otherwise just describe. Copilot in the IDE is incredible for this pattern because the tools are SO formulaic — same shape, different body. Open the repo, type that prompt, watch it generate a fully-shaped ITool with the JSON Schema correctly populated. The trick is the "use the same patterns as FileSystemTool" — it tells Copilot which file to look at as a reference. Without that hint you get a generic answer; with it you get something that compiles in your codebase.
-->

---

# 🌐  Stage 2 — MCP Tools

<!--
SPEAKER NOTES — Stage 2 divider.
Now the new bit. MCP — Model Context Protocol — is the open standard for letting agents talk to external tool servers. We baked it into OpenClawNet so the agent can use both surfaces without knowing the difference.
-->

---

## What is MCP, in 30 seconds

- **Model Context Protocol** — open spec, JSON-RPC over stdio or HTTP
- A **server** exposes: tools, prompts, resources
- A **host** (us) consumes them and surfaces to the model
- Ecosystem: GitHub, Filesystem, Notion, Slack, Postgres, Browser, …

> Anything you can package as an MCP server, OpenClawNet can use.

<!--
SPEAKER NOTES — MCP intro.
For folks who haven't seen MCP yet: it's an open protocol from Anthropic, now adopted across the industry, that standardises how agents talk to external tool providers. A server speaks JSON-RPC over stdio or HTTP, exposes a list of tools (and optionally prompts and resources), and the agent's host wires those tools into the model's manifest. The huge win is a fast-growing ecosystem: there are MCP servers for GitHub, the filesystem, Notion, Slack, Postgres, Playwright browsers, and dozens more. The moment your agent host speaks MCP, you get all of them for free.
-->

---

## Why OpenClawNet has both

```
┌─────────────────────────────────────────────────┐
│           DefaultAgentRuntime                   │
│   (the agent loop, provider-agnostic)           │
├─────────────────────────────────────────────────┤
│        IToolRegistry  +  IToolExecutor          │
│      (one approval gate, one event stream)      │
├─────────────────────┬───────────────────────────┤
│   in-process ITool  │     MCP tool wrapper      │
│  (FileSystem, Web,  │   (StdioMcpHost or        │
│   Shell, Image, ...) │    InProcessMcpHost)      │
└─────────────────────┴───────────────────────────┘
```

- The model sees **one merged tool manifest**
- The runtime emits the **same NDJSON events** for both
- Telemetry tags `tool.source` = `"in-process"` or `"mcp:<server>"`

<!--
SPEAKER NOTES — unified architecture.
This is the diagram that explains the whole design. Look at the bottom row: in-process ITools on the left, MCP tools on the right. They both feed UP into one IToolRegistry and one IToolExecutor — one merged manifest, one approval gate, one event stream. The runtime above doesn't know or care which surface a tool came from. The only place the distinction shows up is in OpenTelemetry, where we tag every span with tool.source so you can filter your traces by surface.
-->

---

## The 5 bundled MCP servers

| Server | Type | What it does |
|--------|------|--------------|
| `OpenClawNet.Mcp.FileSystem` | in-process | sandboxed file ops |
| `OpenClawNet.Mcp.Web` | in-process | HTTP fetch + search |
| `OpenClawNet.Mcp.Shell` | in-process | guarded shell exec |
| `OpenClawNet.Mcp.Browser` | in-process | Playwright-driven browser |
| `OpenClawNet.Mcp.Abstractions` | (lib) | shared contracts |

> Bundled = ship-by-default, registered via `IBundledMcpServerRegistration`.

<!--
SPEAKER NOTES — bundled servers.
We ship five MCP servers in the box. FileSystem, Web, and Shell mirror the in-process tools — same capability, exposed over the MCP protocol so external agents can use them too. Browser is Playwright-driven and is the only one that needs a Chromium download on first run. Abstractions is just the shared contracts library. All five are "bundled" — they auto-register on startup via IBundledMcpServerRegistration, no JSON config needed. You can also add external MCP servers via the /mcp-settings UI page, which we'll see in a minute.
-->

---

## Two transports out of the box

<div class="cols">
<div>

### `StdioMcpHost`
- Subprocess, JSON-RPC over stdin/stdout
- Perfect for `npx` / `uvx` servers
- Lifecycle: start, health-check, restart
- Crash isolation

</div>
<div>

### `InProcessMcpHost`
- In-memory pipe, zero serialization cost
- For our 5 bundled servers
- Same MCP protocol on the wire
- Easier to debug

</div>
</div>

<!--
SPEAKER NOTES — transports.
Two transport choices. StdioMcpHost is what you use for community servers — most of them are distributed as npx or uvx packages, and stdio is the lowest-common-denominator way to talk to them. We spawn a subprocess, pipe JSON-RPC over its stdin/stdout, and add lifecycle management — health checks, automatic restart on crash, structured logs out of stderr. InProcessMcpHost is an optimisation for our bundled servers: same protocol on the wire, but the wire is an in-memory pipe instead of an OS pipe. Zero serialisation overhead, easier to set breakpoints. The host abstraction means the rest of the system doesn't care which one a server uses.
-->

---

## Lifecycle: `McpServerLifecycleService`

```
start  ──►  initialize handshake  ──►  list tools
   │                │                       │
   │                ▼                       ▼
   │        capabilities cached     register in IToolRegistry
   │
   └──►  health pings  ──►  restart on failure
```

- Background `IHostedService` (Aspire-friendly)
- Per-server status: `Stopped | Starting | Running | Failed`
- Surfaced on `/mcp-settings` page, with one-click **Restart**

<!--
SPEAKER NOTES — lifecycle.
McpServerLifecycleService is a hosted service that runs in the background. On app startup it iterates registered MCP server definitions, spawns each one through the chosen host, performs the MCP initialize handshake, lists their tools, caches capabilities, and registers each tool into the unified IToolRegistry. After that it ping-polls each server for health and restarts crashed ones with exponential backoff. The status of every server is exposed on the /mcp-settings UI page with a one-click restart button — really useful when you're iterating on a local server during development.
-->

---

## `McpToolOverride` — your last line of defence

```csharp
public sealed record McpToolOverride(
    string ServerId,
    string ToolName,
    string? RenamedTo = null,
    string? RewrittenDescription = null,
    bool ForceApproval = false,
    bool Disabled = false);
```

- Rename a tool (avoid collisions across servers)
- Rewrite the description (better grounding for your model)
- **Force approval** even if the server says it's safe
- Disable a tool entirely without touching the server

<!--
SPEAKER NOTES — overrides.
Critical slide for production. When you depend on third-party MCP servers you don't control their tool definitions or their idea of what's safe. McpToolOverride is the policy patch you apply locally. You can rename tools to avoid collisions when two servers expose a "search" tool. You can rewrite the description to teach your model when to use the tool — "use this only for documents in /var/data, NOT for system files". You can flip ForceApproval to true even if the server says no approval needed — for example, a database MCP server might mark "select" as safe but you want a human-in-the-loop for any production query. And you can fully disable a tool you don't trust without uninstalling the server. Defence in depth.
-->

---

## Secrets: `DpapiSecretStore`

- Per-server credentials never in plain text
- DPAPI on Windows, `libsecret` / Keychain shims on Linux/macOS
- Stored alongside the `McpServerDefinition` in SQLite (encrypted blob)
- Decrypted **only** when the lifecycle service spawns the subprocess

```csharp
var token = await _secrets.GetAsync(serverId, "GITHUB_TOKEN", ct);
process.StartInfo.EnvironmentVariables["GITHUB_TOKEN"] = token;
```

<!--
SPEAKER NOTES — secrets.
Many MCP servers need credentials — a GitHub PAT, an Azure connection string, a Notion API key. We store them via DpapiSecretStore: DPAPI on Windows because it's the OS-native API for "encrypt this so only this user on this machine can decrypt", with shims to libsecret and Keychain on the other platforms. The encrypted blob lives in SQLite next to the server definition, but it's only ever decrypted at the moment we spawn the subprocess and inject the value into its environment variables. Nothing logged, nothing rendered to the UI, nothing in process memory longer than necessary.
-->

---

## Discoverability — `/mcp-settings`

Three pages, in increasing order of helpfulness:

1. **Index** — list / start / stop / restart your servers
2. **Edit** — definition, transport, env vars, secrets, overrides
3. **Suggestions** — curated catalogue, **one-click install**

> Backed by `McpSuggestionsProvider` + `McpRegistryClient` (queries the public MCP registry).

<!--
SPEAKER NOTES — UI surface.
Three UI pages handle the MCP surface. The Index page shows you every server, its current status, and gives you start/stop/restart controls. The Edit page is where you configure a single server: its definition (command + args), the transport, environment variables, secrets, and any tool overrides. The third page is the magic one — Suggestions, backed by McpSuggestionsProvider and McpRegistryClient, which queries the public MCP registry and gives you a curated, one-click installable catalogue. New community server published yesterday? It shows up here today.
-->

---

## 🤖 Copilot moment — convert install command to definition

> "Here's the install command from the GitHub MCP server's README:
> `npx -y @modelcontextprotocol/server-github`.
> Convert this into a `McpServerDefinition` JSON for OpenClawNet, including required env vars."

- Copilot reads `McpServerDefinition.cs` for the schema
- Generates JSON ready to paste into `/mcp-settings/edit`
- Highlights any missing secrets

<!--
SPEAKER NOTES — Copilot moment 2.
The friction point with MCP is always: README says "run this command", you have to translate that into your host's config format. Copilot does this conversion really well if you give it the schema. Open McpServerDefinition.cs in your editor, paste the install command from any MCP README, ask Copilot to convert. It produces JSON ready to paste into the Edit page. Bonus: it'll usually flag the env vars that need secrets, so you don't paste a token into a config file by accident.
-->

---

# 🛡️  Stage 3 — Security Across Both Surfaces

<!--
SPEAKER NOTES — Stage 3 divider.
Both tool surfaces share the same security primitives. This 10-minute stage covers the three attacks you HAVE to defend against, and how the same patterns apply to ITool and MCP.
-->

---

## 3 attacks every tool framework must block

1. **Path traversal** — `..\..\windows\system32\config\sam`
2. **Command injection** — `ls; rm -rf /`
3. **SSRF** — `http://169.254.169.254/latest/meta-data/`

> The model **WILL** generate these if a user asks. Trust no input.

<!--
SPEAKER NOTES — attacks.
There are three attack categories you have to plan for from day one. Path traversal — the model will eventually try to read or write outside the sandbox if a user crafts the right prompt. Command injection — same story for shell commands, separated by semicolons or backticks. And SSRF, server-side request forgery — fetching cloud metadata endpoints, internal services, localhost ports. The model isn't malicious; it's helpful, which means it'll happily try ANY URL or path the user asks about. Your tools have to assume every input is hostile.
-->

---

## FileSystem — kill traversal at resolution time

```csharp
var fullPath = Path.GetFullPath(Path.Combine(_root, requestedPath));
if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
    return ToolResult.Fail(Name, "path escape", elapsed);
```

- Resolve **before** opening the file
- Compare against the canonical sandbox root
- Same pattern in `FileSystemTool` **and** `FileSystemMcpTools`

<!--
SPEAKER NOTES — filesystem.
The defence pattern for path traversal is canonicalise-then-compare. Path.GetFullPath resolves all the .., the symbolic links, the redundant slashes — and gives you the absolute path the OS would actually open. THEN you compare against the canonical sandbox root. If the resolved path doesn't start with your root, deny. The reason this works where simpler string checks fail: a clever attacker can write \\?\C:\Windows or use Unicode normalisation tricks, and Path.GetFullPath will collapse all of that to the real path before you make the comparison. Both our in-process FileSystemTool and the MCP FileSystem server use this exact pattern.
-->

---

## Shell — blocklist + timeout + approval

```csharp
private static readonly string[] Blocked = ["rm", "del", "format", "shutdown", "reg"];
if (Blocked.Any(b => command.StartsWith(b, ...)))
    return ToolResult.Fail(...);

using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(30));
```

- Blocklist is a **floor**, not a ceiling
- Timeout protects against runaway processes
- `RequiresApproval = true` means the user **always** sees the command first

<!--
SPEAKER NOTES — shell.
The shell tool gets THREE layers because it's the highest-risk surface. First, a blocklist — rm, del, format, shutdown, reg, taskkill — these are the verbs that can't be undone. The blocklist is a FLOOR not a ceiling: it covers the obvious dangers, you should still apply more restrictive policies in production. Second, a hard timeout: thirty seconds, no exceptions, the cancellation token kills the process. Third, RequiresApproval is hardcoded to true — the user sees the exact command that's about to run and has to click approve. If your prompt says "run npm install" and the model decides to run something else, you'll see it before it executes.
-->

---

## Web — block SSRF before the socket opens

```csharp
var uri = new Uri(url);
if (IPAddress.TryParse(uri.Host, out var ip) && IsPrivate(ip))
    return ToolResult.Fail(Name, "private IP blocked", elapsed);
if (uri.Host is "localhost" or "metadata.google.internal" or "169.254.169.254")
    return ToolResult.Fail(Name, "blocked host", elapsed);
```

- Resolve and check **before** the HTTP client opens a socket
- Defends against EC2 / GCE / Azure IMDS data theft

<!--
SPEAKER NOTES — web.
SSRF defence runs BEFORE the socket opens. We parse the URI, and if the host is a private-range IP — 10.x, 172.16-31.x, 192.168.x, 127.x, link-local — we deny. Same for the well-known cloud metadata hosts: localhost, metadata.google.internal, 169.254.169.254 — those would leak instance credentials in seconds if reachable. The crucial word is "before". If you defer this check until after the connection completes, you've already leaked a packet to an internal service that may log it. We do the check at URL parse time.
-->

---

## NDJSON event types — same for both surfaces

```jsonl
{"type":"ToolApprovalRequest","tool":"shell","args":{"cmd":"npm install"}}
{"type":"ToolCallStart","tool":"shell","callId":"abc123"}
{"type":"ToolCallComplete","tool":"shell","callId":"abc123","durationMs":4210}
{"type":"ContentDelta","text":"Installed 247 packages..."}
```

- The UI doesn't care if the tool was `ITool` or MCP
- The user gets one consistent timeline
- OpenTelemetry spans tag `tool.source` for filtering

<!--
SPEAKER NOTES — events.
Whatever the surface, the user sees the same events. ToolApprovalRequest is what the UI listens for to render an approval dialog. ToolCallStart marks the moment execution begins so the UI can show a spinner. ToolCallComplete carries duration and the result so we can log timing and render output. ContentDelta is normal streamed text. Every one of these events is identical for in-process and MCP tools — the user can't tell the difference, which is exactly what we want. In your traces, OpenTelemetry tags every tool span with tool.source so you can filter by surface when debugging.
-->

---

# 🔄  Stage 4 — The Agent Loop

<!--
SPEAKER NOTES — Stage 4 divider.
Now we put it all together. The agent loop is the heartbeat that turns a single LLM call into multi-turn, tool-using behaviour.
-->

---

## What an agent loop actually does

```
1. Compose prompt + chat history + tool manifest
2. Call model → get assistant message (text and/or tool calls)
3. If no tool calls → done, return answer
4. For each tool call:
       a. Approval gate
       b. Execute via IToolExecutor
       c. Append tool result to messages
5. Goto 2 (cap at N iterations)
```

> Every modern agent framework is a variant of this loop.

<!--
SPEAKER NOTES — the loop.
This is the loop, every agent framework you've ever heard of is a variant of it. Compose a prompt — system instructions plus chat history plus the tool manifest. Call the model, get back a message that may contain text, tool calls, or both. If there are no tool calls, you're done — return the text. Otherwise, for each tool call, run the approval gate, execute through the executor, append the result back into the messages list, and call the model again. The cap on iterations exists so a misbehaving model can't infinite-loop you into a token bill from hell. Five lines of pseudocode, but the entire field of "agentic AI" is just engineering around this skeleton.
-->

---

## `DefaultAgentRuntime` — the engine

```csharp
public async IAsyncEnumerable<AgentStreamEvent> ExecuteStreamAsync(
    AgentContext ctx,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    for (var i = 0; i < _maxIterations; i++)
    {
        var response = await _client.GetResponseAsync(messages, opts, ct);

        foreach (var ev in StreamContent(response)) yield return ev;
        if (response.ToolCalls.Count == 0) break;

        foreach (var call in response.ToolCalls)
        {
            yield return new ToolCallStartEvent(call);
            var result = await _executor.ExecuteAsync(call, ct);
            yield return new ToolCallCompleteEvent(call, result);
            messages.Add(ToToolMessage(call, result));
        }
    }
}
```

<!--
SPEAKER NOTES — DefaultAgentRuntime.
This is a simplified version of the real method — the production code adds telemetry, summarisation, budget tracking — but the bones are right here. The for-loop is the iteration cap. We call the model, stream out any content, check for tool calls. If there are none, we break and we're done. If there are tool calls, we yield a ToolCallStartEvent so the UI can react, run the executor — which handles the approval gate and timing — yield a ToolCallCompleteEvent, and append the result to the message list so the model sees it on the next iteration. Notice messages.Add at the bottom — that's what closes the loop. The model sees its own tool call and the result, and decides what to do with that information.
-->

---

## How tools get into the prompt

```
IToolRegistry  ─►  GetAllTools()
                       │
                       ▼
   ┌──────── in-process ITool ─────────┐
   │  GreeterTool, FileSystemTool, ... │
   └───────────────────────────────────┘
   ┌────────── MCP tool wrapper ───────┐
   │  fs.read_file, web.fetch, ...     │
   └───────────────────────────────────┘
                       │
                       ▼
        Convert to AIFunction[]
                       │
                       ▼
   new ChatOptions { Tools = [...] }  ──► model
```

<!--
SPEAKER NOTES — tool manifest.
Where the two surfaces actually merge. IToolRegistry.GetAllTools returns one IEnumerable&lt;ITool&gt; that contains both surfaces — the in-process implementations directly, and the MCP tools wrapped in an adapter that satisfies ITool but delegates to the MCP host underneath. We then convert each to a Microsoft.Extensions.AI AIFunction, build a ChatOptions object with Tools = that array, and pass it to the model. From here, everything is standard M.E.AI — the model picks tools, calls them by name, the executor runs them. The model never sees the surface distinction; we never have to write two code paths.
-->

---

# 🧪  Stage 5 — Demos

<!--
SPEAKER NOTES — Stage 5 divider.
Five runnable demos. The first three are no-LLM demos that show framework primitives. The last two need Ollama and exercise the agent loop end-to-end with both tool surfaces.
-->

---

## The 5 demos at a glance

| # | Demo | What it shows | LLM? |
|---|------|--------------|------|
| 1 | `demo1-tool` | Custom `ITool`, metadata, schema | ❌ |
| 2 | `demo2-approval` | `IToolApprovalPolicy` swap | ❌ |
| 3 | `demo3-agent-loop` | Ollama + `AIFunction` tools | ✅ |
| **4** | **`demo4-mcp-stdio`** | **Attach an MCP server, call its tool** | ❌ |
| **5** | **`demo5-hybrid`** | **One agent, one ITool + one MCP tool** | ✅ |

```pwsh
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"
dotnet run --project docs\sessions\session-2\code\demo5-hybrid
```

<!--
SPEAKER NOTES — demos overview.
Five demos in the repo. One through three you saw last time — building blocks. Demo 4 is new — it shows how to spin up an MCP server in-process and invoke a tool through it WITHOUT an LLM, so you can isolate "does the MCP wiring work?" from "does the model pick the right tool?". Demo 5 is the showstopper: one agent, one ITool and one MCP tool, and the model decides which to use. That's the whole pitch of this session in a single 200-line file.
-->

---

## Demo 4 — Attach an MCP server (stdio)

```csharp
var def = new McpServerDefinition(
    Id: "fs-demo",
    Name: "FileSystem (npx)",
    Transport: McpTransport.Stdio,
    Command: "npx",
    Args: ["-y", "@modelcontextprotocol/server-filesystem", "./sandbox"]);

await using var host = new StdioMcpHost(def, logger);
await host.StartAsync(ct);

var tools = await host.ListToolsAsync(ct);
foreach (var t in tools) Console.WriteLine($"  • {t.Name} — {t.Description}");

var result = await host.CallToolAsync("read_file",
    new { path = "README.md" }, ct);
Console.WriteLine(result.Content);
```

<!--
SPEAKER NOTES — demo 4.
Demo 4 is the cleanest possible MCP demo. We define a McpServerDefinition pointing at the official filesystem MCP server distributed as an npm package — one of dozens of community servers that "just work". StdioMcpHost spawns the subprocess, performs the MCP initialize handshake. Then we list the tools the server exposes, and we call one of them by name with a JSON arguments object. No model in the loop yet — this is just "is the wiring working?". Run this to convince yourself the stdio transport works, then go to demo 5 to add the model.
-->

---

## Demo 5 — Hybrid agent (`ITool` + MCP)

```csharp
services.AddToolFramework();
services.AddTool<CalculatorTool>();           // in-process

services.AddOpenClawMcp();
services.AddMcpServerDefinition(new McpServerDefinition(
    Id: "fs-demo", Transport: McpTransport.Stdio,
    Command: "npx", Args: ["-y", "@modelcontextprotocol/server-filesystem", "./sandbox"]));

services.AddSingleton<IAgentProvider, OllamaAgentProvider>();
```

> Then ask: *"Read sandbox/numbers.txt, sum the values, return the average."*

The model picks `fs-demo.read_file` then `calculator` then answers. **One loop, two surfaces.**

<!--
SPEAKER NOTES — demo 5.
Demo 5 is the payoff. We register one in-process tool (Calculator), add the MCP framework, attach the filesystem MCP server. We give the agent a prompt that REQUIRES both surfaces: read a file using the MCP tool, then do math using the ITool, then return the result. The model has no idea one came from C# code and one from a Node.js subprocess. It just sees "fs-demo.read_file" and "calculator" in its manifest and picks them in the right order. When you watch the logs you'll see the approval gate fire for whichever tools you flagged, the events in NDJSON form, and the final answer. That's the architecture — collapsed into one runnable file.
-->

---

## Going further — built-in templates

`/jobs/templates` ships with one-click recipes:

- 📂 **Watched folder summarizer** — every 5 min, summarize new docs
- 🌐 **Daily site digest** — fetch, extract, summarize, email
- 📰 **Inbox triage** — IMAP + classify + move
- ⏰ **Cron meets agent** — run an agent on a schedule

> All built on the same `IToolExecutor` you just learned about.

<!--
SPEAKER NOTES — templates.
We ship a Templates page in the UI with one-click recipes for the most common agent patterns. Watched folder summarizer is the one we walk through in docs/demos/tools — every 5 minutes scan a folder, convert docs to markdown, summarize. Daily site digest, inbox triage, cron-driven agents — all of them are built on the same IToolExecutor you just saw. The point of the templates page is so people don't start from a blank canvas; they fork a working recipe.
-->

---

# 🎯  Where we go next

- **Session 3** — Memory, summarisation, and conversation budgets
- **Session 4** — Multi-agent: orchestrator + workers + handoff
- **Bonus** — Production hardening: secrets, telemetry, rate limits

> Today: tools you trust. Next: an agent that **remembers**.

<!--
SPEAKER NOTES — what's next.
Session 3 is about memory — we've been keeping every message in a list this whole time, which is fine until your conversation hits the model's context window. Summarisation, conversation budgets, vector memory — all next session. Session 4 goes multi-agent: an orchestrator that hands work off to specialised workers. The bonus session is production hardening: secrets management, telemetry, rate limits, the boring stuff that turns a demo into a deployment.
-->

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
`docs/sessions/session-2/` for everything from today

<!--
SPEAKER NOTES — closing.
Thanks everyone. The repo is github.com/elbruno/openclawnet, MIT licensed, contributions very welcome. Everything from today — slides, demos, walkthrough — lives under docs/sessions/session-2/. If you want to keep going, demo 5 is the most fun to extend: try replacing the Calculator tool with a Weather tool that calls a real API, or swap the filesystem MCP server for the GitHub one and ask the agent about your repos. Questions?
-->
