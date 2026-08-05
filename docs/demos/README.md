# OpenClaw .NET — Demo Guide

Three tracks of step-by-step demos for the **OpenClaw .NET** platform:

| Track | Folder | Best for |
|---|---|---|
| 🚀 **Aspire Stack** | [`aspire-stack/`](aspire-stack/README.md) | Full solution: Gateway + Web UI + Aspire Dashboard |
| 🔌 **Gateway Only** | [`gateway-only/`](gateway-only/README.md) | Just the Gateway API, no UI or Aspire needed |
| 🛠️ **Tools** | [`tools/`](tools/README.md) | Recipe-style demos that combine built-in tools (jobs, agent runs) |
| 🎯 **Real-World Scenarios** | [`real-world/`](real-world/README.md) | Advanced production patterns: scheduling, events, skills, audit |

---

## Which track should I use?

**Start with [Gateway Only](gateway-only/README.md)** if you want to explore the API with PowerShell/curl commands, step through each feature individually, or don't have a full Aspire environment ready.

**Use [Aspire Stack](aspire-stack/README.md)** once you want to run the full solution — streaming Web UI, Aspire Dashboard, GenAI visualizer, distributed traces, and the complete agent observability story.

**Choose [Real-World Scenarios](real-world/README.md)** after completing either track above, when you're ready to build production patterns: scheduled jobs, event-driven workflows, skill composition, multi-agent orchestration, and compliance audit trails.

---

## Quick Start

### Aspire Stack (full solution)
```powershell
aspire start src\OpenClawNet.AppHost
# Open http://localhost:15888 for the dashboard
```

### Gateway Only
```powershell
dotnet run --project src/OpenClawNet.Gateway
# Swagger at http://localhost:5010/swagger
```

---

## Prerequisites (both tracks)

| Tool | Install | Notes |
|------|---------|-------|
| .NET 10 SDK | `winget install Microsoft.DotNet.SDK.10` | |
| Aspire workload | `dotnet workload install aspire` | Required for Aspire Stack |
| Ollama | https://ollama.com | `ollama pull gemma4:e2b` (recommended) |
| Git | Repo cloned, on `main` | |

**Aspire version used:** 13.3.x — the repo pins `Aspire.AppHost.Sdk` 13.3.5 and `Aspire.Hosting.Testing` 13.3.0.

---

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  Aspire 13 Dashboard (:15888)                        │
│  Logs · Traces · Metrics · GenAI Visualizer          │
└──────────────┬──────────────────────────────────────┘
               │ orchestrates
┌──────────────▼──────────────────────────────────────┐
│  OpenClaw .NET.Web  (Blazor Server)                    │
│  Chat UI with HTTP SSE streaming + toolbar status   │
│  [Provider] [Model] [Jobs] [Connection ●]           │
└──────────────┬──────────────────────────────────────┘
               │ HTTP + SSE streaming
┌──────────────▼──────────────────────────────────────┐
│  OpenClaw .NET.Gateway  (ASP.NET Minimal API)          │
│  IAgentOrchestrator → tools → skills → memory       │
│  /api/chat  /api/chat/stream  /api/sessions         │
│  /api/webhooks  /api/jobs  /api/settings            │
└──────────────┬──────────────────────────────────────┘
               │
        ┌──────┴──────┐
  Ollama / Foundry   SQLite
  (IAgentProvider)   (IConversationStore)
```
