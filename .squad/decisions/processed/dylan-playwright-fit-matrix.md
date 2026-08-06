# Decision: Playwright E2E Fit Matrix — AspireHostFixture Migration

**Author:** Dylan (Tester)  
**Date:** 2026-05-25  
**Status:** DRAFT — Awaiting team review  
**Scope:** All Playwright E2E test classes in `tests/OpenClawNet.PlaywrightTests/`  
**Companion:** `.squad/decisions/inbox/mark-aspirehostfixture-migration-plan.md`

---

## Summary

**Can ALL Playwright E2E tests move to AspireHostFixture semantics?**  
*Semantics = status check (`aspire describe`) → start if down (`aspire start`) → proceed if up (attach).*

**Answer: Yes — all 29 test classes can migrate. None are fundamentally incompatible.**  
However, fit varies significantly. 15 are direct-fit (mechanical swap), 12 need moderate refactor, and 2 have blockers that make "proceed if up" (attach mode) risky in practice. Those 2 should be documented as requiring the "start if down" path and should carry explicit warnings about attach-mode hazards.

---

## Blocker Inventory (fixture-level)

These are concerns at the fixture layer, not the test layer. Tests themselves are mostly unaffected by how Aspire starts — but if the fixture doesn't preserve these guarantees, tests break silently or flake.

