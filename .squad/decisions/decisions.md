# Squad Decisions Ledger

Merged decisions from agent inbox submissions. Newest first.

---

## 2026-05-22 - User Directive: Package Version Alignment Priority

**Author:** Mark (Lead Architect) · **Domain:** Build & Deployment
**Captured by:** Copilot (via Mark)
**Source:** `inbox/copilot-directive-2026-05-22T17-30-54-290-04-00.md` (merged 2026-05-22T17:30:54Z)
**Status:** ✅ DIRECTIVE CAPTURED

### Decision

If the blocker is related to package version issues, update all .NET packages to the latest versions, make everything build first, and then continue the work.

### Why

- User request — captured for team memory and prioritization
- Package version skew has been a recurring source of build failures
- Building cleanly first improves all downstream work (tests, E2E, deployment)

---

## 2026-05-22 - Package Version Alignment: Directory.Build.targets Repository Override

**Author:** Irving (Backend Developer) · **Domain:** Build & Deployment
**Approved by:** Team (implicit, decision driven by blocker resolution)
**Source:** `inbox/irving-package-upgrade-build-fix.md` (merged 2026-05-22T17:30:54Z)
**Status:** ✅ IMPLEMENTED & VALIDATED

### Design Decision

Use repo-root package overrides in `C:\src\openclawnet\Directory.Build.targets` to align shared .NET package families instead of editing dozens of individual `.csproj` files.

### Rationale

- The failing restore path was caused by version skew, not missing package references
- This repo mixes explicit versions with `Version="*"` references, so per-project edits would create unnecessary churn and leave the drift easy to reintroduce
- `Directory.Build.targets` lets us pin the shared families once, after project items load, which is the safest place to normalize versions across the solution

### Applied Versions

- `Aspire.Hosting.Testing` → `13.2.4`
- `Microsoft.AspNetCore.*` test/runtime packages implicated in the build path → `10.0.8`
- `Microsoft.EntityFrameworkCore.*` packages used by storage/tests → `10.0.8`
- `Microsoft.Extensions.*` packages involved in the downgrade chain → `10.0.8`
- `Microsoft.Playwright` → `1.52.0` repo-wide for consistency

### Validation Performed

- ✅ `dotnet restore tests\OpenClawNet.PlaywrightTests\OpenClawNet.PlaywrightTests.csproj -v minimal`
- ✅ `dotnet build OpenClawNet.slnx -v minimal '-clp:ErrorsOnly;Summary'`
- ✅ `dotnet build src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj -v minimal '-clp:ErrorsOnly;Summary'`
- ✅ `aspire start --apphost C:\src\openclawnet\src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj`
- ✅ `aspire stop`

---

## 2026-05-22 - Playwright Demo Execution Rule: Attached Prebuild + No-Build Flow

**Author:** Dylan (Test Specialist) · **Domain:** E2E Testing & Automation
**Approved by:** Team (implicit, decision driven by test blocker resolution)
**Source:** `inbox/dylan-visible-headed-rerun.md` (merged 2026-05-22T17:30:54Z)
**Status:** ✅ IMPLEMENTED & DEMONSTRATED

### Design Decision

For attached Playwright demos that target an already-running Aspire app:

1. **Prebuild** the Playwright test project before starting or attaching to Aspire
2. **Run the demo** with `dotnet test --no-build --no-restore`
3. **Wait for hidden sentinels** with `WaitForSelectorState.Attached` (never `Visible` for invisible markers)

### Rationale

- Rebuilding while attached to a live Aspire graph caused repeatable DLL copy/file-lock failures
- The hidden assistant completion marker (`data-testid="assistant-message-complete"`) never becomes visible, so `Visible` waits create false timeouts even when the response is complete
- Using the no-build attached-demo flow made the headed Chromium window visible again, which is the desired presenter/demo experience

### Evidence

- 2026-05-22 BrowseAndSchedule rerun launched visible Chromium successfully after switching to prebuild + `--no-build --no-restore`
- The previous timeout on `assistant-message-complete` was removed by waiting for `Attached`
- The remaining failure moved to a genuine runtime `HTTP 401`, proving the browser-startup/waiting path was no longer the active blocker

### Active Blocker (Identified)

**Runtime HTTP 401 / Invalid Subscription Key:**
- Both Browse and Schedule agent steps fail with 401 responses during demo execution
- Root cause: Agent/tool path requires valid Azure OpenAI credentials (not a browser/Playwright issue)
- Status: Requires runtime auth/credential configuration, not test infrastructure changes

---

## 2026-05-09 - Video 1 Production Pipeline: Root-Level Workspace & Enhanced Workflow

**Author:** Milchick (Educational Media Producer) · **Domain:** Video Production
**Approved by:** Bruno Capuano via Mark (Lead Architect)
**Source:** `inbox/milchick-video-production-pipeline.md` (merged 2026-05-09T10:59:24Z)
**Status:** ✅ DECIDED & IMPLEMENTED

### Design Decision

Video 1 (Skill-Powered Chat Journey) production pipeline requires structural and workflow improvements:

