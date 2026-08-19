# Session 1: Foundation + Local Chat

**Duration:** 75-90 minutes | **Level:** Intermediate .NET | **Series:** OpenClawNet — Microsoft Reactor

## Goal

Build a working AI chatbot with Aspire, local LLMs (Ollama, Azure OpenAI, or GitHub Copilot SDK), and HTTP NDJSON streaming — then understand every layer of the architecture that makes it work.

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| .NET SDK | 10.0+ | [dot.net/download](https://dot.net/download) |
| Ollama or Foundry Local | Latest | [ollama.com](https://ollama.com) / [Foundry Local](https://github.com/microsoft/foundry-local) |
| VS Code | Latest | [code.visualstudio.com](https://code.visualstudio.com) |
| GitHub Copilot | Active subscription | VS Code extension |

**Pre-session setup:**

```bash
# Option A: Ollama
ollama pull llama3.2
# Option B: Foundry Local
foundrylocal model download --assigned phi-4

dotnet workload install aspire
```

## What You'll Learn

1. **Architecture & Core Abstractions** — How 27 projects are organized into clean vertical slices, and why `IAgentProvider` (Microsoft Agent Framework) is the contract that makes everything pluggable
2. **Local LLM Provider + Data Layer** — How HTTP NDJSON streaming works with `IAsyncEnumerable`, and how EF Core entities store conversations
3. **Gateway + SSE + Blazor** — How Minimal APIs, HTTP streaming with NDJSON, and Aspire orchestration come together to deliver a complete chat experience

## Session Materials

| Resource | Description |
|----------|-------------|
| [speaker-script.md](speaker-script.md) | Minute-by-minute dual-presenter timeline |
| [demo-scripts.md](demo-scripts.md) | Three step-by-step live demos with recovery plans |
| [bonus-demos.md](bonus-demos.md) | Additional demos for extra time or Q&A |
| [guide.md](guide.md) | Detailed walkthrough guide (English) |
| [guide-es.md](guide-es.md) | Detailed walkthrough guide (Spanish) |
| [demo-agents/](demo-agents/) | Sample agent persona files for Demo 3 (pirate, chef, robot) |
| [code/](code/) | Demo-ready code: console app (demo1), bug injection scripts (demo2), agent personalities (demo3) |

## Projects Covered

| Project | LOC | Description |
|---------|-----|-------------|
| `OpenClawNet.Models.Abstractions` | 93 | `IAgentProvider` interface, `ChatRequest`, `ChatResponse`, `ChatMessage` records |
| `OpenClawNet.Models.Ollama` | 181 | Ollama provider (`OllamaAgentProvider`) with streaming via OllamaSharp |
| `OpenClawNet.Models.AzureOpenAI` | 185 | Azure OpenAI provider via Azure.AI.OpenAI SDK (3 auth modes: API Key, Integrated, Federated) |
| `OpenClawNet.Models.FoundryLocal` | 165 | Foundry Local bridge provider via Microsoft.AI.Foundry.Local |
| `OpenClawNet.Models.GitHubCopilot` | 142 | GitHub Copilot SDK provider via `GitHub.Copilot` (v1.0.9) with environment-based auth |
| `OpenClawNet.Storage` | 275 | EF Core DbContext, entities (ChatSession, ChatMessageEntity, AgentProfile, etc.) |
| `OpenClawNet.ServiceDefaults` | 105 | Aspire service defaults — telemetry, health checks, OpenAPI |
| `OpenClawNet.AppHost` | 18 | Aspire orchestration — wires Gateway + Web with service discovery |
| `OpenClawNet.Gateway` | 625 | Minimal APIs, HTTP NDJSON streaming endpoint (`POST /api/chat/stream`), Model Providers, Agent Profiles |
| `OpenClawNet.Web` | 28 | Blazor web app — chat UI with real-time HTTP NDJSON streaming + inline agent selector badge (provider/model) |

## Quick Start

```bash
# Clone and build
git clone https://github.com/elbruno/openclawnet.git
cd openclawnet
dotnet build

# Verify local LLM is running
ollama list   # Should show llama3.2 (if using Ollama)

# Launch the full stack (from repo root)
aspire run
```

**Expected endpoints:**

- 🌐 Web UI: `http://localhost:5001`
- 🔌 Gateway API: `http://localhost:5000`
- 📊 Aspire Dashboard: `https://localhost:15100`

## Architecture at a Glance

```
┌──────────────────────────────────────────────────────┐
│                    Blazor Web UI                      │
│                 (OpenClawNet.Web)                     │
├──────────────────────────────────────────────────────┤
│            HTTP NDJSON + REST API                          │
│             (OpenClawNet.Gateway)                     │
├──────────────────────────────────────────────────────┤
│              RuntimeAgentProvider                     │
│          (routes to active provider)                 │
├─────────┬──────────┬─────────┬──────────┬────────────┤
│ Ollama  │Azure     │Microsoft│Foundry   │GitHub      │
│         │OpenAI    │Foundry  │Local     │Copilot SDK │
├─────────┴──────────┴─────────┴──────────┴────────────┤
│      Storage (EF Core)    │    ServiceDefaults        │
│       + AgentProfile      │       (Aspire)            │
└───────────────────────────┴──────────────────────────┘
```

## Next Session

**Session 2: Tools & Agent Workflows** — Give the chatbot superpowers with file system access, web fetching, shell execution, and the agent tool-call loop.