| ID | Blocker | Current resolution in `AppHostFixture` | Risk if attach-mode skips it |
|----|---------|---------------------------------------|------------------------------|
| B1 | `OPENCLAW_OLLAMA_MODEL=gemma4:e2b` must be set **before** AppHost starts | Set on line 137 before `DistributedApplicationTestingBuilder.CreateAsync()` | Aspire uses whatever default model it was started with; tests that reference `ToolCapableTestModel` in profiles are unaffected (profiles use explicit model), but the system default in the running app is wrong |
| B2 | `OPENCLAW_ENABLE_SQLITE_WEB=false` must be set before AppHost starts | Set on line 141 | Running Aspire may have SQLite Web resource trying to boot Docker; fail-open behavior but adds noise |
| B3 | `CleanAgentSkillState()` wipes per-agent skill JSON before first test | Called on line 148, deletes `C:\ProgramData\openclawnet\skills\agents\` tree | Stale skills from previous runs poison tool selection — especially `doc-processor` and journey-test-created skills (`emoji-teacher-journey`, `pirate-mode-journey`). This is the **highest-severity attach-mode blocker** |
| B4 | Endpoint resolution: `_app.GetEndpoint("web","https")` etc. | Provided by `DistributedApplicationTestingBuilder` in-process | Must be replaced with `aspire describe --format Json` parsing; technically solvable but is a non-trivial implementation change in the fixture (not in individual tests) |
| B5 | Ollama model availability probe (`IsToolCapableModelAvailable`, `ProbeOllamaToolCallCompatibilityAsync`) | Direct HTTP to `localhost:11434` — independent of how Aspire started | **Not a blocker** — these probes work regardless of startup mode and must be ported to `AspireHostFixture` |
| B6 | Scheduler HTTP endpoint needed by `WebsiteWatcherE2ETests` | `_app.GetEndpoint("scheduler","http")` | `aspire describe` output includes the scheduler resource URL; requires JSON parsing plumbing |

---

## Fit Matrix

### ✅ Category A — Direct Fit (15 classes)

**Definition:** Can migrate with a mechanical swap of `[Collection("AppHost")]` → `[Collection("AspireHost")]` and fixture injection type. No test-body changes required. No pre-boot env var or skill-state dependency.

| Test Class | Why it's direct fit |
|-----------|---------------------|
| `BlazorNavigationTests` | Pure UI navigation; no LLM; no skill state; just needs `Fixture.WebBaseUrl` |
| `HelloWorldScreenshotsTest` | Screenshot only; no LLM or state deps |
| `JobsScreenshotsTest` | Screenshot only |
| `SettingsScreenshotsTest` | Screenshot only |
| `ToolsScreenshotsTest` | Screenshot only |
| `ActivityPanelExportTests` | Explicitly documented: "does NOT require an LLM"; self-contained CRUD |
| `SessionsDeleteConfirmationTests` | UI dialog test; no LLM; does own cleanup |
| `SecretsVaultTests` | Vault CRUD; no LLM; explicit create/delete/purge per test |
| `SkillsImportE2ETests` | Import workflow; no LLM; does own teardown |
| `SkillsBulkDeleteE2ETests` | Bulk delete CRUD; no LLM; does own teardown |
| `GatewayApiTests` | API-only (no browser); needs `Fixture.CreateGatewayHttpClient()` → gateway URL only |
| `GatewayOnlyDemoTests` | API-only; same pattern as `GatewayApiTests` |
| `ProviderSwitchTests` | API-only; PUT/GET settings; restores original settings on dispose |
| `PirateJourneyAttachedTests` *(Demos/)* | Already uses `AttachedAspireTestBase` which IS the target model; trivial migration |
| `ChatRssDailyTaskAttachedTests` *(Demos/)* | Same — already attached-mode |

**Migration effort:** ~30 min per class. Mechanical: swap Collection attribute, swap constructor parameter type. No test-body edits.

**Total direct-fit estimate:** ~7–8 hours for all 15 classes.

---

### ⚠️ Category B — Conditional Fit (12 classes)

**Definition:** Can migrate, but requires moderate refactoring at the fixture-call level or needs explicit guard validation in attach mode. Test logic is sound; the work is in making the fixture provide equivalent guarantees.

#### B1 — LLM-gated chat tests (no skill state concern)

| Test Class | Refactor needed | Notes |
|-----------|----------------|-------|
| `ChatFlowTests` | Verify `RequiresModel` skip guards fire correctly with ported probes | Uses `Fixture.IsReady` guard; already trait-gated |
| `ChatAutoNameTests` | Same; uses `Fixture.IsToolCapableModelAvailable` | The auto-name test doesn't care what default model Aspire started with — it just needs Ollama available |
| `ChatUrlSummaryE2ETests` | Creates profiles with explicit `RequireToolApproval` flags | Profile model comes from caller, not from the Aspire default |
| `ChatRssDailyTaskE2ETests` | Uses `AppHostFixture.ToolCapableTestModel` constant in profile creation | Profile specifies `gemma4:e2b` explicitly; works even if Aspire default is different — **but the model must be pulled in Ollama** |
| `AspireDashboardTests` | 2/3 tests need only `Fixture.GatewayBaseUrl`; 1 test is `RequiresModel` | Dashboard URL itself is NOT a resource endpoint — it is exposed by the Aspire CLI dashboard, not via `aspire describe`. Need special handling for `Dashboard_WebAppHomePage_IsReachable` only. |

**Attach-mode risk:** The `OPENCLAW_OLLAMA_MODEL=gemma4:e2b` env var (B1) was not set before the running Aspire started. Tests guard on `IsAnyToolCapableModelAvailable` which probes Ollama directly and is fine. But if the user started Aspire with a different default model, the system default chat (without explicit profile) will use that model. Tests that use explicit profiles are unaffected.

#### B2 — Skills journey tests (skill state contamination risk)

| Test Class | Refactor needed | Notes |
|-----------|----------------|-------|
| `SkillsPirateJourneyE2ETests` | `AspireHostFixture` **must** call `CleanAgentSkillState()` even in attach mode | Blocker B3 applies directly — stale `pirate-mode-journey` skill from a previous run causes tool-selection noise |
| `SkillsEmojiTeacherJourneyE2ETests` | Same — stale `emoji-teacher-journey` skill is the documented poison vector | Explicitly called out in `AppHostFixture` wave-5 comment |
| `SkillsBulletPointJourneyE2ETests` | Same pattern | Creates `bullet-point-journey` skill |
| `SkillsPirateModeE2ETests` | CRUD lifecycle; lower contamination risk (creates/deletes own skill per test) | Still benefits from clean state on start |

**Attach-mode risk (HIGH for this sub-group):** If Aspire is already running with leftover journey skills enabled for the default agent, these tests will fail or produce non-deterministic results. `AspireHostFixture` MUST call `CleanAgentSkillState()` in `InitializeAsync` regardless of whether it started Aspire or attached — this is safe (tests own the agent folder, not the app itself).

#### B3 — Advanced orchestration tests

| Test Class | Refactor needed | Notes |
|-----------|----------------|-------|
| `WebsiteWatcherE2ETests` | Needs scheduler URL from `aspire describe` JSON parsing (Blocker B6) | Uses both `CreateGatewayHttpClient()` and `CreateSchedulerHttpClient()`; the scheduler resource appears in `aspire describe` output but the key name must be verified |
| `SettingsGeneralE2ETests` | Settings UI; likely no LLM; needs validation that no model-dependent assertions exist | Low risk |
| `SettingsAndSkillsTests` | Settings + skills UI; same low-risk category | |
| `ToolApprovalFlowTests` | Uses `Fixture.ProbeOllamaToolCallCompatibilityAsync()` (must be ported to `AspireHostFixture`); uses `Fixture.IsToolCapableModelAvailable`; creates profiles with `AppHostFixture.ToolCapableTestModel` explicitly; `ToolApproval` category already excluded from default CI | The `ProbeOllamaToolCallCompatibilityCoreAsync` method is self-contained (direct Ollama HTTP call) and fully portable. The critical risk is attach mode with wrong model, but tests skip cleanly when model probe fails. |
| `ToolMatrixE2ETests` | Same fixture-method dependencies as `ToolApprovalFlowTests` | Matrix tests multiple tool types × approval modes — inherently sensitive to model non-determinism |

**Migration effort for Category B:** 2–4 hours per class, plus ~1 day for `CleanAgentSkillState()` verification in attach mode and `aspire describe` scheduler URL parsing plumbing.

**Total conditional-fit estimate:** ~24–32 hours for all 12 classes.

---

### ❌ Category C — Poor Fit / Should Remain Separate Mode (0 pure, 2 caveated)

**Finding: No test class is fundamentally incompatible with AspireHostFixture semantics.**  
However, two classes are "conditionally risky" in the **attach** path specifically and should carry explicit documentation warnings:

| Test Class | Specific attach-mode hazard | Recommended guard |
|-----------|--------------------------|-------------------|
| `ToolApprovalFlowTests` | Tool approval behavior depends on LLM consistently picking specific tools. In attach mode, if Aspire was started with a different model OR if `gemma4:e2b` wasn't available when Aspire started, the test may reach the agent loop with a model that doesn't support tool calls well — and skip guards may not catch this because the probe checks Ollama directly, not the *running Aspire configuration*. | Add an attach-mode assertion: if `_weStartedAspire == false`, verify the running Aspire's active model setting via `GET /api/settings` matches `ToolCapableTestModel` before proceeding. If mismatch → skip with explanation. |
| `ToolMatrixE2ETests` | Same as above; matrix of tool×approval mode combinations is highly sensitive to model behavior | Same guard recommendation |

These two classes should be migrated as part of Mark's Wave 3e (highest-risk wave) and should carry a clear comment: *"This test requires Aspire to have been started with `OPENCLAW_OLLAMA_MODEL=gemma4:e2b`. If running in attach mode, verify `GET /api/settings` returns the correct model."*

---

## Blocker Callouts

### 1. `AppHostFixture` boot-time env vars (B1, B2)

**Current behavior:** `AppHostFixture` sets `OPENCLAW_OLLAMA_MODEL=gemma4:e2b` and `OPENCLAW_ENABLE_SQLITE_WEB=false` BEFORE Aspire boots. These are read by the AppHost at startup.

**AttachedAspireTestBase behavior:** Does NOT set these env vars. Assumes operator configured Aspire correctly before running the test.

**AspireHostFixture requirement:** 
- On the "start" path: set both env vars before calling `aspire start`. ✅ Can be done.
- On the "attach" path: env vars cannot be retroactively applied. The running Aspire has whatever config it was started with. Tests that depend on `OPENCLAW_OLLAMA_MODEL` being a specific value must verify via `GET /api/settings` or accept they're testing against the user's active configuration.

### 2. `CleanAgentSkillState()` — most critical attach-mode blocker

**Current behavior:** Called once per `AppHostFixture` lifetime, before AppHost boots. Deletes the entire `C:\ProgramData\openclawnet\skills\agents\` tree.

**Attach-mode problem:** If Aspire is already running, the fixture can't "un-enable" skills that were enabled during Aspire's session. The running app has in-memory skill state that won't be refreshed by deleting the files.

**Recommendation:** `AspireHostFixture` must call `CleanAgentSkillState()` in `InitializeAsync` regardless of path. AND: for skill journey tests, add a test-level setup that also calls `DELETE /api/skills/agents/{name}/enabled` for known journey skill names before each test run. Belt-and-suspenders.

### 3. `AttachedAspireTestBase` already IS the target model

The `Demos/` folder tests already implement the "check if up, use if up" pattern. They are direct evidence that AspireHostFixture semantics are viable for the demo use case. Mark's plan correctly identifies these as Phase 2 migration targets.

### 4. `DistributedApplicationTestingBuilder` removal (B4)

**This is NOT a test-class concern; it's a fixture implementation concern.** No individual test class calls `DistributedApplicationTestingBuilder` directly — all go through `AppHostFixture`/`PlaywrightTestBase` abstractions. Removing the in-process testing builder and replacing with `aspire describe` + `aspire start` CLI calls is work that happens entirely in the new `AspireHostFixture.cs`. Individual test classes require no changes for this.

---

## Assumptions & Constraints That Must Hold

For AspireHostFixture semantics to work for all categories, the following must remain true:

1. **`aspire describe --format Json` exposes web, gateway, AND scheduler endpoints** — verify with current Aspire CLI version before Phase 1.
2. **`CleanAgentSkillState()` is called in `InitializeAsync` on ALL paths** (start AND attach) — non-negotiable for Category B2 tests.
3. **Model probes (`IsToolCapableModelAvailable`, `ProbeOllamaToolCallCompatibilityAsync`) are ported to `AspireHostFixture`** — `ToolApprovalFlowTests` and `ToolMatrixE2ETests` depend on them; they are self-contained Ollama HTTP calls and portable.
4. **`CreateSchedulerHttpClient()` reads scheduler URL from `aspire describe` output** — `WebsiteWatcherE2ETests` requires this.
5. **`PlaywrightTestBase` is updated to accept `AspireHostFixture` instead of `AppHostFixture`** — Mark's Phase 3 plan calls this out correctly.
6. **Demo tests keep `[Trait("Category", "DemoLive")]` trait** — filtering contract must not change; DemoLauncher depends on it.

---

## Migration Effort Summary

| Category | Count | Effort per class | Total estimate |
|----------|-------|-----------------|----------------|
| A — Direct fit | 15 | ~30 min | ~7–8 hours |
| B1 — LLM chat, no skill concern | 5 | ~1–2 hours | ~6–8 hours |
| B2 — Skills journey | 4 | ~2–3 hours (+ attach-mode skill cleanup work) | ~12 hours |
| B3 — Advanced orchestration | 3 | ~3–4 hours | ~10 hours |
| C — Caveated (ToolApproval, ToolMatrix) | 2 | ~4 hours + attach-mode verification guard | ~10 hours |
| **Total** | **29** | | **~45–50 hours** |

Note: the 45–50h estimate covers test-class migration only. Fixture implementation (`AspireHostFixture.cs`, `CleanAgentSkillState` portability, `aspire describe` JSON plumbing, Ollama probe porting) is a separate effort estimated in Mark's plan.

---

## Recommendation

1. **Proceed with Mark's phased plan.** The wave structure (3a screenshots → 3b navigation → 3c chat → 3d skills → 3e advanced) maps cleanly to Category A → B1 → B2/B3/C.

2. **Blocker B3 (`CleanAgentSkillState` in attach mode) is the only test-correctness risk.** Address it explicitly in `AspireHostFixture.InitializeAsync` — clean skill state on ALL paths, not just "we started Aspire."

3. **ToolApprovalFlowTests and ToolMatrixE2ETests** should include an attach-mode model verification guard: `GET /api/settings` → assert `model == ToolCapableTestModel`. Skip with a clear message if mismatch. This turns a silent failure into a visible skip.

4. **No test class needs to remain on the old `AppHostFixture` permanently.** The split model can be eliminated per Mark's plan.

---

## File Touchpoints Added by This Analysis

| Finding | Action |
|---------|--------|
| `CleanAgentSkillState` must run in ALL init paths | `AspireHostFixture.cs` — call regardless of start/attach |
| `ProbeOllamaToolCallCompatibilityAsync` must be ported | `AspireHostFixture.cs` — port from `AppHostFixture` |
| Scheduler URL from `aspire describe` JSON | `AspireHostFixture.cs` — add scheduler URL resolution |
| ToolApproval/ToolMatrix attach-mode guard | `ToolApprovalFlowTests.cs`, `ToolMatrixE2ETests.cs` — add model mismatch skip |
| `AttachedAspireTestBase` removal safe after Phase 2 | Confirm via `PirateJourneyAttachedTests` + `ChatRssDailyTaskAttachedTests` passing with new fixture |
