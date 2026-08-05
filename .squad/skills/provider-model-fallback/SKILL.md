---
name: provider-model-fallback
description: "Propagate model from provider definition to test profiles and implement provider-level model fallback chain."
category: backend
tags: [ollama, model-providers, agent-profiles, test-endpoints, fallback]
examples:
  - "Test endpoint 404s because no model is passed to the provider"
  - "Add model fallback to a new IAgentProvider implementation"
  - "Fix missing model in synthetic AgentProfile used for health-check calls"
enabled: true
---

# Provider Model Fallback Pattern

Use this pattern whenever a provider's `CreateChatClient` must pick a model and the caller may not always supply one.

## Problem

`AgentProfile` objects constructed in-memory for test/health-check endpoints frequently omit `Model`. If a provider reads only from global DI options, the per-provider model configured in the database is silently ignored, causing 404 errors on the Ollama API.

## Solution

### 1. Provider-level fallback chain

Inside `CreateChatClient(AgentProfile profile)`, resolve model in this priority order:

```csharp
var model = profile.Model           // per-call override (from endpoint or runtime)
    ?? _options.Value.Model         // globally configured default (DI/appsettings)
    ?? "gemma4:e2b";                // hardcoded safe fallback
```

### 2. Test-endpoint model propagation

When constructing a synthetic `AgentProfile` for a test call, always copy `Model` from the definition:

**`/api/model-providers/{name}/test`:**
```csharp
var testProfile = new AgentProfile
{
    Provider = def.ProviderType,
    Endpoint = def.Endpoint,
    Model    = def.Model,          // required — provider won't have it otherwise
    ApiKey   = def.ApiKey,
    ...
};
```

**`/api/agent-profiles/{name}/test`:**
```csharp
var testProfile = new AgentProfile
{
    Provider = definition.ProviderType,
    Endpoint = definition.Endpoint,
    Model    = profile.Model ?? definition.Model,  // profile wins, provider is fallback
    ...
};
```

## Rule

Any endpoint that builds a synthetic `AgentProfile` for testing/health purposes **must** explicitly set `Model`. This applies to all providers, not only Ollama.

## Files Where This Pattern Applies

- `src/OpenClawNet.Models.Ollama/OllamaAgentProvider.cs`
- `src/OpenClawNet.Gateway/Endpoints/ModelProviderEndpoints.cs`
- `src/OpenClawNet.Gateway/Endpoints/AgentProfileEndpoints.cs`
- Any future `IAgentProvider` implementation that reads a model parameter
