# OpenClaw .NET Components

## Agent Runtime

The agent runtime (`OpenClawNet.Agent`) orchestrates the complete AI interaction loop:

1. **Bootstrap Loading** — Loads workspace files (AGENTS.md, SOUL.md, USER.md) at session start
2. **Prompt Composition** (`IPromptComposer`) — Assembles system prompt + workspace context + skills + memory + history
3. **Model Invocation** (`IAgentProvider`) — Sends to the configured LLM provider (via `RuntimeAgentProvider` routing)
4. **Tool Loop** (`IToolExecutor`) — Parses tool calls, executes them, feeds results back
5. **Response** — Final assistant response after all tool iterations
6. **Context Compaction** (`ISummaryService`) — Condenses long conversations (>20 messages)

Max tool iterations: 10 (prevents infinite loops).

**Internal Design:** The agent runtime uses an internal `IAgentRuntime` abstraction (with `DefaultAgentRuntime` implementation) that separates the public `IAgentOrchestrator` boundary from the implementation details. The implementation uses **Microsoft Agent Framework** (`Microsoft.Agents.AI`) as the execution engine.

### Agent Framework Components

#### `ModelClientChatClientAdapter`

Bridges the OpenClawNet `IModelClient` interface to the Agent Framework `IChatClient` interface. This adapter allows any local or cloud model provider (Ollama, Foundry Local, Azure OpenAI, GitHub Copilot) to be used by the Agent Framework without requiring native Agent Framework SDK support.

```csharp
// IModelClient → IChatClient bridge
public class ModelClientChatClientAdapter : IChatClient
{
    // Wraps IModelClient.StreamAsync() as IChatClient.GetStreamingResponseAsync()
}
```

#### `ChatClientAgent`

The Agent Framework `AIAgent` implementation. Wraps any `IChatClient` and a list of `IAIContextProvider` instances (such as `AgentSkillsProvider`). On each invocation, context providers can enrich the messages before the model call — enabling progressive skill disclosure without coupling the runtime to any specific skills format.

#### `AgentSkillsProvider`

Implements the Agent Skills specification (agentskills.io) as an `IAIContextProvider`. Before each model call, it reads the skills directory and advertises available skills to the model by prepending skill summaries to the messages. Skills are loaded progressively: advertised first, full content loaded only when the agent selects a skill.

Skills are organized as subdirectories, each containing a `SKILL.md` file:

```
skills/
├── file-system/
│   └── SKILL.md          ← skill specification (name, description, tools)
├── web-search/
│   └── SKILL.md
└── dotnet-code-style/
    └── SKILL.md
```

#### `ToolAIFunction`

Wraps an `ITool` instance as an Agent Framework `AIFunction`, making OpenClawNet tools available to the Agent Framework's tool-calling pipeline. Each tool's input/output schema is derived from the `ITool` definition at registration time.

#### `DefaultAgentRuntime` Orchestration

The runtime follows a two-phase execution strategy:

1. **First call** — via `ChatClientAgent.RunStreamingAsync()`: skill advertisements are injected by `AgentSkillsProvider.InvokingAsync()`, giving the model full context about available skills. The model sees what it can do.
2. **Subsequent tool iterations** — via `ModelClientChatClientAdapter.GetStreamingResponseAsync()` directly: the conversation history already contains the skill context from the first call, so subsequent calls skip skill re-injection for efficiency.

This approach ensures the model receives skill context exactly once per turn, avoiding redundant prompt overhead on tool-call follow-up iterations.

---

## Workspace & Bootstrap Files

Every agent session is anchored to a **workspace** — a directory that contains optional bootstrap files defining the agent's identity and context.

| File | Purpose | Injected Into |
|------|---------|---------------|
| `AGENTS.md` | Agent behavior, persona, instructions | System prompt (top section) |
| `SOUL.md` | Agent values, principles, ethical constraints | System prompt (values section) |
| `USER.md` | User preferences, profile, personalization data | System prompt (user context section) |

**Loading rules:**

- Bootstrap files are optional; missing files are silently skipped
- All three are loaded at session initialization before the first prompt composition
- Workspace path is set per-session (defaults to the current working directory)
- Changes to bootstrap files take effect on the next session — not mid-session

**Example — `AGENTS.md`:**

```markdown
# Agent Persona

You are a helpful assistant specialized in .NET development.
You prefer concise answers and always include runnable code examples.
When asked about architecture, reference the OpenClaw pillar model.
```

