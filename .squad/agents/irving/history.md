# Irving — Backend Dev History

## 2026-08-19 — Issue #236: Agent Profile Test Agent ignored unsaved provider overrides

### Context
Users editing an Agent Profile, switching the Model Provider combo box (e.g. to Microsoft
Foundry or OpenAI) without saving, then clicking **Test Agent**, got a confusing "connection
refused (localhost:11434)" error — the test endpoint always read `profile.Provider` from the
**persisted** profile, ignoring the unsaved form selection and silently falling back to
whatever provider (often Ollama) was last saved.

### Fix (mirrors the approved ModelProviderTestOverrides pattern from Issue #230)
- `POST /api/agent-profiles/{name}/test` now accepts an optional `[FromBody] AgentProfileTestOverrides?`
  body: `Provider`, `Model`, `Instructions`, `RetrievalLevel`. Non-blank overrides win over the
  stored profile **for the test call only**; the stored `AgentProfile` and persisted entity are
  never mutated with override values — only test-result metadata (`LastTestedAt`/
  `LastTestSucceeded`/`LastTestError`) is saved, exactly as the Model Provider test endpoint does.
- The provider **definition lookup itself** now uses the (possibly overridden) provider name,
  not `profile.Provider` — this is the actual fix, since the bug was that overriding the model
  provider had no effect on which `ModelProviderDefinition` was resolved.
- `AgentProfiles.razor`'s edit-form Test Agent button now sends `_form.Provider`/
  `_form.Instructions`/`_form.RetrievalLevel` as overrides; the list-row Test button is
  unchanged (still sends `null` body, tests saved state).

