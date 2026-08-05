# Issue #151 Analysis: Vault Secret References for Model Providers and Agent Profiles

**Analyst:** Irving (Backend Dev)  
**Issue:** #151 - Integrate Vault secret references into Model Providers and Agent Profiles  
**Date:** 2026-05-13  
**Status:** Analysis Complete - Ready for Implementation

---

## Executive Summary

Issue #151 requests enabling vault secret references (vault://) for Model Providers and Agent Profiles so secrets are resolved at runtime rather than stored as plaintext. The existing `VaultConfigurationResolver` pattern already provides the infrastructure—this task extends it to storage entities and provider runtime resolution.

**Scope:** Backend-only changes in Storage and Gateway layers. No UI changes required (UI already supports text input; vault:// references are opaque strings until runtime).

**Complexity:** Low-Medium. The vault:// pattern exists; we're adding new resolution points in the provider creation path.

**Estimated Effort:** 6-8 hours (schema migration, resolver wiring, tests, docs)

---

## Current State

### Existing Vault Infrastructure

1. **VaultConfigurationResolver** (`src/OpenClawNet.Storage/VaultConfigurationResolver.cs`)
   - Parses `vault://SecretName` references from IConfiguration
   - Caches resolved secrets (5-minute TTL)
   - Already wired into Gateway startup: `Program.cs:394`

2. **IVault Interface** (`src/OpenClawNet.Storage/IVault.cs`)
   - `ResolveAsync(name, context, ct)` - audited resolution with caller tracking
   - Implemented by `VaultService` with full audit trail to `SecretAccessAudit` table

3. **Secrets Storage** (`src/OpenClawNet.Storage/SecretsStore.cs`)
   - SQLite-backed secrets with DataProtection encryption
   - Versioning support (Phase 4)
   - Soft-delete and lifecycle management

4. **Pattern Documentation** (`.squad/skills/secrets-vault-pattern/SKILL.md`)
   - Comprehensive guide for vault:// integration
   - Audit requirements, cache invalidation, redaction, error shielding

### Current Runtime Flow (Azure OpenAI Example)

```
User saves provider definition with ApiKey="vault://AzureOpenAI/Key"
  ↓
ModelProviderDefinitionStore.SaveAsync() - stores literal string
  ↓
AgentProfileEndpoints.MapPost("/{name}/test") - test endpoint retrieves definition
  ↓
AzureOpenAIAgentProvider.CreateChatClient(profile)
  ↓
var apiKey = profile.ApiKey ?? opts.ApiKey  ← plaintext expected; vault:// not resolved
  ↓
new AzureOpenAIClient(..., new AzureKeyCredential(apiKey))  ← FAILS if vault:// literal
```

**Problem:** Provider implementations expect plaintext secrets. No resolution happens between storage retrieval and provider instantiation.

---

## Proposed Solution

### Architecture: Runtime Resolution in Provider Creation

**Principle:** Resolve vault:// references immediately before passing credentials to SDK clients, within the provider's `CreateChatClient` method.

**Rationale:**
1. Minimal surface area: All providers funnel through `CreateChatClient`
2. Audit trail: Each resolution logs caller context (VaultCallerType.System, CallerId="ProviderInit:{name}")
3. Cache benefit: Repeated chat client creations reuse cached secrets
4. Error isolation: VaultException surfaces as InvalidOperationException at provider instantiation

### Schema Changes

**ModelProviderDefinition** (`src/OpenClawNet.Storage/Entities/ModelProviderDefinition.cs`)
- No schema change required; existing `ApiKey` and `Endpoint` columns already store strings
- vault:// references are stored as-is (e.g., "vault://AzureKey")

**AgentProfileEntity** (`src/OpenClawNet.Storage/Entities/AgentProfileEntity.cs`)
- No schema change required; existing `ApiKey`, `Endpoint`, `DeploymentName` columns support strings
- vault:// references are opaque at storage layer

**Migration:** None required (purely runtime resolution change)

### Implementation Plan

#### 1. Create Vault-Aware Provider Base Helper

**New File:** `src/OpenClawNet.Models.Abstractions/VaultAwareProviderHelper.cs`

```csharp
public static class VaultAwareProviderHelper
{
    public static async Task<string?> ResolveSecretAsync(
        string? value,
        IVault vault,
        string providerName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (!VaultConfigurationResolver.TryParseVaultReference(value, out var secretName))
            return value; // Plaintext passthrough

        var context = new VaultCallerContext(
            VaultCallerType.System,
            $"ProviderInit:{providerName}",
            null);

        return await vault.ResolveAsync(secretName, context, ct);
    }
}
```

#### 2. Update Provider Implementations

**AzureOpenAIAgentProvider** (`src/OpenClawNet.Models.AzureOpenAI/AzureOpenAIAgentProvider.cs`)

Changes:
- Inject `IVault` via constructor
- Resolve `profile.Endpoint` and `profile.ApiKey` before SDK client creation

```csharp
public sealed class AzureOpenAIAgentProvider : IAgentProvider
{
    private readonly IOptions<AzureOpenAIOptions> _options;
    private readonly ILogger<AzureOpenAIAgentProvider> _logger;
    private readonly IVault _vault;

    public AzureOpenAIAgentProvider(
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIAgentProvider> logger,
        IVault vault)
    {
        _options = options;
        _logger = logger;
        _vault = vault;
    }

    public IChatClient CreateChatClient(AgentProfile profile)
    {
        var opts = _options.Value;
        var endpointRef = profile.Endpoint ?? opts.Endpoint;
        var apiKeyRef = profile.ApiKey ?? opts.ApiKey;
        var authMode = profile.AuthMode ?? opts.AuthMode;
        var deployment = profile.DeploymentName ?? opts.DeploymentName ?? "gpt-5-mini";

        // Resolve vault references synchronously (CreateChatClient is sync)
        var endpoint = VaultAwareProviderHelper.ResolveSecretAsync(
            endpointRef, _vault, ProviderName, CancellationToken.None).GetAwaiter().GetResult();
        var apiKey = VaultAwareProviderHelper.ResolveSecretAsync(
            apiKeyRef, _vault, ProviderName, CancellationToken.None).GetAwaiter().GetResult();

        if (string.IsNullOrEmpty(endpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint not configured.");

        AzureOpenAIClient azureClient;
        if (authMode?.Equals("integrated", StringComparison.OrdinalIgnoreCase) == true)
            azureClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
        else if (!string.IsNullOrEmpty(apiKey))
            azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        else
            throw new InvalidOperationException("Azure OpenAI: no API key configured and not using integrated auth.");

        _logger.LogDebug("Creating Azure OpenAI IChatClient: endpoint={Endpoint}, deployment={Deployment}", endpoint, deployment);

        var innerClient = azureClient.GetChatClient(deployment).AsIChatClient();
        return new ChatClientBuilder(innerClient)
            .UseOpenTelemetry(sourceName: "OpenClawNet.AzureOpenAI")
            .Build();
    }
}
```

**Similar changes needed for:**
- `OllamaAgentProvider` (endpoint resolution)
- `FoundryAgentProvider` (endpoint + API key)
- `FoundryLocalAgentProvider` (endpoint)
- `GitHubCopilotAgentProvider` (likely no secrets, but audit for completeness)

#### 3. Gateway Endpoint Updates

**ModelProviderEndpoints** (`src/OpenClawNet.Gateway/Endpoints/ModelProviderEndpoints.cs`)

Test endpoint (`POST /api/model-providers/{name}/test`) already constructs a test profile and passes it to provider.CreateChatClient—no changes needed. Vault resolution happens transparently.

**AgentProfileEndpoints** (`src/OpenClawNet.Gateway/Endpoints/AgentProfileEndpoints.cs`)

Test endpoint (`POST /api/agent-profiles/{name}/test`) also constructs a profile and delegates to provider—no changes needed.

#### 4. Documentation Updates

**Create:** `docs/architecture/vault-provider-integration.md`
- Document vault:// syntax for provider/profile secrets
- Runtime resolution flow diagram
- Security considerations (no plaintext in DB, audit trail)
- Migration guide for existing deployments

**Update:** `docs/testing/e2e-test-index.md`
- Add test entries for vault-aware provider tests (see Test Plan below)

---

## Test Plan

### Unit Tests

**New File:** `tests/OpenClawNet.UnitTests/Models/VaultAwareProviderTests.cs`

Scenarios:
1. `ResolveSecretAsync_PlaintextValue_ReturnsUnchanged`
2. `ResolveSecretAsync_VaultReference_ResolvesFromVault`
3. `ResolveSecretAsync_MissingSecret_ThrowsVaultException`
4. `ResolveSecretAsync_NullValue_ReturnsNull`

**Update:** `tests/OpenClawNet.UnitTests/Models/AzureOpenAIAgentProviderTests.cs`

New scenarios:
1. `CreateChatClient_WithVaultEndpoint_ResolvesAndCreatesClient`
2. `CreateChatClient_WithVaultApiKey_ResolvesAndCreatesClient`
3. `CreateChatClient_WithMissingVaultSecret_ThrowsInvalidOperationException`

### Integration Tests

**New File:** `tests/OpenClawNet.IntegrationTests/VaultProviderIntegrationTests.cs`

End-to-end flow:
1. Seed secret via `ISecretsStore.SetAsync("TestAzureKey", "fake-key-value")`
2. Create ModelProviderDefinition with `ApiKey = "vault://TestAzureKey"`
3. Call `POST /api/model-providers/{name}/test`
4. Assert: test fails gracefully (fake key rejected by Azure), but vault resolution succeeded
5. Verify audit trail: `SecretAccessAudit` contains row with CallerType=System, CallerId=ProviderInit:azure-openai

### E2E Tests

**Update:** `tests/OpenClawNet.E2ETests/SecretsVaultPhase4E2ETests.cs`

New test: `ProviderDefinitionWithVaultReference_ResolvesAtRuntime`

1. Create secret: `PUT /api/secrets/E2EProviderKey` with value "test-key"
2. Create provider: `PUT /api/model-providers/e2e-azure` with ApiKey="vault://E2EProviderKey"
3. Retrieve provider: `GET /api/model-providers/e2e-azure`
4. Assert: response.HasApiKey=true (does not expose plaintext)
5. Query `ISecretsStore` directly: verify ApiKey column stores "vault://E2EProviderKey"
6. Query audit table: verify resolution was logged

### Manual Testing Checklist

1. Create Azure OpenAI secret via Secrets Vault UI
2. Create Model Provider with vault:// reference for endpoint/key
3. Test provider connection—should resolve and attempt authentication
4. Rotate secret in vault
5. Test provider again—should pick up new value after cache TTL (5 min)
6. Delete secret from vault
7. Test provider—should fail with clear error (not expose vault:// literal to Azure SDK)

---

## Security Considerations

### Threat Model Compliance

| Gate | How Satisfied |
|------|---------------|
| No plaintext in DB | ✅ vault:// reference stored, not secret value |
| No plaintext in logs | ✅ IVault redactor tracks resolved values; VaultService never logs plaintext |
| Audit trail | ✅ Every resolution writes to SecretAccessAudit with caller context |
| No LLM exposure | ✅ Providers resolve secrets before SDK client init; never passed to LLM context |
| Least privilege | ✅ VaultCallerType.System isolates provider init from tool/config paths |
| Fail-safe errors | ✅ VaultException masked as "configuration unavailable" in user-facing errors |

### Cache Invalidation

VaultConfigurationResolver already implements 5-minute TTL with version-based invalidation. Provider resolutions reuse this cache:

- `ISecretsStore.SetAsync` → `IVaultCacheInvalidator.Invalidate(name)` → version bump → cache miss on next resolve
- Rotation/delete operations trigger immediate invalidation
- New chat client creations within TTL reuse cached secret (performance benefit)

---

## Rollout Strategy

### Phase 1: Backend Implementation (This Issue)

1. Add `VaultAwareProviderHelper` to Models.Abstractions
2. Update 5 provider implementations (Azure, Ollama, Foundry, FoundryLocal, GitHubCopilot)
3. Add unit tests for vault resolution logic
4. Add integration test for end-to-end provider vault flow
5. Update E2E test for storage-level vault:// persistence

**Branch:** `squad/151-vault-provider-integration`  
**Worktree:** Not required (single-issue focus, backend-only, no UI conflicts)  
**PR Target:** `dev` (per git-workflow skill)

### Phase 2: Documentation (This Issue)

1. Create `docs/architecture/vault-provider-integration.md`
2. Update `docs/testing/e2e-test-index.md` with new test entries
3. Update `.squad/skills/secrets-vault-pattern/SKILL.md` with provider examples

### Phase 3: Validation (Post-Merge)

1. Manual smoke test with real Azure OpenAI credentials in vault
2. Verify audit log shows provider init resolutions
3. Confirm cache behavior (rotate secret, observe 5-min pickup delay)

---

## Build and Test Commands

### Full Build
```powershell
# From repo root (plan repo is now canonical per PR #133)
dotnet build OpenClawNet.slnx
```

**Note:** Per Irving history (2026-05-06), plan repo currently has 52 build errors due to incomplete migration. Focus on incremental build for changed projects:

```powershell
dotnet build src\OpenClawNet.Models.Abstractions
dotnet build src\OpenClawNet.Models.AzureOpenAI
dotnet build src\OpenClawNet.Storage
dotnet build src\OpenClawNet.Gateway
```

### Run Unit Tests
```powershell
dotnet test tests\OpenClawNet.UnitTests --filter "FullyQualifiedName~VaultAwareProvider"
dotnet test tests\OpenClawNet.UnitTests --filter "FullyQualifiedName~AzureOpenAIAgentProvider"
```

### Run Integration Tests
```powershell
dotnet test tests\OpenClawNet.IntegrationTests --filter "FullyQualifiedName~VaultProviderIntegration"
```

### Run E2E Tests (Vault Category)
```powershell
# Per e2e-test-index.md and team decisions:
# 1. Aspire must be running (aspire start)
# 2. Azure OpenAI credentials required for live tests

dotnet test tests\OpenClawNet.E2ETests --filter "Category=Vault"
```

---

## Open Questions / Decisions Needed

### 1. Sync vs Async in CreateChatClient

**Problem:** `IAgentProvider.CreateChatClient` is synchronous, but vault resolution is async.

**Options:**
- A) Use `.GetAwaiter().GetResult()` (proposed above)
- B) Change IChatClient signature to async (breaking change, ripple effects)
- C) Pre-resolve secrets in caller (RuntimeAgentProvider), not in provider implementation