**Example — `SOUL.md`:**

```markdown
# Core Values

- Honesty: Never fabricate information. Say "I don't know" when uncertain.
- Safety: Always warn before running shell commands that modify the system.
- Privacy: Do not store or repeat sensitive data shared in conversation.
```

**Example — `USER.md`:**

```markdown
# User Profile

Name: Bruno
Preferred language: C#
Experience level: Advanced
Topics of interest: AI agents, .NET 10, distributed systems
```

---

## Sessions

### Main Sessions

Standard sessions maintain full conversation history with automatic context compaction. Each session has:

- Unique session ID
- Associated workspace path
- Full message history (with older messages compacted into summaries)
- Tool call audit log
- Creation timestamp and metadata

### Isolated Sessions

**Isolated sessions** are sandboxed sessions with **no cross-contamination** between sessions. Use cases:

- Running multiple concurrent agents without shared state
- Automated jobs that must not inherit conversation history from other sessions
- Testing and evaluation scenarios requiring clean state

```csharp
var session = await sessionManager.CreateIsolatedSessionAsync(new IsolatedSessionOptions
{
    WorkspacePath = "/path/to/workspace",
    TimeoutMinutes = 30,
    InheritSkills = false,   // isolated from global skill state
    InheritMemory = false    // clean context, no prior history
});
```

Isolated sessions are automatically cleaned up when their timeout expires or the task completes.

---

## Context Compaction

Long conversations accumulate tokens that exceed model context windows and increase latency. OpenClawNet uses a two-level compaction strategy:

### Level 1 — Summarization (automatic)

When a session exceeds **20 messages**, `ISummaryService` automatically:

1. Takes the oldest N messages (batch)
2. Sends them to the model with a summarization prompt
3. Stores the resulting summary as a `SessionSummary` entity
4. Removes the original messages from the active context window
5. On next prompt composition, the summary is prepended to the context

```
Active context at turn 30:
  [Summary: "User is building a .NET 10 agent platform. Discussed..."]
  [Message 21 through 30 — full fidelity]
  [User message 31]
```

### Level 2 — Context compression (future)

For very long sessions, multi-level compression will be supported:

- Summary of summaries (hierarchical compression)
- Embedding-indexed retrieval of relevant past context
- Selective context injection based on semantic similarity to current turn

**Configuration:**

```json
{
  "Memory": {
    "SummarizationThreshold": 20,
    "SummaryBatchSize": 10,
    "MaxSummariesBeforeArchive": 5
  }
}
```

---

## Model Providers

All providers implement `IAgentProvider` (the primary abstraction). `IModelClient` still exists as a lower-level interface but `IAgentProvider` is used for registration, routing, and agent profile resolution.

| Provider | Package | Class | Use Case |
|----------|---------|-------|---------|
| **Ollama** | `OpenClawNet.Models.Ollama` | `OllamaAgentProvider` | Local REST API at `http://localhost:11434` |
| **Azure OpenAI** | `OpenClawNet.Models.AzureOpenAI` | `AzureOpenAIAgentProvider` | Via `Azure.AI.OpenAI` SDK |
| **Foundry** | `OpenClawNet.Models.Foundry` | `FoundryAgentProvider` | OpenAI-compatible cloud endpoint |
| **FoundryLocal** | `OpenClawNet.Models.FoundryLocal` | `FoundryLocalAgentProvider` | Foundry running on-device (local inference) |
| **GitHub Copilot** | `OpenClawNet.Models.GitHubCopilot` | `GitHubCopilotAgentProvider` | GitHub Copilot via `CopilotChatClient` |
| **RuntimeAgentProvider** | `OpenClawNet.Gateway` | `RuntimeAgentProvider` | Router — resolves and delegates to the active provider |

### Multi-Instance Providers

Each provider type supports **multiple named configurations** via `ModelProviderDefinition` entities in SQLite. For example, you can configure separate Azure OpenAI instances for chat vs. embeddings, or multiple Ollama endpoints. Managed via the **Model Providers** settings page or the REST API.

### Agent Profiles

`AgentProfile` entities define named agent configurations with instructions, a provider reference, and tool filtering. When a session or scheduled job specifies an `AgentProfileName`, the runtime resolves the profile and applies its settings. Managed via the **Agent Profiles** settings page, or imported from Markdown with YAML front-matter using `AgentProfileMarkdownParser`.