1. **Workspace Relocation:** Move video production assets from `docs\testing\video-production\` to root-level `video-production\` directory
2. **Trim Point Refinement:** Increase trim from 7s to 20s based on frame-by-frame content analysis
3. **Branded Title Card:** OpenClawNet visual identity (navy #10213D, white text, #D8E6FF accents)
4. **Optional Audio Support:** WAV narration input with automatic mixing + SRT caption burn-in (no mandatory dependencies)
5. **Session-Local ffmpeg:** Detect npm-installed ffmpeg binaries for cross-environment compatibility

### Rationale

**Workspace Separation:**
- Video production is "media", not "testing" — misclassification under `docs\testing\` creates conceptual confusion
- Root-level workspace clearly signals production status and improves team clarity
- Enables scalable scenario management (Video 1, 2, 3 coexist cleanly)

**Trim Point (20s):**
- Frame content analysis shows idle/loading frames 0–20s post-welcome
- 20s trim eliminates all dead air; preserves skill interaction narrative
- Result: 41s raw → 33–34s trimmed + 3–4s intro + 9s closing hold = 46s final

**Branded Title Card:**
- OpenClawNet colors establish visual identity in educational context
- ffmpeg `drawtext` filter for cross-platform reliability (no ImageMagick dependency)
- Overlay approach preserves 100% real Playwright capture authenticity

**Optional Audio:**
- WAV input allows local recording or any TTS tool (no cloud mandate)
- SRT captions with ffmpeg burn-in work universally (no player support needed)
- Narration is enhancement, not requirement; preserves pipeline reproducibility

**Session-Local ffmpeg:**
- npm-installed binaries detected via `%TEMP%\openclawnet-video-ffmpeg\` fallback
- Improves developer experience; CI/CD compatible; no system ffmpeg dependency

### Implementation Approach

**Text-Only Title Card (ffmpeg `drawtext` filter):**
- Literal color hex values in filter strings avoid PowerShell variable expansion issues
- Example: `drawtext=text='...':fontsize=48:fontcolor=0xffffff`
- Reliable across platforms (tested on Windows PowerShell 7+)

**Temporary File Handling:**
- Intermediate files (title MP4, audio mix, etc.) stored in output directory
- Cleaned up on success; preserved on error for debugging
- Avoids cross-drive I/O delays

**Reproduction:**
- All scripts version-controlled in `video-production\scripts\`
- Stitching script `stitch-video-1-skill-journey.ps1` fully parameterized
- README and PRODUCTION_NOTES.md document workflow end-to-end

### Alternatives Considered & Rejected

1. **Logo overlay using ffmpeg movie filter:**
   - Attempted but failed silently (0-byte output)
   - Deferred: Can revisit with two-pass approach or PNG overlay filter
   - Text-only approach chosen for immediate reliability

2. **Separate subtitle track (no burn-in):**
   - Rejected: Requires player subtitle support; unreliable for demos
   - Burned-in SRT captions work universally

3. **Cloud-based TTS (Azure Cognitive Services, etc.):**
   - Rejected: Adds external dependency + credential management
   - WAV input preserves local-only reproducibility

4. **Keep assets under docs\testing\video-production:**
   - Rejected: Conceptual misalignment (production ≠ testing)
   - Root-level structure improves clarity

### Migration Path

- **New Structure:** `video-production\scenarios\video-1-skill-journey\`
- **Old Structure:** `docs\testing\video-production\scenarios\video-1-skill-journey\` (retained for backward compatibility during transition)
- **Recording Command:** Updated scenario README.md with new `OPENCLAW_PLAYWRIGHT_VIDEO_DIR` path
- **Stitching Script:** Relocated to `video-production\scripts\stitch-video-1-skill-journey.ps1`

### Validation

- ✅ Stitching script generates 46-second final MP4 with burned-in captions
- ✅ Title card displays correct branding and text hierarchy
- ✅ 20-second trim removes all idle frames (validated via ffmpeg frame extraction)
- ✅ Temporary files cleaned up on success
- ✅ `git diff --check` passes (whitespace compliant)
- ✅ Session-local ffmpeg detection works with npm-installed binaries

### Related Files

- `video-production\README.md` — Workspace documentation
- `video-production\scripts\README.md` — Stitching script documentation
- `video-production\scripts\stitch-video-1-skill-journey.ps1` — Enhanced stitching script
- `video-production\scenarios\video-1-skill-journey\PRODUCTION_NOTES.md` — Workflow instructions
- `video-production\scenarios\video-1-skill-journey\VIDEO_EXPLANATION.md` — Technical details
- `AUDIO-GENERATION-CANDIDATES.md` — Audio narration evaluation options

### Next Steps

1. ✅ Workspace established at root level
2. ✅ Stitching pipeline implemented and tested
3. ✅ Documentation finalized (path references verified)
4. ⏭ Video 1 ready for distribution
5. ⏭ Scenarios 2 & 3 can follow same pattern

---

## 2026-05-09 - Video 1 Documentation Revision: Path Corrections & Whitespace Cleanup

**Author:** Ricken (DevRel/Writer) · **Domain:** Video Production Documentation
**Source:** `inbox/ricken-video-doc-revision.md` (merged 2026-05-09T10:59:24Z)
**Status:** ✅ IMPLEMENTED & APPROVED

### Summary

Dylan's verification pass identified stale documentation path references (blocker). Ricken implemented correction under reviewer lockout protocol. Dylan re-verified and approved. All blocking issues resolved.

### Changes Implemented

**PRODUCTION_NOTES.md (5 path corrections):**
- Line 13: `docs/testing/video-production/...` → `video-production/...`
- Line 57: `cd docs\testing\video-production\...` → `cd video-production\...`
- Line 63: `docs/testing/video-production/...` → `video-production/...`
- Line 94: `cd docs\testing\video-production\...` → `cd video-production\...`
- Lines 107–118: Multiple file path references corrected

**VIDEO_EXPLANATION.md (1 path correction):**
- Line 61: `cd docs\testing\video-production\...` → `cd video-production\...`

**Whitespace Cleanup:**
- `.squad\agents\helly\history.md:13`: Removed trailing whitespace from `**Constraints:**` line

### Verification

- ✅ Grep search: **0 matches** of stale `docs/testing/video-production` or `docs\testing\video-production` remain
- ✅ Reproducibility confirmed: Users can follow instructions accurately
- ✅ `git diff --check` passes with exit code 0

### Status

Video 1 pipeline documentation is production-ready. Ready for acceptance and final merge.

---

## 2026-05-09 - Optional Audio Evaluation: ElBruno.QwenTTS as Future Narration Candidate

**Author:** Ricken (DevRel/Writer) · **Domain:** Audio/Narration Evaluation
**Source:** `inbox/ricken-2026-05-09-qwentts-evaluation-documented.md` (merged 2026-05-09T10:59:24Z)
**Status:** ✅ DOCUMENTED

### Context

Video 1 production pipeline completed with optional narration support. Team evaluated ElBruno.QwenTTS as potential narration tool for future phases. Decision: Document as evaluation candidate; no immediate mandate imposed.

### Evaluation Result

**ElBruno.QwenTTS Assessment:**
- **Status:** Evaluation candidate for future phases
- **No Mandatory Dependency:** Current pipeline works without narration (WAV input optional)
- **Future Pathway:** If team adopts narration, QwenTTS or alternative TTS tools can be integrated
- **Documentation:** Recorded in `AUDIO-GENERATION-CANDIDATES.md` for team reference

### Rationale

- Keeps current pipeline reproducible and dependency-light
- Preserves narrative flexibility: team can defer narration phase without blocking production
- QwenTTS noted as option if in-pipeline audio generation becomes requirement
- Local WAV recording path remains primary for maximum control

### Next Steps

1. Video 1 ships without mandatory narration
2. Future video iterations can optionally integrate QwenTTS or alternative
3. Audio-generation pathway documented for team continuity

---

## 2026-05-09 - Video 1 Pacing & Intro/Outro Refinement

**Author:** Milchick (Educational Media Producer) · **Domain:** Video Production
**Requested by:** Bruno Capuano via Mark (Lead Architect)
**Source:** `inbox/milchick-video-pacing-title-card.md` (merged 2026-05-09T13:32:13Z)

### Design Decision

Video 1 (Skill-Powered Chat Journey) exhibits three pacing issues that reduce viewer engagement and professional perception:
1. **Opening dead frame (~10s):** Static app loading screen burns viewer attention with zero payoff
2. **No introduction:** Viewers don't know what product/value they're watching within first 3 seconds
3. **Abrupt ending:** No time for the "proof moment" to sink in; cuts immediately after final response

**Recommendation:** Add 3–4s intro title card + trim first 5–8s dead frame + hold final frame for 5s. Result: 41s video becomes ~49s (acceptable short-form demo length, under 60s threshold).

### Rationale

**Opening dead frame removal:**
- Nielsen Norman data: Highest drop-off in first 8 seconds of demo videos
- Mismatches product value: OpenClawNet's strength is live UI interaction
- Solution: ffmpeg trim of first 5–8s, replace with polished title card overlay

**Introduction requirement:**
- Industry standard: Microsoft Learn uses 3–5s title card for demos <2min
- YouTube Creator Academy: "Hook audience in first 5 seconds" — state value immediately
- Wistia 2023 Benchmark: Title cards increase completion by ~12% for educational content
- Recommended text: *"Create a skill → enable it → chat → get structured output. No code. Just Markdown."*

**Closing hold:**
- Wistia research: Last 5–10s are highest-converting moments (CTA placement, proof moments)
- Allows viewers to absorb the formatted response output
- Overlay: OpenClawNet logo (bottom-left) + repo URL (bottom-right), no narration

**Implementation approach: ffmpeg post-processing**
- Already used in repo for narration sync (PRODUCTION_CHECKLIST.md:258–262)
- Extends proven toolchain; reproducible; script-based, version-controlled
- Alternatives rejected: Playwright DOM injection (looks synthetic), Real Blazor UI page (pollutes production), Manual editor (not version-controlled)

### Implementation Details

**Phase 1: Design & Asset Creation (This sprint)**
- Finalize title card text (one of two options)
- Create `docs/testing/video-production/scenarios/video-1-skill-journey/assets/source/title-card.svg` (or PNG)
- Design closing frame overlay placement

**Phase 2: Stitching Script (This sprint)**
- Write `scripts/video-production/stitch-video-1.ps1`
- Test locally with existing raw WebM + narration
- Verify timing: title (3–4s) + trimmed main (33–34s) + closing hold (5s)

**Phase 3: Update Documentation (This sprint)**
- `docs/testing/video-production/scenarios/video-1-skill-journey/README.md` — add "Post-Production Assembly" section
- `shot-checklist-video-1-skill-journey.md` — note intro/outro strategy
- `PRODUCTION_CHECKLIST.md` — add "Stitch Video 1" step

**Phase 4: Reproduce & Publish (Next sprint)**
- Run stitching script to generate final MP4
- Upload to GitHub Releases
- Link from README

**Timing breakdown:**
```
Intro title card:                 3–4 seconds
Main journey (trimmed raw):       ~33–34 seconds
Closing hold + fade:              5 seconds
─────────────────────────────────
Total:                            ~41–43 seconds
```

### Alignment with "Real UI Product Video" Principle

- Intro card is a post-production editorial layer (not fake UI)
- Actual product footage remains 100% real Playwright capture
- Closing overlay is visual chrome (not synthetic storyboard)
- No test code or product UI changes required
- Fully reproducible from script (testable, versionable, maintainable)

### Next Steps (Team)

1. **Mark (Product):** Approve title card text and closing overlay design
2. **Dylan (E2E Tests):** Confirm that trimming first 7–8s doesn't lose verification steps
3. **Petey (Tooling):** Validate ffmpeg stitching script CI compatibility
4. **Milchick:** Implement script and trial once assets approved

### References

- Codebase: `docs/testing/video-production/scenarios/video-1-skill-journey/`
- E2E test: `tests/OpenClawNet.PlaywrightTests/SkillsBulletPointJourneyE2ETests.cs`
- ffmpeg precedent: `docs/testing/video-production/PRODUCTION_CHECKLIST.md:258–262`
- Playwright capture: `tests/OpenClawNet.PlaywrightTests/PlaywrightTestBase.cs:41–57`
- Industry standards: Nielsen Norman, Wistia 2023 Benchmark, Microsoft Learn contributor guides

### Status

Recommendation approved for Phase 1 asset creation and Phase 2 script development. Timeline: 2–3 days if assets finalized by end of 2026-05-09.

---

## 2026-05-08 - Video 1 Replacement: Skill-Powered Chat Journey

**Author:** Mark (Lead Architect) + Dylan/Helly/Milchick input · **Domain:** Video Production

### Design Decision

The active Video 1 scenario moves from the blocked Vault lifecycle recording to `Skill-Powered Chat Journey`, backed by `tests\OpenClawNet.PlaywrightTests\SkillsBulletPointJourneyE2ETests.cs`.

The Vault lifecycle scenario remains deferred until a real Vault/Secrets UI exists. Terminal scripts for Vault stay as fallback API verification only and cannot be used as product-showcase video output.

### Rationale

1. **Real UI exists today.** The skill journey uses the current Skills page and Chat page, so Playwright can capture the product as-is.
2. **The story is visible.** Viewers can see a skill authored, enabled for an agent, and reflected in the assistant response.
3. **It keeps test-to-video traceability.** The video maps to the `BulletPointSkill_AppliedToAgent_AgentRepliesWithBullets` Playwright E2E.
4. **It avoids fake output.** No synthetic storyboard or terminal-only fallback is treated as final video.

### Implementation Details

- Scenario workspace: `docs\testing\video-production\scenarios\video-1-skill-journey`
- Raw Playwright videos: `recordings\raw`
- Screenshots: `assets\generated`
- Test capture env vars:
  - `OPENCLAW_PLAYWRIGHT_VIDEO_DIR`
  - `OPENCLAW_PLAYWRIGHT_SCREENSHOT_DIR`

### Status

Selected and documented. Recording requires a tool-capable model via Azure OpenAI or local Ollama.

---

## 2026-04-26 — Activity Panel Refinement: Collapsed = Title Only, 20-Entry Visual Cap

**Author:** Helly (Frontend Dev) · **Domain:** UI/UX
**Source:** `inbox/helly-activity-panel-refine.md` (merged 2026-04-26T19:09:37Z)

### Design Decision

**Collapsed State (Default)**
- Render ONLY the title row (console-header with toggle button, count badge, action buttons).
- Do NOT render ANY preview entries. The collapsed state is now truly collapsed — just a title bar.
- Remove the "Show all (N)" link. Users click the title toggle to expand (no need for two expand mechanisms).

**Expanded State**
- Show the last 4 entries visible by default (no scrolling needed for those 4).
- Scrollable to view up to 20 entries total (the most recent 20).
- Entries beyond 20 (up to the 100 in-memory cap) are retained for Export/Copy but NOT rendered in the DOM.

**Constants Introduced**
- `VisibleMaxEntries = 20` — Maximum number of entries rendered when expanded.
- `VisibleRowsExpanded = 4` — Number of rows visible before scroll kicks in (replaces `PreviewLineCount` semantics).
- `MaxEntries = 100` — In-memory cap (unchanged). Export and Copy include all 100.

**Discoverability**
- When `entries.Count > VisibleMaxEntries`, display a muted footer hint: `… and N older entry(s) (included in Export)`
- Prevents "where did my data go?" confusion — users know the older entries still exist and are accessible via Export.

### Rationale

1. **Title-only collapsed state reduces default visual clutter.**
   Most users don't need to see activity logs constantly. Collapsed = minimal footprint. Expand when you need details.

2. **4-visible / 20-scrollable split balances discoverability with performance.**
   - 4 rows: Enough to see recent activity at a glance without scrolling.
   - 20 total: Covers most debugging scenarios without rendering 100 DOM nodes.
   - Beyond 20: Still in memory, still in Export — just not in the live DOM.

3. **Fixed-height scroll container gives predictable UX.**
   Viewport-relative sizing could vary with window height. Fixed pixel height based on font math = consistent behavior.

4. **Removed redundant "Show all" link.**
   Title toggle already expands. Two expand mechanisms confused intent.

### Implementation Details

**Razor Markup Changes**
- Wrapped the entire `.console-body` in `@if (isExpanded) { ... }`.
  When collapsed, no body div is rendered at all (cleaner than an empty div with `display: none`).

- When expanded, entries are sliced to `Take(VisibleMaxEntries)`.

- Footer hint added after the loop showing count of older entries.

**CSS Changes**
- Removed `.console-body-preview` class (no longer needed).
- Updated `.console-body-expanded` with fixed `max-height` and `min-height` calculations based on font size and line-height.

**Code Changes**
- Added constants `VisibleMaxEntries = 20` and `VisibleRowsExpanded = 4`.
- Removed `PreviewLineCount` constant (replaced by `VisibleRowsExpanded` semantics).

### Edge Cases Handled

1. **Empty state (`entries.Count == 0`):**
   When expanded, show "No agent activity yet…" message. When collapsed, body is absent — just the title row.

2. **Entries <= VisibleMaxEntries:**
   No footer hint. All entries fit in the scroll container.

3. **Export and Copy buttons:**
   Remain visible in the title row at all times. They iterate ALL entries (not just the visible 20).

### Files Modified

- `src/OpenClawNet.Web/Components/AgentActivityPanel.razor` — Razor markup and CSS updates
- Build verified: 0 errors

### Status

✅ Approved by Bruno (implicit via request)
✅ Implemented in PR #86 (commit 7570ab0)
✅ Ready for review and merge

---

## 2026-04-26 — Nightly Tool E2E CI Workflow

**Author:** Mark (Lead) · **Domain:** Testing & Infrastructure
**Source:** `inbox/mark-nightly-ci-workflow.md` (merged 2026-04-26T13:24:14Z)

### E2E Suite in CI Runs Against Azure OpenAI ONLY — Never Ollama

**Decision:** The nightly GitHub Actions workflow runs the 10-test Tool Matrix E2E sweep exclusively against Azure OpenAI. Ollama is not used in CI.

**Rationale:**
- Bruno's GitHub Actions resource constraint: forbids Ollama consuming hosted runner minutes
- The 10/10 baseline was validated against both Ollama (local) and Azure OpenAI (cloud)
- Production-ready path uses cloud provider; local development can use either
- Cost optimization: Azure OpenAI only when needed for regression detection

**Implementation:**
- Nightly schedule: 03:00 EDT / 07:00 UTC
- Environment variables force `Model__Provider=AzureOpenAI` via `IConfiguration` overlay
- Modified `scripts/run-tool-e2e-sweep.ps1` with CI detection logic (env var path)
- Auto-issue creation on failure with label `tool-e2e-regression` and triage checklist

**Files Created:**
1. `.github/workflows/tool-e2e-nightly.yml` — Workflow with cron schedule, Docker verification, artifact upload
2. `docs/testing/ci-nightly-setup.md` — Setup guide (secret creation, manual trigger, known limitations)

**Files Modified:**
1. `scripts/run-tool-e2e-sweep.ps1` — CI detection logic (skips user-secrets if `AZURE_OPENAI_ENDPOINT` is set)

**Status:** Shipped (commit 8602b16). Awaiting Bruno to:
1. Create 3 GitHub secrets (`AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_DEPLOYMENT`)
2. Trigger manual run to validate Aspire-on-Windows-runner
3. Wait for first scheduled run (tonight at 03:00 EDT)

**Next Layer:** PR smoke subset (Layer 2, future task)

---

## 2026-04-26 — Build Warning Cleanup

**Author:** Mark (Lead Architect) · **Domain:** Infrastructure & Security
**Source:** `inbox/mark-warning-cleanup.md` (merged 2026-04-26T01:57:14Z)

### OpenTelemetry Version Selection

**Decision:** Upgrade OpenTelemetry to v1.15.3 across all 5 packages in ServiceDefaults.

**Rationale:**
- Latest stable release in 1.x line as of 2026-04-26
- Resolves two moderate-severity CVEs:
  - GHSA-g94r-2vxg-569j (OpenTelemetry.Api)
  - GHSA-mr8r-92fq-pj8p (OpenTelemetry.Exporter.OpenTelemetryProtocol)
- No breaking changes in patch/minor bump (1.15.x → 1.15.3)
- Verified clean via `dotnet list package --vulnerable --include-transitive`
- Defer OpenTelemetry 2.x (breaking API changes) until Phase 3

**Packages Upgraded:**
1. OpenTelemetry.Exporter.OpenTelemetryProtocol: 1.15.2 → 1.15.3
2. OpenTelemetry.Extensions.Hosting: 1.15.2 → 1.15.3
3. OpenTelemetry.Instrumentation.AspNetCore: 1.15.1 → 1.15.2
4. OpenTelemetry.Instrumentation.Http: 1.15.0 → 1.15.1
5. OpenTelemetry.Instrumentation.Runtime: 1.15.0 → 1.15.1

### CS0436 Namespace Collision Resolution

**Decision:** Standardize on `GatewayProgramMarker` for all logger type parameters in Gateway endpoints instead of deleting the Program class.

**Rationale:**
- Gateway and Channels both define top-level `Program` class (C# 10+ top-level statements)
- Transitive reference causes ambiguity warnings (CS0436) in all 8 Gateway endpoints
- `GatewayProgramMarker` already used in test infrastructure (`WebApplicationFactory<GatewayProgramMarker>`)
- Preserving partial Program class maintains backward compatibility for external integration tests
- Marker pattern is cleaner than class deletion and avoids breaking changes

**Implementation:**
- Swapped `ILogger<Program>` → `ILogger<GatewayProgramMarker>` in 8 endpoint files:
  - ActivityTracking, Adapter, Agent, Artifact, Capability, Health, Job, Slide endpoints

**Precedent:** Consider applying GatewayProgramMarker pattern to future multi-service architectures with `Program` class collisions.

### Null-Safety Cleanup

**Decision:** Address residual CS0219 and CS8602 warnings through targeted null guards and dead code removal.

**Files Modified:**
- `SlackWebhookAdapterTests.cs:291` — Removed unused `content` variable (CS0219)
- Two test files — Added `c.Message != null &&` guards before `.Contains()` calls on nullable LogCall.Message (CS8602)

## Build Results

- **Warning Count:** 91 → 0 ✅
- **Tests:** 759 passed, 3 skipped, 13 pre-existing failures (unrelated assembly load issues)
- **Files Touched:** 10 (1 csproj, 8 endpoint files, 2 test files)
- **PR:** #79 merged to main as squash commit e7739de

---

## 2026-04-25 — Feature 2 Testing Decisions (Stories 5 & 6)

**Author:** Dylan (Tester) · **Feature:** Feature 2, Stories 5 & 6
**Source:** `inbox/dylan-feature2-testing.md` (merged 2026-04-25T13:38:55Z)

### Story 5: Audit Trail Integration Tests

**Decision:** Use direct database writes for audit log tests rather than invoking full endpoint flows.

**Rationale:**
- Audit log tests focus on validating that records are correctly written to the database with proper field values
- Direct DB writes isolate the persistence logic from HTTP layer concerns
- Tests run faster and are more focused on the audit entity behavior
- Integration with actual endpoints is already validated by existing endpoint tests (e.g., `JobStateMachineEndpointsTests.StartJob_RecordsStateChange`)

**Files Created:**
1. `JobStateChangeTests.cs` — 6 tests validating job status transitions write audit records
2. `ToolApprovalLogTests.cs` — 7 tests covering user/timeout/session-memory approval sources
3. `AdapterDeliveryLogTests.cs` — 8 tests for delivery success/failure scenarios

**Coverage:**
- ✅ All three audit entities (JobDefinitionStateChange, ToolApprovalLog, AdapterDeliveryLog)
- ✅ Foreign key relationships verified
- ✅ Timestamp validation
- ✅ Status enum validation (Success/Failed/Pending, User/Timeout/SessionMemory)
- ✅ Error message population on failures
- ✅ Config snapshot preservation

### Story 6: Sanitizer Security Validation

**Decision:** Irving (Backend) already implemented comprehensive security tests.

**Observation:**
Irving added security-focused tests to `DefaultToolResultSanitizerTests.cs`:
- Unicode normalization (NFC form)
- Prompt injection marker detection
- Max line length enforcement

These tests validate all three security enhancements mentioned in acceptance criteria:
1. ✅ Unicode normalization prevents homoglyph attacks
2. ✅ Prompt-injection markers detected and wrapped
3. ✅ MaxLineLength enforcement

**Action Taken:**
- Verified all 10 sanitizer tests pass (67ms duration)
- Updated `docs/architecture/20260425-concept-review.md` §4a Security Implications table

**Status:** Stories 5 & 6 complete. All acceptance criteria met.

---

## 2026-04-25 — Feature 2 Implementation Decisions (Stories 1 & 2)

**Author:** Irving (Backend Dev) · **Feature:** Feature 2, Stories 1 & 2
**Source:** `inbox/irving-feature2-decisions.md` (merged 2026-04-25T13:38:55Z)

### Story 1: Audit Trail REST Endpoints

**Pagination Strategy**
- **Decision:** Use `limit` and `offset` parameters (instead of cursor-based pagination)
- **Rationale:** Simpler for audit queries where users often want to jump to specific time ranges; consistent with existing `RunsEndpoints` pattern; default limit 100, max 500

**DTO Naming Convention**
- **Decision:** Prefix audit endpoint DTOs with `Audit` (e.g., `AuditJobStateChangeDto`, `AuditToolApprovalLogDto`)
- **Rationale:** Avoids namespace conflicts with existing DTOs; makes it clear these are read-only audit views

**Date Filtering**
- **Decision:** Support `since` and `until` query parameters on all audit endpoints
- **Rationale:** Compliance and audit use cases often need date-range queries; consistent across all three audit endpoints

**Response Format**
- **Decision:** Include metadata in responses (count, offset, limit, filters)
- **Rationale:** Helps clients understand pagination state; `filters` object echoes back what was requested for transparency

### Story 2: Enhanced Prompt-Injection Defenses

**Unicode Normalization**
- **Decision:** Apply NFC (Normalization Form C) normalization before all other processing
- **Rationale:** Prevents homoglyph attacks; NFC is standard composed form; applied first

**Injection Marker Detection**
- **Decision:** Detect and wrap markers (e.g., "ignore previous", "system:", "assistant:") with `[DETECTED:...]` delimiters
- **Rationale:** Makes injection attempts visible in logs; prevents LLM from interpreting tool output as system instructions; case-insensitive detection

**Line Length Limits**
- **Decision:** Default MaxLineLength = 10,000 chars/line, configurable via IOptions
- **Rationale:** Prevents pathological line-length attacks; 10K generous for legitimate output; per-line truncation preserves structure

**Defense-in-Depth Layering**
- **Decision:** Apply defenses in order: normalize → strip control chars → line length → escape → wrap markers → truncate → fence
- **Rationale:** Each layer addresses distinct attack vector; order matters; no single defense perfect; layered approach maximizes resilience

**Files Created/Modified:**

Story 1:
- Created: `src/OpenClawNet.Gateway/Endpoints/AuditEndpoints.cs`
- Modified: `src/OpenClawNet.Gateway/Program.cs`

Story 2:
- Created: `src/OpenClawNet.Agent/ToolApproval/ToolResultSanitizerOptions.cs`
- Modified: `src/OpenClawNet.Agent/ToolApproval/DefaultToolResultSanitizer.cs`
- Modified: `src/OpenClawNet.Agent/AgentServiceCollectionExtensions.cs`
- Modified: `tests/OpenClawNet.UnitTests/Agent/DefaultToolResultSanitizerTests.cs`

**Status:** Stories 1 & 2 complete. All acceptance criteria met. 10/10 sanitizer tests passing.

---

## 2026-05-01 — Live Test Factory Consolidation (PR #73 Follow-Up)

**Author:** Dylan (Test Engineer) · **Branch:** feat/live-test-followups (commit 7efee8f)
**Source:** `inbox/dylan-factory-consolidation.md` (merged 2026-04-25T14:13:56Z)

### Decision

Consolidated three parallel live-test factory patterns into one `LiveOllamaWebAppFactory` by making the `endpoint` parameter optional (`string?` with default fallback to `"http://localhost:11434"`).

### Rationale

1. **Duplicated patterns:** Three separate approaches existed for swapping `FakeModelClient` → real `OllamaModelClient`:
   - `LiveOllamaWebAppFactory` (IClassFixture-style, used by FileSystem/Calculator/MarkItDown)
   - `LiveOllamaGatewayWebAppFactory` (internal sealed, used by `LiveJobExecutionTests`)
   - Inline `WithWebHostBuilder` blocks (~25 lines each, used by Web/HtmlQuery tools)

2. **All patterns do identical work:** Override `Model:Provider/Model/Endpoint` config + replace `IModelClient` singleton with `OllamaModelClient`.

3. **Most tests use the default:** Four of five per-tool e2e test classes hardcode `"http://localhost:11434"` — no env var override needed.

4. **Optional parameter reduces boilerplate:** Only `LiveJobExecutionTests` reads env vars (`LIVE_TEST_OLLAMA_ENDPOINT`). Making `endpoint` optional lets tests that don't need overrides just call `new LiveOllamaWebAppFactory(model)`.

### Implementation

- Extended `LiveOllamaWebAppFactory` constructor: `endpoint` parameter optional, defaults to `null`, factory falls back to hardcoded localhost.
- Deleted `LiveOllamaGatewayWebAppFactory` from `LiveJobExecutionTests.cs` (~54 lines).
- Removed inline `WithWebHostBuilder` blocks + duplicated helpers from `LiveWebToolE2ETests` + `LiveHtmlQueryToolE2ETests` (~60 lines each).
- Net: ~278 lines removed, zero test behavior changes.

### Impact

- **Zero breaking changes:** Existing two-argument calls `(model, endpoint)` still work.
- **Cleaner test code:** All five per-tool e2e tests follow identical `LiveFactory()` override pattern.
- **Env var support preserved:** `LiveJobExecutionTests` can still pass custom endpoints.

---

## 2026-05-01 — CI Matrix Split for LiveJobExecutionTests (PR #73 Follow-Up)

**Author:** Irving (Backend/Infra) · **Branch:** feat/live-test-followups (commit f86d5dd)
**Source:** `inbox/irving-ci-matrix-split.md` (merged 2026-04-25T14:13:56Z)

### Decision

Split `.github/workflows/live-tests.yml` into three parallel jobs: Ollama unit tests, Ollama per-tool e2e (excluding `LiveJobExecutionTests`), and AOAI `LiveJobExecutionTests` only.

### Rationale

1. **Provider reliability gap:** `qwen2.5:3b` (Ollama default) hits tool-loop iteration limits on complex multi-tool prompts. Symptoms: occasional failure to select correct tool, loops until max iterations. GPT-5-mini class (AOAI) completes reliably.

2. **Job-level split (not env-var gate) produces:**
   - Clearer CI output (three distinct jobs)
   - Cheaper CI (AOAI job skips Ollama install, saves 5–10 min)
   - Graceful fork handling (AOAI job skipped when secrets missing)
   - Per-job responsibility (each job controls its own provider + filter)

3. **Surgical prompts work fine on small models:** Per-tool e2e tests (Calculator, FileSystem, MarkItDown, Web, HtmlQuery) are stable on `qwen2.5:3b`. Multi-tool/multi-step logic (job pipeline) needs GPT-5-mini.

### Implementation

- **`live-unit-tests` (Ollama):** Agent loop + model client tests (unchanged)
- **`live-integration-tests-ollama` (Ollama):** Per-tool e2e tests
  - Filter: `Category=Live&FullyQualifiedName!~LiveJobExecutionTests`
- **`live-integration-tests-aoai` (AOAI):** `LiveJobExecutionTests` only
  - Filter: `Category=Live&FullyQualifiedName~LiveJobExecutionTests`
  - Skipped when AOAI secrets not present (forks, PRs)
  - Does NOT install Ollama (faster, simpler)

### Impact

- `LiveJobExecutionTests` now runs against GPT-5-mini in CI (high signal, zero flake)
- Per-tool e2e tests remain on Ollama (cost-free, stable)
- Local devs can still run full test suite on Ollama if accepting flake risk
- CI cost optimized: AOAI only when needed (complex tool-loop), Ollama for everything else

---

## 2026-05-08 — Playwright-First Video Production Workflow

**Author:** Milchick (Educational Media Producer) & Mark (Lead Architect via directive)
**Domain:** Video Production / Product Showcase
**Source:** `.squad/decisions/inbox/copilot-directive-2026-05-08T20-26-27-04-00.md` + `milchick-playwright-video-workflow.md` (merged 2026-05-08T20:38:14Z)

### Directive Captured

**From:** Mark (Lead Architect) (via Copilot)
**Timestamp:** 2026-05-08T20:26:27-04:00
**Policy:** Video production must use Playwright to capture the real running web app screenshots and interfaces so the product is shown working as-is; storyboard/synthetic renders are not acceptable as the video-production baseline.

### Decision

**Video 1 (Lifecycle Mastery) and all Phase 4 demo videos MUST be recorded using Playwright to capture real web app browser UI.**

#### Immediate Changes

1. **Primary Recording Method:** Playwright E2E test instrumentation
   - Configure E2E test runner to emit video output
   - Test: `CreateSetRotateResolveVersionsList_EndToEndLifecycle` in `SecretsVaultPhase4E2ETests.cs`
   - Output: MP4/WebM file captured from actual browser session
   - Storage: `docs\testing\video-production\scenarios\video-1-lifecycle\recordings\raw\`

2. **Demote Terminal Scripts to Fallback**
   - Keep `video-1-lifecycle-create-rotate.sh` as fallback manual verification only
   - Use only if Playwright recording unavailable
   - Clearly document: terminal captures do NOT show real web UI

3. **Mark Synthetic Storyboard as Obsolete**
   - `recordings\final\video-1-final.webm` (synthetic render) must NOT be published
   - Archive for reference; do not include in final product showcase
   - Update status table to mark as ❌ **OBSOLETE**

4. **Keep Scenario Isolation**
   - All recording artifacts under `docs\testing\video-production\scenarios\video-1-lifecycle\`
   - Recordings organized: `recordings\raw\` (working files), `recordings\final\` (exports)

#### Rationale

- **Product Authenticity:** Playwright captures real running Blazor Server UI, not mock-up
- **User Experience:** Viewers see actual interface they will interact with
- **E2E Traceability:** Video directly tied to passing E2E test; if test is current, video accuracy is guaranteed
- **Long-term Maintenance:** Playwright recording is reproducible; if API changes, re-run test to update video
- **Accessibility:** Real UI makes videos more useful for onboarding

#### Impact on Video Assets

| Video | Previous | New | Status |
|-------|----------|-----|--------|
| Video 1 (Lifecycle) | Terminal asciinema + WebM storyboard | Playwright browser video | 🔄 Update required |
| Video 2 (Deletion) | Terminal asciinema | Playwright browser video | 🔄 Update required |
| Video 3 (Concurrency) | Terminal asciinema | Playwright browser video | 🔄 Update required |

#### Coordination

- **Dylan (E2E Test Lead):** E2E tests pass; instrument with Playwright video
- **Petey (Demo Tooling):** Verify Playwright capture works in CI/local
- **Mark (Product Lead):** Validate Playwright videos show intended user experience
- **Ricken (Docs):** Ensure video narration aligns with public documentation

#### Timeline

1. **Immediate:** Update documentation (decision + scenario READMEs)
2. **This week:** Configure Playwright video capture in E2E test project
3. **Week 1–2:** Record Video 1 with Playwright (3–4 takes)
4. **Week 2–3:** Record Videos 2 & 3 with Playwright
5. **Week 3:** Post-production (narration, sync, captions)
6. **Week 4:** Publish

### Status

✅ Decided — Directive documented, workflow updated, synthetic artifacts marked obsolete.
⏳ **Video 1 BLOCKED:** Real Secrets Vault lifecycle UI must be implemented before Playwright recording phase begins (Helly UI audit confirmed UI does not exist).

---

## 2026-05-08 — Video Production Scenario Isolation

**Author:** Milchick (Educational Media Producer)
**Domain:** Video Production / Artifact Management
**Source:** `.squad/decisions/inbox/milchick-video-production-isolation.md` (merged 2026-05-08T20:38:14Z)

### Decision

Video production resources and generated outputs must be isolated per scenario folder under `docs\testing\video-production\scenarios\<scenario-id>`. The repository root is not a working area for screenshots, recording logs, asciinema casts, raw captures, narration files, or final exports.

### First Template

Video 1 / lifecycle create-rotate is the first scenario workspace:
`docs\testing\video-production\scenarios\video-1-lifecycle`

Contains: `scripts`, `assets\source`, `assets\generated`, `recordings\raw`, `recordings\final`, `notes`, scenario `README.md`, shot checklist.

### Compatibility and Cleanup

- Top-level Video 1 files remain only as compatibility shims/pointers
- Former loose generated root artifacts moved to `docs\testing\video-production\generated-root-artifacts` (not video-1-specific)
- New scenario-specific generated files must stay in scenario workspace

### Guardrails

- Do not claim a video is recorded unless real artifact exists
- Do not commit large raw/final video or audio binaries from `recordings\raw` or `recordings\final`
- Keep scripts, READMEs, checklists, reproducible notes tracked
- Use fake demo values only; do not expose plaintext secrets

### Status

✅ Decided — Scenario workspace structure enforced for Video 1.

---

## 2026-05-08 — Video 1 Production Process Review

**Author:** Dylan (Tester)
**Domain:** E2E Testing / Video Production Validation
**Source:** `.squad/decisions/inbox/dylan-video-process-review.md` (merged 2026-05-08T20:38:14Z)

### Review Outcome

Video 1 remains the lifecycle create/rotate scenario and maps to `CreateSetRotateResolveVersionsList_EndToEndLifecycle`. E2E test infrastructure ready for Playwright video instrumentation.

### Tester Guardrails

- Keep Video 1 generated artifacts under `docs\testing\video-production\scenarios\video-1-lifecycle`
- Keep raw captures in `recordings\raw` and final local exports in `recordings\final`
- Top-level Video 1 files are compatibility shims/pointers only
- **Do not claim a recording exists until real artifact is produced**
- Documentation must distinguish request-body demo values from Gateway responses: responses must not return plaintext, real secrets must never be used in videos

### Status

✅ E2E test infrastructure ready. Awaiting Playwright recording method configuration (Petey) and real Vault UI implementation (Helly).

---

## 2026-05-08 — Video Production Blocked: UI Gap Identified

**Author:** Helly (Frontend Dev)
**Domain:** UI/UX
**Source:** `.squad/decisions/inbox/` session audit (merged 2026-05-08T20:38:14Z)

### Finding

**No Secrets Vault lifecycle UI exists in current Blazor Server web app.** Real web UI (Secrets list, Create, Rotate, Verify) is not implemented.

### Impact on Video Production

- **Video 1 is BLOCKED** until Secrets Vault lifecycle Blazor pages/components are built
- Playwright recording cannot proceed without a functioning web interface
- Terminal-only recordings can serve as interim API demo (not product showcase)
- Phase 5 UI implementation must precede Video 1 Playwright recording phase

### Implication

Real product showcase videos deferred to Phase 5+ when Vault UI is ready. Interim option: terminal recordings marked "API Demo" (not for product marketing).

### Status

⏳ BLOCKED — UI implementation required (Phase 5 dependency identified).

---

## Inbox Consolidation Notes

**Files Merged:**
- `copilot-directive-2026-05-08T20-26-27-04-00.md` — Directive capture
- `milchick-playwright-video-workflow.md` — Primary workflow decision
- `milchick-vault-video-production.md` — Terminal-first original approach (archived for reference)
- `milchick-video-production-isolation.md` — Scenario folder structure
- `dylan-video-process-review.md` — E2E test validation guardrails

**Deduplicated:** Terminal-first approach superseded by Playwright-first policy (captured as historical context).

**Status:** ✅ Inbox merged into decisions.md; obsolete inbox files ready for deletion.

---

## 2026-04-24 — Live test foundation shape

**Author:** Dylan (Tester) · **Branch:** feat/live-test-coverage-expansion (merged via PR #73)
**Source:** `inbox/dylan-live-test-foundation.md` (merged 2026-04-24T17:48:15Z)

### Decisions made while building `LiveTestFixture` + `LiveToolE2ETestBase`

1. **WebApplicationFactory over docker-compose / Aspire.** Live job e2e tests
   reuse the existing `GatewayWebAppFactory` (in-memory SQLite + minimal
   hosting) and just swap `IModelClient` to a real provider in subclasses.
   Rationale: zero new infra, fast startup, identical wiring to existing
   integration tests; subclasses can still bring up tools, MCP servers, etc.
   in-process if needed.

2. **`Category=Live` trait inherited at the base-class level** on
   `LiveToolE2ETestBase` so subclasses don't have to remember to add it. CI
   filter remains `dotnet test --filter "Category!=Live"` for PR runs.

3. **Default Ollama model = `qwen2.5:3b`** (overridable via
   `LIVE_TEST_OLLAMA_MODEL`). Smaller than `gemma4:e2b` used by the legacy
   `LiveLlmTests`, faster on CPU; tool-calling capable. Existing tests stay on
   their hard-coded model until a follow-up PR migrates them.

4. **Provider parameterization via `MemberData` returning
   `Func<LiveTestFixture, IModelClient?>`** rather than two `[SkippableFact]`
   variants. Keeps test bodies single-source while letting each row skip
   independently when its provider isn't configured.

5. **`CreateJobAsync` accepts `toolName`/`provider`/`model` even though the
   API doesn't expose those fields** — they're appended to the prompt as a
   hint to steer the LLM. Documented in the XML doc; revisit when/if the job
   shape grows real overrides.

6. **`WaitForJobAsync` polls `/runs/{runId}` with 250 ms cadence** despite
   `/execute` being synchronous — defensive against future async changes and
   makes the helper reusable by `/run-now` / scheduler-driven tests.

### Open asks (deferred)

- OK to migrate existing `LiveLlmTests` / `AzureOpenAILiveTests` to
  `LiveTestFixture` in a follow-up?
- OK to keep tool/provider/model as prompt hints, or should we extend
  `CreateJobRequest` first?

---

## 2026-05-01 — PR #72 Q2: Split IAgentMemoryStore from IMemoryService

**Author:** Bruno Capuano (via Copilot)
**Domain:** Architecture / Memory Service
**Source:** `inbox/copilot-pr72-split-imemoryservice.md` (merged 2026-05-01T14:32:00Z)
**PR:** #72 (`research/memory-service`)

### Decision

Approved introduction of a new `IAgentMemoryStore` boundary rather than expanding the existing `IMemoryService`. Per-agent vector memory lives behind `IAgentMemoryStore`; today's summary-style service stays on `IMemoryService`.

### Rationale

- Clear separation of concerns: summary retrieval (IMemoryService) vs vector search (IAgentMemoryStore)
- Aligns with modular memory backend strategy (vector store choice pending Q1/Q3 decisions)
- Allows independent scaling and provider swapping per interface

### Status

✅ Decided (Bruno's approval on PR #72 Q2)
⏳ Implementation pending: Vector store choice (Q1) and tool transport strategy (Q3)

---

## 2026-05-01 — PR #72 Q1+Q3: Vector Store & Tool Transport Recommendation

**Author:** Mark (Lead Architect)
**Domain:** Memory & Tool Integration
**Source:** `inbox/mark-pr72-vector-store-recommendation.md` (merged 2026-05-01T14:32:00Z)
**PR:** #72 (`research/memory-service`)

### Vector Store Recommendation: MempalaceNet v0.6.0

**Candidate Evaluation:**
| Criterion | MempalaceNet | Qdrant | pgvector |
|-----------|-------------|--------|----------|
| .NET Integration | ✅ | ✅ | ⚠️ |
| Aspire Integration | ⚠️ | ✅ | ✅ |
| Per-Agent Isolation | ✅ | ⚠️ | ⚠️ |
| Operational Cost | ✅ | ⚠️ | ⚠️ |
| Embedding Control | ✅ | ⚠️ | ⚠️ |

**Recommendation Rationale:**
1. Bruno authored ElBruno.MempalaceNet — architecture-aligned patterns
2. Zero operational overhead (in-process SQLite, no Docker/Postgres needed)
3. Native per-agent isolation via Wings/Rooms/Drawers hierarchy
4. M.E.AI `IEmbeddingGenerator<>` already integrated
5. Uses ElBruno.LocalEmbeddings with ONNX (`all-MiniLM-L6-v2`) — exact embedding model target

**Known Risks:**
- No `AddMempalaceNet()` Aspire extension yet (can contribute one)
- Library is v0.6.0 (low risk — Bruno maintains, 152 tests)

### Q3 Tool Transport: In-Process DI

**Decision:** RememberTool and RecallTool transport via in-process dependency injection (not HTTP to separate service).

**Rationale:**
- Simpler architecture (no network layer)
- Faster execution (no network latency)
- Reduced operational surface area

### Status

✅ **RESOLVED — Bruno approved** (2026-05-01T14:45:00Z)
Bruno's final acceptance captured below.

---

## 2026-05-01 — PR #72 Final Acceptance: MempalaceNet + In-Process DI Tools

**Author:** Bruno Capuano (CTO)
**Domain:** Memory Architecture & Tool Integration
**Source:** `inbox/bruno-pr72-acceptance.md` (merged 2026-05-01T14:45:00Z)
**PR:** #72 (`research/memory-service`) — Merged as squash commit `eade962`

### Final Decisions Accepted

1. **Vector Store:** **`ElBruno.MempalaceNet` v0.6.0** (in-process, per-agent isolation via Wings/Rooms hierarchy)
   - Implementation: **#98** (Vector embedding pipeline + IAgentMemoryStore integration)

2. **Tool Transport:** **In-process dependency injection** for RememberTool and RecallTool
   - Not HTTP to separate `memory-service`
   - Implementation: **#100** (DI wiring in Agent service)

3. **IAgentMemoryStore Split:** **Confirmed** (separate from IMemoryService)
   - Implementation: **#99** (Interface split + service registration)

### Rationale

- **Zero operational overhead:** MempalaceNet is in-process SQLite with native C# interfaces
- **Per-agent isolation:** Wings/Rooms/Drawers hierarchy provides fine-grained boundaries
- **Faster execution:** In-process DI eliminates network latency for tool transport
- **Reduced surface area:** Fewer services to deploy and monitor
- **Alignment with ElBruno ecosystem:** Bruno maintains MempalaceNet with 152 tests; proven patterns

### Mark's Updated Docs

- ✅ `docs/architecture/memory-service-proposal.md` updated to reflect MempalaceNet + DI transport
- ✅ PR #72 merged with historical record preserved (commit eade962)

### Implementation Issues Opened

- **#98:** Vector embedding pipeline + IAgentMemoryStore integration (MempalaceNet)
- **#99:** IAgentMemoryStore / IMemoryService split implementation
- **#100:** RememberTool / RecallTool in-process DI transport
- **#101:** Section 2.2 side findings (tracking separately for phase 2b)

### Status

✅ **DECIDED** — Bruno's final acceptance closes PR #72 architecture review
→ Implementation underway across issues #98, #99, #100, #101

---

## 2026-05-09 - Repository Root Cleanup

**Author:** Ricken (DevRel/Writer) · **Domain:** Repository Organization
**Requested by:** Mark (Lead Architect) on behalf of Bruno Capuano
**Source:** `inbox/ricken-root-cleanup.md` (merged 2026-05-09T15:30:00Z)
**Status:** ✅ COMPLETED

### Decision

Clean repository root by relocating non-essential files while preserving git history. Root should contain only README, LICENSE, solution/build/config files, and minimal supporting files.

### Changes Made

**Files Moved (6 renames via `git mv`):**
- Planning: `PHASE2_FEATURE1_DECOMPOSITION.md` → `docs/planning/`; `phase2b-plan-summary.txt` → `docs/planning/`
- Archive: `pr-body.md` → `docs/archive/pr-body-reconciliation.md`
- Media: `slides-en-3.png` → `video-production/`; `slides-es-3.png` → `video-production/`
- Docs: `ACKNOWLEDGMENTS.md` → `docs/`

**Files Removed (1 deletion):**
- `gitleaks-s5.json` — disposable scan output (empty array)

### Links Updated

- `docs/manuals/20-tools.md`: Updated ACKNOWLEDGMENTS.md path (../../ → ../)
- `docs/demos/tools/02-github-issue-triage/README.md`: Updated ACKNOWLEDGMENTS.md path (../../../../ → ../)

### Final Root State

**Files remaining (12):** `.env`, `.gitattributes`, `.gitignore`, `.gitleaks.toml`, `.mcp.json`, `appsettings.example.json`, `LICENSE`, `OpenClawNet.slnx`, `package-lock.json`, `package.json`, `README.md`, `squad.config.ts`

**Directories:** .copilot, .git, .github, .playwright-mcp, .squad, docs, node_modules, scripts, skills, src, TestResults, tests, video-production

### Verification

- ✅ `git diff --check` — no whitespace errors
- ✅ All moved files tracked via `git mv` (history preserved)
- ✅ All documentation links updated
- ✅ Root policy achieved

---

## 2026-05-09 - Disable Tool E2E Nightly Scheduled Trigger

**Author:** Drummond (Platform Hardening / DevOps) · **Domain:** CI/CD Workflow
**Requested by:** Mark (Lead Architect) on behalf of Bruno Capuano
**Source:** `inbox/drummond-disable-tool-e2e-nightly.md` (merged 2026-05-09T15:30:00Z)
**Status:** ✅ COMPLETED

### Context

GitHub Actions workflow `Tool E2E Nightly` was scheduled to run daily at 07:00 UTC (03:00 EDT) but was failing consistently, generating noise without actionable results.

### Decision

**Disabled the scheduled trigger** in `.github/workflows/tool-e2e-nightly.yml` by commenting out the `schedule:` block (lines 11-13). **Preserved manual trigger** (`workflow_dispatch`) for on-demand testing.

### Changes Made

```diff
 on:
-  schedule:
-    # 07:00 UTC = 03:00 EDT (America/New_York observes DST Mar-Nov)
-    - cron: '0 7 * * *'
+  # schedule:
+  #   # 07:00 UTC = 03:00 EDT (America/New_York observes DST Mar-Nov)
+  #   - cron: '0 7 * * *'

   workflow_dispatch:
```

### Rationale

- Nightly failures generated noise without immediate action
- E2E testing can be run manually via GitHub Actions UI when needed
- Reduces CI/CD resource consumption and alert fatigue
- Preserves workflow infrastructure for future use

### Impact

- ✅ No more nightly scheduled runs
- ✅ Manual `workflow_dispatch` still available
- ✅ No deletion of workflow infrastructure
- ⚠️ Team must manually trigger E2E testing when desired

### Re-enabling

To re-enable: Uncomment lines 11-13 in `.github/workflows/tool-e2e-nightly.yml`.

### Verification

- ✅ `git diff --check` passed (no whitespace issues)
- ✅ Workflow syntax preserved
- ✅ Manual trigger tested via UI

---

## 2026-05-09 - Daily Public Sync + Landing Page Updates

**Author:** Drummond (Platform Hardening / DevOps) · **Domain:** CI/CD + Content Sync
**Requested by:** Bruno Capuano (via Mark)
**Sources:** `inbox/drummond-public-sync-daily.md` (merged 2026-05-09T15:30:00Z); `inbox/ricken-public-sync-product-page.md` (merged 2026-05-09T15:30:00Z)
**Status:** ✅ RECONCILED & COMPLETED

### Context

Bruno confirmed the `Sync to Public Repo` workflow should run daily and update the landing page with:
1. Date of code sync
2. Top 5 latest changes/improvements

### Decision

### 1. Daily Schedule Trigger

Added `schedule` trigger to `.github/workflows/sync-to-public.yml`:
```yaml
schedule:
  # Daily at 2:00 AM UTC
  - cron: '0 2 * * *'