**Recommendation:** Option A for Phase 1 (minimal change), consider B for future refactor.

### 2. Partial Vault References

**Problem:** User configures `Endpoint="https://my-azure.openai.azure.com"` (plaintext) but `ApiKey="vault://Key"` (reference).

**Behavior:** Current design allows mixing plaintext and vault references. VaultAwareProviderHelper only resolves vault:// prefixes; plaintext passes through unchanged.

**Decision:** Accept mixed mode. Document in `vault-provider-integration.md` that vault:// is optional per field.

### 3. Error Messages to User

**Problem:** Provider test fails due to missing vault secret. What does user see?

**Current:** `InvalidOperationException: "Vault secret resolution failed."` (generic)

**Improvement:** Catch VaultException in provider, wrap with context: `throw new InvalidOperationException($"Failed to resolve vault secret for {ProviderName}. Check that secret '{secretName}' exists and is accessible.")`

**Decision:** Implement context-aware error wrapping in VaultAwareProviderHelper.

---

## Dependencies

### External
- None (all infrastructure exists)

### Internal
- Storage layer must be initialized and migrated (SchemaMigrator applied)
- Secrets Vault Phase 4 endpoints must be functional (`/api/secrets/*`)
- IVault, ISecretsStore, VaultService already registered in DI (Gateway Program.cs)