Each profile has a `Kind` (`ProfileKind` enum) that controls where it can be selected:

| Kind | Visible in Chat picker | Visible in Job picker | Used by |
|------|------------------------|-----------------------|---------|
| `Standard` | ✅ | ✅ | Day-to-day chat sessions, channels, scheduled jobs. Only Standard profiles can be marked **default**. |
| `System` | ❌ | ❌ | Internal platform tasks — e.g. natural-language → cron expression translation in `SchedulerHelpersEndpoints`. Resolved automatically; never picked by the user. |
| `ToolTester` | ❌ | ❌ | Invoked from the Tools page Agent-Probe button to convert a natural-language request into JSON arguments matching a tool's `ParameterSchema`. Recommended provider: a powerful model such as Azure OpenAI. |

The Agent Profiles page surfaces a one-click **suggestion banner** when no `System` or `ToolTester` profile exists, pre-filling sensible defaults so the operator can opt in without writing front-matter by hand.

### Tool Test Surface

The `Tools` page (`/tools` in the Web UI) lists every registered `ITool` and exposes a **Test** button per tool. Two test modes are supported:

- **Direct Invoke** — the operator supplies a JSON arguments object; the gateway calls `ITool.ExecuteAsync` directly. No LLM is involved. Useful for verifying that a tool works in isolation and for sanity-checking schema changes.
- **Agent Probe** — the configured `ToolTester` profile receives the tool name, description, and `ParameterSchema`, plus a natural-language prompt. It returns a JSON arguments object which is then passed to the tool. This validates that the tool's schema is interpretable by an LLM (a common cause of agent failures in chat).

Results are persisted in the `ToolTestRecords` table (last test timestamp, success flag, message, mode) and surfaced as a status pill next to each tool. See [`design/tool-test-design.md`](../design/tool-test-design.md) for the rationale.

### Fallback Chain

The model layer supports a **fallback chain** — if the primary provider is unavailable or returns an error, the system automatically retries with the next configured provider:

```json
{
  "Model": {
    "Primary": "ollama",
    "Fallbacks": ["foundrylocal", "azure-openai"]
  }
}
```

This provides graceful degradation: local-first with cloud backup, or multi-cloud resilience.

Provider switching is configured in `appsettings.json` and can also be changed at runtime via the settings API (`RuntimeAgentProvider`).

---

## Tool Framework

Tools implement `ITool` and register with `IToolRegistry`. The executor manages the tool-calling loop including approval gates and iteration limits.

### First-Class Tools

| Tool | Class | Category | Approval Required |
|------|-------|----------|-------------------|
| `file_system` | `FileSystemTool` | filesystem | No |
| `shell` | `ShellTool` | shell | Yes |
| `web_fetch` | `WebTool` | web | No |
| `schedule` | `SchedulerTool` | scheduler | No |
| `browser` | `BrowserTool` | browser | No |

**Future tools** (planned, matching original OpenClaw spec):
- `canvas` — visual canvas rendering
- `nodes` — remote node interaction (camera, location, screen)
- `sessions` — agent-to-session control (create/terminate isolated sessions)
- `channel_action` — send messages back through channel adapters

### Safety Features

- **Path traversal protection** (FileSystemTool) — blocks `../` escapes
- **Blocked command list** (ShellTool) — prevents dangerous commands
- **SSRF protection** (WebTool) — blocks internal network addresses
- **Tool approval policy** — `IToolApprovalPolicy` interface for custom gates
- **Max iteration limit** — 10 iterations per agent turn

### Browser Tool

`BrowserTool` (backed by Playwright) provides headless browser automation:

- Navigate to URLs and extract content
- Take screenshots for visual inspection
- Execute JavaScript in page context
- Fill forms and interact with web UIs
- Returns structured content for model consumption

---

## Skills System

Skills are markdown files that inject reusable instructions into the agent's system prompt. OpenClawNet supports two skill formats, loaded by `FileSkillLoader` with Awesome-Copilot subdirectory support:

- **Legacy skills** — a single `SKILL.md` or `.md` file with YAML frontmatter (used for simple instruction injection)
- **Agent Framework skills** — a subdirectory with a `SKILL.md` file following the agentskills.io specification (used with `AgentSkillsProvider` for progressive disclosure)

### File Format — Legacy Skill