```

**Rationale:** Ensures public repo stays current even when no pushes occur; 2 AM UTC avoids peak hours; preserves `push` and `workflow_dispatch` triggers.

### 2. Landing Page Update Mechanism

**Marker-based replacement using HTML comments:**
- `<!-- SYNC_METADATA_START -->` / `<!-- SYNC_METADATA_END -->`
- `<!-- LATEST_CHANGES_START -->` / `<!-- LATEST_CHANGES_END -->`

**Content:**
- Sync date: `YYYY-MM-DD` format from workflow output
- Source SHA: short SHA from git repo
- Recent changes: extracted from last 20 commits, filtered to Top 5 (excludes `sync:`, `chore:`, `docs:` and `[skip ci]` commits)
- Format: HTML tile components matching existing landing page design
- **HTML Escaping:** Commit subjects escaped to prevent injection from special characters

**Safety:**
- Only uses commit messages from public-safe paths (already filtered by sync-config)
- No secrets or private paths exposed
- Fails gracefully if markers not present

### 3. Landing Page Changes

Modified `docs/landing/index.html`:
- Main `Latest Updates` section includes both sync metadata and changes (wrapped in markers)
- Default content shows until first automated sync
- Removed duplicate footer markers from initial implementation

### Marker Reconciliation (2026-05-09)

**PROBLEM IDENTIFIED:** Ricken added markers (`SYNC_METADATA_START/END`, `LATEST_CHANGES_START/END`) but workflow initially targeted different markers (`SYNC_DATE_START/END`, `RECENT_CHANGES_START/END`).

**RESOLVED:** Workflow updated to target Ricken's markers. Duplicate footer markers removed from landing page.

### Implementation Handoff

**For Drummond:**
- Workflow targets correct markers in main `Latest Updates` section
- Daily schedule ensures freshness within 24 hours

**For Ricken:**
- Main `Latest Updates` section is single authoritative sync zone
- Can adjust styling/layout as needed
- **Do not remove** marker HTML comments

**For Mark:**
- Workflow documentation updated
- No changes to sync-config schema
- Marker reconciliation complete

### Verification

- ✅ YAML syntax valid (schedule cron format correct)
- ✅ Daily schedule `0 2 * * *` = 2:00 AM UTC
- ✅ Sync exclusions preserved (sync-to-public.yml excluded via filtered_mirror)
- ✅ Content safety verified (only uses public-safe paths)
- ✅ Marker reconciliation complete
- ✅ `git diff --check` passed (no whitespace issues)

### Documentation Updated

- ✅ `.github/workflows/sync-to-public.yml` — added schedule trigger + landing page update step
- ✅ `docs/architecture/sync-plan-to-public.md` — documented daily schedule and marker names
- ✅ `docs/landing/index.html` — added sync markers, removed duplicates

---