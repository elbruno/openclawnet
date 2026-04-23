# 🦀 OpenClaw .NET

**Build an AI Agent Platform in .NET 10 with GitHub Copilot**

OpenClaw .NET is the companion repo for the **Microsoft Reactor live series** on building production‑ready AI agents with **.NET 10**, **Aspire**, **Blazor** and **GitHub Copilot**. It contains the complete application source, tests, manuals, demos, and per‑session attendee materials — everything you need to follow along live, or rebuild the whole platform on your own machine.

> 👉 **First time here? Start with [SETUP.md](./SETUP.md)** for a step‑by‑step local setup (prerequisites, hardware, clone, build, run, first chat, troubleshooting).

> 🌐 **Browse the published site:** **<https://elbruno.github.io/openclawnet/>** — session slides, live test dashboard, and quick links to materials.

---

## 📺 Reactor Series — Register

| Language | Series Page |
|----------|-------------|
| 🇺🇸 **English** | [Building an AI Agent Platform in .NET 10 with GitHub Copilot](https://developer.microsoft.com/en-us/reactor/series/s-1652/) |
| 🇪🇸 **Español** | [Construye una Plataforma de Agentes AI en .NET 10 con GitHub Copilot](https://developer.microsoft.com/en-us/reactor/series/S-1653/) |

---

## 📅 The 4‑Session Journey (50 min each)

| # | Session | Focus | Materials |
|---|---------|-------|-----------|
| **1** | Foundation + Local Chat | Architecture, local LLM providers, SignalR streaming, Blazor UI | [sessions/session-1](./sessions/session-1/) |
| **2** | Tools + Agent Workflows | Tool framework, security model, agent orchestrator loop | [sessions/session-2](./sessions/session-2/) |
| **3** | Skills + Memory | Markdown skills, context management, semantic search | [sessions/session-3](./sessions/session-3/) |
| **4** | Automation + Cloud | Cloud providers, job scheduling, testing, production readiness | [sessions/session-4](./sessions/session-4/) |

Each session uses an **Explain → Explore → Extend** approach: walk the architecture, run live demos, then add features with GitHub Copilot.

---

## 🚀 Quick Start

```powershell
git clone https://github.com/elbruno/openclawnet.git
cd openclawnet

# Pull a local model
ollama pull llama3.2

# Build & run
dotnet build
aspire start
```

Open the **Web** URL from the Aspire dashboard (typically <http://localhost:5010>) and say hello.

For the full guide — including hardware requirements, the four model providers, the Jobs/Tools demos, and troubleshooting — see **[SETUP.md](./SETUP.md)**.

---

## 🏗️ Architecture

```
Blazor Web UI ──SignalR──▶ Gateway API ──▶ Agent Orchestrator
                                              │
                           ┌──────────────────┼──────────────┐
                           ▼                  ▼              ▼
                     Model Provider      Tool Framework   Skills System
                     (Ollama/Foundry     (File/Shell/     (Markdown +
                      Local/Azure/...)    Web/Schedule)    YAML)
                                              │
                                              ▼
                                         SQLite Storage
```

> Session 1 starts with just the chatbot path (UI → Gateway → Model → Storage). Each session adds a new layer until you have the full agent platform.

---

## 📁 What's in the Repo

- **[`src/`](./src/)** — the full .NET 10 + Aspire application (40+ projects: Gateway, Web, Agent, Memory, Skills, Storage, model providers, tools, MCP servers).
- **[`tests/`](./tests/)** — Unit, Integration, and Playwright E2E test suites.
- **[`docs/manuals/`](./docs/manuals/)** — user‑facing manuals (prerequisites, installation, settings, tools, jobs).
- **[`docs/demos/`](./docs/demos/)** — short demo scripts (aspire‑stack, gateway‑only, tools, real‑world scenarios).
- **[`docs/analysis/`](./docs/analysis/)** — architecture and design analyses.
- **[`scripts/`](./scripts/)** — helper PowerShell scripts (prereqs, reset, dashboard publish).
- **[`sessions/`](./sessions/)** — per‑session attendee materials (READMEs, scripts, slides, demo code).

See the **What's Where** section in [SETUP.md](./SETUP.md#11-whats-where) for a per‑folder breakdown.

---

## 💬 Community

- **Discord:** [Microsoft Foundry Community](https://aka.ms/ai-discord/dotnet) (.NET channel)
- **Issues:** [Open an issue](https://github.com/elbruno/openclawnet/issues)
- **Resources:** [Generative AI for Beginners .NET](https://aka.ms/genainet) · [.NET Aspire](https://aspire.dev)

---

## 📄 License

MIT License — see [LICENSE](./LICENSE) for details.

**Built with ❤️ for the .NET and AI developer community.** 🦀
