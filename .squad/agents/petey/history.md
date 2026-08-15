# Petey — Agent Platform Specialist History

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
