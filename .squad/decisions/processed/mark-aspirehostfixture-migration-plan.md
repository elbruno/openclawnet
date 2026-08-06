# Decision: AspireHostFixture Migration Plan — Phased E2E Execution Model

**Author:** Mark (Lead / Architect)  
**Date:** 2026-05-25  
**Status:** PENDING_BRUNO_REVIEW  
**Scope:** Playwright E2E test infrastructure — fixture lifecycle  

---

## Problem Statement

Today there are **two separate execution models** for Playwright E2E tests:

1. **`AppHostFixture`** (xUnit `IAsyncLifetime`) — boots Aspire in-process via `DistributedApplicationTestingBuilder`, waits for resources, resolves endpoints. Used by all CI/regression tests.
2. **`AttachedAspireTestBase`** — attaches to an already-running Aspire instance via `aspire describe`. Used only by `Demos/` tests.

Neither model supports the optimal local workflow: *"check if Aspire is already up; if yes reuse it; if no, start it automatically."*

The **DemoLauncher** (`PlaywrightDemoLauncher/Program.cs`) already checks Aspire status but **fails with an error** when Aspire is down rather than auto-starting it.

---

## Proposed Solution: `AspireHostFixture` (new unified fixture)

A new fixture class (`AspireHostFixture`) that implements a **three-state lifecycle**:

```
┌────────────────────────────────────────────────┐
│ 1. aspire describe --format Json               │
│    → Aspire is UP? Resolve endpoints → ATTACH  │
│    → Aspire is DOWN? → step 2                  │
├────────────────────────────────────────────────┤
│ 2. aspire start src\OpenClawNet.AppHost        │
│    → Wait for resources (web, gateway, sched.) │
│    → Resolve endpoints → mark "we started it"  │
├────────────────────────────────────────────────┤
│ 3. On dispose: if WE started Aspire → stop it  │
│    If we ATTACHED → leave it running           │
└────────────────────────────────────────────────┘
```

---

## Recommendation: Migrate 100% to AspireHostFixture (split model eliminated)

**Rationale:**
- The current `AttachedAspireTestBase` duplicates ~70% of Playwright setup logic.
- `AppHostFixture` duplicates service-resolution logic from `AttachedAspireTestBase`.
- A single fixture with the 3-state lifecycle handles both scenarios.
- Demo tests get auto-start convenience; CI tests get the same code path.
- Trait-based filtering (`Category=DemoLive`) already isolates demo-only tests from CI — the fixture model doesn't need to diverge.

**Kill the split.** One fixture, one lifecycle, trait-based scope control.

---

## Phased Implementation Plan

### Phase 1: Core Fixture (Green-Field)

**Goal:** Create `AspireHostFixture` with the 3-state lifecycle, passing one existing demo test.

| Task | File Touchpoint | Notes |
|------|----------------|-------|
| Create `AspireHostFixture.cs` | `tests/PlaywrightTests/AspireHostFixture.cs` (new) | Replaces eventual `AppHostFixture` |
| Implement `aspire describe` probe | Same | Extract from `AttachedAspireTestBase.TryResolveUrlsFromDescribeAsync` |
| Implement `aspire start` fallback | Same | Use `aspire start src\OpenClawNet.AppHost`, poll until healthy |
| Implement dispose-time `aspire stop` (conditional) | Same | Only if fixture started Aspire; track via `_weStartedAspire` flag |
| Endpoint health-check loop | Same | Reuse `WaitForEndpointReadyAsync` pattern from `AppHostFixture` |
| Playwright browser bootstrap | Same | Headed/headless from `PLAYWRIGHT_HEADED`; SlowMo from `PLAYWRIGHT_SLOWMO` |
| Orphan process cleanup on init | Same | Port `CleanupOrphanedPlaywrightNodeProcesses` from DemoLauncher |
| Create `AspireHostCollection.cs` | `tests/PlaywrightTests/AspireHostCollection.cs` (new) | xUnit `[CollectionDefinition]` |
| Unit test: fixture attaches when Aspire up | `tests/PlaywrightTests/AspireHostFixtureTests.cs` (new) | Integration-level smoke |

**Acceptance criteria:**
- `PirateJourneyAttachedTests` runs against `AspireHostFixture` (headed, Aspire pre-started) and passes.
- When Aspire is NOT running, fixture starts it, test passes, fixture stops it on dispose.
- No orphaned `dotnet`, `node`, or `chromium` processes after test run.

