---
marp: true
title: "OpenClawNet — Session 3: Skills, Storage & Memory"
description: "Agent personality through skills, fail-closed storage, and memory system planning"
theme: openclaw
paginate: true
size: 16:9
footer: "OpenClawNet · Session 3 · Skills, Storage & Memory"
---

<!-- _class: lead -->

# OpenClawNet
## Session 3 — Skills, Storage & Memory

**Microsoft Reactor Series · ~75 min · Intermediate .NET**

> *Same agent. Different personality. Safer disk. Longer memory.*

<br>

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

<!--
SPEAKER NOTES — title slide.
Welcome back to Session 3. In Session 1 we built the foundation. In Session 2 we gave the agent hands. Today we give it personality, a safe place to live on disk, and a roadmap for long-term memory. We also ship two new dev-tools that you'll want on your taskbar. Big session — let's go.
-->

---

## Where Sessions 1–2 left off

- **Session 1** — Aspire app, `IAgentProvider`, NDJSON streaming, SQLite
- **Session 2** — `ITool` + MCP, one approval gate, one runtime
- **5** in-process tools, **5** bundled MCP servers, **5** demos
- 3 attacks blocked: path traversal, command injection, SSRF
- `aspire start` → chat with tool-using agent in 30 seconds

> Today we add: **personality, safe storage, and a memory roadmap.**

<!--
SPEAKER NOTES — recap.
Quick recap so latecomers can catch up. Session 1 = a working chat app over Aspire/Blazor with five providers. Session 2 = the agent loop, two tool surfaces, one approval gate, and the security primitives every tool framework needs. Both sessions are recorded and the code is on the repo. If you missed them, the slides and demos are at docs/sessions.
-->

---

## Today's scope, in one slide

1. 🛠️ **Extra tools** — Ollama Monitor + Aspire Monitor on your taskbar
2. 🔧 **Tool-calling updates** — OpenAI alignment, sanitizers, refactors
3. 🎭 **Skills system** — Markdown personality, hot-reload, per-agent
4. 💾 **Storage refactor** — `C:\openclawnet\`, `ISafePathResolver`, H-1..H-8
5. 🧠 **Memory roadmap** — context windows, summarization, embeddings
6. 🧪 **Console demos** — `aspire start`, `/api/skills`, manual skill drop-in

<!--
SPEAKER NOTES — scope.
Six chapters. The first two are "what shipped since session 2 — quality of life and tool-calling polish". Chapters three and four are the meat — skills and storage. Chapter five is forward-looking: where memory is going. Chapter six is hands-on. We'll move fast on the recap-y bits and slow down on the design choices.
-->

---

## The session-3 mental model

<div class="cols">
<div>

### Behavior
- Skills shape **what the agent does**
- Markdown + YAML, no code
- Hot-reload, per-agent enablement

</div>
<div>

### Boundaries
- Storage shapes **where the agent writes**
- One root: `C:\openclawnet\`
- Fail-closed containment

</div>
</div>

> Behavior + Boundaries = a personalized agent you can trust on disk.

<!--
SPEAKER NOTES — mental model.
The whole session pivots on this. Skills = behavior, storage = boundaries. Both are about giving the agent more autonomy SAFELY. You can't ship skills if the agent can write anywhere on disk; you can't trust storage hardening if anyone can drop a malicious skill. They land together.
-->

---

<!-- _class: lead -->

# 🛠️  Part 1 — Extra Tools

<!--
SPEAKER NOTES — Part 1 divider.
Two new dev-tools we shipped between Session 2 and Session 3. They live in the system tray and they make day-to-day OpenClawNet development much less painful. Ten minutes total.
-->

---

## Why we needed extra tools

- Ollama dies silently → "why is my agent slow?"
- Aspire dashboard is great, but **buried in a browser tab**
- Local LLM dev = 3 terminals + 2 dashboards open all day
- Demos crash live because *something* wasn't running

> We wanted **glanceable status** — not "go open a tab".

<!--
SPEAKER NOTES — pain points.
This is what every local-LLM developer experiences. You forget to start Ollama, the agent times out, you debug for ten minutes before realizing the model server isn't even up. Or your Aspire app is running but you closed the dashboard tab. We built two tray apps that put the answers in your system tray.
-->

---

## Ollama Monitor — what it is

- 📦 **NuGet dotnet tool** — `dotnet tool install -g OpenClawNet.Tools.OllamaMonitor`
- 🟢 **System tray icon** with color-coded health
- 📊 **Real-time metrics** — model loaded, GPU layers, tokens/sec
- 🔔 **Toast notifications** when Ollama goes down
- 🪟 Windows-first (works on macOS/Linux trays too)

<!--
SPEAKER NOTES — Ollama Monitor.
First tool: Ollama Monitor. Distributed as a global dotnet tool — one command to install, lives in your system tray. Green = Ollama is up and serving. Yellow = up but slow. Red = down. Click the icon and you get model details, GPU stats, recent requests. The toasts are the killer feature — you find out before your demo does.
-->

---

## Ollama Monitor — features

- ⚡ **Health probes** every 5s (configurable)
- 📈 **Last 60 seconds** of throughput in a sparkline
- 🧠 **Loaded model** + **size** + **quantization**
- 🎯 **Active requests** counter
- 🛑 **Quick stop / start** from the tray menu

<div class="cols">
<div>

### Color codes
- 🟢 Healthy, < 200ms latency
- 🟡 Healthy, > 200ms latency
- 🟠 Degraded (timeouts)
- 🔴 Unreachable

</div>
<div>

### Toast triggers
- Process exit
- Health probe fail (3x)
- Model unload
- Recovery

</div>
</div>

<!--
SPEAKER NOTES — features detail.
Every five seconds we hit /api/tags and /api/ps. The sparkline shows tokens/sec aggregated across active requests. Quick stop/start uses the Ollama CLI under the hood. The status colors are deliberate — yellow doesn't mean broken, it means "your laptop is on battery and the model is paged out". Triple-fail before we toast so we don't spam you on a flaky network.
-->

---

## Ollama Monitor — install

```pwsh
# Install once
dotnet tool install -g OpenClawNet.Tools.OllamaMonitor

# Run on demand
ollama-monitor

# Or auto-start with Windows
ollama-monitor --autostart
```

- Settings live at `%APPDATA%\OpenClawNet\OllamaMonitor\settings.json`
- Override Ollama URL with `--endpoint http://host:11434`
- Logs to `%LOCALAPPDATA%\OpenClawNet\OllamaMonitor\logs\`

<!--
SPEAKER NOTES — install.
Three lines to install and run. Auto-start adds a Windows scheduled task at logon — survives reboots, no startup-folder shortcut to manage. Settings are JSON, you can sync them across machines if you like. Log directory uses the standard "local app data" pattern — same place every other modern Windows app puts logs.
-->

---

## Ollama Monitor — demo

```text
🟢 Ollama (llama3.2:3b)            127.0.0.1:11434
   Loaded model:   llama3.2:3b-instruct-q4_K_M
   Size:           2.0 GB
   GPU layers:     33 / 33
   Throughput:     78 tok/s  ▁▂▅▇▇▆▅▃▂▁
   Active reqs:    2
   ───────────────────────────
   Open dashboard      [Ctrl+D]
   Restart Ollama      [Ctrl+R]
   Settings…
   Exit
```

> Right-click the tray icon → see the agent's heartbeat.

<!--
SPEAKER NOTES — demo.
Live demo if Ollama is running. Right-click the tray icon. Show the loaded model, the GPU layer count — for a quantized 3B that should be 33/33 meaning fully on GPU. Sparkline shows the last 60 seconds. Active reqs goes up when you fire a chat request. The dashboard hotkey opens a more detailed window with per-request timeline.
-->

---

## Aspire Monitor — what it is

- 📦 **NuGet dotnet tool** — `dotnet tool install -g OpenClawNet.Tools.AspireMonitor`
- 🟢 **Windows system tray** companion for `aspire start`
- 📁 **Working folder watch** — auto-detects which app you're running
- 📌 **Pinned resources** mini window — your favorite endpoints
- ⏯️ **Start / Stop** controls from the tray

<!--
SPEAKER NOTES — Aspire Monitor.
Second tool. Aspire Monitor solves a different pain point: you're working on multiple Aspire apps, you forget which one is running, the dashboard URL changes every restart. This pins itself to a folder, knows which AppHost lives there, and gives you start/stop without going back to the terminal.
-->

---

## Aspire Monitor — features

- 🔍 **Auto-discovery** of `*.AppHost` projects in the watched folder
- 📊 **Per-resource status** (running, starting, failed) with one-click logs
- 📌 **Pinned mini window** — keep 3-5 endpoints always visible
- 🎯 **Dashboard URL** copied to clipboard on hover
- 🔁 **Restart** any single resource without restarting the host

<div class="cols">
<div>

### What it watches
- AppHost stdout/stderr
- Aspire dashboard API
- Resource health endpoints
- Working folder file changes

</div>
<div>

### What it surfaces
- ✅ Running resources count
- ⏱️ Startup time
- 🌐 Endpoint URLs (HTTP + HTTPS)
- ⚠️ Crash / restart events

</div>
</div>

<!--
SPEAKER NOTES — Aspire features.
The pinned window is the feature people fall in love with. You pin "Gateway", "Ollama", and "Open Dashboard" — those three sit in a tiny floating window in the corner of your screen. One click and you're at any of them. Behind the scenes we're hitting the Aspire dashboard's resource API, so we get health for free.
-->

---

## Aspire Monitor — install

```pwsh
# Install once
dotnet tool install -g OpenClawNet.Tools.AspireMonitor

