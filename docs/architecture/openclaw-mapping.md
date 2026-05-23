# OpenClaw to OpenClaw .NET: Architecture Mapping

## Overview

OpenClaw is a free, open-source agent platform that defines a modular architecture for building AI agents with tool calling, memory, scheduling, and extensible skills. **OpenClaw .NET** is the .NET 10 implementation of this architecture, preserving the core concepts while using modern .NET patterns and tooling.

---

## Core Concepts Mapping

| OpenClaw Concept | OpenClawNet Implementation | Notes |
|---|---|---|
| **Agent Runtime** | `IAgentOrchestrator` / `IAgentRuntime` | Prompt composition, tool loop, response generation |
| **Model Provider** | `IAgentProvider` + providers (Ollama, FoundryLocal, AzureOpenAI, Foundry, GitHubCopilot) + `RuntimeAgentProvider` (router) | Pluggable abstraction for swapping LLM backends. Multi-instance `ModelProviderDefinition` configs |
| **Fallback Chain** | `RuntimeAgentProvider` + fallback config | Primary → Fallback[0] → Fallback[1] — automatic provider failover |
| **Tool System** | `ITool` / `IToolRegistry` / `IToolExecutor` | File, shell, web, browser, and scheduled tools with safety guards |
| **Skills** | Markdown + YAML frontmatter loaded from `skills/` | Behavior customization injected into system prompt |
| **Skills Precedence** | workspace > local > bundle | Workspace-level skills override bundle defaults at runtime |
| **Memory** | `IMemoryService` + `ISummaryService` + `IEmbeddingsService` | Session persistence, conversation summarization, semantic search |
| **Persistence** | SQLite via EF Core | ChatSession, ChatMessage, SessionSummary, ToolCallRecord, ScheduledJob, ModelProviderDefinition, AgentProfile |
| **Job Scheduler** | `JobSchedulerService` BackgroundService | Recurring tasks with cron syntax; isolated session per job |
| **Webhooks** | `WebhookEndpoints` | HTTP-triggered agent sessions; GitHub, calendar, custom sources |
| **Web Interface** | Blazor Web App + HTTP SSE/NDJSON | Real-time chat streaming and status updates |
| **Control UI** | Blazor management dashboard (in `OpenClawNet.Web`) | Sessions, tools, skills, jobs, memory, channels — operator surface |
| **WebChat** | Blazor chat component (in `OpenClawNet.Web`) | End-user chat UI served from the same Blazor app |
| **API Gateway / Control Plane** | ASP.NET Core Minimal APIs (Gateway) | Persistent process — REST APIs, HTTP streaming (SSE), webhook endpoints, channel manager, cron |
| **Workspace** | `WorkspaceLoader` + session workspace path | Directory-level context loaded at session start |
| **Bootstrap Files** | `AGENTS.md`, `SOUL.md`, `USER.md` | Agent persona, values, and user profile injected into system prompt |
| **Isolated Sessions** | `IsolatedSessionOptions` + session manager | Sandboxed sessions with no cross-contamination; used by jobs/webhooks |
| **Context Compaction** | `ISummaryService` auto-summarization | Messages >20 → summarized and compressed; sliding context window |
| **Channels** | `IChannel` abstraction + `OpenClawNet.Adapters.Teams` | Teams implemented; WhatsApp/Telegram/Slack/Discord planned |
| **Nodes** | Planned: remote device endpoints | Mobile/desktop agents with camera, location, screen record capabilities |
| **Browser Tool** | `BrowserTool` (Playwright) | Headless browser for web automation and content extraction |

---

## Architecture Layers

### 1. **Orchestration Layer** (AppHost)

- **Aspire** orchestrates the entire system
- Service discovery and health checks
- Aspire Dashboard for local observability
- Manages startup order and dependencies

### 2. **Gateway Layer** — Control Plane

- Persistent process; not a stateless proxy
- REST API for chat, sessions, tools, skills, jobs, memory, channels
- HTTP Streaming (SSE/NDJSON) for real-time token delivery and channel event push via `POST /api/chat/stream`
- **Channel Manager** — maintains persistent channel connections
- **Webhook Endpoints** — receive external events and trigger agent sessions
- **Cron Scheduler** — persistent job scheduling backed by SQLite
- Serves both **Control UI** (management) and **WebChat** (end-user) surfaces
- Request/response contracts via `OpenClawNet.Models.Abstractions`

### 3. **Agent Layer** (Agent + Runtime)

- **IAgentOrchestrator**: Public boundary, stable contract
- **IAgentRuntime**: Internal runtime (extensible for future Agent Framework integration)
- **WorkspaceLoader**: Loads AGENTS.md, SOUL.md, USER.md at session start
- **IPromptComposer**: System prompt assembly with workspace context, skills, history, summary
- **IToolExecutor**: Tool calling loop with iteration limits and approval gating
- **Tool definitions** built from registry manifest

