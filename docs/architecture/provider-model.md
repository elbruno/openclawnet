# Provider Model

## Primary Interface — `IAgentProvider`

The primary model provider abstraction is `IAgentProvider`. All model providers implement this interface:

```csharp
public interface IAgentProvider
{
    string ProviderName { get; }
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct);
    IAsyncEnumerable<ChatResponseChunk> StreamAsync(ChatRequest request, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
```

> **Note:** `IModelClient` still exists in the codebase as a lower-level interface, but `IAgentProvider` is the primary abstraction used for provider registration, agent profile resolution, and runtime routing.

---

## Available Providers

| Provider | Project | Class | Type | Notes |
|----------|---------|-------|------|-------|
| **Ollama** | `OpenClawNet.Models.Ollama` | `OllamaAgentProvider` | Local REST | Default; no API key needed |
| **FoundryLocal** | `OpenClawNet.Models.FoundryLocal` | `FoundryLocalAgentProvider` | Local inference | On-device Foundry runtime |
| **Azure OpenAI** | `OpenClawNet.Models.AzureOpenAI` | `AzureOpenAIAgentProvider` | Cloud (Azure) | Via `Azure.AI.OpenAI` SDK |
| **Foundry** | `OpenClawNet.Models.Foundry` | `FoundryAgentProvider` | Cloud (Azure AI) | OpenAI-compatible endpoint |
| **GitHub Copilot** | `OpenClawNet.Models.GitHubCopilot` | `GitHubCopilotAgentProvider` | Cloud (GitHub) | Via `CopilotChatClient` |
| **RuntimeAgentProvider** | `OpenClawNet.Gateway` | `RuntimeAgentProvider` | Router | Routes to active provider based on settings |

**LM Studio** support is planned.

---

## Multi-Instance Model Providers

Providers support **multiple named configurations** via the `ModelProviderDefinition` entity stored in SQLite. This allows operators to configure several instances of the same provider type (e.g., two different Azure OpenAI deployments or multiple Ollama endpoints):

```csharp
public class ModelProviderDefinition
{
    public string Name { get; set; }         // e.g., "ollama-local", "azure-gpt4o", "azure-embedding"
    public string ProviderType { get; set; } // e.g., "ollama", "azure-openai", "foundry"
    public string Endpoint { get; set; }
    public string Model { get; set; }
    public string? ApiKey { get; set; }
    // ... additional config
}
```

Model provider definitions are managed via the **Model Providers** settings page in the Control UI, or via the REST API (`/api/model-providers`).

---

## Agent Profiles

**`AgentProfile`** is a named agent definition that combines a provider reference with instructions and tool filtering. Stored in SQLite:

```csharp
public class AgentProfile
{
    public string Name { get; set; }              // e.g., "code-assistant", "summarizer"
    public string? AgentProfileName { get; set; } // provider definition to use
    public string? Endpoint { get; set; }         // provider endpoint URL override
    public string? ApiKey { get; set; }           // API key for the provider
    public string? DeploymentName { get; set; }   // deployment name (Azure OpenAI)
    public string? AuthMode { get; set; }         // "apikey" or "integrated"
    public string? Instructions { get; set; }     // custom system prompt additions
    public string? ToolFilter { get; set; }       // comma-separated allowed tool names
}
```

The expanded `AgentProfile` now carries full provider connection details (`Endpoint`, `ApiKey`, `DeploymentName`, `AuthMode`), all persisted in SQLite. This enables per-profile endpoint overrides without relying solely on DI-registered options.

Agent profiles are managed via the **Agent Profiles** settings page, or imported from Markdown files using `AgentProfileMarkdownParser` (Markdown with YAML front-matter).

When a chat session or scheduled job references an `AgentProfileName`, the runtime resolves the associated `ModelProviderDefinition` and applies the profile's instructions and tool filtering.

---

## Provider Resolution Flow

