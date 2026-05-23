# Local Installation

This guide walks you through installing and running **OpenClaw .NET** on your local machine. By the end you will have the full Aspire orchestration running with the Gateway API, Web UI, Scheduler, and Ollama all wired up.

> **Before you start:** Complete every item in **[00-prerequisites.md](./00-prerequisites.md)**. This guide assumes the .NET 10 SDK, Aspire CLI, Docker Desktop, Ollama, and Git are already installed and verified.

---

## 1. Clone the Repository

Open a terminal in the directory where you want the project to live and clone the repo:

```bash
git clone https://github.com/elbruno/openclawnet-plan.git
cd openclawnet-plan
```

Verify you are on the default branch:

```bash
git status
# On branch main
# Your branch is up to date with 'origin/main'.
```

---

## 2. Pull a Local Model

OpenClaw .NET ships configured for **Ollama** as the default model provider. Pull at least one model before launching the app — without a model, the Gateway cannot answer chat requests.

```bash
# Recommended: best tool-use support
ollama pull gemma4:e2b

# Alternative defaults
ollama pull llama3.2
ollama pull phi4

# Lightweight tool-calling alternatives (verified in live tests)
ollama pull qwen2.5:3b
ollama pull phi3
```

Confirm the model is available:

```bash
ollama list
# NAME              ID              SIZE      MODIFIED
# gemma4:e2b        abc123def456    1.7 GB    2 minutes ago
```

> **Tip:** Keep `ollama serve` running in the background, or let Docker Desktop start the Ollama container that Aspire provisions for you.

---

## 3. Restore .NET Dependencies

From the repository root, restore all NuGet packages for the solution:

```bash
dotnet restore OpenClawNet.slnx
```

This pulls down every dependency for the 27 projects under `src/`. The first restore can take a few minutes.

---

## 4. Build the Solution

Verify everything compiles before you launch:

```bash
dotnet build OpenClawNet.slnx -c Debug
```

You should see `Build succeeded` with **0 Error(s)**. Warnings are normal.

---

## 5. Launch with the Aspire AppHost

OpenClaw .NET is an **Aspire-orchestrated** distributed application. The `OpenClawNet.AppHost` project starts every service, container, and integration for you.

### Option A — `aspire start` (recommended)

```bash
aspire start src/OpenClawNet.AppHost
```

When prompted, select option **3** to run the App Host with Aspire dashboard integration.

### Option B — Interactive Aspire Dashboard

For more control, use the interactive menu that `aspire start` provides. You can also use `aspire describe` to see available resources without running them.

The **Aspire Dashboard** opens automatically in your default browser once services are running. If it does not open automatically, copy the dashboard URL printed in the terminal — it usually looks like:

```
Login to the dashboard at: http://localhost:15888/login?t=<token>
```

---

## 6. Verify the Aspire Dashboard

Once the dashboard loads, confirm every resource shows a green **Running** state:

| Resource | Type | What It Does |
|----------|------|--------------|
| `gateway` | Project | The HTTP/REST control plane |
| `web` | Project | The Blazor/React Web UI and chat surface |
| `scheduler` | Project | Cron-based job scheduler |
| `ollama` | Container | Local LLM runtime (Docker) |
| `openclawnet-db` | SQLite | Persistent storage (sessions, jobs, memory) |
| `sqlite-web` | Container | Browser-based admin UI for the database |

Click any resource to view live logs, metrics, environment variables, and traces.

---

## 7. Open the Web UI

From the Aspire Dashboard, click the URL next to the **`web`** resource. You should see the OpenClaw .NET chat interface.

Try a sanity-check prompt:

```
Hello! What can you do?
```

If the model responds, your installation is fully working. 🎉

---

## 8. Open the SQLite Admin UI

The Aspire orchestration includes the **SQLite Web** container so you can browse the database without leaving the dashboard.

1. Open the Aspire Dashboard.
2. Click the URL next to the **`sqlite-web`** resource.
3. Browse tables such as `Sessions`, `Messages`, `Jobs`, and `Memories`.

The database file lives at:

```
src/OpenClawNet.AppHost/.data/openclawnet.db
```

> **Reset the database:** Stop the AppHost, delete `openclawnet.db`, and start again. Aspire recreates the schema on startup.

---

## 9. Stop the Application

In the terminal running the AppHost, press:

```
Ctrl + C
```

Aspire shuts down every project, stops every container it started, and releases the ports. Wait for the `Press any key to exit` prompt before closing the window.

---

## Configuration

Most defaults work out of the box. To customize the model, provider, or endpoints, edit `src/OpenClawNet.Gateway/appsettings.json`:

```json
{
  "Model": {
    "Provider": "ollama",
    "Model": "gemma4:e2b",
    "Endpoint": "http://localhost:11434",
    "Temperature": 0.7,
    "MaxTokens": 4096
  }
}
```

Supported providers:

| Provider | `Provider` value | Required settings |
|----------|------------------|-------------------|
| Ollama | `ollama` | `Endpoint`, `Model` |
| Foundry Local | `foundrylocal` | `Model` |
| Azure OpenAI | `azureopenai` | `Endpoint`, `Deployment`, `ApiKey` (or managed identity) |
| Microsoft Foundry | `foundry` | `Endpoint`, `AgentId` |
| GitHub Copilot | `githubcopilot` | `Token` (PAT with Copilot access) |

> **Note:** `ConnectionStrings:openclawnet-db` is injected automatically by Aspire — do not set it manually.

See **[10-settings.md](./10-settings.md)** for the full Settings UI walkthrough.

---

## Troubleshooting

### `aspire: command not found`

Install the Aspire CLI workload (see prerequisites):

```bash
dotnet workload install aspire
```

### `Docker daemon is not running`

Start Docker Desktop and wait for the whale icon to stop animating, then retry.

### `ollama not connecting` or empty model responses

```bash
# Verify Ollama is reachable
curl http://localhost:11434/api/tags

# If empty, pull a model
ollama pull gemma4:e2b
```

### Port already in use

Aspire dynamically picks ports for most projects, but the dashboard defaults to `15888`. To change it, set the environment variable before launching:

```bash
# PowerShell
$env:DOTNET_DASHBOARD_OTLP_ENDPOINT_URL = "http://localhost:16888"

# bash/zsh
export DOTNET_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:16888"
```

### Build fails with `NETSDK1045: requires .NET 10`

Your installed .NET SDK is older than 10.0. Upgrade from [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0).

### Database is locked or corrupt

```bash
# Stop the AppHost first, then:
rm src/OpenClawNet.AppHost/.data/openclawnet.db
```

The schema is recreated on next launch.

---

## Next Steps

- **[10-settings.md](./10-settings.md)** — Configure the model provider, scheduler, and runtime settings from the Web UI.
- **[20-tools.md](./20-tools.md)** — Explore the built-in tools (FileSystem, Shell, Web, Scheduler).
- **[30-jobs.md](./30-jobs.md)** — Create scheduled and one-shot jobs.

---

## See Also

- [Local Setup Guide](../setup/local-setup.md)
- [Ollama Setup Guide](../setup/ollama.md)
- [Architecture Overview](../architecture/overview.md)
