# Decisions Archive

## Issue #230 — Test Connection Production Implementation

**Date:** 2026-08-17  
**Status:** Implemented and Approved

### Context
Foundry and Azure OpenAI providers configured via Model Providers UI fail Test Connection with "not configured" errors even after users enter valid credentials. Root cause: `POST /api/model-providers/{name}/test` reads credentials exclusively from the persisted `ModelProviderDefinition` database record, not from unsaved form state.

### Decision: Accept Form Overrides Transiently
The `POST /api/model-providers/{name}/test` endpoint must:
1. Accept an optional request body with override fields: `endpoint`, `model`, `apiKey`, `deploymentName`, `authMode`.
2. Apply overrides **transiently** to the `AgentProfile` used for the test call only.
3. Persist **only** test-result metadata: `LastTestedAt`, `LastTestSucceeded`, `LastTestError`, `IsSupported`, `UpdatedAt`.
4. Never write override values back to the stored `ModelProviderDefinition`.
5. Treat the `[vault-backed]` UI sentinel as "keep existing vault reference" — never persist it as a literal API key value.

**Rationale:**
- Users test with form values before saving to avoid disrupting live production configuration.
- Vault references (`vault://secret-name`) are resolved at runtime; overwriting them with literal values or display sentinels would break credential resolution.
- Persisting override endpoint/key values would silently replace production configuration without explicit save.

**Implementation:** Irving implemented transient local variables resolved from `(override ?? storedValue)` before test runs. The `def` object is only modified on test-result fields, never on configuration fields.

### Enforcement
Five regression tests in `ModelProviderEndpointTests.cs` enforce this contract:
- `PostTest_WithOverrides_ForwardsOverrideEndpointAndApiKeyToProvider`
- `PostTest_WithOverrides_OverrideValuesNotPersistedOnFailure`
- `PostTest_WithOverrides_OverrideValuesNotPersistedOnSuccess`
- `PostTest_WithVaultBackedSentinel_PreservesStoredVaultReference`
- `PostTest_NoBody_UsesStoredValues_AndListViewUnaffected`

**Approval Status:** Mark (Lead Architect) approved with 44 targeted tests passing. Petey confirmed UX workflow (form values passed to test endpoint). Dylan verified test coverage of production seam and override non-persistence contract.

---

## Pattern: Form-State vs Stored-State Test Endpoints

**Author:** Petey (Agent Platform Specialist)  
**Date:** 2026-08-17

### Principle
Any endpoint that "tests" or "validates" a resource configured via a UI form should **either**:
1. Accept override values in the request body so the caller can test unsaved state, **or**
2. Enforce a save-before-test flow in the UI and communicate this clearly to the user.

Option 1 was chosen for the model provider test endpoint because it preserves API flexibility and avoids implicit auto-saves that could surprise the user.

### Vault-Reference Sentinel Convention
The `"[vault-backed]"` string (`VaultReferenceSanitizer.RedactedReferenceDisplay`) is a UI-level sentinel meaning "API key is stored as a vault reference; do not replace it." Any endpoint that accepts API keys from the UI must treat this value as "use what's in the store, not this placeholder."

---

## PR #209 Documentation Security & Process Review

**Date:** 2026-08-06  
**Author:** Drummond (Platform Hardening / DevOps)  
**PR:** #209 (`docs/release-guidance-20260806`)  
**Verdict:** ✅ **APPROVED**

### Security Checks
- ✅ No hardcoded secrets (API keys, tokens, PATs)
- ✅ NuGet publishing explicitly excluded in README, SETUP, RELEASE-GUIDANCE, INDEX
- ✅ Example code uses placeholder patterns only (`"your-subscription-id"`, `"your-secret"`, `"github_pat_..."`)
- ✅ No workflow file changes — docs-only PR

### Process Accuracy
- ✅ Tag-gated GitHub Releases correctly described
- ✅ Known test blockers accurately documented with mitigation steps (Ollama, Docker, Playwright, Azure, GitHub Copilot, port conflicts, timing flakes)
- ✅ NuGet scope clearly and repeatedly stated as out of scope

