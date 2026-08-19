# Petey — Agent Platform Specialist History

## 2026-08-19 — Issue #233: GitHub.Copilot.SDK 1.0.9 Security Upgrade

### Problem
`OpenClawNet.Models.GitHubCopilot` was pinned to `GitHub.Copilot.SDK` 0.3.0, carrying old
namespace usage and previously flagged transitive advisory risk (`MessagePack`/`Nerdbank.MessagePack`).

### Migration
- Upgraded package: `GitHub.Copilot.SDK` **0.3.0 → 1.0.9**.
- Namespace migration: `using GitHub.Copilot.SDK;` → `using GitHub.Copilot;`.
- API migration:
  - `CopilotSession.On(...)` now called as `On<SessionEvent>(...)`.
  - `CopilotClientOptions.CliPath` replacement:
    `Connection = RuntimeConnection.ForStdio(path: options.CliPath)`.
- Session compatibility hardening for 1.0.9:
  - `EnableManagedSettings = false` when using `PermissionHandler.ApproveAll`.
  - `InfiniteSessions.Enabled = false` preserved to keep request-scoped session behavior.

### Tests Added
- Added focused tests for migration seams:
  - `BuildClientOptions` CLI path/token mapping
  - `CreateSessionConfig` managed settings, streaming, and infinite-session defaults

### Validation
- `OpenClawNet.UnitTests` non-live suite: **Passed** (`Passed: 1151, Skipped: 46, Failed: 0`).
- `OpenClawNet.UnitTests.Azure`: **Passed** (`Passed: 12, Failed: 0`).
- `scripts/ImageGenerator`: build + prompt list + `--dry-run all` smoke checks passed.

### Advisory Outcome
- `OpenClawNet.Models.GitHubCopilot` now shows **no** `MessagePack`/`Nerdbank.MessagePack`
  transitive dependencies and no vulnerable packages from configured feeds.
- Solution-wide transitive versions resolved to `MessagePack 2.5.302` and
  `Nerdbank.MessagePack 1.2.4`, with no vulnerable-package findings.

---

## 2026-08-17 — Issue #230: "Foundry/Azure OpenAI not configured" on Test Connection

### Problem
Reporter configured "foundry-default" and "azure-openai-default" via the UI edit form with
endpoint, API key, and model (Phi-4-reasoning), then clicked "Test Connection" and received:
- `Foundry is not configured. Set Endpoint and ApiKey.`
- `Azure OpenAI: no API key configured and not using integrated auth.`

### Root Cause
**Test Connection in edit mode tested STORED values, not current form values.**

The `TestProvider()` Blazor method always POSTed to `POST /api/model-providers/{name}/test`
with no body. The gateway endpoint read the definition from the database. The seeded defaults
("foundry-default", "azure-openai-default") have **no Endpoint and no ApiKey**. When a user
fills in the form but clicks Test before (or without) clicking "Update Provider", the stored
record still has null Endpoint and null ApiKey → both providers throw their "not configured"
guard.

The providers throw:
- `FoundryAgentProvider.CreateChatClient`: `string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey)`
- `AzureOpenAIAgentProvider.CreateChatClient`: no apiKey and authMode ≠ "integrated"

### Fix
Two-file fix, no breaking changes to list-view test path:

1. **`OpenClawNet.Gateway/Endpoints/ModelProviderEndpoints.cs`**  
   Changed `POST /{name}/test` to accept an optional `ModelProviderTestOverrides?` body.
   When provided, non-null/non-sentinel values override the stored definition for the duration
   of the test (not persisted). The `"[vault-backed]"` sentinel is ignored so vault references
   are resolved from the store, not replaced with the UI placeholder.

2. **`OpenClawNet.Web/Components/Pages/ModelProviders.razor`**  
   When `_isEditing && _form.Name == name`, `TestProvider` now calls `PostAsJsonAsync` with
   current form values as the override body. List-view test buttons still call `PostAsync` with
   null body (unchanged behaviour).

### What Worked
- Gateway builds clean (0 errors, 47 pre-existing warnings).
- Web builds clean (0 errors, 0 warnings).
- 43 Foundry/ModelProvider/NormalizeAzure unit tests pass.
- 7 pre-existing AzureOpenAILiveTests failures are due to missing live credentials — unrelated.