# Watch the current folder
aspire-monitor

# Watch a specific folder
aspire-monitor --folder C:\src\openclawnet

# Auto-start with the AppHost
aspire-monitor --auto
```

- Settings live at `%APPDATA%\OpenClawNet\AspireMonitor\settings.json`
- Pinned items survive restarts
- Multiple instances = multiple watched folders

<!--
SPEAKER NOTES — Aspire install.
Same install pattern as Ollama Monitor — dotnet tool, global, no admin needed. The --auto flag is for CI / demo recording: it starts the AppHost the moment the monitor opens. You can run multiple copies pointing at different folders and they'll show up as separate tray icons.
-->

---

## Aspire Monitor — demo

```text
🟢 OpenClawNet.AppHost (3 of 3 running)
   ├── 🟢 gateway        https://localhost:7234
   ├── 🟢 ollama         http://localhost:11434
   └── 🟢 dashboard      https://localhost:17000
   ─────────────────────────────────────
   📌 Pinned
      Gateway · /chat
      Skills page
      Aspire dashboard
   ─────────────────────────────────────
   Start    Stop    Restart    Logs…
```

> One tray icon, one folder watch, three endpoints pinned.

<!--
SPEAKER NOTES — demo.
Right-click the tray icon. Three resources, all green, with their endpoints. Pinned section at the bottom — the three URLs I open ten times a day. Start/Stop/Restart applies to the whole AppHost. Logs opens the dashboard's logs page directly, not the dashboard root, so you skip a click.
-->

---

<!-- _class: lead -->

# 🔧  Part 2 — Tool-Calling Updates

<!--
SPEAKER NOTES — Part 2 divider.
Now the under-the-hood work. Between Session 2 and 3 we did a chunk of plumbing on tool calling — alignment with OpenAI's format, a refactor of FileSystemTool, and three new sanitizers. None of it is glamorous; all of it makes the agent more reliable.
-->

---

## Why touch tool-calling at all?

- Session 2 shipped a working stack — but...
- Different providers expect **different tool-call formats**
- `FileSystemTool` had grown to a 600-line single file
- Sanitizers were duplicated across 3 tools
- "It works on Ollama" ≠ "it works on Azure OpenAI"

> Goal: **one canonical format**, reusable sanitizers, smaller tools.

<!--
SPEAKER NOTES — why.
Honest origin story. We tested mostly on Ollama in Session 2. As soon as we hit Azure OpenAI and Foundry with the same agent, we saw subtle differences in how tool calls were serialized — argument order, JSON quoting, error shapes. Same with the FileSystemTool: it had become a kitchen sink. This part of the session shows what we changed and why.
-->

---

## OpenAI format alignment

```jsonc
// Canonical tool call (matches OpenAI / Azure OpenAI / Foundry)
{
  "id": "call_abc123",
  "type": "function",
  "function": {
    "name": "file_system",
    "arguments": "{\"action\":\"read\",\"path\":\"README.md\"}"
  }
}
```

- Arguments are a **JSON string**, not an object (legacy gotcha)
- `id` is opaque — providers generate it, we just echo it back
- `type` is always `"function"` for v1 tools

<!--
SPEAKER NOTES — format.
The OpenAI tool-call shape is the de-facto standard. Arguments-as-string is the historical wart that everyone supports because the original API shipped that way. Microsoft.Extensions.AI gives us the right abstraction (AIFunction) but providers can drift. We canonicalized everything internally so we never have to special-case "is this Ollama or Azure?" in the runtime.
-->

---

## What changed since Session 2

| Component | Before | After |
|-----------|--------|-------|
| Tool result envelope | `{ok, value}` | `ToolResult.Ok/Fail` (record) |
| Argument parsing | per-tool ad-hoc | `JsonSchema` + `JsonElement` |
| Error shapes | strings | `ToolError(code, message)` |
| Sanitizers | duplicated | `IInputSanitizer` registry |
| `FileSystemTool` | 1 file, 600 LOC | 5 files, ~120 LOC each |

> Same public contract. Cleaner internals. Same NDJSON events.

<!--
SPEAKER NOTES — diff table.
What actually changed. The public ITool interface is identical — your custom tools from Session 2 still compile and run. What we cleaned up is the inside: a record-based ToolResult so callers can pattern-match, schema-driven argument parsing, structured error codes, sanitizers behind an interface. The FileSystemTool refactor is the one that matters most to the next slide.
-->

---

## FileSystemTool refactor

```text
OpenClawNet.Tools/FileSystem/
├── FileSystemTool.cs         (orchestrator — 110 LOC)
├── Operations/
│   ├── ReadOperation.cs
│   ├── WriteOperation.cs
│   ├── ListOperation.cs
│   └── DeleteOperation.cs
└── Validation/
    ├── PathValidator.cs       (delegates to ISafePathResolver)
    └── ContentValidator.cs    (size, encoding)
```

- One operation per file — single responsibility
- All path resolution **flows through `ISafePathResolver`** (more in Part 4)
- Tests now mirror the file layout

<!--
SPEAKER NOTES — refactor.
This is the structure we'll repeat for the other tools over the next sessions. One orchestrator that picks the operation, one file per operation, validators in their own folder. The win is testability — instead of mocking the entire tool, you test ReadOperation against a fake IFileSystem. The PathValidator is a thin wrapper that delegates to the new ISafePathResolver, which is the bridge into Part 4.
-->

---

## Tool sanitizers — the new contract

```csharp
public interface IInputSanitizer<TInput>
{
    SanitizationResult<TInput> Sanitize(TInput input);
}

public sealed record SanitizationResult<T>(
    bool IsAccepted,
    T? Value,
    string? RejectionReason);
```

- One interface, many implementations
- Path, URL, shell-command, JSON-schema sanitizers
- Composable: chain `PathSanitizer` → `SizeSanitizer` → `EncodingSanitizer`

<!--
SPEAKER NOTES — sanitizer contract.
Sanitizers are now first-class. Each tool declares which sanitizers it needs and they run in order before the operation body sees the input. This is the pattern we want for any future tool: never trust LLM-supplied strings, always sanitize, always have a structured rejection reason. The rejection reason flows back to the model so it can correct itself on the next turn.
-->

---

## Three sanitizers ship with v1

<div class="cols">
<div>

### `PathSanitizer`
- Reject reparse points
- Enforce containment (H-1..H-4)
- Apply name allowlist (H-5)
- Delegates to `ISafePathResolver`

</div>
<div>

### `UrlSanitizer`
- HTTPS-only by default
- Block private IP ranges
- Block cloud metadata hosts
- Cap redirects + body size

</div>
</div>

### `JsonArgumentSanitizer`
- Validate against tool's `JsonSchema`
- Strip unknown properties (fail-loud option)
- Coerce types only when safe (`"42"` → `42` for int props)

<!--
SPEAKER NOTES — three sanitizers.
PathSanitizer is the bridge to Part 4 — it's the user-facing layer over ISafePathResolver. UrlSanitizer keeps the Session 2 SSRF defenses but as a reusable component. JsonArgumentSanitizer is the one that pays for itself fastest: every time the model invents a property name or sends a number as a string, the sanitizer either coerces it correctly or rejects with a clear message. Tokens saved on retries pay for the engineering effort within a week.
-->

---

## End-to-end: what a tool call looks like now

```
LLM emits tool_call
        │
        ▼
┌──────────────────────┐
│ JsonArgumentSanitizer│ → validate against JsonSchema
└────────┬─────────────┘
         │ valid
         ▼
┌──────────────────────┐
│ PathSanitizer        │ → ISafePathResolver
└────────┬─────────────┘
         │ contained
         ▼
┌──────────────────────┐
│ IToolApprovalPolicy  │ → human-in-the-loop?
└────────┬─────────────┘
         │ approved
         ▼
   ToolResult.Ok / .Fail   (audit on every branch)