### Known Limitation (Non-Blocking)
RELEASE-GUIDANCE.md references `.github/workflows/release.yml` (does not exist on `main` today; actual file is `squad-release.yml` from PR #207, not yet merged). Docs will be ahead of `main` until PR #207 merges. Acceptable if merge ordering is coordinated.

**Verdict:** APPROVED — no secrets, NuGet publishing correctly excluded, test blockers accurately documented, tag-gated release process correctly described.

---

## NuGet Package Upgrade — Held Packages (Architectural & Licensing Decisions)

**Date:** 2026-08-17  
**Author:** Irving (Backend Dev)  
**Branch:** `chore/update-nuget-packages`  
**Test Status:** 1,136 unit tests passing; 0 failures  
**Approval:** Mark (Lead Architect) — final APPROVED with follow-up disclosure requirements

### Scope: Stable Mutual Compatibility
All direct dependencies at a stable newer version with the same major version were updated across all 35 csproj files. This PR bundles routine updates only; major version upgrades deferred for dedicated follow-up PRs.

---

## Hold Decision 1: ModelContextProtocol 1.3.0 → 2.1.0

**Status: HELD FOR MAJOR MIGRATION PR**

**Reason:** MCP 2.x is a major API rewrite affecting in-process hosting, server/client factory signatures, and transport layer. Affected projects: `OpenClawNet.Mcp.Core`, `OpenClawNet.Mcp.Browser`, `OpenClawNet.Mcp.Web`, `OpenClawNet.Mcp.Shell`, `OpenClawNet.Mcp.FileSystem`, `OpenClawNet.Gateway`, `OpenClawNet.UnitTests`.

**Decision:** Defer to dedicated PR. Scope: This upgrade PR updates only same-major-version stable releases.

---

## Hold Decision 2: SixLabors.ImageSharp 3.1.12 → 4.0.0

**Status: HELD FOR COMMERCIAL LICENSE DECISION**

**Reason:** ImageSharp 4.0.0 changed from **MIT to commercial license**. Build fails unless `$(SixLaborsLicenseKey)`, `$(SixLaborsLicenseFile)`, or `sixlabors.lic` is provided. Affects: `OpenClawNet.Tools.ImageEdit`, `OpenClawNet.UnitTests`.

**Decision:** Procurement/licensing decision required. Hold at 3.1.12 (MIT, actively maintained) or obtain Six Labors license for v4 before upgrade.

---

## Hold Decision 3: GitHub.Copilot.SDK 0.3.0 → 1.0.9

**Status: HELD FOR MAJOR MIGRATION PR**

**Reason:** Namespace restructuring removes `GitHub.Copilot.SDK` namespace; types `CopilotClient`, `CopilotSession`, `SessionConfig`, event types (`AssistantMessageEvent`, etc.), and utility methods (`PermissionHandler.ApproveAll`) move to new namespace(s). Namespace mapping required before code migration. Affects: Critical live dependency requiring validation against Copilot subscription, not just compilation.

**Decision:** Defer to dedicated PR with Copilot subscription testing. Scope: This upgrade PR updates only same-major-version stable releases.

---

## Boundary: Azure.AI.OpenAI 2.9.0-beta.1 (Intentional Prerelease)

**Status: PRESERVED**

**Rationale:** Latest stable (2.1.0) is older than current 2.9.0-beta.1. Intentional beta preserve for access to unreleased features. No action required unless stable 2.x ≥ 2.9.0 is released.

---

## Resolved: ElBruno.Text2Image.Foundry 0.8.0 → 1.5.1 with HttpClient Fix

**Status: MERGED INTO THIS PR**

**What Changed:** Generators in 1.5.1 take injected `HttpClient` as parameter. Fixed in `scripts/ImageGenerator/Program.cs`: declared `using var httpClient = new HttpClient()` before generator-selection block; shared instance disposed after generator use. Zero product-behavior change; clean build with 0 warnings; smoke tests pass.

---

## Transitive Vulnerability Alert (Not Direct References)

`MessagePack 2.5.198` and `Nerdbank.MessagePack 1.0.2` appear as transitive vulnerabilities (NU1902/NU1903 warnings) from `GitHub.Copilot.SDK`. Cannot update without upgrading parent package (which is held for major migration).

---

## Follow-Up Disclosure Requirements (Mark)

Per Mark's final APPROVED review, the following issues must be opened post-merge for tracking:

1. **ModelContextProtocol 2.1.0 Major Migration** — Namespace, API, and transport changes require dedicated PR.
2. **GitHub.Copilot.SDK 1.0.9 Security & Namespace Migration** — Security priority + namespace restructuring require dedicated PR and Copilot subscription testing.
3. **SixLabors.ImageSharp 4.0 Commercial License Decision** — Procurement decision on v4 license vs hold at v3.1.12 (MIT).

---

## Approval Summary

**Final Review:** Mark (Lead Architect)  
**Verdict:** ✅ **APPROVED**  
**Condition:** Follow-up issues opened for three held packages (MCP 2.1, Copilot SDK 1.0.9, ImageSharp 4.0) and transitive vulnerability disclosure.
