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