### 4. **Provider Layer** (Models)

- **IAgentProvider**: Primary model provider abstraction
- **IModelClient**: Lower-level model interface (still present, bridged by adapter)
- **OllamaAgentProvider**: Local REST API calls to Ollama
- **FoundryLocalAgentProvider**: On-device Foundry inference
- **AzureOpenAIAgentProvider**: Azure OpenAI SDK integration
- **FoundryAgentProvider**: Foundry-compatible OpenAI endpoint
- **GitHubCopilotAgentProvider**: GitHub Copilot via `CopilotChatClient`
- **RuntimeAgentProvider**: Router — resolves active provider from `ModelProviderDefinition` settings
- **ModelProviderDefinition**: Multi-instance named provider configurations in SQLite
- **AgentProfile**: Named agent definitions with instructions, provider reference, tool filtering

### 5. **Tool Layer** (Tools)

- **ITool** interface for pluggable tools
- **FileSystemTool**: Safe file read/write/list
- **ShellTool**: Command execution with approval policy
- **WebTool**: HTTP fetch with SSRF protection
- **SchedulerTool**: Job creation and management
- **BrowserTool**: Headless browser via Playwright

### 6. **Memory Layer** (Memory + Embeddings)

- **IMemoryService**: Session and conversation management
- **ISummaryService**: Long-context summarization and compaction
- **IEmbeddingsService**: Local embeddings via Ollama
- Supports semantic search via cosine similarity

### 7. **Persistence Layer** (Storage)

- EF Core DbContext
- SQLite by default (easily swappable)
- Entities: ChatSession, ChatMessage, SessionSummary, ToolCallRecord, ScheduledJob, JobRun, ModelProviderDefinition, AgentProfileEntity, ProviderSetting

### 8. **UI Layer** (Blazor Web App)

- **Control UI**: Management dashboard — sessions, tools, skills, jobs, memory, channels, settings (Model Providers, Agent Profiles, General)
- **WebChat**: End-user chat with real-time streaming via HTTP SSE/NDJSON (`POST /api/chat/stream`)
- Both served from `OpenClawNet.Web` (same Blazor app, different routes/components)

### 9. **Channel Layer** (Adapters)

- **IChannel**: Abstraction for bidirectional messaging
- **Teams Adapter** (`OpenClawNet.Adapters.Teams`): Azure Bot Framework integration
- Future: WhatsApp, Telegram, Slack, Discord adapters

---

## Data Flow Example: Chat Request

```
Blazor UI (POST /api/chat)
    ↓
Gateway (MapChatEndpoints)
    ↓
IAgentOrchestrator.ProcessAsync()
    ├── Store user message
    ├── Load history
    ├── Check compaction need (>20 messages)
    ↓
WorkspaceLoader (bootstrap files already in session context)
    ↓
IPromptComposer.ComposeAsync()
    ├── AGENTS.md + SOUL.md + USER.md context
    ├── Active skills
    ├── Session summary
    └── Conversation history
    ↓
RuntimeAgentProvider → IAgentProvider.CompleteAsync() or StreamAsync()
    ├── If response has tool_calls → Tool Loop
    │   ├── IToolExecutor.ExecuteAsync()
    │   ├── Call model again (max 10x)
    │   └── Return tool results
    └── If no tools → Final response
    ↓
Store assistant message
    ↓
Return to UI via HTTP streaming (SSE/NDJSON)
```

---

## Data Flow Example: Channel Message (Teams)

```
Teams user sends message
    ↓
Teams Adapter (Bot Activity Handler)
    ↓
Gateway ChannelManager.RouteMessageAsync()
    ├── Lookup/create session for (ChannelId + UserId)
    └── Forward to IAgentOrchestrator
    ↓
Agent processes (standard flow above)
    ↓
ChannelManager.SendResponseAsync()
    ↓
Teams Adapter sends reply via Bot Framework Connector
    ↓
Message delivered in Teams
```

---

## Key Design Decisions

### 1. **Interface-Driven Everything**

Following OpenClaw's principle: every major component is behind an interface. This enables:

- Swapping providers (Ollama → FoundryLocal → Azure → Foundry) without code changes
- Testing with mocks
- Future integration with frameworks (e.g., Microsoft Agent Framework)

### 2. **Local-First, Cloud-Optional**

- Default configuration uses local Ollama or FoundryLocal
- Azure/Foundry are additive, not required
- No paid service is a blocker for getting started
- Fallback chain ensures cloud is only used when local is unavailable

### 3. **Gateway as Control Plane**