### Preservation of Existing Behaviour
- Integrated-auth providers: `authMode = "integrated"` in form → sent as override → Azure path
  short-circuits before the `apiKey` check.
- Vault-backed API keys: `_form.ApiKey = null` (ToResponse redacts plain keys, returns null) or
  `"[vault-backed]"` sentinel → neither overrides the stored vault ref → resolved at runtime.
- List-view Test button: no form active → null body → reads fully from store as before.

---

## 2026-08-15 — Issue #223: Provider Test Connection 404

### Problem
Test Connection returned 404 for Phi-4-reasoning at two endpoint shapes:
- Foundry project endpoint: `https://<resource>.services.ai.azure.com/api/projects/proj-default`
- Azure OpenAI v1 endpoint: `https://<resource>.openai.azure.com/openai/v1`

### Root Causes

1. **FoundryModelClient** — Classic HttpClient `BaseAddress` + relative-path pitfall.
   `BaseAddress` was set to the endpoint without a trailing slash; relative paths used a
   leading slash (`/chat/completions`). A leading-slash relative URI resolves from the
   authority root, discarding the BaseAddress path. The actual POST went to
   `https://host/chat/completions` instead of `https://host/api/projects/proj/chat/completions`.
   **Fix**: append `"/"` to BaseAddress; drop leading slashes from relative paths.

2. **FoundryAgentProvider** — Same leading-slash bug in `IsAvailableAsync`'s `/models` call.

3. **FoundryAgentProvider** — `profile.Model` not forwarded: `opts.Model` (global DI default)
   was used instead of `profile.Model ?? opts.Model`.

4. **AzureOpenAIAgentProvider** — Azure SDK builds its own `/openai/deployments/…` path.
   When endpoint contains `/openai/v1`, the final URL doubles the prefix → 404.
   **Fix**: `NormalizeAzureEndpoint()` strips any `/openai/…` path from the resource URI.

### What Worked
- Direct `System.Uri` resolution verified bug and fix in PowerShell before commit.
- Unit tests added for `NormalizeAzureEndpoint` (5 shapes) and `FoundryModelClient` URL
  construction (5 endpoint shapes × chat/completions + models routes).
- OpenClawNet.Models.Foundry builds clean; pre-existing NU1605 restore errors in the test
  project/AzureOpenAI project are unrelated to this change (confirmed on main branch).

### Limitations
- Live Azure credentials unavailable; fix validated through deterministic URL-construction
  tests and in-process Uri resolution.


### Problem
Test Connection returned 404 for Phi-4-reasoning at two endpoint shapes:
- Foundry project endpoint: `https://<resource>.services.ai.azure.com/api/projects/proj-default`
- Azure OpenAI v1 endpoint: `https://<resource>.openai.azure.com/openai/v1`

### Root Causes

1. **FoundryModelClient** — Classic HttpClient `BaseAddress` + relative-path pitfall.
   `BaseAddress` was set to the endpoint without a trailing slash; relative paths used a
   leading slash (`/chat/completions`). A leading-slash relative URI resolves from the
   authority root, discarding the BaseAddress path. The actual POST went to
   `https://host/chat/completions` instead of `https://host/api/projects/proj/chat/completions`.
   **Fix**: append `"/"` to BaseAddress; drop leading slashes from relative paths.

2. **FoundryAgentProvider** — Same leading-slash bug in `IsAvailableAsync`'s `/models` call.

3. **FoundryAgentProvider** — `profile.Model` not forwarded: `opts.Model` (global DI default)
   was used instead of `profile.Model ?? opts.Model`.

4. **AzureOpenAIAgentProvider** — Azure SDK builds its own `/openai/deployments/…` path.
   When endpoint contains `/openai/v1`, the final URL doubles the prefix → 404.
   **Fix**: `NormalizeAzureEndpoint()` strips any `/openai/…` path from the resource URI.

### What Worked
- Direct `System.Uri` resolution verified bug and fix in PowerShell before commit.
- Unit tests added for `NormalizeAzureEndpoint` (5 shapes) and `FoundryModelClient` URL
  construction (5 endpoint shapes × chat/completions + models routes).
- OpenClawNet.Models.Foundry builds clean; pre-existing NU1605 restore errors in the test
  project/AzureOpenAI project are unrelated to this change (confirmed on main branch).

### Limitations
- Live Azure credentials unavailable; fix validated through deterministic URL-construction
  tests and in-process Uri resolution.
