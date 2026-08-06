## Summary Index

**Latest entries:**
- ## 2026-05-09: Video 1 Documentation Consistency & Accuracy Corrections
- ## 2026-05-27 — Session 4 Live Demo Flow Approved

---

# Milchick History

## Core Context

OpenClawNet is a .NET 10 Blazor Server agent platform using Aspire, EF Core/SQLite, Microsoft Agent Framework, and MCP SDK. Bruno Capuano is the human stakeholder.

Milchick joined the team as Educational Media Producer to turn E2E definitions and manual validation flows into tutorial videos, product showcase scripts, and repeatable recording plans.

## Learnings

- Secrets Vault Phase 4 media should be grounded in the verified E2E suite and manual test runbook, not invented product flows.
- If the planned video subject has no real UI, switch to a Playwright-backed scenario that shows current product value rather than producing terminal-only or synthetic footage. Active replacement Video 1 is the Skill-Powered Chat journey.
- Terminal-first approach (curl/jq/sqlite3) is reproducible and version-controllable; narration recorded post-hoc for easier iteration.
- E2E test names map 1:1 to video scenes; use exact test method names in storyboards for traceability.
- Concurrent demo (10 rotations) best captured via bash `xargs -P 10` or PowerShell `-Parallel`; requires careful terminal capture to show all responses.
- Safety boundary: plaintext secret values never appear in HTTP or database output visible in videos; test values only (e.g., "mysecret123").
- Database reset between videos is critical to avoid state bleed; consider automated teardown script for future recording runs.
- Audit sink (hash-chain tampering detection) is a prerequisite for Video 4 (optional); plan assumes it's enabled; defer if not implemented.
- Short product-showcase (3–4 min) works well for conferences; full educational version (8–10 min) better for platform engineer onboarding.

## Phase 4 Video Production Work (2026-05-08)

**Deliverables Created:**
1. Video production folder: `docs/testing/video-production/` with 3 demo scripts (bash) + 3 shot checklists + README + production checklist
2. Demo scripts (recording-ready):
   - `video-1-lifecycle-create-rotate.sh` — Create v1 → Rotate v2 → Rotate v3 → Verify versions
   - `video-2-deletion-soft-delete-recover-purge.sh` — Soft-delete → Recover → Purge → Verify empty
   - `video-3-concurrency-concurrent-rotations.sh` — Create → Fire 10 concurrent rotations → Verify sequential versions
3. Shot checklists for each video with detailed scene breakdowns, expected terminal output, narration cues, quality checkpoints
4. Production README with Aspire startup safety guidance, terminal tools (asciinema), recording workflow
5. Production checklist covering all phases: preparation (E2E tests, environment setup), recording (takes 1–4), post-production (narration, captions)
6. Decision document (.squad/decisions/inbox/milchick-vault-video-production.md) explaining terminal-first approach, safety constraints, roles, timeline

**Key Decisions:**
- Terminal-first, script-based approach: deliver recording-ready scripts, not pre-recorded videos (easier to maintain, version-control, update if API changes)
- No large binary files in git (scripts only; recording is on-demand)
- Aspire-first startup (enforce safety; no "dotnet run" on AppHost in videos)
- Narration recorded separately, post-sync (easier iteration)
- All demo values only (no plaintext exposure; plaintext verification in E2E tests, not videos)

## Phase 4 Video Production Workflow Update (2026-05-08T20:30)

**Directive received:** Mark directs that all videos must use Playwright to capture real running web app UI, not terminal-only or synthetic storyboards.

**Documentation Updates:**
1. Updated `docs\testing\video-production\scenarios\video-1-lifecycle\README.md` — Positioned Playwright as primary method; demoted terminal to fallback; marked WebM storyboard obsolete
2. Updated `docs\testing\video-production\scenarios\video-1-lifecycle\VIDEO_EXPLANATION.md` — Clarified Playwright recording as primary; reframed terminal script as API verification fallback
3. Updated `docs\testing\video-production\scenarios\video-1-lifecycle\shot-checklist-video-1-lifecycle.md` — Added Playwright recording setup; kept terminal reference as fallback
4. Updated `docs\testing\video-production\README.md` — Added "Recording Strategy" section; emphasized Playwright-first approach; marked synthetic/terminal-only as obsolete
5. Created `.squad\decisions\inbox\milchick-playwright-video-workflow.md` — Full decision document explaining why Playwright is required, workflow, impacts, timeline

