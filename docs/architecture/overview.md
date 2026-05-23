# OpenClaw .NET Architecture Overview

## What is OpenClaw .NET?

OpenClaw .NET is a free, open-source agent platform built with .NET 10. It provides a local-first AI assistant with optional cloud provider support, designed as both a real working application and a teaching asset for a multi-session live series.

OpenClaw .NET is the .NET implementation of the original **OpenClaw** platform architecture — a modular, channel-connected agent system with persistent sessions, workspace-aware runtimes, a multi-surface UI, and a pluggable skills layer.

---

## Documentation Map

This document provides the high-level overview. For detailed architecture, see:

| Document | Focus |
|----------|-------|
| **[agent-runtime.md](agent-runtime.md)** | Agent execution engine, two-phase model calling, streaming pipeline, context compaction |
| **[provider-model.md](provider-model.md)** | 5 model providers, multi-instance configs, provider resolution, fallback chain |
| **[storage.md](storage.md)** | SQLite schema, entity relationships, initialization, query patterns |
| **[components.md](components.md)** | Agent Framework integration, workspace bootstrap, skills, tools, memory |
| **[runtime-flow.md](runtime-flow.md)** | Detailed flow diagrams (8 scenarios: standard chat, streaming, webhooks, scheduler, isolated sessions, context compaction, channels, etc.) |

---

## High-Level Architecture

```
┌───────────────────────────────────────────────────────────────────────────┐
│                    Aspire Orchestrator (AppHost)                           │
│         (Service discovery, health checks, local dashboard)                │
└──────────────────────────────┬────────────────────────────────────────────┘
                               │
        ┌──────────────────────▼──────────────────────────────┐
        │              Gateway  (Control Plane)                │
        │  ┌─────────────────────────────────────────────────┐ │
        │  │  HTTP/REST APIs · Server-Sent Events (SSE)    │ │
        │  │  Webhook Endpoints · Cron Scheduler           │ │
        │  │  Channel Manager                              │ │
        │  └──────────────────────┬──────────────────────────┘ │
        │                         │                             │
        │   ┌─────────────────────┼──────────────────────┐     │
        │   │                     │                       │     │
        │   ▼                     ▼                       ▼     │
        │ ┌───────────┐    ┌────────────┐      ┌─────────────┐ │
        │ │ Control   │    │  WebChat   │      │  Channels   │ │
        │ │   UI      │    │  Surface   │      │  (IChannel) │ │
        │ │ (Blazor)  │    │  (Blazor)  │      ├─────────────┤ │
        │ └───────────┘    └────────────┘      │ Teams       │ │
        │                                      │ WhatsApp*   │ │
        └──────────────────────────────────────│ Telegram*   │─┘
                                               │ Slack*      │
                                               └──────┬──────┘
                                                      │
                              ┌───────────────────────┘
                              │
                    ┌─────────▼──────────┐
                    │  Agent Orchestrator │
                    │  ┌───────────────┐ │
                    │  │  Workspace    │ │
                    │  │  AGENTS.md    │ │
                    │  │  SOUL.md      │ │
                    │  │  USER.md      │ │
                    │  └───────────────┘ │
                    │  Prompt Composition │
                    │  Tool Loop          │
                    │  Context Compaction │
                    └──┬────┬────┬───────┘
                       │    │    │
              ┌────────┘    │    └─────────┐
              ▼             ▼              ▼
        ┌──────────┐ ┌──────────────┐ ┌──────────────┐
        │  Model   │ │    Tools     │ │  Skills +    │
        │ Provider │ │  Framework   │ │  Memory      │
        ├──────────┤ ├──────────────┤ ├──────────────┤
        │ Ollama   │ │ FileSystem   │ │ Markdown     │
        │ AzureOAI │ │ Shell        │ │ Skills       │
        │ Foundry  │ │ Web          │ │ Embeddings   │
        │ Foundry  │ │ Scheduler    │ │ Summarization│
        │  Local   │ │ Browser      │ └──────────────┘
        └────┬─────┘ └──────────────┘
             │    (+ canvas, nodes: future)
             │
             └──────────────┐
                            ▼
                    ┌───────────────┐
                    │ SQLite Storage│
                    │  (EF Core)    │
                    └───────────────┘

* = planned channel adapters
```