```markdown
---
name: dotnet-code-style
description: Enforces .NET coding conventions
enabled: true
category: development
---

# .NET Code Style

Always use `var` for local variable declarations when the type is obvious.
Use `async/await` instead of `.Result` or `.Wait()`.
Prefer `IEnumerable<T>` for read-only sequences.
```

### File Format — Agent Framework Skill (`SKILL.md`)

```markdown
---
name: file-system
description: Read and write files in the workspace
version: 1.0.0
tools:
  - file_read
  - file_write
  - file_list
---

# File System Skill

Enables the agent to read, write, and list files within the current workspace.
Use this skill when the user asks about files, code, or project structure.

## Usage

- `file_read(path)` — read file content
- `file_write(path, content)` — write or overwrite a file
- `file_list(directory)` — list files and directories
```

### Loading Precedence

Skills are loaded from three sources with a **defined precedence** (later wins):

```
1. Bundle (built-in)   →  skills/built-in/     (shipped with OpenClawNet)
2. Local machine       →  ~/.openclaw/skills/   (user-installed skills)
3. Workspace           →  {workspace}/skills/   (project-specific overrides)
```

A workspace skill with the same `name` as a bundle skill **overrides** the bundle version at runtime. This allows per-project customization without modifying the core installation.

### Runtime Enable/Disable

Skills can be toggled at runtime without restart:

```http
PATCH /api/skills/{name}
{ "enabled": false }
```

This is reflected immediately on the next prompt composition.

---

## Channels

Channels are persistent connections between the Gateway and external messaging platforms. The Gateway manages channel lifecycle and routes messages bidirectionally.

### IChannel Abstraction

```csharp
public interface IChannel
{
    string ChannelId { get; }
    string DisplayName { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SendMessageAsync(string sessionId, string text, CancellationToken ct);
    IAsyncEnumerable<ChannelMessage> ReceiveAsync(CancellationToken ct);
}
```

### Implemented Channels

| Channel | Adapter Project | Status |
|---------|----------------|--------|
| WebChat | Built-in (Blazor + HTTP SSE/NDJSON) | ✅ Implemented |
| Microsoft Teams | `OpenClawNet.Adapters.Teams` | ✅ Implemented |
| WhatsApp | — | 🔜 Planned |
| Telegram | — | 🔜 Planned |
| Slack | — | 🔜 Planned |
| Discord | — | 🔜 Planned |

### Teams Adapter

The Teams adapter (`OpenClawNet.Adapters.Teams`) integrates the Azure Bot Framework with the OpenClawNet channel model:

- Registers a bot activity handler
- Maps Teams conversation turns to OpenClawNet `ChannelMessage`
- Routes agent responses back through the Bot Framework connector
- Handles proactive messaging for scheduled job notifications

---

## Toolbar Status Component

The **Toolbar Status** component (`ToolbarStatus.razor`) provides real-time status visibility across both Control UI and WebChat surfaces. It displays:

### Left Section (Status Information)

| Item | Icon | Purpose |
|------|------|---------|
| **Provider + Model** | 🦙 (Ollama), ☁️ (Azure/Foundry), 🖥️ (Local) | Current active AI provider and model name. Clicking switches providers at runtime. |
| **Agent Profile** | 🧑‍💼 | Currently active agent profile (if set). Loaded from workspace AGENTS.md context. |
| **Active Jobs Badge** | ⚡ | Running job count. Clicking navigates to `/jobs` page for job monitoring. |

### Right Section (System Health)

| Item | Icon | Purpose |
|------|------|---------|
| **Aspire Dashboard Link** | 📊 | Links to local Aspire Dashboard (if available) for service observability. |
| **Documentation Link** | ℹ️ | Links to ASP.NET Core documentation. |
| **Connection Indicator** | 🟢/🔴 | Green dot = Gateway connected. Red dot = Gateway disconnected. Polls every 15 seconds. |

### Auto-Refresh Behavior

- **Provider/Model/Profile**: Updated on component load; reflects runtime model switching
- **Job Count**: Polled every 30 seconds from `GET /api/jobs`
- **Connection Status**: Polled every 15 seconds from `GET /health`

The toolbar persists across page navigation and is visible in the app shell layout.

---

## Nodes

Nodes are remote agent endpoints — typically mobile or desktop applications — that extend the agent's reach with device-native capabilities.

### Node Capabilities (Planned)