```
Chat request arrives (with optional AgentProfileName)
    │
    ▼
ProviderResolver.ResolveAsync(profileName)
    │
    ├── AgentProfileName specified?
    │     ├── Yes → Load AgentProfile from DB
    │     │         ├── Resolve ModelProviderDefinition by profile's provider reference
    │     │         ├── Apply instructions + tool filter
    │     │         └── Build ResolvedProviderConfig (endpoint, model, key, deployment)
    │     └── No  → Use default active provider from settings
    │
    ▼
RuntimeModelSettings.Update(resolvedConfig)
    │   ← Syncs DB-based definition to the runtime singleton
    │
    ▼
Resolved IAgentProvider instance (configured with correct endpoint)
    │
    ▼
IAgentProvider.CompleteAsync() or StreamAsync()
```

> **Key detail:** The `ProviderResolver` bridges the DB-stored `ModelProviderDefinition` to the runtime. `ChatStreamEndpoints` and `ChatEndpoints` both call `RuntimeModelSettings.Update()` with the resolved provider config before invoking the orchestrator, ensuring the provider uses the definition's endpoint — not just the DI-registered default.

---

## Provider Configuration

### Ollama (Default — local, no API key)
```json
{
  "Model": {
    "Provider": "ollama",
    "Model": "llama3.2",
    "Endpoint": "http://localhost:11434"
  }
}
```

### FoundryLocal (local on-device inference)
```json
{
  "Model": {
    "Provider": "foundrylocal",
    "Model": "phi-3.5-mini",
    "Endpoint": "http://localhost:5273"
  }
}
```

FoundryLocal runs the Foundry inference runtime locally — no cloud connectivity required. It supports a subset of OpenAI-compatible models optimized for on-device execution.

### Azure OpenAI (cloud)
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com",
    "ApiKey": "your-api-key",
    "DeploymentName": "gpt-4o"
  }
}
```

### Foundry (Azure AI cloud endpoint)
```json
{
  "Foundry": {
    "Endpoint": "https://your-foundry-endpoint.inference.ai.azure.com",
    "ApiKey": "your-api-key",
    "Model": "your-model-name"
  }
}
```

---

## Per-Definition Endpoint Override

All providers now follow a consistent endpoint resolution order:

1. **`profile.Endpoint`** — If the `AgentProfile` (or its resolved `ModelProviderDefinition`) specifies an endpoint, that takes priority.
2. **DI-registered options** — If no profile endpoint is set, the provider falls back to its injected options (e.g., `OllamaOptions.Endpoint` from `appsettings.json`).

This means `ModelProviderDefinition` endpoints are used for **both** the test endpoint and the chat flow. Previously, the test endpoint would use the definition's endpoint while chat used the DI default — leading to configuration mismatches.

### Example: Two Ollama Instances

```
ModelProviderDefinition "ollama-local"
  → Endpoint: http://localhost:11434
  → Model: llama3.2

ModelProviderDefinition "ollama-remote"
  → Endpoint: http://gpu-server:11434
  → Model: llama3.2

AgentProfile "code-assistant"
  → Provider: ollama-remote
  → Endpoint: http://gpu-server:11434   ← resolved from definition
```

When a chat request specifies the `code-assistant` profile, the provider uses `http://gpu-server:11434` — not the DI-registered default of `localhost:11434`.

### How It Works

```
ProviderResolver resolves definition name → ModelProviderDefinition
    │
    ▼
ChatStreamEndpoints / ChatEndpoints call RuntimeModelSettings.Update()
    │   with: endpoint, model, apiKey, deploymentName from the definition
    │
    ▼
Provider.CreateChatClient(profile)
    │   checks profile.Endpoint first, then falls back to DI options
    │
    ▼
Correct endpoint used for the LLM call
```

---

## Fallback Chain

The model layer supports a **fallback chain** — when the primary provider is unavailable, the system automatically retries with the next provider in the chain. This provides graceful degradation without user-visible errors.

### Configuration

```json
{
  "Model": {
    "Primary": "ollama",
    "Fallbacks": ["foundrylocal", "azure-openai"]
  }
}
```