- The Gateway is a persistent process, not a stateless proxy
- It maintains channel connections, manages cron jobs, and handles webhooks
- All UI surfaces (Control UI + WebChat) are served from the same process
- This mirrors the original OpenClaw architecture's single-process gateway model

### 4. **Workspace-Aware Sessions**

- Bootstrap files (AGENTS.md, SOUL.md, USER.md) enable per-project customization
- No code changes needed to change agent behavior — edit markdown files
- Isolated sessions prevent cross-contamination in automated/webhook scenarios

### 5. **Safety by Design**

- Path traversal protection in FileSystemTool
- Command blocklist in ShellTool
- SSRF protection in WebTool
- Tool approval policy for sensitive operations
- Max iteration limit (10) to prevent infinite loops

### 6. **Educational Structure**

- Modular 27-project layout (one responsibility per project)
- 4-session teaching path with clear checkpoints
- Aspire Dashboard visible at startup (immediate feedback)
- Demo-friendly with realistic prompts and examples

### 7. **Pluggable DI Architecture**

- Every service registered in DI container
- Easy to swap implementations at startup
- No tight coupling to specific providers or tools
- Future-proof for framework evolution

---

## Comparison Table: OpenClaw Pillar → .NET Realization

| OpenClaw Pillar | OpenClawNet Realization | Why This Design |
|---|---|---|
| Gateway as control plane | `OpenClawNet.Gateway` persistent process | Single process manages all connections, APIs, jobs, and webhooks |
| Agent runtime with workspace | `IAgentOrchestrator` + `WorkspaceLoader` | Bootstrap files loaded at session start; workspace-scoped behavior |
| Main + isolated sessions | `ChatSession` + `IsolatedSessionOptions` | Full history for main; clean sandbox for automated/webhook sessions |
| Context compaction | `ISummaryService` + auto-compaction | >20 messages triggers summarization; sliding context window |
| First-class tools | `ITool` + FileSystem, Shell, Web, Browser, Scheduler | Extensible tool registry with safety gates |
| Skills with precedence | `SkillLoader` + workspace > local > bundle | Non-technical users can override behavior with markdown files |
| Model fallback chain | `RuntimeAgentProvider` + fallback config | Local-first with cloud fallback; zero-downtime provider switching |
| Control UI + WebChat | Blazor (management) + Blazor (end-user) | Both surfaces from same Gateway-backed app |
| Channels + Nodes | `IChannel` + Teams adapter + node concept | Teams implemented; mobile/desktop nodes planned |
| Automation (cron + webhooks) | `JobSchedulerService` + `WebhookEndpoints` | Persistent scheduling; GitHub/calendar/custom event triggers |
| Modular agent | `IAgentOrchestrator` + `IAgentRuntime` | Stable public API, internal evolution possible |
| Pluggable models | `IAgentProvider` + provider implementations + `ModelProviderDefinition` multi-instance configs | Easy demo switches; local dev doesn't need Azure |
| Safe tool calling | `ITool` + registry + approval policy | Production-ready; prevents misuse |
| Persistent memory | SQLite + EF Core + summarization | Simple, reliable, no external DB needed |
| Real-time UI | Blazor + HTTP SSE/NDJSON streaming | Full C# stack; real-time without persistent WebSocket connections |
| Observability | Aspire Dashboard + structured logging | Built-in; visible from day one |

---

## Future Evolution

OpenClawNet is architected to support:

- **Microsoft Agent Framework** integration (behind IAgentRuntime)
- **Distributed caching** (add IMemoryCache layer)
- **Multi-agent workflows** (Stateful Agent pattern)
- **Custom tool development** (ITool interface is extensible)
- **Auth/RBAC** (Layer into Gateway endpoints)
- **Persistent embeddings** (Swap IEmbeddingsService implementation)
- **Node registration protocol** (mobile/desktop agent endpoints)
- **Additional channel adapters** (WhatsApp, Telegram, Slack, Discord)
- **Hierarchical context compression** (summary of summaries for very long sessions)

All without breaking the `IAgentOrchestrator` public contract.

---

## Conclusion

OpenClawNet is a faithful .NET translation of OpenClaw's modular agent architecture. By using interfaces, dependency injection, and layered design, it preserves the conceptual elegance of OpenClaw while taking advantage of .NET 10's async/await, Minimal APIs, Blazor, and Aspire orchestration.

The implementation covers all 9 original OpenClaw pillars: persistent gateway control plane, workspace-aware agent runtime, main and isolated sessions with context compaction, first-class tools (including browser), workspace-precedence skills, model abstraction with fallback chains, cron + webhook automation, dual UI surfaces (Control UI + WebChat), and channel/node connectivity.

The result is a reference implementation that is both a **working application** and a **teaching asset**.