**Dependencies:** None (new code).

---

### Phase 2: Demo Migration Wave

**Goal:** Migrate all `Demos/` tests from `AttachedAspireTestBase` → `AspireHostFixture`.

| Task | File Touchpoint |
|------|----------------|
| Migrate `PirateJourneyAttachedTests` | `tests/PlaywrightTests/Demos/PirateJourneyAttachedTests.cs` |
| Migrate `ChatRssDailyTaskAttachedTests` | `tests/PlaywrightTests/Demos/ChatRssDailyTaskAttachedTests.cs` |
| Update `AttachedAspireTestBase` → soft-deprecate (leave for rollback) | `tests/PlaywrightTests/Demos/AttachedAspireTestBase.cs` |
| Update `Demos/README.md` | `tests/PlaywrightTests/Demos/README.md` |
| Verify DemoLauncher compatibility | `src/OpenClawNet.PlaywrightDemoLauncher/Program.cs` |

**Acceptance criteria:**
- All `[Trait("Category", "DemoLive")]` tests pass with `AspireHostFixture`.
- DemoLauncher (`dotnet run --project src/OpenClawNet.PlaywrightDemoLauncher`) still works (it doesn't touch the fixture — just calls `dotnet test`).
- No behavior changes for `PLAYWRIGHT_HEADED=true` + `PLAYWRIGHT_SLOWMO=1500` workflow.

**Dependencies:** Phase 1.

---

### Phase 3: Regression Suite Migration Wave

**Goal:** Migrate CI/regression tests from `AppHostFixture` → `AspireHostFixture`.

| Task | File Touchpoint |
|------|----------------|
| Replace `AppHostFixture` with `AspireHostFixture` in `PlaywrightTestBase` | `tests/PlaywrightTests/PlaywrightTestBase.cs` |
| Update collection definition (`AppHostCollection` → `AspireHostCollection`) | All test files using `[Collection("AppHost")]` |
| Remove `DistributedApplicationTestingBuilder` path from fixture | `AspireHostFixture.cs` — CI mode uses CLI-based start like local |
| Preserve model probing (Ollama, Azure OpenAI) | Move into `AspireHostFixture` |
| Preserve agent skill state cleanup | Move into `AspireHostFixture` |
| Run full suite (`dotnet test tests/OpenClawNet.PlaywrightTests`) | Validate no regressions |

**Migration waves (by test complexity):**

| Wave | Tests | Risk |
|------|-------|------|
| 3a | Screenshot tests (`HelloWorld`, `Jobs`, `Settings`, `Tools`) | Low — no LLM |
| 3b | Navigation + settings (`BlazorNavigation`, `SettingsGeneral`, `ProviderSwitch`) | Low |
| 3c | Chat flows (`ChatFlow`, `ChatUrlSummary`, `ChatRssDaily`) | Medium — LLM timing |
| 3d | Skills journeys (`SkillsPirate`, `SkillsEmoji`, `SkillsBulletPoint`, `SkillsImport`, `SkillsBulkDelete`) | Medium |
| 3e | Tool approval + advanced (`ToolApprovalFlow`, `ToolMatrix`, `WebsiteWatcher`, `ActivityPanel`, `GatewayApi`, `SecretsVault`, `SessionsDelete`) | High |

**Acceptance criteria:**
- 100% of tests pass against `AspireHostFixture` with same pass/skip rates as `AppHostFixture`.
- `AppHostFixture` and `AppHostCollection` deleted.
- `AttachedAspireTestBase` deleted.
- `docs/testing/e2e-test-index.md` regenerated via `scripts\test-and-publish.ps1`.

**Dependencies:** Phase 2.

---

### Phase 4: Cleanup & Documentation

**Goal:** Remove dead code, update docs, harden process cleanup.

| Task | File Touchpoint |
|------|----------------|
| Delete `AppHostFixture.cs` | `tests/PlaywrightTests/AppHostFixture.cs` |
| Delete `AppHostCollection.cs` | `tests/PlaywrightTests/AppHostCollection.cs` |
| Delete `AttachedAspireTestBase.cs` | `tests/PlaywrightTests/Demos/AttachedAspireTestBase.cs` |
| Update DemoLauncher to report fixture mode | `src/OpenClawNet.PlaywrightDemoLauncher/Program.cs` |
| Update `docs/testing/e2e-test-index.md` | Regenerate |
| Update `tests/catalog.yaml` if test names changed | `tests/catalog.yaml` |
| Add orphan-process kill step to fixture dispose | `AspireHostFixture.cs` |
| Update `docs/testing/playwright-demo-launcher.md` | Architecture note |

**Acceptance criteria:**
- No references to `AppHostFixture` or `AttachedAspireTestBase` remain.
- `docs/testing/e2e-test-index.md` fresh.
- Full test run leaves zero orphaned processes (verify with `tasklist` assertion in CI).

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| `aspire start` takes 30-60s → test suite slower on cold start | Medium | Fixture starts once per collection (xUnit shared fixture). Identical to current `AppHostFixture` timing. |
| Aspire port conflicts if user has another instance | Medium | `aspire describe` detects existing instance first; only starts if truly down. |
| `aspire stop` fails → orphaned processes | High | Dispose calls `aspire stop`, then runs `kill-orphaned-aspire.ps1` as safety net. |
| Node/Chromium zombies from Playwright | Medium | `CleanupOrphanedPlaywrightNodeProcesses()` in fixture init AND dispose. |
| Tests assume specific port numbers | Low | All URLs resolved dynamically from `aspire describe` output — no hardcoded ports. |
| CI breaks during migration | High | Phases 1-2 introduce new code without removing old. Phase 3 uses wave-based rollout with per-wave validation. |

---

## Rollback Strategy

- **Phase 1-2:** Old `AppHostFixture` and `AttachedAspireTestBase` remain untouched. Rollback = revert new files.
- **Phase 3:** Each wave is a separate PR. Revert the wave PR to roll back to previous fixture.
- **Phase 4:** Only executed after Phase 3 is stable on `main` for ≥2 days. Rollback = revert + restore deleted files from git history.

---

## Stability Validation: Orphan Process Avoidance

1. **Fixture init:** Call `CleanupOrphanedPlaywrightNodeProcesses()` (already exists in DemoLauncher).
2. **Fixture dispose:** 
   - Close browser → dispose Playwright → `aspire stop` (if we started it).
   - Run `scripts/kill-orphaned-aspire.ps1` as safety net.
   - Assert no orphaned `node.exe` processes with commandline containing "playwright".
3. **Post-test hook (optional):** Add a finalizer test that asserts process tree is clean.
4. **Team rule:** Document in `.squad/decisions.md` that `aspire stop` is mandatory (aligns with existing skill `.squad/skills/aspire-lifecycle/SKILL.md`).

---

## File Touchpoint Summary

| File | Phase | Action |
|------|-------|--------|
| `tests/PlaywrightTests/AspireHostFixture.cs` | 1 | CREATE |
| `tests/PlaywrightTests/AspireHostCollection.cs` | 1 | CREATE |
| `tests/PlaywrightTests/Demos/PirateJourneyAttachedTests.cs` | 2 | MODIFY |
| `tests/PlaywrightTests/Demos/ChatRssDailyTaskAttachedTests.cs` | 2 | MODIFY |
| `tests/PlaywrightTests/Demos/AttachedAspireTestBase.cs` | 2→4 | DEPRECATE→DELETE |
| `tests/PlaywrightTests/PlaywrightTestBase.cs` | 3 | MODIFY (swap fixture type) |
| `tests/PlaywrightTests/AppHostFixture.cs` | 3→4 | MODIFY→DELETE |
| `tests/PlaywrightTests/AppHostCollection.cs` | 4 | DELETE |
| All `*E2ETests.cs`, `*ScreenshotsTest.cs` files | 3 | MODIFY (`[Collection]` attr) |
| `src/OpenClawNet.PlaywrightDemoLauncher/Program.cs` | 2,4 | VERIFY, minor update |
| `docs/testing/e2e-test-index.md` | 4 | REGENERATE |
| `tests/catalog.yaml` | 4 | VERIFY |
| `docs/testing/playwright-demo-launcher.md` | 4 | UPDATE |
| `scripts/kill-orphaned-aspire.ps1` | 1 | REFERENCE (already exists) |

---

## Decision

**Migrate 100% of tests to a single `AspireHostFixture` with the 3-state lifecycle.** Kill the split model. Use phased rollout with per-wave validation and easy rollback via PR revert.

Priority: local/demo workflows first (Phases 1-2). CI alignment follows (Phase 3).