| Capability | Description |
|-----------|-------------|
| **Camera** | Capture images from device camera; agent can analyze them |
| **Location** | GPS/geolocation context injected into sessions |
| **Screen recording** | Record and share screen content with the agent |
| **Push notifications** | Agent-initiated proactive messages to the device |
| **Local file access** | Agent can request files from the node's filesystem |

### Node Registration

Nodes register with the Gateway via a handshake protocol, advertising their capabilities. The Gateway routes tool calls to the appropriate node when the agent requests a node capability.

```
Agent: "Take a photo of the whiteboard"
  → Gateway routes to registered camera-capable node
  → Node captures image, returns base64 to Gateway
  → Agent receives image content in tool result
```

Nodes are modeled as a specialized type of `IChannel` — they receive messages and return structured responses.

---

## Memory and Embeddings

### Memory Service

`IMemoryService` tracks sessions and conversation state:

- Load conversation history by session ID
- Store chat messages with metadata
- Query past conversations by timestamp or session
- Support for isolated session scoping

### Summarization Service

`ISummaryService` keeps long contexts manageable:

- Summarize conversations >20 messages automatically
- Keep recent messages in full context
- Compress older messages into semantic summaries
- Improves token efficiency and model focus

### Embeddings Service

`IEmbeddingsService` provides semantic search:

- **Default:** Ollama embeddings endpoint (local, free, no API keys)
- Converts text to vector embeddings for similarity search
- Supports batch embedding operations
- Cosine similarity calculation for finding relevant past conversations
- Future: Swappable implementations (Azure, Foundry embeddings)

**Local-First Advantage:** Embeddings stay local; no external API needed for semantic search.

---

## Automation

### Cron Scheduler (External Service)

The scheduler runs as a standalone Aspire-managed service (`OpenClawNet.Services.Scheduler`) with its own Blazor Server dashboard. It polls the shared SQLite database for due jobs and executes them via the Gateway's chat API.

**Architecture:**
- `SchedulerPollingService` — `BackgroundService` that polls on a configurable interval
- `SchedulerSettingsService` — thread-safe singleton, persists settings to `scheduler-settings.json`
- `SchedulerRunState` — lightweight singleton tracking active job count (shown in dashboard)
- Concurrent execution via `SemaphoreSlim(MaxConcurrentJobs)` — each job gets its own `IServiceScope` + `DbContext`
- Per-job timeout via linked `CancellationTokenSource.CancelAfter(JobTimeoutSeconds)`

**Configurable at runtime** (via Settings UI or Scheduler dashboard):

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| `MaxConcurrentJobs` | 3 | 1–20 | Parallel job execution cap |
| `JobTimeoutSeconds` | 300 | 10–3600 | Force-cancel threshold per job |
| `PollIntervalSeconds` | 30 | 5–3600 | How often to check for due jobs |

**Blazor Dashboard** — served at `https://scheduler/` (Aspire service discovery URL):
- **Jobs** (`/`) — live job list, status badges, Trigger / Cancel actions, 5-second auto-refresh
- **Job Detail** (`/jobs/{id}`) — run history table with start/end times, duration, result/error
- **Settings** (`/settings`) — form to adjust all three execution-limit settings live

The main web app (`OpenClawNet.Web`) includes a Scheduler settings card on its own Settings page, mirroring the same `GET/PUT /api/scheduler/settings` API.

### Webhook Triggers

`WebhookEndpoints` expose HTTP endpoints that external systems can POST to in order to trigger agent sessions:

- **GitHub events** — issue created, PR opened, push, etc.
- **Calendar events** — meeting reminders, scheduled notifications
- **Custom webhooks** — any HTTP-capable external system

Each webhook trigger creates a new agent session (or isolated session) and runs the configured agent task.

---

## Storage

SQLite via EF Core with these entities:

| Entity | Purpose |
|--------|---------|
| `ChatSession` | Sessions with metadata, workspace path, and optional `AgentProfileName` |
| `ChatMessageEntity` | Messages with role, order index, and session reference |
| `SessionSummary` | Conversation summaries for compacted context |
| `ToolCallRecord` | Tool execution audit log |
| `ScheduledJob` | Job definitions with cron expression, status, and `AgentProfileName` |
| `JobRun` | Job execution history with outcome |
| `ModelProviderDefinition` | Named provider configurations (multiple instances per provider type) |
| `AgentProfileEntity` | Named agent definitions with instructions, provider reference, tool filtering |
| `ProviderSetting` | Runtime key-value settings (including active model) |
