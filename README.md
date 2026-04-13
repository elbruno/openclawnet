# 🦀 OpenClawNet

**Build an AI Agent Platform in .NET 10 with GitHub Copilot**

Welcome to OpenClawNet! This repository contains session materials and working code from the **Microsoft Reactor live series** on building production-ready AI agents using .NET 10, Blazor, and GitHub Copilot.

> **📌 This repo grows with each session.** Code is added incrementally — after Session 1, you'll have a working chatbot. By Session 4, a full AI agent platform.

---

## 📺 Reactor Series — Register Now!

| Language | Series Page |
|----------|------------|
| 🇺🇸 **English** | [Building an AI Agent Platform in .NET 10 with GitHub Copilot](https://developer.microsoft.com/en-us/reactor/series/s-1652/) |
| 🇪🇸 **Español** | [Construye una Plataforma de Agentes AI en .NET 10 con GitHub Copilot](https://developer.microsoft.com/en-us/reactor/series/S-1653/) |

---

## 📅 The 4-Session Journey (50 min each)

### Session 1: Foundation + Local Chat
**Architecture, Local LLM Providers, SignalR Streaming, Blazor UI**

Walk through a pre-built .NET 10 solution: model abstractions, local LLM integration (Ollama/Foundry Local) with SSE streaming, EF Core storage, Gateway API, and a real-time Blazor chat UI — all orchestrated with Aspire.

- 📊 [Slides](sessions/session-1/slides.html) · 💻 [Code](sessions/session-1/code/) · 🤖 [Copilot Prompts](sessions/session-1/copilot-prompts.md)
- 🇺🇸 [Register](https://developer.microsoft.com/en-us/reactor/events/26919/) · 🇪🇸 [Registrarse](https://developer.microsoft.com/en-us/reactor/events/26923/)

---

### Session 2: Tools + Agent Workflows
**Tool Framework, Security Model, Agent Orchestrator Loop**

Upgrade the chatbot to an agent: add a tool framework with approval policies, built-in tools (FileSystem, Shell, Web, Scheduler) with security gates, and the core agent reasoning loop.

- 📦 *Materials available after Session 1*
- 🇺🇸 [Register](https://developer.microsoft.com/en-us/reactor/events/26920/) · 🇪🇸 [Registrarse](https://developer.microsoft.com/en-us/reactor/events/26924/)

---

### Session 3: Skills + Memory
**Markdown Skills, Context Management, Semantic Search**

Give the agent personality with Markdown+YAML skills and long-term memory through conversation summarization and local embeddings for semantic search.

- 📦 *Materials available after Session 2*
- 🇺🇸 [Register](https://developer.microsoft.com/en-us/reactor/events/26921/) · 🇪🇸 [Registrarse](https://developer.microsoft.com/en-us/reactor/events/26925/)

---

### Session 4: Automation + Cloud 🎉
**Cloud Providers, Job Scheduling, Testing, Production Readiness**

Connect Azure OpenAI and Foundry providers, add cron-based job scheduling, run the full test suite, and see the complete platform in action.

- 📦 *Materials available after Session 3*
- 🇺🇸 [Register](https://developer.microsoft.com/en-us/reactor/events/26922/) · 🇪🇸 [Registrarse](https://developer.microsoft.com/en-us/reactor/events/26926/)

---

## 🚀 Quick Start

```bash
# Clone the repo
git clone https://github.com/elbruno/openclawnet.git
cd openclawnet

# Build and run (requires .NET 10 SDK + Local LLM)
# Navigate to any session's stage-3-final:
cd sessions/session-1/code/stage-3-final
dotnet run --project src/OpenClawNet.AppHost
```

The Aspire dashboard opens at `https://localhost:15888` — from there you can access the Gateway and Blazor Web UI.

---

## 📋 Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- **VS Code + GitHub Copilot** — [Setup](https://github.com/features/copilot)
- **Ollama** — [Download](https://ollama.ai/) or **Foundry Local** — [Docs](https://devblogs.microsoft.com/foundry/foundry-local-ga/) (run `ollama pull phi4-mini` or use Foundry Local SDK)
- **Docker** — [Docker Desktop](https://www.docker.com/products/docker-desktop)

For Session 4 (optional): **Azure subscription** — [Free account](https://azure.microsoft.com/free/)

---

## 🏗️ Architecture

```
Blazor Web UI ──SignalR──▶ Gateway API ──▶ Agent Orchestrator
                                              │
                           ┌──────────────────┼──────────────┐
                           ▼                  ▼              ▼
                     Model Provider      Tool Framework   Skills System
                     (Ollama/Foundry     (File/Shell/     (Markdown +
                      Local/Azure)        Web/Schedule)    YAML)
                                              │
                                              ▼
                                         SQLite Storage
```

> 💡 **Session 1 starts with just the chatbot path** (UI → Gateway → Model → Storage). Each session adds a new layer until you have the full agent platform.

---

## 📖 How to Follow Along

Each session uses an **Explain → Explore → Extend** approach:

1. **Explain** — Walk through the pre-built code and architecture
2. **Explore** — Run live demos, test endpoints, see behavior
3. **Extend** — 2-3 small Copilot completions to add features

The code grows incrementally:
| After Session | What's in the repo |
|---------------|-------------------|
| **1** | Chatbot: Models, Local LLMs, Storage, Gateway, Blazor, Aspire |
| **2** | + Agent: Tools framework, security gates, agent loop |
| **3** | + Personality: Skills system, memory, summarization |
| **4** | + Production: Cloud providers, scheduling, tests |

---

## 💬 Community

- **Discord:** [Azure AI Community](https://aka.ms/ai-discord/dotnet) (.NET channel)
- **Issues:** [Open an issue](https://github.com/elbruno/openclawnet/issues)
- **Resources:** [Generative AI for Beginners .NET](https://aka.ms/genainet) · [Aspire](https://aspire.dev)

---

## 📄 License

MIT License — see [LICENSE](./LICENSE) for details.

**Built with ❤️ for the .NET and AI developer community.** 🦀