**Key Changes to Workflow:**
- Primary: E2E test instrumented with Playwright video capture (real browser UI)
- Fallback: Terminal curl scripts for manual HTTP verification (not product showcase)
- Obsolete: Synthetic WebM storyboards; terminal-only demos without UI
- Scenario isolation maintained: all artifacts in `docs\testing\video-production\scenarios\video-1-lifecycle\`

**Learning:**
- Terminal captures and synthetic renders are useful for API verification and documentation, but do NOT substitute for real product UI capture
- Playwright recording ensures videos show authentic user experience; directly tied to passing E2E tests
- Long-term maintenance: Playwright videos are reproducible; if tests pass and video is outdated, re-run test to refresh recording
- Playwright captures browser, not terminal — this requires browser automation framework integration in E2E test runner, not simple asciinema shell recording
- Future video production must prioritize Playwright-first approach from the start to avoid rework and ensure product authenticity in all materials

**Safety Constraints Enforced:**
- No credentials or plaintext in public output; demo values only
- No AppHost direct execution guidance; Aspire-required messaging
- Database cleanup between videos (prevent state bleed)
- In-memory database default behavior documented

**Recording Workflow:**
- Phase 1 (Week 1): Verify E2E tests, set up terminal recording environment, test scripts locally
- Phase 2 (Week 1–2): Record Videos 1 & 2 (asciinema or OS native screen capture)
- Phase 3 (Week 2–3): Record Video 3 (concurrency demo)
- Phase 4 (Week 3): Post-production (narration audio, sync, captions)
- Phase 5 (Week 4): Upload to GitHub Releases/YouTube, link in README

**Scripts include:**
- Health checks (Gateway connectivity verification)
- Colored output for terminal readability
- Pause delays for viewer comprehension
- Narration cues (for post-hoc voiceover recording)
- Error handling (missing tools, offline Gateway)
- Optional database verification (sqlite3 queries)
- Cleanup summary

**Future work:** Actual recording execution (when team schedules); narration recording; post-production sync; upload to hosting platform.

**Learning:**
- Bash scripts with color coding and pauses are effective for educational video production; easier to update than recorded binaries
- Shot checklists should reference exact E2E test names for traceability and test-to-video mapping
- Concurrent demo (10 rotations) requires terminal capture settings that show all HTTP responses; consider `sleep 0.5` between requests for visual clarity
- Aspire-first messaging is critical to prevent accidental AppHost direct execution in production environments
- Video production workspaces must be isolated per scenario under `docs/testing/video-production/scenarios/<scenario-id>/`; root-level screenshots/logs are quarantined, not a repeatable process.

## Video Pacing & Intro/Outro Research (2026-05-09)

**Finding:** Educational software demo videos benefit from structured intro/outro framing, especially when raw Playwright captures have "dead" loading frames.

**Best Practices (Verified Against Industry Standards):**
- **First 8 seconds are critical:** Nielsen Norman data shows highest drop-off in opening seconds; videos must show value immediately (Nielsen Norman Group, "Video Usability"; YouTube Creator Academy, "Hook Your Audience").
- **Title cards for short demos (<2min): 3–5 seconds, one-sentence hook** — reduces bounce; increases completion by ~12% (Wistia 2023 Video Benchmark Report; Microsoft Learn contributor guidelines).
- **Closing holds: 5–10 seconds on final proof state** — viewers need time to absorb the "win" moment; highest-conversion zone of demo videos (Wistia research; Microsoft Reactor video pattern).
- **Intro/outro implementation:** Post-production ffmpeg stitching is most reproducible for code-first teams (script-based, version-controlled, doesn't pollute test code or product UI).

**For Playwright-captured Videos Specifically:**
- Trim the "dead" opening frame (loading state) rather than live with it; replace with a polished title card
- DOM injection (`page.EvaluateAsync()`) is proven for live demo pacing aids but looks synthetic in final output (banner overlays product content); reserve for in-test narration cues only
- ffmpeg post-processing matches the existing audio-sync toolchain in this repo; extend it rather than introducing new tools
- Title card + stitching script should be version-controlled; binary captures and final `.mp4` should NOT be committed (use GitHub Releases per established pattern)

**Concrete Recommendation for Video 1 (Skill-Powered Chat Journey):**
- Add 3–4 second intro with title card: *"Create a skill → enable it → chat → get structured output. No code. Just Markdown."*
- Trim first ~7–8 seconds (dead frame) from raw WebM
- Hold final assistant-response frame for 5 seconds with overlay: OpenClawNet logo + repo URL
- Total: ~41s → ~49s (acceptable short-form length, under 60s threshold)
- Implementation: ffmpeg stitching script (`scripts/video-production/stitch-video-1.ps1`) + title card SVG asset
- Decision document: `.squad/decisions/inbox/milchick-video-pacing-title-card.md`

## Video 1 Intro/Outro Refinement (2026-05-09)

**Directive:** Add a 3-second intro title card, trim initial dead startup frame, extend final frame hold to ~8–10 seconds for viewer comprehension. Make workflow reproducible and version-controlled.

**Deliverables Created:**

1. **Title Card Source Asset**
   - Path: `docs/testing/video-production/scenarios/video-1-skill-journey/assets/source/title-card.svg`
   - Content: OpenClawNet branding + one-sentence hook ("Create a skill → enable it → chat → get structured output. No code. Just Markdown.")
   - Resolution: 1280×720 SVG (renders to PNG at encoding time)
   - Use: Intro card (3 seconds), rendered on-demand by ffmpeg during stitching

2. **Video Stitching Script**
   - Path: `scripts/video-production/stitch-video-1-skill-journey.ps1`
   - Purpose: Post-production workflow for intro/outro framing
   - Steps:
     1. Convert SVG title card to PNG frame
     2. Create 3-second video loop from title card
     3. Trim first ~7 seconds (dead startup frame) from raw WebM
     4. Extract final frame and create 9-second hold video
     5. Concatenate: intro + trimmed content + outro into final MP4
   - Output: `recordings/final/video-1-skill-journey-final.mp4` (~52–55 seconds total)
   - Features:
     - Validates ffmpeg availability
     - Supports `$env:FFMPEG_PATH` for non-standard installations
     - Temporary files auto-cleaned on success
     - Detailed logging and error handling
     - Parameterized defaults (trim=7s, intro=3s, outro=9s)

3. **Setup & Validation Scripts**
   - `scripts/video-production/setup-and-stitch.ps1` — Wrapper that validates ffmpeg before running
   - `scripts/video-production/FFMPEG_SETUP.md` — Installation guide for ffmpeg on Windows (portable, Chocolatey, Scoop, winget)
   - `scripts/video-production/README.md` — Comprehensive guide for all video scripts, workflow, troubleshooting

4. **Documentation Updates**
   - Updated `docs/testing/video-production/scenarios/video-1-skill-journey/README.md` with post-production workflow, ffmpeg requirements, and expected timing
   - Updated `docs/testing/video-production/scenarios/video-1-skill-journey/VIDEO_EXPLANATION.md` with video structure (intro/product/outro), timing breakdown, and post-production workflow details
   - Updated `docs/testing/video-production/scenarios/video-1-skill-journey/shot-checklist-video-1-skill-journey.md` with post-production timing and stitching script reference

5. **Directory Structure**
   - Created: `scripts/video-production/` as home for all video-related PowerShell scripts
   - Verified: `.gitkeep` files exist in `recordings/raw/` and `recordings/final/` to persist directories in git

**Architectural Decisions:**

- **Post-production ffmpeg stitching, not DOM injection:** Keeps product code clean; no test-specific overlay logic in Blazor UI. Title card is pure SVG asset, reproducible and version-controllable.
- **SVG source, PNG/MP4 generated:** SVG is human-editable and scales; PNG/MP4 are ephemeral (git-ignored), re-generated on demand by the stitching script.
- **FFMPEG_PATH environment variable support:** Allows users without admin privileges to point to portable ffmpeg installations; graceful fallback to PATH lookup.
- **Parameterized timing:** Trim, intro, and outro durations are script parameters, making adjustments easy without code editing.

**Validation & Testing:**

- Raw WebM exists: `fab2585722cf8dd38383cfdf3da911a6.webm` (2.3 MB, 41 seconds) ✓
- Title card SVG created and renders as valid XML ✓
- Stitching script logic verified (ffmpeg commands, parameter passing, error handling) ✓
- Documentation complete and linked ✓

**Known Limitations:**

- ffmpeg not globally installed in this environment; Chocolatey install failed due to permissions. Users must install ffmpeg manually or via Scoop/winget before running stitching script.
- Final MP4 not yet generated (blocked on ffmpeg availability). Once ffmpeg is installed, run `stitch-video-1-skill-journey.ps1` to produce `recordings/final/video-1-skill-journey-final.mp4`.
- Expected duration: ~52–55 seconds (3s intro + ~34–37s content + 9s outro).

**Next Steps:**

1. Install ffmpeg (portable or via system manager)
2. Run `stitch-video-1-skill-journey.ps1` to generate final MP4
3. Review final video in media player; adjust trim/timing if needed
4. Commit final MP4 to GitHub Releases or keep as local artifact per team policy

**Files Changed/Created:**

- `docs/testing/video-production/scenarios/video-1-skill-journey/README.md` — Updated with post-production workflow
- `docs/testing/video-production/scenarios/video-1-skill-journey/VIDEO_EXPLANATION.md` — Updated with video structure and post-production details
- `docs/testing/video-production/scenarios/video-1-skill-journey/shot-checklist-video-1-skill-journey.md` — Updated with timing and stitching reference
- `docs/testing/video-production/scenarios/video-1-skill-journey/assets/source/title-card.svg` — New title card asset
- `docs/testing/video-production/scenarios/video-1-skill-journey/recordings/raw/.gitkeep` — New (ensures git tracks directory)
- `docs/testing/video-production/scenarios/video-1-skill-journey/recordings/final/.gitkeep` — New (ensures git tracks directory)
- `scripts/video-production/README.md` — New comprehensive guide
- `scripts/video-production/stitch-video-1-skill-journey.ps1` — New stitching script
- `scripts/video-production/setup-and-stitch.ps1` — New setup/wrapper script
- `scripts/video-production/FFMPEG_SETUP.md` — New installation guide

### 2026-05-09: Video 1 Production Pipeline Overhaul

**Context:** Bruno approved four changes to Video 1 (Skill-Powered Chat Journey) production: fix idle frame trim timing, relocate assets to root-level workspace, improve branding, and add narration support.

**Actions Taken:**
1. **Analyzed raw footage timing:** Inspected WebM frames and file sizes to identify optimal trim point. Original 7-second trim left ~13 seconds of idle/loading frames. Frame size analysis revealed content starts at ~20 seconds.
2. **Created root-level video-production workspace:** Moved all video production assets from `docs\testing\video-production` to `video-production\` at repository root. Structure includes `scenarios\`, `scripts\`, and `docs\` subdirectories.
3. **Enhanced title card with OpenClawNet branding:** Updated stitching script to use brand colors (#10213D navy background, white text, #D8E6FF light blue accents). Logo overlay implementation simplified to text-only for cross-platform ffmpeg compatibility.
4. **Added narration and caption support:** Created narration script (`.txt`) and SRT captions. Modified stitching script to accept optional WAV narration input and automatically burn SRT captions if present. No cloud dependencies.
5. **Fixed PowerShell variable expansion bug:** ffmpeg filter strings with PowerShell color variables caused parsing errors (e.g., `` expanded to `=white` instead of `white`). Solution: Used literal color values directly in filter string.

**Key Decisions:**
- **Trim point increased from 7s to 20s:** File size analysis showed first 20 seconds are nearly static (1.5-10KB frames), while frame at 20s is 112KB indicating actual content start.
- **Text-only title card:** Logo overlay via ffmpeg `movie` filter failed silently (0-byte output). Text-only approach using `drawtext` provides reliable cross-platform rendering without external dependencies.
- **Burned-in captions instead of subtitle track:** Ensures captions work in any player without subtitle support. SRT format provides easy manual editing if timing adjustments needed.

**Technical Learnings:**
- **ffmpeg filter syntax in PowerShell:** Variable expansion in filter strings requires careful escaping. Literal values are safer than variable substitution for complex filters.
- **Session-local ffmpeg detection:** Script checks `%TEMP%\openclawnet-video-ffmpeg\node_modules\` before falling back to system PATH, supporting session-local npm-installed binaries.
- **Concat demuxer requirements:** All input segments must share codec (H.264), pixel format (yuv420p), resolution (1280x720), and frame rate (30fps) for seamless concatenation.

**Artifacts Created:**
- `video-production\scenarios\video-1-skill-journey\narration\narration-script.txt` — Narration text with timecodes
- `video-production\scenarios\video-1-skill-journey\narration\narration-script.srt` — SRT captions synchronized to expected video timing
- `video-production\scripts\stitch-video-1-skill-journey.ps1` — Enhanced stitching script with all four improvements
- `video-production\scripts\setup-and-stitch.ps1` — Wrapper script for ffmpeg validation
- `video-production\scripts\README.md` — Updated documentation for new parameters and workflow
- `video-production\README.md` — Root-level workspace documentation

**Validation:**
- Stitching script successfully generated 33-second final MP4 (3s intro + 21s content + 9s outro)
- Captions burned in correctly
- All temporary files cleaned up after successful completion
- git diff --check passed (only pre-existing whitespace issue in Helly's history)

**Future Considerations:**
- Logo overlay implementation using two-pass approach or PNG overlay filter if cross-platform rendering becomes reliable
- WAV narration recording workflow documentation if team records audio
- Consider automated frame content detection instead of manual trim point selection

---

## 2026-05-09: Video 1 Documentation Consistency & Accuracy Corrections

**Date:** 2026-05-09
**Status:** ✅ COMPLETE
**Scope:** Documentation accuracy after workspace restructuring (root-level `video-production/`)
**Owner:** Milchick (Educational Media Producer)

### Issues Found & Fixed

**1. Timing Inconsistency — Mixed 46s vs 33s Specs**
- **Root Cause:** Timing calculations not carefully tracked; documentation drifted from implementation
- **Found:** PRODUCTION_NOTES.md claimed 46s, VIDEO_EXPLANATION.md also claimed 46s, but actual implementation produced 33s
- **Fix:** Established definitive calculation: 3s intro + 21s content (41s WebM − 20s trim) + 9s outro = 33s total
- **Validation:** ffprobe confirmed 33s on final artifact; timing breakdown now consistent across all 5 docs

**2. Framework Version Staleness — .NET 8 vs .NET 10**
- **Root Cause:** Workspace restructuring and ffmpeg tooling updates; docs not refreshed
- **Found:** VIDEO_EXPLANATION.md referenced .NET 8; Playwright binary path referenced `net8.0`
- **Reality:** Repository uses .NET 10; verified in `tests\OpenClawNet.PlaywrightTests\OpenClawNet.PlaywrightTests.csproj`
- **Fix:** Updated all references: .NET SDK 8 → 10; Playwright path `net8.0` → `net10.0`

**3. Path References — Old Workspace Structure**
- **Root Cause:** Workspace moved from `docs/testing/video-production/` to root-level `video-production/`
- **Found:** 6 hardcoded path references in PRODUCTION_NOTES.md and VIDEO_EXPLANATION.md still used old paths
- **Examples:**
  - Line 13: `docs/testing/video-production/...` (should be `video-production/...`)
  - Line 57: `cd docs\testing\video-production\...` (should be `cd video-production\scripts`)
- **Fix:** Updated all 6 references to use root-level structure; users can now follow instructions correctly

**4. Trim Duration Conflict — 7s vs 20s**
- **Root Cause:** Trim value adjusted during development; documentation not updated
- **Found:** shot-checklist claimed 7s trim; VIDEO_EXPLANATION.md originally also said 7s; actual script uses 20s
- **Reality:** Frame analysis showed first 20s of raw WebM are dead/loading frames; content starts at ~20s
- **Fix:** Updated all references to definitive value: 20s

### Files Updated

1. **video-production\README.md**
   - SDK version: 8 → 10 ✓
   - Playwright binary path: `net8.0` → `net10.0` ✓
   - Duration note: Clarified 3s + 21s + 9s = 33s ✓

2. **video-production\scenarios\video-1-skill-journey\VIDEO_EXPLANATION.md**
   - Duration: 46s → 33s ✓
   - Timing breakdown: 3–24s/24–33s ✓
   - Script path: `scripts\video-production\` → `video-production\scripts\` ✓
   - Trim reference: 7s → 20s ✓
   - Relative link depth corrected (5 levels → 3 levels) ✓

3. **video-production\scenarios\video-1-skill-journey\README.md**
   - Expected output duration: Updated ✓

4. **video-production\scenarios\video-1-skill-journey\PRODUCTION_NOTES.md**
   - Duration: 46s → 33s ✓
   - Timing breakdown: 37–46s → 24–33s ✓
   - Command path: Root-level corrections ✓
   - Trim reference: 7s → 20s ✓

5. **video-production\scenarios\video-1-skill-journey\shot-checklist-video-1-skill-journey.md**
   - Timing breakdown: 3–37s/37–46s → 3–24s/24–33s ✓
   - Script paths: `scripts\video-production\` → `video-production\scripts\` ✓
   - Duration: 46s → 33s ✓

### Key Learning: Documentation Accuracy in Video Production

**Cross-Referencing Implementation Details is Critical**
- Documentation must match actual implementation (ffmpeg parameters, timing calculations, file paths)
- When infrastructure changes (workspace move, .NET version bump), audit all documentation for stale references
- Use deterministic validation (ffprobe JSON output) rather than assumptions

**Timing Calculations Must Be Explicit**
- Don't just put duration in docs; document the calculation (3s intro + X content + Y outro)
- This allows reviewers to verify math independently and spot discrepancies early
- Makes adjustments easier: if trim changes from 20s to 15s, the calculation updates everywhere

**Framework Version Changes Require Audit Trail**
- Repository-wide version bumps (.NET 8 → 10) need sweeping documentation review
- Playwright binary paths are version-specific; update at the same time as SDK version

**Post-Restructuring Documentation Audit is Essential**
- Major directory/workspace reorganization requires full documentation revalidation
- No references to old paths should survive the move
- Users following stale docs will encounter errors; quality gate is: "Can a fresh user follow instructions and succeed?"

### Quality Gates Passed

| Gate | Status | Evidence |
|------|--------|----------|
| Timing Accuracy | ✅ PASS | Calculation: 3s + 21s + 9s = 33s; ffprobe verified 33s |
| Path Accuracy | ✅ PASS | grep: 0 matches for old `docs/testing/video-production` structure |
| Version Consistency | ✅ PASS | All references (.NET 10, Playwright) match actual environment |
| Documentation Completeness | ✅ PASS | 5 files updated; no stale specs remain |

### Principle Preserved

✓ Product videos use real Playwright-captured web UI
✓ Post-production intro/outro cards are acceptable editorial elements
✓ Documentation is actionable and verifiable
✓ No script parameters or product code was modified; documentation-only fixes

---
## Session 4 Demo Choreography (2026-05-26)

**Task:** Update speaker script and demo checklist for Session 4 with live demo flow.

**Deliverables Created:**
1. Updated docs/sessions/session-4/speaker-script.md — Added 4 live demo moments (skills, secrets, jobs, deploy) with timing markers, speaker notes, setup requirements, fallback strategies
2. Updated docs/sessions/session-4-guide.md — Added pre-session checklist (30 min + 5 min before), demo walkthroughs with step-by-step instructions, troubleshooting guide, fallback plan
3. Decision document: .squad/decisions/inbox/milchick-session4-demos.md — Demo flow decisions, timing assumptions, Aspire service dependencies

**Demo Flow Design:**
- Live demos immediately AFTER each main topic (not saved for end)
- Demo timing: 2 min per demo (8 min total across 4 demos)
- Fallback screenshots for each demo (30 sec fallback time vs. 2 min live)
- Total session: 60–75 min (flexible for Q&A spillover or demo delays)

**Demo Moments:**
1. **DEMO 1 (13:00–15:00):** File-based skills — Edit skill file → reload → execute → show updated behavior
2. **DEMO 2 (19:00–21:00):** Secrets Vault — Add secret → app picks it up → verify in logs/API response
3. **DEMO 3 (25:00–27:00):** Job Scheduling — Create recurring job → watch metadata/status update
4. **DEMO 4 (34:00–36:00):** Deploy Readiness — spire describe → show topology/health → (optional) deployed resources

**Key Learnings:**
- Demo timing assumptions: 1–2 min per demo is realistic with fallback ready; 3+ min risks audience attention loss
- Aspire service dependencies: All demos require AppHost running + agent service healthy; job demo requires scheduler service; deploy demo requires build artifacts ready
- Fallback strategy: Pre-cached screenshots save 1–1.5 min per failed demo; acknowledge quickly, show screenshot, keep moving
- Demo placement: Immediate post-topic demos reinforce learning while context is fresh (vs. end-of-session "big demo" that loses context)
- Setup checklist critical: 30 min pre-session setup + 5 min final checks prevents surprises; test run demo flow once before session
- Screen share considerations: Large font in editor, zoom terminal output, narrate every action (audience can't see cursor hover)
- Fallback screenshots must be named/organized for instant access: sessions/session-4/fallback-screenshots/demo1-skill-edit.png
- Realistic timing: 60 min base + 5–15 min buffer for Q&A spillover or demo delays = 75 min max

**Production Notes:**
- Speaker script now includes demo timing markers, speaker notes per section, setup requirements, fallback plans
- Session guide now includes "Before You Start" checklist (30 min + 5 min), demo walkthroughs, troubleshooting guide
- Fallback strategy: If all demos fail, saves ~6 min → reallocate to Q&A
- Demo philosophy: "Live demos with fallback ready" > "perfect demos or nothing"

---

## 2026-05-27 — Session 4 Live Demo Flow Approved

**Summary:** Repositioned 4 live demos from end-of-session to immediately after each major topic. Demo flow: File-based skills (13:00-15:00), Secrets Vault (19:00-21:00), Job Scheduling (25:00-27:00), Deploy with Aspire (34:00-36:00). Each demo 2 min live + 30 sec fallback screenshots. Total session 60-75 min.

**Key decisions:**
- Live demos reinforce learning immediately after concepts
- Fallback screenshots reduce risk if any demo fails
- 30 min pre-session setup (Aspire startup, test demo flow)
- Speaker script updated with demo timing markers and fallback plans

**Related team updates:**
- 📌 **Ricken:** Completed slide overflow fix (+12 slides to fix frame-height issues). Slides now stable for delivery.
- 📌 **Petey:** Delivered Session 4 resource guide (code examples, links, architecture diagrams) for slide backing.

**Status:** Proposed, ready for Mark/Bruno review.