---

## Gateway as Control Plane

The **Gateway** is the single persistent process that forms the control plane of OpenClawNet. Unlike stateless API gateways, it:

- Maintains **persistent channel connections** (Teams, and future adapters)
- Exposes **HTTP/REST APIs** for all management operations
- Serves both the **Control UI** and the **WebChat** surfaces via HTTP with Server-Sent Events (SSE) for real-time streaming
- Manages **webhook endpoints** for external event triggers
- Runs the **persistent cron scheduler** for automated jobs
- Fires and handles **system events** (job started, message received, session created)

Everything passes through the Gateway. It is the nerve center of the system.

---

## Two UI Surfaces

| Surface | Project | Purpose | Audience | Protocol |
|---------|---------|---------|----------|----------|
| **Control UI** | `OpenClawNet.Web` (Blazor) | Management dashboard — sessions, tools, skills, jobs, memory, channels | Operators / developers | HTTP + REST |
| **WebChat** | `OpenClawNet.Web` (Blazor component) | End-user chat interface with real-time streaming | End users | HTTP + Server-Sent Events (SSE/NDJSON) |

Both surfaces are served by the same Gateway-backed Blazor app and communicate via HTTP. Chat streaming uses `POST /api/chat/stream` returning NDJSON (newline-delimited JSON) for real-time token delivery.

---

## Workspace Concept

Every agent session operates within a **workspace** — a directory that provides context and identity for the agent. The workspace contains three optional bootstrap files:

| File | Purpose |
|------|---------|
| `AGENTS.md` | Agent behavior, persona, and instructions |
| `SOUL.md` | Agent values, principles, and ethical constraints |
| `USER.md` | User preferences, profile, and personalization |

These files are loaded at session start and injected into the system prompt, allowing workspace-level customization without changing code.

---

## Channels and Nodes

### Channels

Channels are persistent connections to external messaging platforms. The Gateway manages channel connections and routes inbound messages to the Agent Orchestrator.

| Channel | Status | Adapter |
|---------|--------|---------|
| WebChat | ✅ Implemented | Built-in (Blazor + HTTP SSE/NDJSON) |
| Microsoft Teams | ✅ Implemented | `OpenClawNet.Adapters.Teams` |
| WhatsApp | 🔜 Planned | — |
| Telegram | 🔜 Planned | — |
| Slack | 🔜 Planned | — |
| Discord | 🔜 Planned | — |

### Nodes

Nodes are remote agent endpoints — typically mobile or desktop applications — that extend the agent's capabilities with device-native features:

- **Camera** — capture and analyze images
- **Location** — GPS/geolocation-aware responses
- **Screen recording** — screen capture for assistance
- **Push notifications** — proactive agent-initiated messages

Nodes register with the Gateway and communicate via the standard IChannel abstraction.

---

## The 9 OpenClaw Pillars → .NET Implementation

