# 🐾 OpenClawNet

**Build an AI Agent Platform in .NET 10 with GitHub Copilot**

Welcome to OpenClawNet! This repository contains session materials and working code from the **Microsoft Reactor live series** on building production-ready AI agents using .NET 10, Blazor, and GitHub Copilot. If you attended the live sessions, you'll find the scaffolded code and demo materials here.

---

## What's in This Repo?

- **4 progressive session branches:** Code builds incrementally across sessions—clone and follow along at your own pace
- **Session guides & demos:** Architecture diagrams, demo scripts, and learning goals for each session
- **Working examples:** Complete, runnable code showcasing agent orchestration, tool calling, skills, and cloud integration
- **Documentation:** Architecture, setup instructions, and code walkthroughs

---

## 📺 Reactor Series — Register Now!

This is a **Microsoft Reactor live series**. Register and attend live, or watch the recordings after each session.

| Language | Series Page |
|----------|------------|
| 🇺🇸 **English** | [Building an AI Agent Platform in .NET 10 with GitHub Copilot](https://developer.microsoft.com/en-us/reactor/series/s-1652/) |
| 🇪🇸 **Español** | [Construye una Plataforma de Agentes AI en .NET 10 con GitHub Copilot](https://developer.microsoft.com/en-us/reactor/series/S-1653/) |

---

## 📅 The 4-Session Structure

### **Session 1: From Zero to ChatBot in 60 Minutes**
**Scaffolding + Gateway + Local Chat**

Start with an empty .NET solution and build a working chat UI in one hour. You'll scaffold Aspire orchestration, create a minimal Gateway API, and hook up a Blazor web UI with SignalR real-time messaging. **Output:** A ChatBot you can talk to locally.

- **Topics:** Project setup, Aspire, Minimal APIs, Blazor, SignalR
- **Hands-on:** Build and run the chat interface
- **Outcome:** Real-time client-server messaging layer ready for AI
- 🇺🇸 [Register (English)](https://developer.microsoft.com/en-us/reactor/events/26919/) · 🇪🇸 [Registrarse (Español)](https://developer.microsoft.com/en-us/reactor/events/26923/)

> 📦 **Materials:** 📹 Recording · 📊 Slides · 💻 Source Code · 🎯 Demo Prompts — *Coming soon after the session*

---

### **Session 2: Teach Your AI to Act, Not Just Talk**
**Tools + Agent Workflows**

Connect an AI model and teach it to use tools. Build the Agent Orchestrator, integrate model providers (Ollama/Azure OpenAI), and implement a tool framework so your AI can execute file, web, and shell operations. **Output:** An AI that reasons and acts, not just responds.

- **Topics:** Agent orchestration, model integration, tool registry, tool calling loops
- **Hands-on:** Prompt the AI to execute real tasks
- **Outcome:** Agentic behavior with constrained tool access
- 🇺🇸 [Register (English)](https://developer.microsoft.com/en-us/reactor/events/26920/) · 🇪🇸 [Registrarse (Español)](https://developer.microsoft.com/en-us/reactor/events/26924/)

> 📦 **Materials:** 📹 Recording · 📊 Slides · 💻 Source Code · 🎯 Demo Prompts — *Coming soon after the session*

---

### **Session 3: Customize Behavior & Remember Conversations**
**Skills + Memory**

Build skills (reusable AI behaviors) and add persistent memory. Store conversations in SQLite, implement embedding-based retrieval for context, and organize AI capabilities as composable markdown + YAML skills. **Output:** An agent that learns and remembers.

- **Topics:** Skills framework, conversation memory, embeddings, EF Core + SQLite
- **Hands-on:** Create custom skills and persistent message history
- **Outcome:** Stateful, context-aware agent behavior
- 🇺🇸 [Register (English)](https://developer.microsoft.com/en-us/reactor/events/26921/) · 🇪🇸 [Registrarse (Español)](https://developer.microsoft.com/en-us/reactor/events/26925/)

> 📦 **Materials:** 📹 Recording · 📊 Slides · 💻 Source Code · 🎯 Demo Prompts — *Coming soon after the session*

---

### **Session 4: From Local Dev to Production Ready**
**Automation + Azure + Foundry**

Deploy to Azure and scale with Microsoft Foundry. Set up GitHub Actions pipelines, containerize with Docker, provision cloud resources, and use Foundry for advanced agent management and observability. **Output:** Production-hardened AI platform.

- **Topics:** Docker, Azure deployment, CI/CD, Microsoft Foundry, observability
- **Hands-on:** Deploy locally, push to Azure, use Foundry dashboard
- **Outcome:** Cloud-ready, monitored agent platform
- 🇺🇸 [Register (English)](https://developer.microsoft.com/en-us/reactor/events/26922/) · 🇪🇸 [Registrarse (Español)](https://developer.microsoft.com/en-us/reactor/events/26926/)

> 📦 **Materials:** 📹 Recording · 📊 Slides · 💻 Source Code · 🎯 Demo Prompts — *Coming soon after the session*

---

## 📋 Prerequisites

### For All Sessions (Local Development)

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- **VS Code + GitHub Copilot** — [Setup](https://github.com/features/copilot)
- **Foundry Local** (run AI models locally) — [GitHub](https://github.com/microsoft/Foundry-Local) · [Website](https://www.foundrylocal.ai/)
- **Docker** — [Docker Desktop](https://www.docker.com/products/docker-desktop)
- **Git** — [Download](https://git-scm.com/)

### For Session 4 (Cloud Deployment)

- **Azure subscription** — [Create a free account](https://azure.microsoft.com/free/) (free tier works!)
- **Azure CLI (`az`)** — [Install](https://learn.microsoft.com/cli/azure/install-azure-cli)

---

## 🎒 Before Your First Session

1. ✅ **Install prerequisites** above (especially .NET 10, VS Code + Copilot, and Foundry Local)
2. ✅ **Register** for the sessions — [🇺🇸 English](https://developer.microsoft.com/en-us/reactor/series/s-1652/) or [🇪🇸 Español](https://developer.microsoft.com/en-us/reactor/series/S-1653/)
3. ✅ **Clone this repo** — `git clone https://github.com/elbruno/openclawnet.git`
4. ✅ **Test Foundry Local** — Run `foundry model run phi-4-mini` to verify local AI models work on your machine
5. ✅ **Join the community** — [Azure AI Discord](https://aka.ms/ai-discord/dotnet) for live Q&A during sessions

---

## 🏗️ Architecture

```
Blazor Web UI ──SignalR──▶ Gateway API ──▶ Agent Orchestrator
                                              │
                           ┌──────────────────┼──────────────┐
                           ▼                  ▼              ▼
                     Model Provider      Tool Framework   Skills System
                     (Ollama/Azure/      (File/Shell/     (Markdown +
                      Foundry)            Web/Schedule)    YAML)
                                              │
                                              ▼
                                         SQLite Storage
```

**Key Components:**

- **Blazor Web UI** — Real-time chat interface (SignalR)
- **Gateway API** — Entry point and request routing (Minimal APIs)
- **Agent Orchestrator** — Core reasoning loop, tool calling, skill composition
- **Model Provider** — Abstraction over Ollama, Azure OpenAI, and Microsoft Foundry
- **Tool Framework** — Registry of available tools (file, shell, web, custom)
- **Skills System** — Markdown + YAML-based reusable behaviors
- **SQLite + EF Core** — Persistent storage for conversations and state

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| **Frontend** | Blazor WebAssembly, SignalR |
| **API** | ASP.NET Core Minimal APIs |
| **Orchestration** | .NET Aspire |
| **Agent Runtime** | Custom .NET orchestrator |
| **Models** | Ollama (local), Azure OpenAI, Microsoft Foundry |
| **Storage** | SQLite, EF Core |
| **Tooling** | Docker, GitHub Actions, Azure |
| **IDE** | VS Code, GitHub Copilot |

---

## 📖 How to Follow Along

Each session adds code incrementally. Code is organized by branches or folders:

1. **After Session 1:** Chat UI and Gateway are working
2. **After Session 2:** Agent Orchestrator and tool calling active
3. **After Session 3:** Skills and persistent memory enabled
4. **After Session 4:** Cloud deployment and observability running

Clone the repo and start at **Session 1**. As sessions roll out, new code is added. You can:
- **Follow live** during the Reactor session
- **Code along** at your own pace using the session guides
- **Reference** completed code to compare against your work

---

## 📚 Documentation

Full documentation is in the [`docs/`](./docs/) folder:

- **Architecture** — Detailed component design and data flows
- **Session Guides** — Step-by-step learning paths with Copilot prompts
- **Setup** — Installation and environment configuration
- **Prompts** — Demo scripts and Copilot conversation starters

---

## 💬 Questions & Community

[![Azure AI Community Discord](https://img.shields.io/discord/1113626258182504448?label=Azure%20AI%20Community%20Discord)](https://aka.ms/ai-discord/dotnet)
[![Microsoft Foundry Discussions](https://img.shields.io/badge/Discussions-Microsoft%20Foundry-blueviolet?logo=github&style=flat-square)](https://aka.ms/ai-discussions/dotnet)

- **💬 Ask questions during sessions:** Join the [Azure AI Community Discord](https://aka.ms/ai-discord/dotnet) — find us in the .NET channel
- **🗣️ Foundry discussions:** [Microsoft Foundry GitHub Discussions](https://aka.ms/ai-discussions/dotnet)
- **📺 Live Sessions:** [🇺🇸 English Series](https://developer.microsoft.com/en-us/reactor/series/s-1652/) · [🇪🇸 Serie en Español](https://developer.microsoft.com/en-us/reactor/series/S-1653/)
- **🐛 Found an issue?** [Open an issue](https://github.com/elbruno/openclawnet/issues) in this repository
- **📝 Feedback:** We'd love to hear how this series helped you

---

## 📚 Additional Resources

- **[Generative AI for Beginners .NET](https://aka.ms/genainet)** — Hands-on .NET AI course from Microsoft (great companion to this series)
- **[Foundry Local](https://www.foundrylocal.ai/)** — Run AI models locally on your hardware
- **[.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)** — Cloud-native orchestration for .NET

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](./LICENSE) file for details.

---

**Built with ❤️ for the .NET and AI developer community.** 🐾

Questions? Start with the [docs](./docs/) or open an issue!
