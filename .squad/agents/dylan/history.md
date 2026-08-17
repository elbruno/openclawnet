# Dylan – Test Engineer History

## 2026-08-17 — Issue #230: Foundry and Azure OpenAI Test Connection regression suite

### Bug anatomy
Issue #230 reports "Test failed: Foundry is not configured. Set Endpoint and ApiKey." and
"Test failed: Azure OpenAI: no API key configured and not using integrated auth." when the user
fills in endpoint + API key via the Model Providers UI and immediately clicks Test Connection.

**Root cause (confirmed by code trace):**
The `POST /api/model-providers/{name}/test` endpoint reads credentials from the *persisted*
`ModelProviderDefinition` via `store.GetAsync(name)`. The seeded defaults
(`foundry-default`, `azure-openai-default`) have no `Endpoint` or `ApiKey`. When a user enters
values in the edit form and clicks Test Connection *without saving first*, the stored definition
still has null fields, so `testProfile.ApiKey = null` → `CreateChatClient` guard fires.

The provider-level logic itself is correct: both `FoundryAgentProvider.CreateChatClient` and
`AzureOpenAIAgentProvider.CreateChatClient` honour `profile.Endpoint ?? opts.Endpoint` and
`profile.ApiKey ?? opts.ApiKey` when non-null. The production fix (Petey's domain) is to either
auto-save before testing or extend the test endpoint to accept inline credentials.

**Explicitly out of scope for test files:** the seam fix itself belongs to production code.

### Tests added

**`tests/OpenClawNet.UnitTests/Models/FoundryAgentProviderTests.cs`** (new file)
- `ProviderName_IsFoundry`
- `IsAvailableAsync_ReturnsFalse_WhenEndpointEmpty`
- `IsAvailableAsync_ReturnsFalse_WhenApiKeyEmpty`
- `CreateChatClient_UsesProfileEndpointAndApiKey_WhenOptsAreEmpty` ← Issue #230 regression
- `CreateChatClient_UsesProfileApiKey_WhenOnlyOptsApiKeyIsEmpty`
- `CreateChatClient_UsesProfileEndpoint_WhenOnlyOptsEndpointIsEmpty`
- `CreateChatClient_Throws_WhenNeitherProfileNorOptsHaveEndpoint`
- `CreateChatClient_Throws_WhenNeitherProfileNorOptsHaveApiKey`

**`tests/OpenClawNet.UnitTests/Models/AzureOpenAIAgentProviderTests.cs`** (additions)
- `CreateChatClient_UsesProfileApiKey_WhenOptsApiKeyEmpty` ← Issue #230 regression
- `CreateChatClient_UsesProfileEndpoint_WhenOptsEndpointEmpty`
- `CreateChatClient_UsesProfileEndpointAndApiKey_WhenBothOptsAreEmpty`

**`tests/OpenClawNet.UnitTests/Gateway/ModelProviderEndpointTests.cs`** (additions)
- `PostTest_ForFoundry_ForwardsStoredEndpointAndApiKeyToProvider` ← Issue #230 regression
- `PostTest_ForAzureOpenAI_ForwardsStoredEndpointAndApiKeyToProvider` ← Issue #230 regression
- `PostTest_ForFoundry_WithNoCredentials_ReportsConfigurationError`
- `PostTest_ForAzureOpenAI_WithNoApiKey_ReportsApiKeyError`

### Run result
18 new tests pass. 4 pre-existing `AzureOpenAILiveTests` (Category=Live, require real credentials)
remain skipped/failed as before — not caused by these changes. 312 non-live tests pass.

### Durable learnings

1. **Profile fields MUST be tested, not just DI opts.** `FoundryAgentProvider` and
   `AzureOpenAIAgentProvider` both fall back `profile.Field ?? opts.Field`. Tests that only
   exercise the DI-opts path give false confidence that the UI-configured (DB-stored) path works.

2. **CapturingAgentProvider throws its own message**, not the provider's guard. If you need to
   assert on the specific error text from a real provider guard, use a test app wired with the
   real provider (see `CreateTestAppWithRealFoundryProviderAsync`).

3. **`CreateTestAppWithCapturingProviderAsync` now accepts a `providerType` parameter.**
   Pass `"foundry"` or `"azure-openai"` to route the test endpoint to the capturing provider.

4. **Seeded defaults have no credentials.** `SeedDefaultsAsync` deliberately omits `Endpoint`
   and `ApiKey` from `foundry-default` and `azure-openai-default`. Any test that saves then
   immediately tests these defaults must supply the fields explicitly.

5. **Production seam dependency on Petey:** the UI workflow (test-without-save) requires either
   an inline-credentials overload on the test endpoint, or auto-save-before-test in the Blazor
   component. Neither can be covered by unit tests alone — flag to Petey before merging #230 fix.
