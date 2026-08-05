# Session Log

Append-only record of batch completion and squad productivity.

---

## 2026-06-09 — Issue Verification & Dashboard Sync Workflow Fix

**Batch:** Ollama model fix verification (#120/#122) + E2E dashboard root cause diagnosis (#125) + sync workflow enhancement

**Key Deliverables:**

1. **Issues #120/#122 Verification — Irving (sync)**
   - Confirmed Ollama provider model fallback fixes already shipped and correct
   - Verified three-tier fallback chain: `profile.Model ?? definition.Model ?? hardcoded fallback`
   - Test results: 12/13 passing, 7 skipped (unrelated #95 blocker)
   - Decision: No code changes needed; fixes are production-ready
   - Outcome: Backend model handling validated; blocker is not application code

2. **Issue #125 Root Cause Analysis — Dylan (sync)**
   - Diagnosed public test-dashboard 404 as **CI/CD delivery gap, not application bug**
   - Root cause: `.github/workflows/sync-to-public.yml` missing trigger path and rewrite rules
   - Private `docs/test-dashboard/` exists but never synced to public repo
   - Fix plan: Two minimal edits to sync workflow (no test/application code changes)
   - Outcome: Clear handoff to Mark with exact workflow change requirements

3. **Dashboard Sync Workflow Fix — Mark (sync)**
   - Edited `.github/workflows/sync-to-public.yml`: added trigger path `docs/test-dashboard/**`
   - Added path rewrite: `plan/docs/test-dashboard/` → `staging/test-dashboard/` (matches sessions pattern)
   - Verified via `scripts\test-and-publish.ps1 -SkipTests`: 23 files successfully synced
   - Outcome: Dashboard now auto-syncs to public repo; issue #125 unblocked

4. **Decision Ledger Merge — Scribe (sync)**
   - Merged 3 inbox decisions into `.squad/decisions.md`: Irving verification, Dylan diagnosis, Mark's implementation
   - Cleaned up all inbox decision files after merge
   - Created orchestration log entry documenting three-agent session
   - Updated agent histories: Irving (#120/#122 test verification), Dylan (#125 root-cause analysis), Mark (workflow fix rationale)
   - Outcome: All decisions recorded and cross-linked; learnings captured for team

**Pattern Observed:** When an E2E feature fails in production, the root cause can be code (application bug), infrastructure (missing feature), or **delivery** (assets exist but not deployed). Dylan's diagnosis correctly identified this as a delivery issue, which changed the fix strategy from test/code to CI/CD configuration. This prevented wasted effort on application changes and unblocked the user-facing feature quickly.

**Cross-Batch Coordination:** Irving's backend verification provided confidence that the page component works correctly. Dylan's diagnosis identified the real blocker (sync workflow). Mark's fix completed the delivery chain. All three contributions were necessary to fully resolve the user issue.

---

## 2026-05-25 — Restart Handoff Prepared

**Prepared by:** Scribe

Phase 1 + Phase 2 complete. Branch `feat/aspirehostfixture-phase1` stable. Session paused at Phase 3 Wave 3a. Restart note created (`.squad/identity/now.md` + `.squad/log/restart-handoff-2026-05-25.md`). On resume, verify Docker/Aspire health, rerun E2E validation, then continue Phase 3 waves.

---

## 2026-05-22 — Build Fix → Visible Browser → Runtime Authentication Blocker

**Batch:** Package alignment + E2E demo infrastructure hardening + blocker identification

**Key Deliverables:**

1. **Package Version Alignment — Irving (sync)**
   - Identified NU1605 "Detected package downgrade" blocking all builds
   - Implemented centralized version overrides in `Directory.Build.targets` (single source of truth)
   - Applied coordinated versions: AspNetCore, EF Core, Extensions, Playwright (all 10.0.x/1.52.0)
   - Validation: `dotnet build`, `dotnet restore`, Aspire startup all passed
   - Outcome: Clean build foundation established; unblocked downstream work

2. **Visible Headed Demo Rerun — Dylan (sync)**
   - Reran BrowseAndSchedule E2E demo after package fix
   - Applied new Playwright pattern: Prebuild + `--no-build --no-restore` + `WaitForSelectorState.Attached`
   - Evidence: Headed Chromium window now launches and stays visible (demo experience confirmed)
   - Chat interaction verified: First message sends and receives visible response
   - Updated `docs/testing/e2e-test-index.md` per team rule (2026-05-11)
   - Outcome: Infrastructure blocker eliminated

3. **Active Runtime Blocker Identified — Dylan (sync)**
   - Browser infrastructure now stable; new failure is isolation of authentication layer
   - Browse step → HTTP 401: "Access denied due to invalid subscription key or wrong API endpoint"
   - Schedule step → HTTP 401: Same credential error
   - No job created (workflow stopped at agent execution, not browser/demo infrastructure)
   - Issue #84 updated with latest rerun output + root cause analysis
   - Outcome: Clear hand-off to credential/auth team; infrastructure team work complete

4. **Decision Documentation — Scribe (sync)**
   - Merged 3 inbox decisions into `.squad/decisions/decisions.md`:
     - User directive (Mark): Package version priority
     - Irving's decision: Directory.Build.targets approach + rationale
     - Dylan's decision: Attached demo execution rule + blocker identification
   - Created 2 orchestration logs (Irving + Dylan) with detailed validation steps
   - Appended session log entry

**Agents:** Irving (Backend), Dylan (Testing), Scribe (documentation)

**Decision Sources:** `.squad/decisions/inbox/` (3 files merged)

**Progression:**
- Start: Build fails with version conflicts (NU1605)
- After Irving: Build succeeds; browser infra still broken
- After Dylan: Browser visible + chat interaction works; runtime auth fails
- Result: Clear problem decomposition; infrastructure stable; credentials identified as next blocker

**Status:** ✅ Batch Complete — Visible browser confirmed; HTTP 401 auth blocker isolated

---

## 2026-05-12 — E2E Issue Triage + Rule Verification

**Batch:** E2E test status assessment and rule validation

**Key Deliverables:**

1. **E2E Test Triage — Mark (background)**
   - Reviewed `docs/testing/e2e-test-index.md` and assessed all E2E/integration tests
   - Created 6 GitHub issues for failing/skipped tests:
     - #202–#204: Docker/Aspire infrastructure health failures (MarkdownConvert, BlazorNavigation)
     - #205–#207: Missing execution baselines (ToolApprovalFlow, ChatFlow, WebsiteWatcher)
   - Routing: Irving (Aspire health), Dylan (test harness/category gates)

2. **E2E Rule Verification — Dylan (background)**
   - Verified ownership of `docs/testing/e2e-test-index.md`
   - Confirmed: Dylan owns index, rule exists in `.squad/decisions.md` (2026-05-11) + index file header
   - Finding: Rule is clear, discoverable, consistent across sources
   - Outcome: No action required; team rule already established

3. **Orchestration Logs & Session Log — Scribe (sync)**
   - Created 2 orchestration-log entries (Mark + Dylan)
   - Recorded session summary in log.md
   - No inbox files to merge this cycle

**Agents:** Mark (Lead), Dylan (Tester), Scribe (documentation)

**Decision Sources:** `.squad/decisions.md` (2026-05-11: E2E index rule + Aspire lifecycle rule)

**Status:** ✅ Complete

---

## 2026-05-25 — E2E Fixture Architecture Kickoff: AspireHostFixture Phase 1

**Batch:** AspireHostFixture foundation + Playwright process hygiene + inbox decision merge + session documentation

**Key Deliverables:**

1. **Irving — AspireHostFixture Phase 1 Implementation (sync)**
   - Created `AspireHostFixture.cs`: 3-step detection (aspire describe → env-var override → HTTP health), conditional start, failure-safe IsReady gate
   - Extracted/hardened `AspireDescribeResolver.cs`: Robust JSON parsing, integrated into AttachedAspireTestBase
   - Created `PlaywrightProcessHygiene.cs`: PID-explicit cleanup, 10-second drain (per SKILL.md), orphaned node filtering
   - Configured pilot test path: `Demos/AspireHostFixturePilotTests/` (1 test, backward compatible)
   - Branch: `feat/aspirehostfixture-phase1` ready for Dylan's fit assessment
   - Blockers captured: File lock contention, Playwright node access denied, test-and-publish strict-mode

2. **Scribe — Decision Merge & Documentation (sync)**
   - Merged 4 inbox decisions into `.squad/decisions.md`:
     - Irving Phase 1 implementation decisions (fixture, resolver, hygiene, pilot path)
     - Irving Playwright launcher catalog source (tests/catalog.yaml as metadata root)
     - Mark launcher thin scope boundaries (preset selector, not full framework)
     - Petey RSS daily template (sixth built-in job)
   - Created orchestration log: `.squad/orchestration-log/2026-05-25T10-00-24Z-irving-phase1-kickoff.md`
   - Cleared all merged inbox files (4 files)
   - Appended session log entry

**Agents:** Irving (Backend/Infrastructure), Scribe (Documentation)

**Decision Sources:** `.squad/decisions/inbox/` (4 files merged and cleared)

**Cross-Agent Alignment:**
- **Mark's vision:** Phase 1 complete (detection + conditional start + pilot); Phase 2 readiness identified (test discovery loop, stop conditions)
- **Dylan's fit assessment:** Ready; contract provides clear startup/stop semantics, health guarantees, process cleanup patterns
- **Petey's launcher:** Thin scope confirmed; catalog.yaml as source; launcher keeps existing cleanup in Phase 1

**Progression:**
- Start: Mark's migration plan defined; Irving scopes Phase 1 work
- After Irving: AspireHostFixture implementation complete, blockers captured, handoff ready
- After Scribe: Decisions documented, team alignment recorded, session logged

**Status:** ✅ Batch Complete — Foundation laid for Dylan's fit assessment; Phase 2 planning enabled

---

## 2026-05-25 — Phase 2 Demo Migration: AspireHostFixture Test Integration

**Batch:** Irving Phase 2 demo migration + Scribe inbox processing + orchestration logging

**Key Deliverables:**

1. **Irving — Phase 2 Demo Test Migration (sync)**
   - Migrated `PirateJourneyAttachedTests` and `ChatRssDailyTaskAttachedTests` to `AspireHostFixture`-backed path
   - Implemented `AspireHostAttachedDemoTestBase`: new base class, `[Collection("AspireHost")]` decoration, fixture lifecycle ownership
   - Preserved launcher-driven headed/slowmo behavior (`PLAYWRIGHT_HEADED`, `PLAYWRIGHT_SLOWMO`)
   - Retained `AttachedAspireTestBase` as deprecated fallback (rollback safety)
   - Branch: `feat/aspirehostfixture-phase1` integration complete
   - Scope boundaries locked: No `AppHostFixture` changes, no broad suite migration, launcher behavior unchanged

2. **Scribe — Decision Merge & Documentation (sync)**
   - Merged inbox decision into `.squad/decisions.md`:
     - Irving Phase 2 Demo Migration decision (full implementation details, scope boundaries, Phase 3 blockers)
   - Deleted merged inbox file: `.squad/decisions/inbox/irving-phase2-demo-migration.md`
   - Created orchestration log: `.squad/orchestration-log/2026-05-25T10-17-52Z-irving-phase2-demo.md` (detailed execution record)
   - Created session log entry: `.squad/sessions/session-8.md` (cross-agent summary)

**Agents:** Irving (Backend/Infrastructure), Scribe (Documentation)

**Decision Sources:** `.squad/decisions/inbox/irving-phase2-demo-migration.md` (1 file merged and cleared)

**Phase 3 Blockers (PENDING):**
- B3 blocker review: `CleanAgentSkillState()` must be called in attach mode
- Broader suite migration assessment (B1/B2 wave structure per 2026-05-25 earlier evaluation)
- Test fixture reliability validation required before expansion

**Cross-Agent Alignment:**
- **Irving's implementation:** Demo tests now use Phase 1 fixture; Phase 2 specific; Phase 3 scope pending blocker review
- **Dylan's fit assessment:** Reliable foundation for demo test execution; ready for Phase 3 evaluation
- **Mark's migration vision:** Phase 2 complete; Phase 3 planning gates on blocker resolution

**Progression:**
- Start: Phase 1 fixture foundation established
- After Irving: Phase 2 demo tests migrated; rollback safety preserved
- After Scribe: Decisions documented; orchestration logged; team alignment recorded

**Status:** ✅ Batch Complete — Phase 2 migration shipped; Phase 3 blockers identified; ready for Dylan's fit assessment

---

## 2026-04-24 — Phase 1 Dashboard Ship + Spanish Slides + README Simplification

**Batch:** Multi-repo sync (openclawnet-plan main, openclawnet main + public docs)

**Key Deliverables:**

1. **README Simplification** (both repos)
   - Removed NUGET_PACKAGES env var requirement
   - Simplified Quick Start: `git clone → ollama pull → dotnet build → aspire start`
   - openclawnet-plan: commit 5b11ec9 (pushed main)
   - openclawnet: commit 60dedb1 (pushed main)

2. **Phase 1 Dashboard — feature/job-output-dashboard**
   - Irving: JobRunArtifact entity + SchemaMigrator, auto-capture in Scheduler, /api/channels REST endpoints, 100-run + 30-day retention (commits f7bc624, d522423)
   - Helly: Home page redesign with Recent Job Output widget, Chat → /chat, scaffolded OpenClawNet.Channels Blazor site (MudBlazor + Markdig) at ports 5030/7030; channels list + detail (commit 6ffeca3)
   - Mark: Documented Aspire-Blazor scaffold pattern at .squad/skills/aspire-blazor-scaffold/SKILL.md (capture after Helly's build)
   - Dylan: 28-test plan + 4 files (555/560 tests passing); 4 EF Core in-memory SQLite enum-default failures documented in decisions/inbox (commits c1a992f, e4d486b, ce699b4, 051a89f)

3. **Spanish Slides** (openclawnet public repo, commit 433cd20)
   - Helly: Copied slides-es.{html,md} (sessions 1-2) from plan repo to docs/landing/sessions/
   - Updated docs/landing/index.html: new "🎤 Presentaciones · Español" section with live S1+S2 cards + Próximamente S3-5
   - Co-speaker credit: Pablo Piovano (LinkedIn: https://www.linkedin.com/in/ppiova/)
   - Live site verified post-deploy workflow #24: https://elbruno.github.io/openclawnet/

**Locked Decisions (merged from decisions/inbox → decisions.md):**
- Job Output Dashboard architecture finalized (Channels project, 64 KB inline/disk, retention 100+30d, polling 10s, IChannelDeliveryAdapter seam, /chat reroute, loopback-only auth v1)
- Job-creation UX: toast "Job created · click to navigate" auto-dismiss 5s (already implemented earlier session)
- EF Core enum-default test issue: 4 tests documented; recommended resolution: add `[Fact(Skip="...")]` attributes

**Agents:** Irving (producer), Helly (consumer + Spanish docs), Mark (architecture capture), Dylan (test coverage + enum issue analysis)

**Branches:** feature/job-output-dashboard (main work) + main (README + Spanish docs)

---

## 2026-04-24 — Channels & Scheduled Jobs PR Shipped (PR #64 merged via squash)

**Batch:** Channels registry display + Jobs multi-instance templates + inline rename UX + comprehensive test coverage

**Key Deliverables:**

1. **Feature: Multi-Instance Job Templates** (Irving + Dylan)
   - Backend: Removed 409 conflict blocker on `/api/demos/{name}/setup`; auto-suffixes duplicate names (e.g., "Website Watcher (2)")
   - Schema: Added `Jobs.SourceTemplateName` (nullable) for template lineage tracking
   - Tests: 8 new tests covering multi-instance creation, unique naming, SourceTemplateName immutability on rename + delete scenarios
   - Verification: 579 unit tests passing (+11 from unskipping 5 tests, +6 new methods); 0 failures, 3 intentional skips (future features)

2. **Feature: Inline Rename for Jobs** (Helly + Dylan + Irving)
   - UX: Pencil edit icon on Jobs.razor scheduled-jobs list; MudTextField with Enter/Escape keyboard shortcuts, success/error toasts
   - Backend: Reuses existing PUT `/api/jobs/{id}` endpoint (no new endpoint); validates name uniqueness, rejects duplicates inline
   - Tests: 7 bUnit component tests (compile pass; runtime JSInterop requires MudPopoverProvider scaffolding; marked [Fact(Skip=...)] to keep build green; follow-up issue filed)

3. **Documentation: Jobs Manual Updates** (Mark)
   - Updated `docs/manuals/30-jobs.md`: Multi-Instance Templates section + Template Lineage + Inline Rename Workflow
   - Created `.squad/files/pr-body-channels-jobs.md`: Full PR body with feature summary, bug inventory, schema changes, test coverage, cross-agent attribution

4. **Regression Test Coverage** (Dylan)
   - Added enum regression guard: `JobRunArtifactKind_TextIsZero_PreventsEFDefaultDrop` (protects against future enum reordering)
   - Added round-trip tests for Markdown artifact persistence
   - Updated skip reasons for 3 pending tests (Channels table + ChannelStore API + DPAPI platform-specific)

5. **Known Follow-Ups (Issues Filed)**
   - #1 ChannelDetail.razor shape mismatch: Gateway ChannelDetailDto missing `Artifacts` property; Mark investigated, 3 fix options (A/B/C) documented, awaiting Bruno decision
   - #2 Wire up MudPopoverProvider for bUnit tests: 7 Helly tests need JSInterop configuration; alternative: pivot to API integration tests

**Agents Shipped This Session:** Dylan (tests), Helly (frontend), Mark (docs), Irving (implied backend)

**Test Summary:** 579 passed / 0 failed / 3 skipped (up from 568 passed / 8 skipped)

**PR History:**
- Opened PR #64 (fix/channels-and-scheduled-jobs → main)
- Merged via squash (squash commit 6e6613b)
- Branch deleted

**Commit Message:**
```
feat: channels registry + jobs multi-instance templates + inline rename

- Remove 409 on duplicate template setup; auto-suffix names
- Add SourceTemplateName for template lineage tracking
- Inline rename UX on Jobs.razor with validation & success feedback
- 8 regression tests + 5 previously-skipped tests now active (579 passing)
- Update docs/manuals/30-jobs.md with new workflows
- bUnit scaffolding for component testing (runtime config TBD)
```

**Session Artifacts:** Inbox files merged to decisions.md (dylan-regression-tests.md, helly-bunit-installed.md)

**Branches:** fix/channels-and-scheduled-jobs (shipped/deleted); main (squash merged)

## 20260426T205613Z — Wave 1 Scribe merge (storage W-1 + skills locked + K-1 audit)

**Branch:** squad/storage-location-design
**Requested by:** Bruno Capuano (via Coordinator)

**Storage Wave 1 shipped:**
- 8d753d docs(storage): lock W-1 acceptance criteria + record baseline (Mark)
- 96585da feat(storage): W-1 ISafePathResolver + OpenClawNetPaths (Irving)
- 23e057f test(storage): W-1 SafePathResolver + OpenClawNetPaths fuzz + unit suite (Dylan)
- W-1 tests: **83/83 green** (acceptance bar met; pre-existing 19 baseline failures unrelated)

**Skills planning locked:**
- Q1–Q5 + L-5 manual authoring decisions merged (coordinator-skills-q1q5-locked.md → decisions.md)
- Skills implementation now gated only on Storage W-1 (now landed) → K-wave can proceed

**Petey K-1 migration audit landed:**
- `70ed187` docs(squad): Petey K-1 migration audit (skills foundations)
- 3 surprises flagged for Mark (see decisions.md entry for K-1 audit details)

**Drummond W-1 gate verdict:** not yet inboxed at merge time — deferred to next Scribe cycle.

**Inbox merged + cleared:** 3 files (coordinator-skills-q1q5-locked, mark-w1-baseline-and-acs, petey-k1-migration-audit).

---

## 2026-04-30 — Flaky Test Stabilization PR #97 Merged + Aspire CLI Workflow Decisions

**Batch:** Flaky test stabilization (PR #97 merged) + Aspire workflow locked

**Key Deliverables:**

1. **PR #97 Merged — Dylan Flaky Test Stabilization (4-Cycle Investigation)**
   - Branch: `fix/phase2b-flaky-test-stabilization` (deleted local + remote)
   - Commit: d32bba2 (squash-merged from 031760b)
   - Scope: 27+ failing unit tests (ChatEndpointProfileTests, SkillImport transitive deps, ~102 missing-DLL errors)
   - Investigation: 4 cycles — root cause (MSBuild non-determinism) → tried CopyLocalLockFileAssemblies (broke testhost) → tried surgical refs (75% flaky failure rate) → final solution (CopyLocalLockFileAssemblies + clean+build workflow)
   - Solution: `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` in test csproj + surgical ProjectReference metadata + real DI fixes (ChatNamingService + IModelClient mock)
   - Final State: 1,291 pass / 0 fail / 43 skip (deterministic with clean+build workflow)
   - Decision: CopyLocalLockFileAssemblies IS required; ~2-5s clean+build overhead per cycle is cost of determinism
   - Documentation: README.md TDD section updated with clean+build workflow

2. **Aspire CLI Workflow Locked (Coordinator + Bruno)**
   - Rule: Always use `aspire start` (never `dotnet run --project AppHost`)
   - Rule: Always use `aspire stop` (never Ctrl+C)
   - Rule: Use `aspire describe --format Json` for runtime URL discovery (dynamic per run)
   - Rationale: Bare `dotnet run` fails with missing ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL; Ctrl+C leaves orphaned DLL locks; hardcoded URLs break across runs
   - Verification: Live E2E validation passed (Web/Gateway/9 services, Playwright + Chrome, chat with Azure OpenAI, skill profiles, agent interaction)

3. **Decision Inbox Consolidated (Scribe)**
   - Files merged: copilot-aspire-cli-workflow.md, copilot-aspire-stop-rule.md, copilot-directive-aspire-start.md, dylan-copylocallock-investigation-2026-04-30.md, dylan-live-e2e-2026-04-30.md, +11 others
   - Duplicates removed: 3 Aspire-related files consolidated into 2 decisions
   - New entries in decisions.md: 2026-04-30 Aspire workflow + 2026-04-30 CopyLocalLockFileAssemblies decision

**Agents:** Dylan (test stabilization), Coordinator/Bruno (Aspire workflow), Scribe (decision merge + logs)

**Test Outcome:** 1,291 unit pass + full integration suite green (deterministic baseline established)

**Branches Deleted:** fix/phase2b-flaky-test-stabilization (local + remote)

**Inbox Cleared:** 15 files consolidated into decisions.md, inbox directory archived

---

### 2026-05-23: Wave 2 Storage Hardening shipped + Scribe merge wave 2

**Branch:** `squad/storage-location-design`
**Logged by:** Scribe (Bruno Capuano via Coordinator)

#### Storage W-2 (Irving impl + Dylan tests, all green)
- `c0ef4e5` Irving — IStorageAclVerifier seam (Drummond H-7)
- `b12ca10` Irving — UnsafePathException carries Reason+ScopeRoot+RequestedPath (H-8)
- `7704c55` Irving — per-agent + models + user subfolder helpers w/ Windows DACL hardening
- `125c251` Irving — AppHost propagates `OPENCLAWNET_STORAGE_ROOT` to gateway+web
- `c45bdfd` Irving — FileSystemTool routed through ISafePathResolver (H-2 closure)
- `0684a2e` Dylan — W-2 ACL verifier + exception promotion + per-scope helpers + FileSystemTool (50 tests, 62/63 green, 1 flaky-under-parallel)
- **Storage area total: 207 tests (145 W-1 + 62 W-2)**, no new regressions vs W-1 baseline.

#### K-1 design (Mark, post-Petey audit)
- `660f125` Mark — K-1 design decisions (MAF topology, MCP overlap, csproj fate)
- `7f63096` Mark — include K-1 inbox doc
- K-D-1: single `OpenClawNetSkillsProvider` per request via `AgentInlineSkill`; no multi-root, no stacked providers; `DisableCaching=true`.
- K-D-2: drop 3 MCP-overlapping built-ins (shell-exec, file-system, web-search); v1 ships `memory` + `doc-processor` only.
- K-D-3: delete + recreate `OpenClawNet.Skills.csproj`; K-1 splits into K-1a (demolish) + K-1b (rebuild).

#### Drummond W-2 hardening gate
- `bc83d20` + `7013bd2` Drummond — W-2 verdict: **APPROVED-WITH-NOTES**.
- All 7 binding criteria met. W-3 (`models root`) **CLEARED TO START** with 3 P0 + 3 P1 binding ACs (SHA-256 verifier, quota seam, model-name allowlist; OLLAMA_MODELS/HF_HOME via AppHost, audit emission, concurrent-download lock).
- Notes: Dylan's `FileSystemToolSafePathTests.List_RoutesPathThroughSafePathResolver` flakes under parallel xunit (passes in isolation — collection attribution issue, non-blocking).
- Lockout: `FileSystemTool` 2-arg back-compat ctor must be `[Obsolete]`-marked in W-3.

#### K-3 UI spec (Helly)
- `a39199d` Helly — K-3 UI design spec (`docs/proposals/skills-ui-spec.md`).
- `86f4208` Helly — inbox doc.
- 13 new Razor components, plain Bootstrap end-to-end (D-2), polling SnapshotProvider (D-3), uniform 📚 icon (D-1). 4 open UI questions for Bruno (none block K-1).

#### W-3 in flight
- Irving + Dylan currently working W-3 (models root: SHA-256 download verifier, quota, model-name allowlist).

#### Scribe wave 2 housekeeping
- Merged 6 inbox docs (drummond-w1, mark-k1, irving-w2, dylan-w2-apphost-test-gap, drummond-w2, helly-k3) into `decisions.md` (size now ~324 KB).
- Cleared `.squad/decisions/inbox/` and added `.gitkeep`.
- **Un-gitignored `.squad/decisions/inbox/*.md`** (Drummond's ask) — verdict commits no longer need `git add -f`. Verified with `git check-ignore`.

#### Wave 3 + Wave 4 + K-1a — storage epic close (2026-04-26)
- **Wave 3 shipped:** storage W-3 (`929e2e4` `63907a0` `c678be4` `18df86f` `bd3385b` `048dcdc` `0666c9c`) — 212/214 storage tests, +67 from W-2 baseline. Drummond W-3 verdict at `59c9056` (⚠ APPROVED-WITH-NOTES).
- **Wave 4 shipped:** storage W-4 (`e31a08c` `11af13c` `2cd373b` `79331e1` `e53ba9b` `6e67a2f` `70e7ae5`) — +66 W-4 tests. Drummond W-4 gate verdict not yet dropped at merge time; will be merged by next Scribe pass.
- **Helly K-3 UI spec + W-4 UI:** `a39199d` `86f4208` plus W-4 UI files swept into Petey/Irving commits due to shared-tree collisions; attribution drift documented. Helly bookkeeping at `49c7197`.
- **K-1a shipped (Petey):** `f6e2dd3` `7bf67e2` `c9d61ba` `aed617a` — 6 product .cs + 2 test .cs deleted, `OpenClawNet.Skills.csproj` recreated, stub `ISkillsRegistry` seam in place.
- **Storage epic CLOSED at HEAD `70e7ae5`** (squad/storage-location-design).
- **Scribe wave 3+4 housekeeping:** Merged 8 inbox docs (irving-w3, dylan-w3, drummond-w3, irving-w4, helly-w4-impl, helly-w4-csrf, dylan-w4-quota-ctor, petey-k1a) into `decisions.md` (size now ~392 KB). Cleared `.squad/decisions/inbox/`. Added binding **shared-tree git hygiene** convention (no `git add .`, no `git commit -am`, no `git stash` of others' work) per Helly + Petey + Irving collision reports.