### Gotcha: adding a second optional body-eligible parameter broke unrelated tests
Adding `AgentProfileTestOverrides? overrides` as a new minimal-API parameter caused **14 unrelated
CRUD/import/export tests** in `AgentProfileEndpointTests.cs` to fail with `NETSDK`-unrelated
`InvalidOperationException: UNKNOWN parameter` at DFA-matcher build time — even though those
tests never call `/test`. Root cause: minimal API's `RequestDelegateFactory` builds/validates
**every** endpoint's parameter-binding metadata for the whole route group on the **first** HTTP
request that WebApplication instance handles — not just the endpoint actually hit. The pre-existing
`IModelProviderDefinitionStore providerStore` parameter was *never actually registered* in the
basic `CreateTestAppAsync()` test helper (only CRUD stores were), but it worked by accident because
it was the sole non-service complex-type parameter and got silently (and uselessly) inferred as
`[FromBody]`. Adding a second complex parameter (`overrides`) claimed the Body slot instead, leaving
`providerStore` with no valid binding source → hard failure for **every** endpoint in that app,
not just `/test`.
**Fix:** (1) explicitly mark the new override parameter `[FromBody]` to remove inference ambiguity,
and (2) register `IModelProviderDefinitionStore` in the basic test helper too — it's registered
app-wide in production `Program.cs`, so the test harness was actually incomplete, not the endpoint.
**Lesson:** when adding a new optional-body parameter to an existing minimal API endpoint, always
run the *full* test file (not just the endpoint's own tests) — a latent DI-registration gap in an
unrelated test helper can surface as router-wide failures once a second body-eligible parameter exists.
Verified the regression was NOT pre-existing by checking out `main` into a separate `git worktree`
(safe — never use `git stash` when there's another agent's unstaged file in the tree, since stash
touches the whole working directory indiscriminately).

### Validation
- `dotnet build src/OpenClawNet.Gateway` and `dotnet build src/OpenClawNet.Web` (after
  `dotnet restore ... -r win-x64`, required per prior NETSDK1047 workaround) — both 0 errors.
- `dotnet test tests/OpenClawNet.UnitTests --filter "FullyQualifiedName~AgentProfileEndpointTests|FullyQualifiedName~ModelProviderEndpointTests|FullyQualifiedName~Gateway"` — 216 passed, 1 pre-existing skip, 0 failed.

### PR
`fix/agent-profile-test-provider-overrides` → PR #237 (main), Fixes #236.


## 2026-08-17 — NuGet Upgrade Revision 2: ElBruno.Text2Image.Foundry 1.5.1 + source fix

### Context
ElBruno.Text2Image.Foundry was held at 0.8.0 in the previous round due to a constructor API break. Instructed to resolve it rather than hold it.

### API Change in 1.5.1
`MaiImage2Generator` and `Flux2Generator` constructors changed signature:
- Old: `(string endpoint, string apiKey, string modelName, string modelId)`
- New: `(string endpoint, string apiKey, HttpClient httpClient, string modelName, string modelId, int? timeoutSeconds = null)`

`HttpClient` is now injected (arg 3), modelName/modelId shift to 4/5.

### Source Change
**File:** `scripts/ImageGenerator/Program.cs`

One `using var httpClient = new HttpClient()` added before the generator-selection block, shared between the MAI and Flux2 branches. Passed as arg 3 to each generator constructor. Disposal order: generator disposed first (via `using (generatorDisposable)`), then `httpClient` (via `using var` scope end) — correct.

No product-behavior change. No test seam exists in the scripts project; validated by:
- `dotnet build` — succeeded, 0 warnings, 0 errors
- `dotnet run --` (list mode) — enumerates all prompts correctly
- `dotnet run -- --dry-run all` — prints all 13 image specs with paths

### Final Outdated Audit (all 69 tracked csproj)
**Zero packages remain stale** outside the 3 accepted decision-sensitive holdbacks:

| Package | Current | Hold Reason |
|---------|---------|-------------|
| ModelContextProtocol | 1.3.0 | Major API rewrite; dedicated migration PR |
| SixLabors.ImageSharp | 3.1.12 | Commercial license in v4 |
| GitHub.Copilot.SDK | 0.3.0 | Namespace removed in 1.x |

### Validation
- `OpenClawNet.UnitTests` (win-x64 restore, filter non-live): **1136 passed, 0 failed, 46 skipped**
- `OpenClawNet.UnitTests.Azure`: **12 passed, 0 failed**
- `scripts/ImageGenerator`: clean build, list + dry-run smoke tests pass

### Learnings
- `ElBruno.Text2Image.Foundry` 1.5.1 moved HttpClient to a constructor parameter (standard .NET HttpClient injection pattern — enables connection pooling, mocking, IHttpClientFactory wiring). Console scripts without DI: `using var httpClient = new HttpClient()` is the correct pattern — single instance for the script lifetime, disposed at scope end after all HTTP work completes.
- For 0.x→1.x ElBruno package upgrades, always check constructor signatures before assuming compatible; the stable milestone often introduces DI-friendly patterns that change constructor arity.



## 2026-08-17 — NuGet Upgrade Follow-up: out-of-solution projects

### Context
Dylan found that `scripts\ImageGenerator\ImageGenerator.csproj` and all tracked `sessions\**\*.csproj` were missed in the initial bulk update (which only audited the solution). Follow-up covers 9 out-of-solution files with stale direct references.

### Additional Updates Applied (9 files)

| File | Package | Old → New | TFM | Reason for version |
|------|---------|-----------|-----|--------------------|
| scripts/ImageGenerator | Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.5 → **10.0.11** | net8.0 | netstandard2.0 target: compatible |
| scripts/ImageGenerator | Microsoft.Extensions.Configuration.Json | 10.0.5 → **10.0.11** | net8.0 | netstandard2.0 target: compatible |
| scripts/ImageGenerator | Microsoft.Extensions.Configuration.UserSecrets | 10.0.5 → **10.0.11** | net8.0 | netstandard2.0 target: compatible |
| session-1/demo1 | Microsoft.Extensions.Configuration.UserSecrets | 10.0.6 → **10.0.11** | net10.0 | same major |
| session-1/demo1 | Microsoft.Extensions.Hosting | 10.0.6 → **10.0.11** | net10.0 | same major |
| session-1/demo3 | Microsoft.Extensions.Hosting | 10.0.6 → **10.0.11** | net10.0 | same major |
| session-2/demo1 | Microsoft.Extensions.DependencyInjection | 10.0.0 → **10.0.11** | net10.0 | same major |
| session-2/demo1 | Microsoft.Extensions.Logging.Console | 10.0.0 → **10.0.11** | net10.0 | same major |
| session-2/demo2 | Microsoft.Extensions.DependencyInjection | 10.0.0 → **10.0.11** | net10.0 | same major |
| session-2/demo2 | Microsoft.Extensions.Logging.Console | 10.0.0 → **10.0.11** | net10.0 | same major |
| session-2/demo3 | Microsoft.Extensions.DependencyInjection | 10.0.6 → **10.0.11** | net10.0 | same major |
| session-2/demo3 | Microsoft.Extensions.Logging.Console | 10.0.0 → **10.0.11** | net10.0 | same major |
| session-3/02-AgentProfileSwitcher | Microsoft.Data.Sqlite | 9.0.0 → **10.0.11** | net10.0 | TFM=net10.0, prefer 10.0.11 |
| session-3/03-MemoryStub | Microsoft.Data.Sqlite | 9.0.0 → **10.0.11** | net10.0 | TFM=net10.0, prefer 10.0.11 |
| session-3/05-ProviderCatalogCli | Microsoft.Data.Sqlite | 9.0.0 → **10.0.11** | net10.0 | TFM=net10.0, prefer 10.0.11 |

**Microsoft.Data.Sqlite decision:** all three projects target net10.0; 10.0.11 is fully compatible and preferred. No 9.0.x holdback needed.

### Additional Holdback: ElBruno.Text2Image.Foundry 0.8.0

`dotnet list --outdated` for ImageGenerator showed `ElBruno.Text2Image.Foundry` 0.8.0 → 1.5.1. This is a 0.x→1.x major boundary. Update attempted; `MaiImage2Generator` and `Flux2Generator` constructors changed arg 3 from `string` (modelName) to `System.Net.Http.HttpClient` — build fails with CS1503. Reverted to 0.8.0. Documented in `.squad/decisions/inbox/irving-nuget-upgrade.md`.

### Validation
- `scripts/ImageGenerator`: restore + build — **OK** (0.8.0 restored)
- `sessions/session-3/02-AgentProfileSwitcher`: restore + build — **OK**
- `sessions/session-3/03-MemoryStub`: restore + build — **OK**
- `sessions/session-3/05-ProviderCatalogCli`: restore + build — **OK**
- `sessions/session-1/demo1`, `demo3`, `session-2/demo1`, `demo2`, `demo3`: build fails with CS0246 (`OpenClawNet.*` not found) — **confirmed pre-existing** (same errors on main; these demos use ProjectReferences that only resolve in full solution context)
- Full outdated audit (solution + all 13 out-of-solution tracked projects): **only 4 holdbacks remain** — ModelContextProtocol 2.1.0, GitHub.Copilot.SDK 1.0.9, SixLabors.ImageSharp 4.0.0, ElBruno.Text2Image.Foundry 1.5.1

### Learnings
- `ElBruno.Text2Image.Foundry` 0.8.0→1.5.1 crosses the 0.x/1.x major boundary with a constructor API break — treat 0.x ElBruno packages as major-bump candidates, not patch updates.
- `Microsoft.Extensions.Configuration.*` 10.0.x targets netstandard2.0, compatible with net8.0 projects.
- Session demo projects that use `ProjectReference` to `src/**` only build cleanly when their transitive project dependencies are also restored. Building the demo alone in isolation hits CS0246 because referenced assemblies aren't in NuGet and the project isn't building the dependency tree. This is a pre-existing demo architecture constraint, not an upgrade regression.
- Always run `dotnet list package --outdated` against every tracked csproj individually (or a superset), not just the solution file, to catch files not included in the .slnx.



## 2026-08-17 — NuGet Package Bulk Update: chore/update-nuget-packages

### Context
Routine update of all direct NuGet `PackageReference` entries across the full repository (69 csproj files: 50 src, 12 sessions, 6 tests, 1 scripts).

### Package Updates Applied (35 files changed)

| Package | Old | New |
|---------|-----|-----|
| AngleSharp | 1.4.0 | 1.7.1 (CVE fix: GHSA-pgww-w46g-26qg) |
| Aspire.Hosting.Docker | 13.4.3 | 13.4.6 |
| Aspire.Hosting.Testing | 13.4.3 | 13.4.6 |
| Azure.Core | 1.55.0 | 1.61.0 |
| bunit | 2.7.2 | 2.9.0 |
| CommunityToolkit.Aspire.Hosting.Sqlite | 13.3.0 | 13.4.0 |
| CommunityToolkit.Aspire.Microsoft.Data.Sqlite | 13.1.1 / 13.3.0 | 13.4.0 |
| coverlet.collector | 10.0.0 | 10.0.1 |
| Elbruno/ElBruno.LocalEmbeddings | 1.4.6 | 1.5.9 |
| ElBruno.MarkItDotNet | 0.6.1 | 0.9.2 |
| ElBruno.QwenTTS | 1.4.7 | 1.7.2 |
| ElBruno.Text2Image.Cpu | 1.2.11 | 1.5.1 |
| ExtendedNumerics.BigDecimal | 3003.0.0.346 | 3003.2.0.161 |
| Google.Apis.Auth | 1.74.0 | 1.75.0 |
| Google.Apis.Calendar.v3 | 1.74.0.4073 | 1.75.0.4206 |
| Google.Apis.Gmail.v1 | 1.74.0.4134 | 1.75.0.4225 |
| Markdig | 1.2.0 | 1.3.2 |
| Microsoft.Agents.AI | 1.5.0 | 1.17.0 |
| Microsoft.Agents.Core | 1.5.181 | 1.7.129 |
| Microsoft.AI.Foundry.Local | 1.1.0 | 1.2.4 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.8 / 10.0.9 | 10.0.11 |
| Microsoft.AspNetCore.OpenApi | 10.0.8 | 10.0.11 |
| Microsoft.AspNetCore.SignalR.Client | 10.0.9 | 10.0.11 |
| Microsoft.AspNetCore.TestHost | 10.0.8 | 10.0.11 |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.8 / 10.0.9 | 10.0.11 |
| Microsoft.Extensions.AI | 10.4.1 / 10.5.2 | 10.9.0 |
| Microsoft.Extensions.AI.Abstractions | 10.5.2 | 10.9.0 |
| Microsoft.Extensions.AI.OpenAI | 10.5.2 | 10.9.0 |
| Microsoft.Extensions.Http.Resilience | 10.5.0 | 10.9.0 |
| Microsoft.Extensions.ServiceDiscovery | 10.5.0 | 10.9.0 |
| Microsoft.Identity.Client | 4.84.0 | 4.87.0 |
| Microsoft.NET.Test.Sdk | 18.5.1 | 18.9.0 |
| Microsoft.Playwright | 1.59.0 | 1.62.0 |
| MudBlazor | 9.3.0 | 9.8.0 |
| NCalcSync | 5.4.2 / 5.12.0 | 7.1.0 |
| OllamaSharp | 5.4.25 | 5.4.30 |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.3 | 1.17.0 |
| OpenTelemetry.Extensions.Hosting | 1.15.3 | 1.17.0 |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.2 | 1.17.0 |
| OpenTelemetry.Instrumentation.Http | 1.15.1 | 1.17.0 |
| OpenTelemetry.Instrumentation.Runtime | 1.15.1 | 1.17.0 |
| System.Security.Cryptography.ProtectedData | 10.0.9 | 10.0.11 |
| WireMock.Net | 2.6.0 | 2.14.0 |
| YamlDotNet | 17.1.0 | 18.1.0 |
| YoutubeExplode | 6.6.0 | 6.6.1 |

### Packages Already at Latest Stable (unchanged)
Azure.Extensions.AspNetCore.DataProtection.Blobs 1.5.3, Azure.Extensions.AspNetCore.DataProtection.Keys 1.6.3, Azure.Identity 1.21.0, Azure.Security.KeyVault.Secrets 4.11.0, Cronos 0.13.0, ElBruno.Text2Image.Foundry 0.8.0, FluentAssertions 8.10.0, GitHub.Models (all), MemPalace.* 0.15.2, Microsoft.ApplicationInsights 3.1.2, Microsoft.AspNetCore.DataProtection* 10.0.11, Microsoft.Bot.Builder.Integration.AspNet.Core 4.23.1, Microsoft.Data.Sqlite 9.0.0, Microsoft.EntityFrameworkCore.Design/Sqlite 10.0.11, Microsoft.Extensions.Configuration.* 10.0.x, Microsoft.Extensions.DependencyInjection* 10.0.11, Microsoft.Extensions.Hosting* 10.0.x, Microsoft.Extensions.Http 10.0.11, Microsoft.Extensions.Logging* 10.0.11, Microsoft.Extensions.Options* 10.0.11, ModelContextProtocol 1.3.0 (held), Moq 4.20.72, NCalcSync held resolved, Octokit 14.0.0, SixLabors.ImageSharp 3.1.12 (held), Spectre.Console 0.57.2, xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Xunit.SkippableFact 1.5.61

### Packages Held (not updated)

1. **ModelContextProtocol 1.3.0** (latest stable 2.1.0) — Major API rewrite; `McpServerPrimitiveCollection<T>`, `McpServer.Create`, in-process transport pair all need migration across 6 projects. Dedicated PR required.
2. **SixLabors.ImageSharp 3.1.12** (latest 4.0.0) — v4 requires commercial license; MIT v3 preserved. Procurement decision logged in `.squad/decisions/inbox/irving-nuget-upgrade.md`.
3. **GitHub.Copilot.SDK 0.3.0** (latest 1.0.9) — Removed namespace `GitHub.Copilot.SDK`; `CopilotClient`, `SessionConfig`, event types need namespace remapping. Blocked on 1.0.9 API surface discovery.
4. **Azure.AI.OpenAI 2.9.0-beta.1** — Intentional prerelease; stable 2.1.0 is older. No update.
5. **Azure.Security.KeyVault.Secrets 4.11.0** — Only newer is 4.12.0-beta.1 (prerelease). Stable preserved.

### Validation
- `dotnet build` (selected non-RID affected projects): all OK
- `dotnet test tests/OpenClawNet.UnitTests` (after win-x64 restore): 1136 passed, 46 skipped (live), 0 failed
- `dotnet test tests/OpenClawNet.UnitTests.Azure`: 12 passed, 0 failed
- NETSDK1047 errors on Gateway/FoundryLocal/E2ETests/IntegrationTests/UnitTests (solution-level build): **confirmed pre-existing** on main (reproduced on stashed main)
- Session-2 demo3 CS0246 errors: **confirmed pre-existing** on main

### Learnings
- **SixLabors.ImageSharp went commercial at 4.0.0.** Always check license changes when a major version bumps for OSS packages. The build error is clear but could catch teams off-guard.
- **GitHub.Copilot.SDK 0.3.0→1.0.9** is a semver-0 → semver-1 transition; the SDK author reorganized namespaces for the stable release. Pre-flight compile check is essential before committing major SDK version upgrades.
- **ModelContextProtocol 2.x** restructured the server/client factory and collection APIs significantly. Infrastructure projects built on in-process MCP hosting need dedicated migration effort.
- **NETSDK1047** on this repo is caused by a Microsoft.AI.Foundry.Local transitive dependency requiring native win-x64 binaries; solution-level restore doesn't generate RID-specific assets. Workaround: `dotnet restore <project> -r win-x64` before testing. This is pre-existing, not caused by upgrades.
- To run solution-wide unit tests on this machine: `dotnet restore tests/OpenClawNet.UnitTests -r win-x64 && dotnet test tests/OpenClawNet.UnitTests --no-restore --filter "Category!=Live&Category!=Integration"`

## 2026-08-17 — Test Connection endpoint: transient override isolation fix

### Context
The `POST /api/model-providers/{name}/test` endpoint was mutating the stored
`ModelProviderDefinition` with override values from the request body before building
`AgentProfile` and calling `SaveAsync`. This meant plaintext API keys from the UI could
replace vault references in persistent storage, and unsaved endpoint/model/auth-mode
changes could be written back as if they were saved.

### Fix
- Removed all mutations of `def` fields (`Endpoint`, `Model`, `ApiKey`, `DeploymentName`,
  `AuthMode`) inside the test handler.
- Added five transient local variables (`testEndpoint`, `testModel`, `testApiKey`,
  `testDeploymentName`, `testAuthMode`) resolved from override + stored fallback, applying
  the same vault sentinel guard (`VaultReferenceSanitizer.RedactedReferenceDisplay`).
- `AgentProfile` is built from those transient variables, not from `def` fields.
- `SaveAsync` is now called with `def` modified **only** on test-result metadata:
  `LastTestedAt`, `LastTestSucceeded`, `LastTestError`, `IsSupported`, `UpdatedAt`.
- Timestamps use a single `testedAt = DateTime.UtcNow` captured before the try/catch so
  all branches record the same consistent test-start time.

### Validation
- Build: `dotnet build OpenClawNet.Gateway.csproj` — succeeded (warnings only, pre-existing).
- Tests: `dotnet test --filter ModelProvider` — 35/35 unit tests passed; 1 E2E failure
  confirmed pre-existing (reproduced against unmodified main branch).

### Learnings
- `ModelProviderDefinition` is a mutable class; calling `store.GetAsync` returns the live
  instance. Mutating it before calling `SaveAsync` persists transient UI state — always
  separate transient resolution from persistence writes.
- The vault sentinel `VaultReferenceSanitizer.RedactedReferenceDisplay` ("[vault-backed]")
  must be checked wherever a user-supplied key value might arrive, not just in the UI.
- Capture a single `DateTime.UtcNow` before try/catch so all failure branches share the
  same timestamp.

## 2026-08-19 — PR #239 revision: CI-eligible CallToolAsync coverage (MCP SDK 2.1.0 upgrade)

### Context
Mark rejected PR #239 (ModelContextProtocol 1.3.0→2.1.0) because the production-critical
`CallToolAsync` request/response path had no CI-eligible test: the existing in-memory
`InProcessMcpHostE2ETests` only proves `ListToolsAsync`/discovery, and its own doc comment
claimed CallTool "doesn't reliably resolve under the xUnit test host" — plus that whole project
(`OpenClawNet.IntegrationTests`) is entirely excluded from PR CI (Docker/Aspire), so it
wouldn't have counted as CI coverage even if it did test CallTool. The stdio Live CallTool
test is also excluded (`Category=Live`).

### Fix
- Added `tests/OpenClawNet.UnitTests/Mcp/InProcessMcpCallToolRoundTripTests.cs` — a new,
  non-Live test in the project PR CI already runs (`Category!=Live` filter). Spins up a real
  `InProcessMcpHost` + real `WebMcpTools`/`WebTool` (stub `HttpMessageHandler`), calls
  `ListToolsAsync` to resolve the `fetch` tool then `CallToolAsync` to invoke it, and asserts
  on the actual returned `TextContentBlock` text — not just that invocation completed.
- Empirically verified the old "doesn't reliably resolve" claim no longer holds under SDK
  2.1.0: the test passed on first try and 20/20 consecutive repeat runs with no hangs/flakes —
  no harness fix, retry, sleep, or suppression was needed.
- `ListToolsAsync`/`CallToolAsync` return `ValueTask` in SDK 2.1.0 (were `Task`-returning
  before) — use `.AsTask()` before `Task.WhenAny` hang-guards.
- Updated the stale doc comment on the original `InProcessMcpHostE2ETests` test to point at
  the new CI-eligible coverage instead of repeating the now-disproven claim.
- Lesson: when a review flags "no CI coverage," always check whether the *project* the
  existing test lives in is itself excluded from the CI job — filtering by trait only helps
  if the project runs at all. Put new required-coverage tests in a project already in scope
  rather than trying to un-exclude a heavier project.
