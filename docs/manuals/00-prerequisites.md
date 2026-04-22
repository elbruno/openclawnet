# Prerequisites

Before installing OpenClaw .NET, ensure your system meets the following hardware and software requirements.

---

## Hardware Requirements

### Minimum
- **RAM:** 8 GB
- **CPU:** 4 cores
- **Free Disk Space:** 20 GB (for Ollama models and development environment)

### Recommended
- **RAM:** 16 GB or more
- **CPU:** 8 cores or more
- **Free Disk Space:** 50 GB or more
- **GPU:** Optional but recommended for faster Ollama inference (NVIDIA GPU with CUDA support)

> **Note:** Local LLM models (Ollama) require significant disk space. Each model ranges from 2–8 GB. Plan accordingly.

---

## Software Prerequisites

### .NET 10 SDK

**Version Required:** .NET 10.0 or later

**Install:**
- Windows/macOS/Linux: [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)

**Verify Installation:**
```bash
dotnet --version
# Expected output: 10.0.x or higher
```

---

### .NET Aspire CLI

**Version Required:** Latest stable release (13.2+)

For up-to-date install instructions across Windows, macOS, and Linux, follow the official Aspire documentation. We intentionally don't inline the commands here so this page never goes stale:

- **Official site:** [https://aspire.dev/](https://aspire.dev/)

**Verify Installation:**
```bash
aspire --version
```

---

### Docker Desktop

**Purpose:** Required for running the Ollama container and other Aspire-managed services.

**Install:**
- Windows: [https://docs.docker.com/desktop/install/windows-install/](https://docs.docker.com/desktop/install/windows-install/)
- macOS: [https://docs.docker.com/desktop/install/mac-install/](https://docs.docker.com/desktop/install/mac-install/)
- Linux: [https://docs.docker.com/desktop/install/linux-install/](https://docs.docker.com/desktop/install/linux-install/)

**Verify Installation:**
```bash
docker --version
# Expected output: Docker version 20.x or higher

docker ps
# Should list running containers (or empty list if none running)
```

> **Important:** Ensure Docker Desktop is running before starting OpenClaw .NET.

---

### Ollama (Local LLM Runtime)

**Purpose:** Provides local model inference for privacy-first AI interactions.

**Install:**
- All Platforms: [https://ollama.ai/download](https://ollama.ai/download)

**Verify Installation:**
```bash
ollama --version
# Expected output: ollama version x.x.x

ollama list
# Lists installed models (empty on first install)
```

**Recommended Models:**

Pull at least one model before running OpenClaw .NET:

```bash
# Best for tool-use (recommended default)
ollama pull gemma4:e2b

# Alternative: lightweight and fast
ollama pull llama3.2:3b

# Alternative: larger, more capable
ollama pull llama3.2
```

**Official Documentation:**
- [https://github.com/ollama/ollama](https://github.com/ollama/ollama)

---

### Git

**Version Required:** 2.30 or later

**Install:**
- Windows: [https://git-scm.com/download/win](https://git-scm.com/download/win)
- macOS: Pre-installed or via Homebrew (`brew install git`)
- Linux: `sudo apt install git` (Debian/Ubuntu) or `sudo yum install git` (RHEL/CentOS)

**Verify Installation:**
```bash
git --version
# Expected output: git version 2.x or higher
```

---

### Terminal / Shell

**Windows:**
- PowerShell 7+ (recommended): [https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows)
- Windows Terminal (recommended): Available via Microsoft Store

**macOS/Linux:**
- Bash, Zsh, or any modern shell (pre-installed)

**Verify (PowerShell 7+ on Windows):**
```powershell
$PSVersionTable.PSVersion
# Expected: Major version 7 or higher
```

---

### Code Editor / IDE

Choose one:

| IDE | Recommended For | Download Link |
|-----|----------------|---------------|
| **Visual Studio 2026** | Full-featured .NET development (Windows/macOS) | [https://visualstudio.microsoft.com/](https://visualstudio.microsoft.com/) |
| **Visual Studio Code** | Lightweight, cross-platform | [https://code.visualstudio.com/](https://code.visualstudio.com/) |
| **JetBrains Rider** | Advanced .NET IDE (paid) | [https://www.jetbrains.com/rider/](https://www.jetbrains.com/rider/) |

**For VS Code Users (optional):** The **C# Dev Kit** extension is recommended for a richer .NET editing experience (solution explorer, test runner, debugging UI), but it is **not required** to build or run OpenClaw .NET. The base **C#** extension (powered by the .NET language server) is sufficient. Install C# Dev Kit only if you want the extra IDE features.

---

## Optional Prerequisites

### Azure Subscription (for Azure OpenAI)

**Purpose:** Required only if you want to use Azure OpenAI models instead of local Ollama.

**Sign Up:**
- [https://azure.microsoft.com/free/](https://azure.microsoft.com/free/)

**Azure OpenAI Service:**
- [https://learn.microsoft.com/en-us/azure/ai-services/openai/](https://learn.microsoft.com/en-us/azure/ai-services/openai/)

You'll need:
- Azure OpenAI resource deployed
- Endpoint URL and API Key (or managed identity)
- Deployed model (e.g., `gpt-4`, `gpt-4o-mini`)

---

### GitHub Account (for GitHub Copilot Provider)

**Purpose:** Required only if you want to use GitHub Copilot as a model provider.

**Sign Up:**
- [https://github.com/](https://github.com/)

**GitHub Copilot:**
- [https://github.com/features/copilot](https://github.com/features/copilot)

---

### Microsoft Foundry Resource (for Foundry Provider)

**Purpose:** Required only if you want to use Microsoft Foundry for agent deployment and evaluation.

**Documentation:**
- [https://learn.microsoft.com/en-us/azure/ai-services/foundry/](https://learn.microsoft.com/en-us/azure/ai-services/foundry/)

---

## Quick Checklist

Before proceeding to installation, ensure you have:

- [ ] .NET 10 SDK installed (`dotnet --version`)
- [ ] Aspire CLI installed (`aspire --version`)
- [ ] Docker Desktop installed and running (`docker ps`)
- [ ] Ollama installed with at least one model (`ollama list`)
- [ ] Git installed (`git --version`)
- [ ] A modern terminal (PowerShell 7+ on Windows, bash/zsh on macOS/Linux)
- [ ] Code editor (VS, VS Code, or Rider)
- [ ] (Optional) Azure subscription with Azure OpenAI deployed
- [ ] (Optional) GitHub account with Copilot access
- [ ] (Optional) Microsoft Foundry resource provisioned

---

## Next Steps

Once all prerequisites are met, proceed to **[01-local-installation.md](./01-local-installation.md)** to install and run OpenClaw .NET.

---

## See Also

- [Architecture Overview](../architecture/overview.md)
- [Local Setup Guide](../setup/local-setup.md)
- [Ollama Setup Guide](../setup/ollama-setup.md)