| OpenClaw Pillar | OpenClawNet Implementation |
|-----------------|---------------------------|
| **1. Gateway as Control Plane** | `OpenClawNet.Gateway` — persistent process, all connections managed here |
| **2. Agent Runtime with Workspace** | `IAgentOrchestrator` + `ChatClientAgent` (Agent Framework) + bootstrap file loading |
| **3. Sessions and Memory** | `IMemoryService` + `ISummaryService` + isolated session support + context compaction |
| **4. First-Class Tools** | `ITool` + FileSystem, Shell, Web, Scheduler, Browser. **Tool Test surface** — Direct Invoke or Agent Probe via the dedicated `ToolTester` profile (+ canvas, nodes: future) |
| **5. Skills System** | `SkillLoader` + Markdown/YAML with precedence: workspace > local > bundle |
| **6. Model Abstraction** | `IAgentProvider` + `RuntimeAgentProvider` (router) + Ollama, AzureOpenAI, Foundry, FoundryLocal, GitHubCopilot. Multi-instance `ModelProviderDefinition` configs + `AgentProfile` named agent definitions, classified by `ProfileKind` (Standard / System / ToolTester) |
| **7. Automation** | `JobSchedulerService` (cron) + `WebhookEndpoints` + GitHub/calendar triggers |
| **8. UI / Surfaces** | Control UI (Blazor management dashboard) + WebChat (end-user chat) |
| **9. Channels and Nodes** | `IChannel` abstraction + Teams adapter + Node concept (future mobile/desktop) |

---

## Key Principles

1. **Local-first**: Runs fully offline with Ollama or FoundryLocal. No cloud required.
2. **Pluggable providers**: Swap between Ollama, FoundryLocal, Azure OpenAI, Foundry, and GitHub Copilot via DI configuration. `RuntimeAgentProvider` routes to the active provider. Multi-instance `ModelProviderDefinition` entities allow multiple named configurations per provider type.
3. **Interface-driven**: Clean abstractions at every boundary — no vendor lock-in.
4. **Aspire-orchestrated**: Aspire manages service startup, health checks, discovery, and observability (dashboard visible at startup).
5. **Educational**: Code is structured for teaching, not just shipping. 4-session incremental progression.
6. **Modular**: 27 focused projects, each with a single responsibility.
7. **Gateway as control plane**: The Gateway is a persistent, stateful process — not a passthrough proxy.
8. **Workspace-aware**: Agent behavior, persona, and user preferences are loaded from workspace bootstrap files.

---

## Project Structure (27 Projects in `src/`)

| Project | Purpose |
|---------|---------|
| `OpenClawNet.AppHost` | Aspire orchestration host |
| `OpenClawNet.ServiceDefaults` | Aspire service defaults (telemetry, health) |
| `OpenClawNet.Gateway` | Backend API, HTTP stream endpoints, webhook endpoints, channel manager |
| `OpenClawNet.Agent` | Agent orchestration, prompt composition, summarization, workspace loading |
| `OpenClawNet.Models.Abstractions` | IAgentProvider, IModelClient, AgentProfile, ModelProviderDefinition, AgentProfileMarkdownParser |
| `OpenClawNet.Models.Ollama` | Ollama REST API provider (`OllamaAgentProvider`) |
| `OpenClawNet.Models.AzureOpenAI` | Azure OpenAI SDK provider (`AzureOpenAIAgentProvider`) |
| `OpenClawNet.Models.Foundry` | Foundry OpenAI-compatible provider (`FoundryAgentProvider`) |
| `OpenClawNet.Models.FoundryLocal` | Foundry Local on-device provider (`FoundryLocalAgentProvider`) |
| `OpenClawNet.Models.GitHubCopilot` | GitHub Copilot provider (`GitHubCopilotAgentProvider`) |
| `OpenClawNet.Tools.Abstractions` | ITool, IToolRegistry, IToolExecutor |
| `OpenClawNet.Tools.Core` | Tool registry and executor |
| `OpenClawNet.Tools.FileSystem` | File read/write/list tool |
| `OpenClawNet.Tools.Shell` | Safe shell execution tool |
| `OpenClawNet.Tools.Web` | HTTP fetch tool |
| `OpenClawNet.Tools.Scheduler` | Job scheduling tool |
| `OpenClawNet.Tools.Browser` | Headless browser tool (Playwright-backed) |
| `OpenClawNet.Skills` | Markdown skill parser (`FileSkillLoader`) with Awesome-Copilot subdirectory support |
| `OpenClawNet.Memory` | Session summary, conversation memory, local embeddings |
| `OpenClawNet.Storage` | EF Core + SQLite persistence (ChatSession, AgentProfile, ModelProviderDefinition, etc.) |
| `OpenClawNet.Adapters.Teams` | Microsoft Teams channel adapter |
| `OpenClawNet.Web` | Blazor Web App (Control UI + WebChat surfaces) |
| `OpenClawNet.Services.Scheduler` | Separate Aspire service (`scheduler`) — Scheduler with Blazor dashboard, polls for due jobs |
| `OpenClawNet.Services.Shell` | Separate Aspire service (`shell-service`) — Shell execution service |
| `OpenClawNet.Services.Browser` | Separate Aspire service (`browser-service`) — Headless browser service |
| `OpenClawNet.Services.Channels` | Separate Aspire service (`channels`) — **Teams Bot Framework webhook** (POST `/api/messages`); `GET /` serves a small landing page that points users at the `channels-website` UI. API-only, not a user dashboard. |
| `OpenClawNet.Channels` | Separate Aspire service (`channels-website`) — **Blazor Server UI** for the job output channels dashboard (`/channels`, `/channels/{jobId}`). Distinct from `channels` above. |
| `OpenClawNet.Services.Memory` | Separate Aspire service (`memory-service`) — Memory and embeddings service |