```

<!--
SPEAKER NOTES — pipeline.
This is the pipeline now. Four explicit gates, each independently testable. JsonArgumentSanitizer first because it's free and rejects most garbage. Then any path-typed argument goes through PathSanitizer. Then the approval policy. Only then does the operation body run. Every gate emits an audit record on accept and reject — H-8 in Part 4.
-->

---

## A concrete diff: `read` operation

```csharp
// Before (Session 2)
public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct)
{
    var path = input.Args["path"]!.ToString();
    var full = Path.GetFullPath(Path.Combine(_root, path));
    if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        return Fail("path escape");
    return Ok(File.ReadAllText(full));
}
```

```csharp
// After (Session 3)
public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct)
{
    var args = _argSanitizer.Sanitize(input);          // schema
    if (!args.IsAccepted) return Fail(args.RejectionReason!);

    var resolved = _paths.Resolve(args.Value!.Path, scope: _scope);
    if (!resolved.IsAllowed) return Fail(resolved.Reason!);

    return Ok(await File.ReadAllTextAsync(resolved.FullPath, ct));
}
```

<!--
SPEAKER NOTES — diff.
Same operation, two different worlds. The before is fine for a demo and a foot-gun in production. The after delegates everything dangerous to a single, audited resolver. No raw Path.GetFullPath on LLM input. No string-prefix check that breaks at C:\openclawnet vs C:\openclawnet-evil. No silent rewrite. This is the pattern we'll repeat for every path-taking tool.
-->

---

## Per-tool metadata is unchanged

```csharp
public sealed record ToolMetadata(
    JsonDocument ParameterSchema,
    bool RequiresApproval,
    string Category,
    string[] Tags);
```

- `ParameterSchema` now drives `JsonArgumentSanitizer`
- `RequiresApproval` still gates the executor
- Tools you wrote in Session 2 keep working

> The contract didn't move. The implementation got safer.

<!--
SPEAKER NOTES — metadata unchanged.
Important reassurance. If you wrote a custom tool in Session 2, NOTHING CHANGES for you at the API level. Same ITool, same metadata, same approval gate. You DO get the new sanitizers for free if you opt in. Backwards compatibility was a hard requirement of this refactor.
-->

---

## NDJSON event additions

```jsonl
{"type":"ToolApprovalRequest","tool":"file_system","args":{...}}
{"type":"ToolCallStart","tool":"file_system","callId":"abc"}
{"type":"ToolSanitizationFailed","tool":"file_system","reason":"reparse-point"}
{"type":"ToolCallComplete","tool":"file_system","callId":"abc","durationMs":12}
{"type":"ContentDelta","text":"File contents..."}
```

- New: `ToolSanitizationFailed` — surfaced in the UI as an inline warning
- Existing events unchanged — UI keeps working

<!--
SPEAKER NOTES — NDJSON.
One new event type — ToolSanitizationFailed — emitted when a sanitizer rejects an input before approval. The UI shows it as a yellow inline note in the conversation so the user can see "the model tried to read C:\Windows\System32 and the sanitizer blocked it". That transparency is gold for debugging prompt-injection attempts.
-->

---

## What it buys you

- ✅ Same agent works on **Ollama, Azure OpenAI, Foundry, Copilot**
- ✅ Path traversal failure modes are now **one bug, not five**
- ✅ Sanitizer reasons feed back to the model → fewer retry loops
- ✅ FileSystemTool is **5× smaller** per file → easier PRs

> The "boring" session-3 work that lets the next 3 parts exist.

<!--
SPEAKER NOTES — payoff.
This is the foundation slide. None of skills, storage hardening, or memory could land cleanly on top of Session 2's tool internals as they were. We had to do this refactor first. The provider-portability win is the big external benefit; the internal benefit is that the next sessions can ADD without rewriting.
-->

---

<!-- _class: lead -->

# 🎭  Part 3 — Skills System

<!--
SPEAKER NOTES — Part 3 divider.
The biggest part of the session — about thirty slides. Skills are the feature most users will notice first. Markdown files that change agent behavior. Hot-reload. Per-agent enablement. Let's go.
-->

---

## What is a skill?

> A **Markdown file with YAML frontmatter** that shapes agent behavior — without code, without redeploy.

```markdown
---
name: dotnet-expert
description: .NET expertise — DI, async, Aspire, EF Core
tags: [dotnet, csharp, aspire]
---

You are a senior .NET architect. When answering:

- Prefer modern C# patterns (records, primary constructors)
- Cite Microsoft Learn for any non-trivial claim
- Show code in full, never abridged with "..."
```

<!--
SPEAKER NOTES — what is a skill.
Read it on screen. Three lines of YAML, a paragraph of Markdown, and the agent now behaves like a senior .NET architect. No C#. No deploy. No restart. The point we're making all session: skills are CONTENT, not CODE. Anyone on the team can write one — your PM, your QA engineer, your security lead.
-->

---

## Why skills (vs. tools)?

|              | **Tools**                       | **Skills**                          |
|--------------|---------------------------------|-------------------------------------|
| Format       | C# code (`ITool`)               | Markdown + YAML                     |
| Authored by  | engineers                       | **anyone** (PM, QA, security…)      |
| Effect       | new **capabilities**            | new **behavior**                    |
| Lifecycle    | compile + deploy                | drop a file, hot-reload             |
| Risk surface | code execution                  | prompt injection                    |

> **Tools = arms.** **Skills = personality.** Different problems.

<!--
SPEAKER NOTES — vs tools.
Lots of people see skills and ask "isn't that just a system prompt?" Yes — but with structure, lifecycle, and audit. The crucial column is "authored by". Tools require an engineer; skills don't. That single fact changes who in the company can shape agent behavior. Risk surface is also genuinely different — skills can't run code in v1 (S-8) but they can prompt-inject the model into doing bad things, which is why approval gates and per-agent enablement matter.
-->

---

## The headline bug we fixed

OpenClawNet had **two parallel skill loaders** that didn't share state:

| System | Reads from | Consumed by |
|--------|------------|-------------|
| Custom `FileSkillLoader` | `skills/built-in`, `skills/samples` | `/api/skills/*` |
| MAF `AgentSkillsProvider` | `Agent:SkillsPath` config | the actual agent |

**Result:** click "Disable" in the UI → `200 OK` → the agent **keeps using the skill**.

<!--
SPEAKER NOTES — headline bug.
Painful one to admit but worth showing. We had two skill subsystems that both worked, neither knew about the other. Click disable, agent keeps using the skill. Click install, file lands in a folder the agent doesn't scan. Hot reload reloads the loader the agent doesn't use. This was the catalyst for the entire skills proposal.
-->

---

## The fix: one loader, one source of truth

```
┌──────────────────────────────────────┐
│  ISkillsRegistry  (singleton)        │
│  3-layer discovery + watcher         │
└────────────┬─────────────────────────┘
             │ BuildFor(agentName)
             ▼
┌──────────────────────────────────────┐
│  OpenClawNetSkillsProvider (scoped)  │
│  AIContextProvider · per-request     │
└────────────┬─────────────────────────┘
             │
             ▼
┌──────────────────────────────────────┐
│  MAF AgentSkillsProvider             │
│  (agentskills.io spec compliant)     │
└──────────────────────────────────────┘
```

> Delete `FileSkillLoader`. Delete `SkillParser`. **MAF is the source of truth.**

<!--
SPEAKER NOTES — fix.
We deleted the parallel loader. MAF — Microsoft Agent Framework — already implements the full agentskills.io spec, including progressive disclosure, YAML parsing, and resource tools. Our job becomes a thin scoped decorator that adds three things: layer attribution, per-agent enablement filtering, and structured logging. The UI calls our registry; the registry feeds MAF; MAF feeds the agent. One pipeline.
-->

---

## 3-layer storage

```
C:\openclawnet\
└── skills\
    ├── system\                          # Ships with app, read-only
    │   ├── memory\SKILL.md
    │   └── doc-processor\SKILL.md
    ├── installed\                       # From imports, shared
    │   ├── awesome-copilot\dotnet-expert\SKILL.md
    │   └── .install-manifest.json
    ├── agents\{agent-name}\             # Per-agent overrides
    │   ├── enabled.json                 # which skills are visible
    │   └── skills\
    │       └── {skill-name}\SKILL.md
    └── .quarantine\                     # imported, not yet approved
        └── {import-id}\…
```

**Precedence:** `agents/{name}/` > `installed/` > `system/`.

<!--
SPEAKER NOTES — 3-layer.
Three layers, one precedence rule. System ships with the app and is read-only. Installed is shared by all agents and lives behind the import pipeline. Agents-slash-name is per-agent overrides — both for enablement (enabled.json) and for genuine custom skills. Quarantine is where imports land before approval. Highest layer wins on name collisions, so an agent can shadow a system skill with its own.
-->

---

## Why shared storage, not per-agent?

> "The actual threat is **content-in-prompt**, not content-on-disk."

- Per-agent *enablement* controls exposure
- Per-agent *storage* would mean N copies of every skill
- N copies → update fatigue → rubber-stamp approvals → CVE
- Per-agent storage is **theater**; per-agent enablement is **real**

<!--
SPEAKER NOTES — shared storage.
Drummond's call from the security review. The threat is what enters the system prompt at runtime, not what sits on disk. If we copied every installed skill into every agent's folder, we'd have N copies to update on every CVE. Users would skip the approval gate just to keep up. Shared storage with per-agent enablement is both safer and saner.
-->

---

## agentskills.io frontmatter

```yaml
---
name: dotnet-expert
description: .NET expertise — DI, async, Aspire, EF Core
license: MIT
metadata:
  openclawnet:
    tags: [dotnet, csharp, aspire]
    category: programming
    examples:
      - "How should I structure DI in a Blazor app?"
      - "Why is my async method blocking?"
---
```

- **Spec-compliant** core: `name`, `description`, `license`
- OpenClawNet extras live under `metadata.openclawnet.*`
- MAF ignores unknown fields gracefully

<!--
SPEAKER NOTES — frontmatter.
agentskills.io is the open spec we're aligning with. It defines the core fields — name, description, license — and reserves a metadata namespace for vendor extensions. Our extras (tags, category, examples) move into metadata.openclawnet.* so we're forward-compatible with any other host that speaks the spec. MAF parses YAML the right way, including quoted multi-line strings — our hand-rolled parser was choking on those.
-->

---

## What gets dropped from old frontmatter

| Old field | Status | Why |
|-----------|--------|-----|
| `enabled: true` | ❌ removed | replaced by per-agent `enabled.json` |
| `category: …` | ✅ moved | now `metadata.openclawnet.category` |
| `tags: […]` | ✅ moved | now `metadata.openclawnet.tags` |
| `examples: […]` | ✅ moved | now `metadata.openclawnet.examples` |

> Spec-compliant on top. OpenClawNet flavor underneath.

<!--
SPEAKER NOTES — old fields.
The big change is dropping enabled-true from the frontmatter itself. Why: enablement is per-agent, not per-skill. Putting it in the file makes it look global. Defaults for "which built-ins are on by default for new agents" move to a SystemSkillsDefaults.json in the gateway content root. Cleaner separation of "what the skill is" from "who has it on".
-->

---

## `FileSystemWatcher` hot-reload

```csharp
public sealed class SkillsLayerWatcher : IDisposable
{
    private readonly FileSystemWatcher _fsw;
    private readonly Channel<Unit> _coalesce;

    public SkillsLayerWatcher(string root, Action onChange)
    {
        _fsw = new FileSystemWatcher(root, "*.md")
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _fsw.Changed += (_, _) => _coalesce.Writer.TryWrite(default);
        // … 500ms debounce loop calls onChange()
    }
}
```

- One watcher **per layer** (system, installed, agents/{name})
- 500 ms debounce — saves are bursty (editor temp file dance)
- On change → registry rebuilds snapshot

<!--
SPEAKER NOTES — watcher.
FileSystemWatcher is notoriously noisy — VS Code saves a file by writing a temp, deleting the original, and renaming. Three events for one save. We coalesce with a 500ms debounce: any number of events inside the window collapse to one rebuild. Per-layer watchers because the layers can live on different drives or volumes and we want independent failure domains.
-->

---

## Snapshot per request

```csharp
public sealed record SkillsSnapshot(
    ImmutableArray<ResolvedSkill> Skills,
    long Version);
```

- Registry holds the **current** snapshot
- Each request gets a **stable** view
- Mid-conversation file edits → next turn picks up the change
- No torn reads

<!--
SPEAKER NOTES — snapshot.
Important design call. When the watcher fires we don't reach into running conversations and patch them. We rebuild a new immutable snapshot. Active turns finish on the old snapshot; the next turn picks the new one. This is the answer to Bruno's open-question Q2 in the proposal — "auto-reload mid-conversation? no — snapshot-per-turn." It avoids torn reads and keeps a single conversation deterministic.
-->

---

## Per-agent enablement: `enabled.json`

```json
{
  "version": 1,
  "skills": {
    "memory": "enabled",
    "doc-processor": "enabled",
    "awesome-copilot/dotnet-expert": "enabled",
    "awesome-copilot/security-auditor": "disabled"
  },
  "default": "use-default"
}
```

- One file per agent at `agents/{name}/enabled.json`
- Three states: `enabled`, `disabled`, `use-default`
- New skills default to **disabled** for fail-closed safety
- Persisted to SQLite; `enabled.json` is the on-disk projection

<!--
SPEAKER NOTES — enabled.json.
Three-valued logic. enabled = explicit on. disabled = explicit off. use-default = "ask the registry what the default is" — useful for new skills the agent hasn't been told about yet. The default for newly imported external skills is disabled. The default for built-ins is enabled. Authoritative state lives in SQLite so we can query "show me every agent that has skill X enabled" without reading every JSON file. The on-disk JSON is the human-friendly projection.
-->

---

## Default = disabled, fail-closed

> Install ≠ active.

- A user imports `security-auditor` from awesome-copilot
- File lands in `installed/awesome-copilot/security-auditor/`
- **No agent uses it yet**
- User goes to Skills page → toggles it on per agent
- Per-agent enablement flips to `"enabled"` → effective next turn

<!--
SPEAKER NOTES — fail closed.
This is S-7 from the proposal — fail closed. It's deliberately friction. We don't want a user to accept an import dialog and have skill content slip into every agent's system prompt. Two gestures: import and enable. The enable step makes you choose which agents get it. If you imported by accident, no agent is affected.
-->

---

## Skills.razor UI — top-level nav

<div class="cols">
<div>

### Browse
- Filter by built-in / installed / enabled
- Source, version, category columns
- "Enabled in: GptAgent, ResearchBot"
- Click a row → inline detail expand

</div>
<div>

### Act
- Toggle per-agent assignment (modal)
- "Install from URL" tab
- Disable / enable / remove
- Usage stats (last 7 days)

</div>
</div>

> **One page** for everything skill-related — not buried in Settings.

<!--
SPEAKER NOTES — Skills page.
We promoted skills out of the Settings sub-menu to a top-level nav item. Two halves: Browse (left) and Act (right). The "Enabled in" column is the killer — at a glance you see which agents have which skills. The per-agent assignment modal is where you flip enablement; we'll see it next.
-->

---

## Per-agent assignment modal

```
┌─────────────────────────────────────────────┐
│ dotnet-expert                          [✕]  │
├─────────────────────────────────────────────┤
│  Default for new agents:   [✓ Enabled]      │
│                                             │
│  Per-agent overrides                        │
│   GptAgent           [enabled  ▼]           │
│   ResearchBot        [disabled ▼]           │
│   SupportBot         [use default ▼]        │
│                                             │
│  Effective on the next chat turn.           │
│                                  [ Save ]   │
└─────────────────────────────────────────────┘
```

> Default toggle on top. Explicit overrides per agent below.

<!--
SPEAKER NOTES — assignment modal.
Single dialog. Default toggle at the top — what new agents inherit. Per-agent dropdowns below — explicit on, explicit off, or "use default". Save persists to SQLite, file projection updates on the next snapshot rebuild. The "effective on the next chat turn" copy is important — sets expectations about hot-reload semantics.
-->

---

## 4-step import wizard

1. **Entry** — paste URL or browse allowlisted collections
2. **Preview** — source, SHA, file list, rendered SKILL.md, diff vs existing
3. **Progress** — download, verify, extract (with detailed log)
4. **Result** — toast: "View / Edit Assignment" or error + retry

> Big yellow banner: *"This content will be injected into the agent's system prompt."*

<!--
SPEAKER NOTES — import wizard.
Four steps, deliberately. Entry collects the URL. Preview is the security-critical step: full file list, sizes, SHA-256 per file, rendered Markdown so you see what the model will see, and a diff against any previously installed version. The "this content enters the system prompt" warning is non-dismissable — that single sentence is the difference between an informed user and a click-through.
-->

---

## v1 import: awesome-copilot only

- One allowlisted source: `github.com/github/awesome-copilot`
- Pinned to a **commit SHA** (not `main`, not a tag)
- SHA-256 manifest of every file in the bundle
- Adding more sources = `appsettings` edit (no UI)

> "Just paste a URL" is a vulnerability, not a feature.

<!--
SPEAKER NOTES — allowlist.
S-12 from the proposal. v1 ships with one trusted source. The reasoning: allowing arbitrary GitHub URLs turns import into a remote prompt injection primitive. Pinning to a commit SHA defeats time-of-check/time-of-use attacks where the upstream changes between preview and confirm. To add a new source, an admin edits appsettings — deliberately high friction.
-->

---

## Manual authoring path

You don't have to use the import pipeline. You can author skills locally:

```pwsh
# 1. Pick a folder
mkdir C:\openclawnet\skills\agents\GptAgent\skills\my-tone-of-voice

# 2. Drop a SKILL.md
@"
---
name: my-tone-of-voice
description: Concise, friendly, no buzzwords
---

When responding: short sentences. No "leverage" or "synergize".
"@ | Out-File ...\SKILL.md

# 3. Watcher picks it up; skill is enabled for that agent
```

<!--
SPEAKER NOTES — manual authoring.
Critical escape hatch. The import pipeline is the safest path for sharing skills. But day-to-day, you and your team will hand-author skills in your editor, save them under agents/{name}/skills/, and the watcher picks them up automatically. No restart, no API call. This is also how you iterate while writing a skill — save, test, save, test.
-->

---

## SKILL.md anatomy in detail

```markdown
---
name: security-auditor
description: Security review focused on .NET + OWASP
license: MIT
metadata:
  openclawnet:
    tags: [security, audit, owasp, dotnet]
    category: security
    examples:
      - "Audit this controller for OWASP Top 10"
      - "Check for SQL injection in this query"
---

You are a security auditor. When reviewing code:

- Identify OWASP Top 10 issues by name
- Flag SQL injection, XSS, path traversal
- Suggest mitigations with code examples
- Sort findings by severity: Critical / High / Medium / Low
```

<!--
SPEAKER NOTES — anatomy.
Concrete example we can copy-paste. Frontmatter at the top — fenced by triple-dash. Body below — pure Markdown. The body becomes part of the system prompt verbatim. The examples array is metadata only — it doesn't get injected, but the UI uses it to show "try asking…" hints. Total file is usually under 2KB; max we allow is 256 KB (S-11) so a single skill can't blow your token budget by accident.
-->

---

## Bounded resources (S-11)

- Per skill: `SKILL.md` ≤ **256 KB**
- Per agent system prompt: skill-injected tokens ≤ **8 KB** (default)
- Oldest-by-load-order dropped if budget exceeded
- WARN audit when drop happens

> A skill that bloats your prompt **degrades gracefully**, doesn't crash.

<!--
SPEAKER NOTES — bounded.
Token budgets are real money. We cap each skill file size and the total contribution to the system prompt. If three big skills are enabled and they exceed the budget, the oldest by load order is dropped — but we WARN-audit so you can find out. Default budget is 8KB which fits comfortably in any modern context window. Configurable per agent.
-->

---

## Hardening invariants S-1..S-12

| # | What |
|---|------|
| **S-1** | Provenance pinning (URL + commit SHA + bundle SHA-256) |
| **S-2** | File-type allowlist (no executables, ever) |
| **S-3** | Storage-path containment (reuse H-1..H-6) |
| **S-4** | Built-in name reservation (`shell-exec` etc. can't be shadowed) |
| **S-5** | Approval gate on install **and** every update |
| **S-6** | No auto-update from external sources |

<!--
SPEAKER NOTES — S-1..S-6.
First half of the hardening list. S-1 is the manifest. S-2 is the file-type allowlist — no .py, .ps1, .dll, no executable bit, nothing starting with MZ or shebang. S-3 hands path resolution to the storage layer. S-4 stops a malicious skill from claiming to be the built-in shell-exec. S-5 is two-step preview/confirm. S-6 means upstream changes never apply silently — you re-run the gate.
-->

---

## Hardening invariants S-1..S-12 (cont.)

| # | What |
|---|------|
| **S-7** | Per-agent enablement, **shared** storage |
| **S-8** | No executable skill content in v1 |
| **S-9** | Audit trail on install/update/load/invoke |
| **S-10** | Revocation effective within **one chat turn** |
| **S-11** | Bounded resource use (256 KB / 8 KB token budget) |
| **S-12** | Source allowlist, deny by default |

> **None negotiable for v1.** Every PR reviewed against this list.

<!--
SPEAKER NOTES — S-7..S-12.
Second half. S-7 we covered. S-8 is "no executable content yet" — that's a future proposal with its own sandbox. S-9 audit trail covers every lifecycle event so you can answer "who installed what when" forensically. S-10 — disable takes effect on the next chat turn, not next process restart. S-11 token budgets. S-12 source allowlist. Twelve invariants, every PR is reviewed against them, no exceptions for v1.
-->

---

## Structured logging — 14 events

```text
SkillLoaderStarted        SkillDiscovered          SkillLoaded
SkillLoadFailed           SkillInvoked             SkillFunctionReturned
SkillFunctionThrew        ImportRequested          ImportApproved
ImportRejected            ImportCompleted          ImportFailed
SkillEnabled              SkillDisabled
```

- All via `LoggerMessage`-source-generated classes
- 8-field correlation: `RunId`, `SkillInvocationId`, `AgentId`, `UserId`, `SkillId`, `FunctionName`, `RequestId`, `Timestamp`
- OTel `ActivitySource: OpenClawNet.Skills` → Aspire dashboard

<!--
SPEAKER NOTES — logging.
Fourteen structured events covering the full lifecycle. LoggerMessage source-generators give us zero-allocation logging at hot-paths. Eight correlation fields means a single SQL query can answer "show me everything that happened during this user's last chat turn including which skills loaded, which functions invoked, which import attempts there were". OTel spans go straight into the Aspire dashboard you saw in Part 1.
-->

---

## What we DON'T log

- ❌ Parameter values (PII, API keys, credentials)
- ❌ Return values (same)
- ❌ SKILL.md body content (attacker-controlled — log injection)
- ❌ Chat content / agent replies / OAuth tokens

> Log **schema** + **size** + **SHA-256** of first 1 KB. That's enough.

<!--
SPEAKER NOTES — what we don't log.
Critical sensitivity rule. Parameter values can contain anything — API keys, passwords, PII. Return values too. SKILL.md bodies are attacker-controlled, so logging them amplifies log-injection attacks. We log SCHEMA — types and shapes — plus size and a partial hash. That's enough for forensics, not enough to leak. Dylan's recommendation in the proposal.
-->

---

## E2E tests — what passes

- ✅ `GET /api/skills` returns the same skills the agent uses
- ✅ `POST /api/skills/{name}/disable` takes effect on next turn
- ✅ Hot-reload: drop a file → next turn sees it
- ✅ Per-agent enablement: toggle for AgentA → AgentB unaffected
- ✅ Import preview → confirm → install round-trip
- ✅ Reserved-name install rejected with clear error

<!--
SPEAKER NOTES — E2E.
Six end-to-end tests are the acceptance criteria for K-1 (the foundational wave). Each one is a real HTTP request against a running gateway. Hot-reload test drops a file and waits one turn. Per-agent test asserts isolation. Import test exercises the full preview-confirm-install pipeline including SHA verification and quarantine cleanup. We run these in CI on every PR.
-->

---

## Implementation waves

| Wave | Scope |
|------|-------|
| **K-1** | Delete parallel loader, single `ISkillsRegistry`, 3-layer + watcher, `enabled.json` |
| **K-2** | 14 log events + 8-field correlation + Activity panel skill rows |
| **K-3** | Import pipeline (preview → confirm), awesome-copilot fetcher |
| **K-4** | UI polish — assignment modal, wizard, usage stats |

> Chains after **W-1..W-4** (storage). Skills can't ship before storage is hardened.

<!--
SPEAKER NOTES — waves.
Four waves. K-1 is the foundation — delete the parallel loader, get one registry. K-2 is observability. K-3 is the import pipeline. K-4 is UX polish. The dependency arrow at the bottom is the punchline of this whole session: skills depend on storage. We can't safely write user-supplied content to disk until the storage layer enforces containment. Which is exactly Part 4.
-->

---

<!-- _class: lead -->

# 💾  Part 4 — Storage Refactor

<!--
SPEAKER NOTES — Part 4 divider.
Twenty slides on storage. This is the load-bearing part of the session. Without H-1..H-8, skills can't ship safely; without a sane default root, users can't find their files. Both problems, one design.
-->

---

## Bruno's question

> "Where are the settings for the OpenClawNet **storage location** — the default place to store files for the application?"

| Scenario | Expected path |
|----------|---------------|
| Agent generates a markdown file | `C:\openclawnet\agents\{name}\out.md` |
| Tool downloads a local model | `C:\openclawnet\models\` |
| User points agent at a folder | `C:\openclawnet\workspaces\samples\` |
| General default | `C:\openclawnet\` |

<!--
SPEAKER NOTES — Bruno's question.
Direct quote from the issue that started this. Bruno wants ONE root, discoverable, predictable. Today's default is buried in bin/Debug/net10.0/ — useless for end users. We need to fix the default AND harden every code path that takes a path from the LLM.
-->

---

## What was wrong

- 🟥 Agent prompts said *"your workspace root is `bin/Debug/net10.0/`"*
- 🟥 `FileSystemTool` defaulted to the **solution root**
- 🟥 No `workspaces/` subfolder for user-named scratch areas
- 🟥 Model downloads landed in `~/.cache/huggingface`
- 🟥 Default root was `C:\openclawnet\storage\` — extra level no-one asked for
- 🟥 `ResolvePath` allowed any **absolute** path through

> The proposal **redirected** writes; this revision **restricts** them.

<!--
SPEAKER NOTES — what was wrong.
Six concrete problems. Five about discoverability. One about safety. The last bullet is the dangerous one: even after we fixed the default to C:\openclawnet, the agent could STILL write anywhere on disk by emitting an absolute path. Redirection isn't restriction. The hardening review made that explicit.
-->

---

## The new defaults

```
C:\openclawnet\
├── agents\{agent-name}\          # per-agent outputs
├── models\                       # local models (Ollama, HF, ONNX)
├── workspaces\{name}\            # user-named scratch
├── uploads\                      # user uploads
├── exports\                      # generated artifacts
├── skills\                       # (Part 3)
└── dataprotection-keys\          # ASP.NET key ring
```

- Configurable via `Storage:RootPath` in `appsettings`
- Or `OPENCLAWNET_STORAGE_ROOT` env var (single canonical name)
- Logs resolved path + source at INFO on startup

<!--
SPEAKER NOTES — defaults.
This is what your C:\openclawnet looks like after Session 3. Seven well-known subfolders, each with a clear purpose. Agents get their own folder per agent name — basis for future per-agent isolation. Models is shared. Workspaces are user-named scratch areas. Uploads and exports separate inbound from outbound user files. Skills you saw. Dataprotection-keys we'll cover in the ACL slide.
-->

---

## Configuration: three sources, one winner

```text
Priority (highest wins):
  1. OPENCLAWNET_STORAGE_ROOT  (env var)
  2. Storage:RootPath          (appsettings.json)
  3. Built-in default          (C:\openclawnet on Windows)
```

```jsonc
// appsettings.json
{
  "Storage": {
    "RootPath": "D:\\openclaw",
    "AdditionalWritablePaths": [ "C:\\shared\\datasets" ]
  }
}
```

> Logged at INFO on startup so misconfiguration is visible in Aspire.

<!--
SPEAKER NOTES — config.
Three sources. Env var wins so containers and CI can override without touching JSON. Appsettings is the everyday answer. Built-in default kicks in for first-run UX. AdditionalWritablePaths is the explicit allowlist for "yes, I want the agent to also be able to write here" — used carefully. The startup INFO log is the hardening recommendation: misconfiguration becomes visible in the dashboard, not silent.
-->

---

## `OPENCLAWNET_STORAGE_ROOT` — one name only

> "Don't have two env vars. An attacker who can set process env could set the *unexpected* one and silently redirect storage."

- Pick **one** name → document it → ignore everything else
- `OPENCLAW_STORAGE_DIR` is **not** consulted (even if present)
- Bonus: log the resolved path **and its source** (env / appsettings / default)

<!--
SPEAKER NOTES — single name.
Subtle threat model from Drummond. If you respect both OPENCLAWNET_STORAGE_ROOT and OPENCLAW_STORAGE_DIR, an attacker who can set one but not the other in a misconfigured container redirects all your writes. Pick one name, document it loudly, ignore everything else. The startup log includes the SOURCE of the value — env var, appsettings, or default — so misconfig is one Aspire dashboard glance away.
-->

---

## `ISafePathResolver` — one resolver, one rule

```csharp
public interface ISafePathResolver
{
    PathResolution Resolve(string requested, string? scope = null);
}

public sealed record PathResolution(
    bool IsAllowed,
    string? FullPath,
    string? Reason);
```

- All path resolution goes here
- No tool calls `Path.GetFullPath` on LLM input directly
- Resolver enforces H-1, H-3, H-4, H-5 in one place
- Optional `scope` parameter for per-agent isolation (H-6)

<!--
SPEAKER NOTES — resolver.
The single chokepoint. Every tool that takes a path delegates to this resolver. Inside it, all the hardening invariants live in ONE testable class — not five copies across five tools. The scope parameter is the seam for future per-agent isolation: today it defaults to RootPath; tomorrow we can pass agents/{name}/ without an API break.
-->

---

## H-1: storage-root containment, fail-closed

```csharp
// Inside the resolver
var full = Path.GetFullPath(requested);
var allowedRoots = new[] { _root, ..._additional };

if (!allowedRoots.Any(root => IsContained(full, root)))
    return PathResolution.Denied("outside storage root");
```

- Reads MAY be broader, writes MUST be inside `RootPath` (+ allowlist)
- **Reject**, don't silently rewrite
- Same gate for `ITool` and MCP tools

<!--
SPEAKER NOTES — H-1.
Most important invariant. Every write — every single one — has to land under the storage root or under an explicit additional-paths allowlist. Reads can be broader because reads are lower-risk and you sometimes legitimately need to look at a sibling project. The crucial design decision: REJECT, not REWRITE. If the LLM emits C:\Windows\System32, we say "no" — we don't say "I'll quietly redirect that to C:\openclawnet\Windows\System32".
-->

---

## H-2: one sanitizer, one resolver

- `ISafePathResolver` is **the** path entry point
- No tool calls `Path.GetFullPath` / `Path.Combine` on LLM input
- Fully unit-tested with positive **and** adversarial cases
- Audited against H-1, H-3, H-4, H-5 in one place

> If you find a tool calling `Path.GetFullPath` on user input, file a bug.

<!--
SPEAKER NOTES — H-2.
The "stop sprawling implementations" rule. The most reliable way to make sure every path resolution is hardened is to have only ONE place that does it. We have a code-review checklist item: any new tool that takes a path string must inject ISafePathResolver and delegate. No exceptions. Adversarial unit tests live next to it.
-->

---

## H-3: no reparse-point escapes

```csharp
var info = new FileInfo(fullPath);
var realTarget = info.ResolveLinkTarget(returnFinalTarget: true);
if (realTarget != null && !IsContained(realTarget.FullName, _root))
    return PathResolution.Denied("reparse-point escape");
```

- `Path.GetFullPath` does **not** resolve junctions / symlinks
- A junction inside `RootPath` → `C:\Windows` would otherwise pass
- Re-check **every parent** of the resolved path
- Symlink creation by the tool itself is forbidden

<!--
SPEAKER NOTES — H-3.
Subtle one. Path.GetFullPath does NOT follow reparse points — it resolves .. and redundant slashes, but a directory junction inside the storage root pointing at C:\Windows passes the prefix check. We use ResolveLinkTarget on the final path AND on every parent directory. Yes, it's expensive on cold paths; we cache. Symlinks created by the agent are forbidden outright — too easy to use as a stash.
-->

---

## H-4: boundary-safe containment

```csharp
static bool IsContained(string path, string root)
{
    root = Path.TrimEndingDirectorySeparator(root);
    return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith(root + Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase);
}
```

- `C:\openclawnet` is a **prefix** of `C:\openclawnet-evil`
- Plain `StartsWith` would silently widen the boundary
- Trailing-separator-or-end check fixes it
- Regression test on the `evil` case ships in the suite

<!--
SPEAKER NOTES — H-4.
String prefix bug that bites every path-handling library at some point. C:\openclawnet vs C:\openclawnet-evil — same prefix, different directory. The fix is to require either equality OR startswith of root+separator. There's a regression test for this exact pair so a future refactor can't reintroduce the bug.
-->

---

## H-5: strict name allowlist

```csharp
private static readonly Regex SafeName =
    new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);

private static readonly HashSet<string> Reserved = new(
    [ "CON", "PRN", "AUX", "NUL",
      "COM1", "COM2", … "COM9",
      "LPT1", "LPT2", … "LPT9" ],
    StringComparer.OrdinalIgnoreCase);
```

- Allowlist `[A-Za-z0-9._-]`, length ≤ 64
- Reject Windows reserved device names (case-insensitive)
- Reject leading dot, trailing dot/space
- Same rule for **agent**, **workspace**, **upload**, **skill** names

<!--
SPEAKER NOTES — H-5.
Goodbye to the old denylist of three substrings. Allowlist beats denylist every time. Sixty-four character cap because Windows MAX_PATH gets ugly past that. Reserved device names — CON, PRN, AUX, NUL, COM1-9, LPT1-9 — are special on Windows and would create a directory you can't delete. Trailing dots and spaces too: Windows silently strips them, so "foo." and "foo" collide in surprising ways.
-->

---

## H-6: per-agent scoping seam

```csharp
public interface ISafePathResolver
{
    PathResolution Resolve(string requested,
                           string? scope = null);  // ← future per-agent root
}
```

- `scope` defaults to `StorageOptions.RootPath`
- Can be set to `agents/{name}/` per request
- **No agent-scoping logic ships in v1** — just the seam
- Avoids an API break later

<!--
SPEAKER NOTES — H-6.
Forward-looking. Today the agent runtime passes scope=null and the resolver uses RootPath. Tomorrow when we ship multi-agent isolation — Slack agent vs Telegram agent vs research agent — the runtime can pass agents/SlackAgent/ and that single agent invocation can ONLY write into its own subtree. Cross-agent leakage becomes impossible. Today: just the parameter. Tomorrow: the policy.
-->

---

## H-7: ACL hardening on credential subdirs

```csharp
// On startup, after EnsureDirectories():
var keysDir = Path.Combine(_root, "dataprotection-keys");

if (OperatingSystem.IsWindows())
    SetExplicitDacl(keysDir, currentUser, FullControl,
                    inheritance: false);
else
    File.SetUnixFileMode(keysDir, UnixFileMode.UserRead | UserWrite | UserExecute);
```

- Verify current user has full control on `RootPath`
- Explicit DACL on `dataprotection-keys/`, `vault/`, `tokens/`
- POSIX: `chmod 0700` on the same
- Refuse to start credential services if ACL check fails

<!--
SPEAKER NOTES — H-7.
ACL hardening for the directories that hold secrets. By default C:\openclawnet inherits from the volume root, which on most Windows installs grants Users(OI)(CI)M — every local user can read it. That's the wrong default for a directory holding ASP.NET DataProtection keys, OAuth tokens, and future API key vaults. We set an explicit DACL on the credential subdirs at startup: current user + SYSTEM, no inheritance. POSIX gets chmod 0700. If the check fails, we refuse to start the credential-bearing services with a clear remediation message.
-->

---

## H-8: audit every write

```jsonc
{
  "type": "FileSystemWriteAudit",
  "agent": "GptAgent",
  "action": "write",
  "path": "C:\\openclawnet\\agents\\GptAgent\\out.md",
  "bytes": 4218,
  "sha256": "9f3c…",
  "source": "llm-suggested",
  "runId": "r-7b4a",
  "timestamp": "2026-05-22T14:03:11Z"
}
```

- **Every successful** write → audit record
- **Every blocked** write → WARN audit with the unresolved input
- Foundation for forensics, billing, retention policies

<!--
SPEAKER NOTES — H-8.
Every write to disk leaves a trace. Successful writes get the resolved path, byte count, SHA-256 of contents, source attribution (was this LLM-suggested or user-explicit), correlation ids. Failed writes — blocked traversal, ACL denied, name allowlist failure — also audited at WARN with the original unresolved input string for forensics. Combined with skill audit (S-9 from Part 3) you can tell exactly what happened during any chat turn.
-->

---

## All eight, side by side

| # | What |
|---|------|
| **H-1** | Storage-root containment, fail-closed |
| **H-2** | One sanitizer / one resolver (`ISafePathResolver`) |
| **H-3** | No reparse-point escapes |
| **H-4** | Boundary-safe containment check |
| **H-5** | Strict name allowlist |
| **H-6** | Per-agent scoping seam |
| **H-7** | Restrictive ACL on root + credential subdirs |
| **H-8** | Audit every write |

> Eight invariants. One resolver. **Fail-closed by design.**

<!--
SPEAKER NOTES — recap.
Eight invariants on one slide. Memorize these — they're the contract any path-taking code must satisfy. Same as the skills S-1..S-12 list, every PR is reviewed against them. We have unit tests covering each one with adversarial cases.
-->

---

## Wiring it up

```csharp
// Program.cs
builder.Services
    .AddOpenClawStorage()         // binds StorageOptions, ensures dirs, ACL
    .AddSafePathResolver()        // ISafePathResolver
    .AddOpenClawTools();          // FileSystemTool uses the resolver

// Anywhere a tool needs a path:
public sealed class MyTool(ISafePathResolver paths) : ITool { … }
```

- One extension method per concern
- DI-injected resolver — no statics, no globals
- Works the same in Gateway, AppHost, MCP servers

<!--
SPEAKER NOTES — wiring.
Three extension methods, in this order. AddOpenClawStorage binds StorageOptions, ensures the directory tree, and runs the ACL hardening. AddSafePathResolver registers the singleton resolver. AddOpenClawTools wires every built-in tool to use the resolver. Custom tools just inject ISafePathResolver and they're done.
-->

---

## Settings UI

- New **"Storage"** card on the `/settings` page
- Shows: current root, source (env / appsettings / default), free space
- "Move root to…" button (writes new path, requires restart)
- Health: ACL status per credential subdir
- Quota meter per top-level subfolder

> Discoverability replaces guessing.

<!--
SPEAKER NOTES — settings UI.
Up until now you had to know about Storage:RootPath in appsettings to even check where files were going. The Settings page now has a Storage card showing the current root, the SOURCE (so you know if it came from env or config), and free space. Move-root requires a restart by design — we don't want to migrate live writes. ACL status surfaces H-7 violations as red dots.
-->

---

## Migration story

- First boot after upgrade: detect old root (`C:\openclawnet\storage\`)
- Offer to move contents to `C:\openclawnet\` (one-click)
- Or keep old path via `Storage:RootPath` override
- Skip migration entirely with `--no-migrate`

> No silent moves. No data loss. **You opt in or you opt out.**

<!--
SPEAKER NOTES — migration.
We dropped the /storage suffix between releases. Existing installs would lose track of their files unless we handle migration explicitly. On first boot we detect the old layout, ask the user, and either move atomically or keep the old root pinned via config. The CLI flag exists for unattended deployments where prompting isn't possible.
-->

---

<!-- _class: lead -->

# 🧠  Part 5 — Memory Roadmap

<!--
SPEAKER NOTES — Part 5 divider.
The forward-looking part. Memory is mostly designed, partly built, and the production-grade version is what Session 4 will pick up. Eight slides on what the problem is, what the strategy is, and what's coming next.
-->

---

## The context window problem

- LLMs have token limits — **8K to 128K** typical
- Every message in history is re-sent on every turn
- Naive truncation = the agent **forgets**
- Cost grows linearly even with local models (latency, GPU)
- 100-message chat at 4K avg = **400K tokens** per turn

> *"Did we already discuss this?" — your agent, every conversation.*

<!--
SPEAKER NOTES — context window problem.
Why memory matters. Even with a 128K context window, every turn re-sends the entire history. After 100 messages your prompts are huge, your latency is high, your GPU is hot, and the model starts losing the middle of the context anyway (the U-shape attention problem). Naive truncation — drop the oldest N — means the agent forgets the user's name. Both are bad.
-->

---

## Summarization strategy

```
┌──────────────── full history ────────────────┐
│                                               │
│  [old]   [old]   [old]   [recent]   [recent]  │
│    │       │       │        │          │     │
│    └───────┴───┬───┘        │          │     │
│         summarize           │          │     │
│            │                │          │     │
│            ▼                ▼          ▼     │
│      [summary]   +   [recent]    [recent]    │
└───────────────────────────────────────────────┘
            │
            ▼
   System prompt: instructions + summary + recent
```

- Recent messages: **verbatim** (last N)
- Older messages: **summarized** into key points
- Very old: **semantic search** on demand

<!--
SPEAKER NOTES — strategy.
Three-tier strategy. Recent messages stay verbatim because the model needs them word-for-word for coherence. Older messages collapse into a paragraph summary. Very old messages are gone from the active prompt entirely but stored in a vector index — the agent can retrieve them by semantic similarity when relevant. This is the standard pattern across every modern agent framework.
-->

---

## `SessionSummary` entity

```csharp
public sealed class SessionSummary
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public string Summary { get; init; } = "";
    public int CoveredMessageCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
```

- One session → many summaries (rolling window as it grows)
- `CoveredMessageCount` tells the composer where to start "recent"
- Cascade-deletes with the parent session

<!--
SPEAKER NOTES — entity.
One small EF Core entity. SessionId is the foreign key, Summary is the actual prose, CoveredMessageCount tells the prompt composer "the first N messages of this session are summarized — start verbatim from message N+1". Each summary is immutable; new summaries are added rather than updated, so we can rebuild any historical view of the conversation.
-->

---

## `IMemoryService` shape

```csharp
public interface IMemoryService
{
    Task<SessionSummary?> GetLatestSummaryAsync(
        Guid sessionId, CancellationToken ct = default);

    Task StoreSummaryAsync(
        SessionSummary summary, CancellationToken ct = default);

    Task<MemoryStats> GetStatsAsync(
        Guid sessionId, CancellationToken ct = default);
}
```

- Backed by `IDbContextFactory<OpenClawDbContext>` (correct async pattern)
- `MemoryStats` exposes total messages, summary count, last-summary time
- Triggered by message-count threshold (default 20)

<!--
SPEAKER NOTES — interface.
Three methods. Get the latest summary so the composer can inject it. Store a new summary when the threshold fires. GetStatsAsync feeds the UI memory panel. The factory pattern is the right one for async services — singleton service, scoped DbContext per call, no thread-safety pitfalls. The 20-message threshold is configurable per session.
-->

---

## Local embeddings — no API calls

- `Elbruno.LocalEmbeddings` — ONNX models, runs in-process
- Embed text → 384-dim vector
- Cosine similarity for nearest-neighbor search
- **No network**, no API key, no data leaves the machine

```csharp
var v1 = await _embeddings.EmbedAsync("dependency injection in .NET");
var v2 = await _embeddings.EmbedAsync("how do I configure IoC?");
var sim = CosineSimilarity(v1, v2);  // ~0.82 — strong match
```

<!--
SPEAKER NOTES — embeddings.
Embeddings are the primitive that powers semantic search. Microsoft offers managed embedding APIs but for local-first development we use Elbruno.LocalEmbeddings which wraps ONNX. 384 dimensions is the all-MiniLM size — tiny, fast, good enough for conversational retrieval. The example shows the win: "dependency injection" and "IoC container" embed to vectors that are 0.82 cosine similar even though they share no surface words.
-->

---

## Semantic search across past sessions

- Every message → embedding → SQLite vector column
- New question → embed → top-K nearest past messages
- Retrieved snippets → injected into the system prompt as context
- Find the conversation about "DI" even if the user typed "IoC"

> The agent **remembers** — without a 1M-token context window.

<!--
SPEAKER NOTES — semantic search.
The eventual third tier. Every message gets embedded once and cached. New question comes in, we embed it, run a cosine-similarity scan over past message embeddings, take the top K, inject those snippets as additional context. SQLite's not a vector DB but at conversation-history scale (thousands of messages) it's perfectly adequate — we use a serialized BLOB column and a hand-rolled top-K. If you grow past that, swap in DuckDB or a real vector store with no API change.
-->

---

## Transparent memory dashboard

```
┌─ Memory ─────────────────────────────────────┐
│ Total messages         : 142                 │
│ Summarized             : 100  (3 summaries)  │
│ Recent (verbatim)      : 42                  │
│ Last summary           : 2 minutes ago       │
│ Estimated prompt tokens: 3.8 K  (was 26 K)   │
└──────────────────────────────────────────────┘
```

- Users **see** what the memory system is doing
- Not a black box — every count auditable
- Backs the `GET /api/memory/{sessionId}/stats` endpoint

<!--
SPEAKER NOTES — dashboard.
Transparency is a feature. Users panic when an AI claims to "remember" things — they want to know how. The dashboard shows total messages, how many are summarized (and into how many summaries), how many are still verbatim, when the last summary fired, and the token impact. The "was 26 K" number is the most powerful — it shows the savings without summarization in concrete terms.
-->

---

## What's coming next (Session 4 preview)

- ✅ Designed: `SessionSummary`, `IMemoryService`, `IEmbeddingsService`
- 🚧 Building: production-grade summarizer with model retry policy
- 🚧 Building: vector index over past messages (SQLite blob → DuckDB)
- 🔜 Coming: per-agent memory namespaces + retention policies
- 🔜 Coming: memory **export / import** for portability

> **Session 4** = cloud + scheduling + memory at scale.

<!--
SPEAKER NOTES — what's coming.
Where we are: the design is done, the entity exists, the interfaces are stable. What's left: a robust summarizer that handles model failures gracefully, a real vector index, per-agent memory isolation, and import-export. All of that lands in Session 4 along with cloud providers and the scheduler. By the end of Session 4 the agent will remember across restarts and across sessions.
-->

---

<!-- _class: lead -->

# 🧪  Part 6 — Console Demos

<!--
SPEAKER NOTES — Part 6 divider.
Three demos, eight minutes total. We'll start the stack, hit the skills API, and drop a hand-authored skill in to prove hot-reload.
-->

---

## Demo 1 — `aspire start` walkthrough

```pwsh
cd C:\src\openclawnet
aspire run

# Expected:
# 🟢 OpenClawNet.AppHost (3 of 3 running)
#    ├── 🟢 gateway       https://localhost:7234
#    ├── 🟢 ollama        http://localhost:11434
#    └── 🟢 dashboard     https://localhost:17000
```

- Watch the Aspire Monitor tray icon turn green
- Open the dashboard → confirm `Storage:RootPath` log line
- INFO log: `Storage root resolved to C:\openclawnet (source: default)`

<!--
SPEAKER NOTES — demo 1.
Live demo. aspire run on the repo, watch Aspire Monitor in the tray go green as resources come up. Open the dashboard, scroll the log to find the storage line we shipped this session — "Storage root resolved to C:\openclawnet (source: default)". Source is the value the hardening review asked for. If env var is set, it shows "source: env". If appsettings, "source: config". Visible at a glance.
-->

---

## Demo 2 — `curl /api/skills`

```pwsh
# List skills the agent actually uses
curl https://localhost:7234/api/skills | jq

# Output:
# [
#   { "name": "memory",        "layer": "system", "enabledFor": ["GptAgent"] },
#   { "name": "doc-processor", "layer": "system", "enabledFor": ["GptAgent"] }
# ]

# Toggle one for a specific agent
curl -X POST https://localhost:7234/api/agents/GptAgent/skills/memory/disable
```

- API and agent share the **same** registry — no drift
- Disable takes effect on the **next** chat turn (S-10)

<!--
SPEAKER NOTES — demo 2.
Curl the skills endpoint. Two skills, both system layer, both enabled for GptAgent. Note the per-agent shape: enabledFor is an array, not a global boolean. POST disable, then send a chat message — the agent responds without the memory skill in its prompt. We verify by checking the prompt audit. This is the unified API the headline-bug slide promised.
-->

---

## Demo 3 — manual skill drop-in

```pwsh
# 1. Author a skill in your editor
code C:\openclawnet\skills\agents\GptAgent\skills\concise-tone\SKILL.md

# 2. File contents:
@"
---
name: concise-tone
description: Short, friendly responses
---

Keep responses under 3 sentences. No buzzwords.
"@ > SKILL.md

# 3. Watch the gateway log:
# [INFO] SkillDiscovered  name=concise-tone layer=agents:GptAgent
# [INFO] SkillLoaded      name=concise-tone duration=12ms

# 4. Send a chat — agent is now concise.
```

<!--
SPEAKER NOTES — demo 3.
The "wow" demo. Author a skill in real time. Save the file. Watch the gateway log emit SkillDiscovered and SkillLoaded — that's the FileSystemWatcher and the registry rebuild firing. Send a chat message in the UI and the response is suddenly two sentences and lacks "leverage". No restart. No deploy. No code. That's the whole pitch in 60 seconds.
-->

---

<!-- _class: lead -->

# 🎯  Closing

<!--
SPEAKER NOTES — closing divider.
Two slides plus a question slide.
-->

---

## Key insights

1. 🧠 **Skills are markdown** — anyone can author, no code, no restart
2. 🛡️ **Storage is fail-closed** — eight invariants, one resolver, no silent rewrites
3. 📈 **Memory is transparent** — users see what's summarized
4. 🔧 **Tool-calling is portable** — same agent, four providers
5. 👀 **Ops is glanceable** — Ollama + Aspire monitors in the tray

> *"Tools are arms. Skills are personality. Storage is the floor."*

<!--
SPEAKER NOTES — insights.
Five takeaways. Skills are content not code — that's the most user-visible win. Storage is fail-closed — that's the safety win. Memory is transparent — that's the trust win. Tool-calling portability is the engineering win. Tray monitors are the dev-experience win. Each item maps to one of the six parts of the session.
-->

---

## What we built today ✅

- ✅ Two NuGet tray apps — Ollama Monitor + Aspire Monitor
- ✅ OpenAI-aligned tool-calling format
- ✅ FileSystemTool refactor (5 files, ~120 LOC each)
- ✅ Three reusable sanitizers (path / URL / JSON)
- ✅ Single `ISkillsRegistry` (deleted parallel `FileSkillLoader`)
- ✅ 3-layer storage with `FileSystemWatcher` hot-reload
- ✅ Per-agent enablement via `enabled.json` (fail-closed)
- ✅ `Skills.razor` UI + assignment modal + import wizard
- ✅ `C:\openclawnet\` default + `OPENCLAWNET_STORAGE_ROOT` env var
- ✅ `ISafePathResolver` enforcing **H-1..H-8**
- ✅ ACL hardening on credential subdirs
- ✅ Memory roadmap: `SessionSummary`, local embeddings, semantic search

<!--
SPEAKER NOTES — what we built.
Twelve checkmarks. Half are user-visible (monitors, skills page, settings card). Half are under-the-hood quality (refactors, sanitizers, hardening). The two halves go together: the user-visible features are only safe BECAUSE the under-the-hood work landed first. That's the lesson of session 3.
-->

---

## Session 4 preview

- ☁️ **Cloud providers** — Azure OpenAI, Foundry at scale
- ⏰ **Job scheduling** — cron expressions, durable jobs
- 🧠 **Memory at scale** — vector index, retention policies
- 🩺 **Health checks + tests** — production hardening
- 🎬 **Series finale** — full platform demo

> Today: an agent with personality, boundaries, and a memory plan.
> Next: an agent that runs while you sleep.

<!--
SPEAKER NOTES — Session 4 preview.
Where we go next. Cloud providers means the same agent runs against Azure OpenAI without code changes — the tool-calling alignment work in Part 2 is what makes that possible. Scheduling means cron-driven jobs that the agent runs autonomously. Memory at scale finishes the work we sketched in Part 5. Tests + health checks turn the demo into a deployment. Session 4 is the finale.
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
`docs/sessions/session-3/` for everything from today

<!--
SPEAKER NOTES — closing.
Thanks everyone. Repo is github.com/elbruno/openclawnet, MIT licensed. Everything from today — slides, speaker script, copilot prompts, the proposal documents — lives under docs/sessions/session-3/. The two new tools install with one dotnet tool install -g command and live in your tray. If you want to extend something, the manual skill drop-in is the most rewarding starting point: write a SKILL.md, save it, see the agent change. Questions?
-->