### How It Works

```
Request arrives at RuntimeAgentProvider
    │
    ▼
Try Primary provider (Ollama)
    ├── Available + responds ✅  → Use response
    └── Unavailable or error ❌
        │
        ▼
    Try Fallback[0] (FoundryLocal)
        ├── Available + responds ✅  → Use response
        └── Unavailable or error ❌
            │
            ▼
        Try Fallback[1] (Azure OpenAI)
            ├── Available + responds ✅  → Use response
            └── All providers exhausted → throw ModelUnavailableException
```

### Availability Check

Before each attempt, `IAgentProvider.IsAvailableAsync()` is called. The check is fast (HEAD request or local health check) to minimize added latency on the happy path.

### Recommended Chain Configurations

| Scenario | Primary | Fallback 1 | Fallback 2 |
|---------|---------|-----------|-----------|
| **Local-first developer** | Ollama | FoundryLocal | Azure OpenAI |
| **On-device + cloud backup** | FoundryLocal | Azure OpenAI | — |
| **Cloud-primary** | Azure OpenAI | Foundry | Ollama |
| **Fully offline** | Ollama | FoundryLocal | — |
| **GitHub Copilot** | GitHub Copilot | Ollama | — |

---

## Model Selection Strategy

The model selection strategy governs which model variant is used within a provider and how to handle model-specific routing.

### Primary Model

The primary model is configured per provider. For Ollama, this is the model tag (e.g., `llama3.2`, `mistral`, `phi3`). For Azure OpenAI, this is the deployment name.

### Per-Request Routing (Future)

The architecture supports per-request model routing rules — for example, routing summarization tasks to a cheaper/faster model and generation tasks to a larger model:

```json
{
  "ModelRouting": {
    "Summarization": "ollama/phi3",
    "Completion": "ollama/llama3.2",
    "Embeddings": "ollama/nomic-embed-text"
  }
}
```

This allows cost optimization (use small models for internal tasks) and capability routing (use embedding-specific models for vector operations).

### Runtime Model Switching

The active provider and model can be changed at runtime without restart using `RuntimeAgentProvider` and the settings API:

```http
PATCH /api/settings/model
{
  "provider": "azure-openai",
  "model": "gpt-4o"
}
```

This updates the `ModelProviderDefinition` entity in SQLite and takes effect on the next request. The Gateway re-resolves the `IAgentProvider` implementation from the updated settings.

Under the hood, `ProviderResolver` converts the definition name into a full `ResolvedProviderConfig` and calls `RuntimeModelSettings.Update()` to sync the runtime. This ensures both the test endpoint and the chat flow use the same resolved endpoint and credentials — eliminating the previous mismatch where tests could pass against a definition's endpoint while chat used the DI-registered default.

---

## Switching Providers

### At Startup (DI Registration)

Provider selection is configured at startup via DI. The Gateway's `Program.cs` registers the desired provider:

```csharp
// Local (default)
builder.Services.AddOllama();

// FoundryLocal (on-device)
builder.Services.AddFoundryLocal();

// Azure OpenAI
builder.Services.AddAzureOpenAI(options => { ... });

// Foundry (cloud)
builder.Services.AddFoundry(options => { ... });

// GitHub Copilot
builder.Services.AddGitHubCopilot(options => { ... });
```

### With Fallback Chain

```csharp
builder.Services
    .AddAgentProvider(primary: "ollama")
    .WithFallback("foundrylocal")
    .WithFallback("azure-openai");
```

### At Runtime (Settings API)

Use `RuntimeAgentProvider` — the outer wrapper that reads the active `ModelProviderDefinition` and delegates to the currently active inner provider:

```csharp
// RuntimeAgentProvider re-resolves the inner IAgentProvider on each call
// based on the current ModelProviderDefinition value in the database.
// No restart required.
```

The settings can be updated via the **Model Providers** page in the Control UI or via the REST API endpoint above.