> **Resource naming note:** The Aspire AppHost exposes two distinct resources whose names look similar — `channels` (the Teams bot webhook) and `channels-website` (the Blazor dashboard UI). Always use the resource name, not the project name, when wiring service references.

**Test projects** (in `tests/`): `OpenClawNet.UnitTests`, `OpenClawNet.IntegrationTests`, `OpenClawNet.PlaywrightTests` — 306 unit + 27 integration + 11 live = 344 total tests.

---

## Orchestration: Aspire

**AppHost** (`OpenClawNet.AppHost`) is the single source of truth for service orchestration:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var gateway = builder
    .AddProject<Projects.OpenClawNet_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.OpenClawNet_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(gateway)
    .WaitFor(gateway);

builder.Build().Run();
```

**Benefits:**

- Service discovery is declarative and code-first
- Health checks are built-in (automatic retry/restart)
- Aspire Dashboard gives visibility into both services at startup
- Local development just works: `aspire run`
- Same orchestration code works locally and in cloud deployment

**Service-discovery scheme:** When a client `HttpClient` targets another Aspire-registered service by name, the base address must use the `https+http://<service-name>` scheme (or the explicit `http://_endpoint.<service-name>` form). A plain `http://<service-name>` URI is sent to DNS literally and fails resolution. Example:

```csharp
builder.Services.AddHttpClient("gateway",
    c => c.BaseAddress = new Uri("https+http://gateway"));
```

This applies to cross-service calls (Web → Gateway, Web → Scheduler) and to a service self-referencing itself for in-process Blazor components (`scheduler.WithReference(scheduler)` in AppHost).

---

## Technology Stack

- **.NET 10** / C# 14
- **ASP.NET Core Minimal APIs** — Gateway backend REST API
- **Blazor Web App** — Control UI + WebChat (server-rendered, interactive)
- **HTTP Streaming (SSE/NDJSON)** — Real-time chat token delivery via `POST /api/chat/stream`
- **Aspire** — Service orchestration and observability
- **Entity Framework Core** — SQLite persistence
- **Playwright** — Headless browser automation (Tools.Browser)
- **xUnit** — Testing
- **Microsoft.Agents.AI** (`1.1.0`) — Agent Framework execution engine
  - `ChatClientAgent` orchestrates model interaction (wraps `IChatClient`)
  - `AgentSkillsProvider` implements Agent Skills spec (agentskills.io) for progressive skill loading
  - `ModelClientChatClientAdapter : IChatClient` bridges local providers (Ollama, Foundry Local) to the Agent Framework