### Coordination
- None (backend-only, no UI or other team dependencies)

---

## Success Criteria

1. ✅ User can save Model Provider with `ApiKey="vault://SecretName"`
2. ✅ Provider test endpoint resolves vault reference and attempts connection
3. ✅ Audit log shows resolution with CallerType=System, CallerId=ProviderInit:{provider}
4. ✅ Database stores vault:// reference, not plaintext
5. ✅ Cache invalidation works (rotate secret, new value used after TTL)
6. ✅ Missing secret produces clear error (not 500 with stack trace to user)
7. ✅ Unit tests cover vault resolution logic (4+ scenarios)
8. ✅ Integration test validates end-to-end flow
9. ✅ E2E test updated in e2e-test-index.md
10. ✅ Documentation created: vault-provider-integration.md

---

## Next Steps

1. **Create worktree/branch** (if needed): `git checkout -b squad/151-vault-provider-integration`
2. **Implement VaultAwareProviderHelper** (Models.Abstractions)
3. **Update 5 provider implementations** (inject IVault, call helper)
4. **Write unit tests** (VaultAwareProviderTests, update existing provider tests)
5. **Write integration test** (VaultProviderIntegrationTests)
6. **Update E2E test** (SecretsVaultPhase4E2ETests)
7. **Create documentation** (vault-provider-integration.md)
8. **Update e2e-test-index.md** (add new test rows)
9. **Run full test suite** (Unit → Integration → E2E)
10. **Create PR to dev** with `Closes #151` in description

**DO NOT close issue #151 in PR**—per user request, issue stays open for tracking after merge.

---

## References

- Issue: https://github.com/elbruno/openclawnet-plan/issues/151
- VaultConfigurationResolver: `src/OpenClawNet.Storage/VaultConfigurationResolver.cs`
- Secrets Vault Pattern Skill: `.squad/skills/secrets-vault-pattern/SKILL.md`
- Git Workflow Skill: `.copilot/skills/git-workflow/SKILL.md`
- E2E Test Index: `docs/testing/e2e-test-index.md`
- Team Decisions: `.squad/decisions.md` (lines 1-100)
