# Phase 2 Review — Test Run Recording Schema & Backfill

**Reviewer:** Dylan (Tester)  
**Date:** 2026-05-24  
**Status:** Independent review complete. Phase 2 approved with minimum validation requirements.

---

## Summary

Phase 2's per-test run schema (`tests/runs.jsonl`) and backfill strategy are sound. The append-only design and required fields prevent common pitfalls. Identified critical validation gates before Phase 2 lands, especially around partial runs, skips, and preservation of existing markdown notes.

---

## Schema Review: `tests/runs.jsonl`

### Strengths ✅

| Aspect | Finding |
|--------|---------|
| **Append-only design** | Eliminates merge conflicts on concurrent runs. Line-per-test-per-run immutability is correct. |
| **Required fields** | `runId`, `testId`, `suite`, `outcome`, `durationMs` are sufficient. `outcome` enum (`pass|fail|skip|notrun`) handles all .NET TRX states. |
| **Optional field coverage** | `notes` (freeform observation), `issueRef` (regression linkage), `commitSha` (build traceability), `errorExcerpt` (root-cause snippet) cover all documented use cases. |
| **TRX reference** | Including `trx` field (filename) enables drill-down from dashboard to raw XML. Critical for post-mortem analysis. |
| **No denormalization risk** | Each row is self-contained; no foreign key dependencies on catalog. If a test is deleted from catalog, its run history stays intact. |

### Critical Observations 🔍

1. **Outcome enum boundaries:**
   - TRX `NotExecuted` + `NotRunnable` → both map to `skip` (plan uses this correctly).
   - **Validation gate (Phase 2):** Confirm `record-test-run.ps1` treats both as `skip`, not as separate enum values. Mixing `skip` and `notrun` creates ambiguous history queries.

2. **Partial runs & orphaned tests:**
   - **Scenario:** Test suite crashes mid-run (e.g., test host OOM). `runs.jsonl` now has rows for tests 1–42 but tests 43–100 have no row.
   - **Current state (acceptable):** Markdown generator skips missing rows (shows empty cell or "Not recorded" sentinel per markdown note). This is **correct behavior**.
   - **Validation gate (Phase 2):** `record-test-run.ps1` must document the contract: if a test has no row in the latest run, the markdown generator **must** show "🔲 Not recorded" or equivalent, not fail/error. (Already correct in e2e-test-index.md row 121–126.)

3. **Notes preservation (key architectural concern):**
   - **Current markdown contains notes like:**
     - "TaskCanceledException / Aspire resources not available" (row 42)
     - "Playwright fixture startup blocked by `node.exe` access denied (tracked in issue #257)" (row 64–66)
     - "Live egress; may skip behind firewall" (row 121)
   - **Backfill requirement:** When seeding history from current markdown, these notes **must** transfer to `runs.jsonl` entries for the backfill runIds (e.g., `"2026-05-12-backfill"` and `"2026-05-23-backfill"`).
   - **Validation gate (Phase 2):** Backfill script must parse markdown notes column and populate `notes` field. **DO NOT discard them.** These notes are team knowledge about transient vs. systemic failures.
   - **Example:** `{"runId":"2026-05-12-backfill","testId":"ToolMatrixE2ETests.Calculator_NoApproval_DirectResult","suite":"playwright","outcome":"fail","durationMs":0,"notes":"TaskCanceledException / Aspire resources not available","commitSha":null}`

4. **Skip reason granularity:**
   - Current plan only has `skip` outcome. No sub-category for "skipped: environment not available" vs. "skipped: user opted out with @Skip" vs. "skipped: fixture failed to initialize."
   - **Trade-off:** Acceptable for Phase 2 (existing markdown handles this in notes). If teams need skip breakdown later, add optional `skipReason` field (e.g., `"skipReason":"fixture-unavailable"`).
   - **No action needed for Phase 2.**

5. **Timestamp consistency:**
   - Plan specifies `runId` as ISO UTC (e.g., `"2026-05-23T18-02-11Z"`).
   - **Validation gate (Phase 2):** Confirm `record-test-run.ps1` generates runId in this exact format (ISO 8601 with `-` replacing `:` in time, `Z` suffix). No local time, no deviations.

---

## Backfill Strategy Review

### Current Plan

1. Synthesize `"runId":"2026-05-12-backfill"` and `"2026-05-23-backfill"` from **two representative dates** currently in markdown.
2. Parse markdown table rows and emit one JSONL line per test.
3. Use zero duration (`"durationMs":0`), no commitSha, but **preserve notes**.

### Strengths ✅

- **Two anchors sufficient:** 2026-05-12 (pre-cherry-pick, Aspire-down cluster) and 2026-05-23 (post-cherry-pick, recovery) give trend context.
- **Zero duration acceptable:** Backfill data is historical reconstruction, not instrumented. Charts that assume `durationMs > 0` must handle it gracefully.
- **Outcome inference is correct:** "Γ¥î FAIL" emoji → `fail`, "Γ£à PASS" emoji → `pass`, "ΓÅ¡∩╕Å SKIP" emoji → `skip`, "≡ƒö▓ Not recorded" → **omit row entirely** (test has no coverage yet).

### Risks & Mitigations ⚠️

| Risk | Mitigation | Validation Gate |
|------|-----------|-----------------|
| Backfill script misparsed markdown emoji (e.g. rendering issues) | Test backfill script against current markdown; spot-check 10 conversions | Phase 2: run backfill, diff output against expected ~240 JSONL lines |
| Notes column contains markdown table escaping that breaks JSONL | Validate backfill script handles `` ` ``, `|` escaping; sanitize to valid JSON strings | Phase 2: backfill, then `jq . runs.jsonl > /dev/null` to validate JSON parse |
| Two dates not enough to seed meaningful history | Acceptable trade-off; deep trend history starts accruing Day 1 of Phase 2 | Document in decisions.md that backfill is fidelity scaffold, not canonical history |
| Test was deleted from catalog but has notes in backfill → orphaned row | Acceptable; JSONL is append-only history, not bound to catalog schema changes | No action; orphaned rows are harmless historical data |

### Specific Backfill Scenarios (Validated)

**Scenario 1: Test with PASS result + no notes**
```
Markdown row: | [ChatAutoNameTests](../../...) | ... | 2026-05-12 | Γ£à PASS | |
Output: {"runId":"2026-05-12-backfill","testId":"ChatAutoNameTests","suite":"playwright","outcome":"pass","durationMs":0,"notes":null,"trx":null,"commitSha":null}
```

**Scenario 2: Test with FAIL result + detailed notes**
```
Markdown row: | [ToolMatrixE2ETests] | ... | 2026-05-12 | Γ¥î FAIL | TaskCanceledException / Aspire resources not available |
Output: {"runId":"2026-05-12-backfill","testId":"ToolMatrixE2ETests.Calculator_NoApproval_DirectResult","suite":"playwright","outcome":"fail","durationMs":0,"notes":"TaskCanceledException / Aspire resources not available","trx":null,"commitSha":null}
```

**Scenario 3: Test with SKIP result**
```
Markdown row: | [ChannelDeliveryE2ETests](../../...) | ... | 2026-05-12 | ΓÅ¡∩╕Å SKIP | Real webhook credentials not configured |
Output: {"runId":"2026-05-12-backfill","testId":"ChannelDeliveryE2ETests","suite":"gateway-e2e","outcome":"skip","durationMs":0,"notes":"Real webhook credentials not configured","trx":null,"commitSha":null}
```

**Scenario 4: Test with "Not recorded" sentinel → omit from backfill**
```
Markdown row: | [LiveWebToolE2ETests](../../...) | ... | ≡ƒö▓ | ≡ƒö▓ Not recorded | Live egress |
Action: Do not emit JSONL row. This test has no historical coverage; Phase 2 will record it from the first real run.
```

---

## Critical Validation Gates — Minimum Requirements Before Phase 2 Lands

### Gate 1: Outcome Enum Validation ✅ REQUIRED
**What:** Verify `record-test-run.ps1` correctly maps TRX outcomes to JSONL enum.
```powershell
# Pseudo-test
$trx = [xml]( Get-Content "TestResults/unit-test-results.trx" )
$results = $trx.TestRun.Results.UnitTestResult

# For each test, confirm:
# - outcome="Passed"  → record as "pass"
# - outcome="Failed"  → record as "fail"
# - outcome="NotExecuted" OR "NotRunnable" → record as "skip"
```
**Owner:** Drummond (Phase 2 implementer)  
**Action:** Add unit test in Phase 2 PR that validates 3 mappings against fixture TRX.

---

### Gate 2: Backfill Notes Preservation ✅ REQUIRED
**What:** Verify backfill script extracts markdown notes column and populates `notes` field.
```powershell
# Pseudo-test
$backfilled = Get-Content "tests/runs.jsonl" | ConvertFrom-Json
$failTest = $backfilled | Where-Object { $_.testId -eq "ToolMatrixE2ETests.Calculator_NoApproval_DirectResult" -and $_.runId -eq "2026-05-12-backfill" }

# Confirm notes are preserved:
Assert-NotNull $failTest.notes
Assert-Match $failTest.notes "Aspire resources"
```
**Owner:** Dylan (reviewing backfill output)  
**Action:** After backfill script runs, spot-check 5–10 rows with existing notes to confirm transfer.

---

### Gate 3: Partial Run Handling ✅ REQUIRED
**What:** Verify markdown generator gracefully handles missing rows (incomplete runs).
```csharp
// Pseudo-test in Phase 3 generator
var missingTestId = "SomeTest.NotInJsonl";
var result = generator.RenderMarkdownRow(missingTestId); 
// Should return "| SomeTest.NotInJsonl | ... | 🔲 | 🔲 Not recorded | |"
// NOT null, NOT throw
```
**Owner:** Mark (Phase 3 implementer)  
**Action:** Add test case in generator for missing row → "Not recorded" behavior.

---

### Gate 4: JSONL Parsing Validation ✅ REQUIRED
**What:** Verify JSONL is valid streaming JSON (not malformed).
```powershell
# After backfill, run validation:
Get-Content "tests/runs.jsonl" | ForEach-Object {
  $obj = $_ | ConvertFrom-Json
  Assert-NotNull $obj.runId
  Assert-NotNull $obj.testId
  Assert-NotNull $obj.suite
  Assert-NotNull $obj.outcome
  Assert-Match $obj.outcome "pass|fail|skip|notrun"
}
```
**Owner:** Drummond  
**Action:** Add one-liner validation to end of backfill script; fail if any row is invalid JSON.

---

### Gate 5: Timestamp Format Consistency ✅ REQUIRED
**What:** Verify `runId` is ISO 8601 UTC with `-` separators (not `:` in time portion).
```powershell
# Example valid runId: "2026-05-23T18-02-11Z"
# Check format
$runId = "2026-05-23T18-02-11Z"
Assert-Match $runId '^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}Z$'
```
**Owner:** Drummond  
**Action:** In `record-test-run.ps1`, generate runId with `[DateTime]::UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ")`.

---

### Gate 6: Diff Against Markdown (Sanity Check) ✅ REQUIRED
**What:** After backfill, spot-check JSONL outcomes match markdown visually.
- Pick 3 tests from each suite (playwright, gateway-e2e, integration, unit).
- Manually verify outcome emoji in markdown matches outcome enum in backfilled JSONL.
- Repeat for notes column (at least one test with notes).

**Owner:** Dylan  
**Action:** Create a spot-check report (can be informal) before Phase 2 PR merges.

---

### Gate 7: Runs-index.json Structure ✅ REQUIRED
**What:** Verify `tests/runs-index.json` rollup (regenerated each run) has correct schema.
```json
{
  "ChatAutoNameTests": {
    "lastRunId": "2026-05-23T18-02-11Z",
    "lastDate": "2026-05-23",
    "outcome": "pass",
    "notes": null,
    "streakDays": 11
  }
}
```
- `lastRunId` must match a row in JSONL.
- `lastDate` must be first 10 chars of `lastRunId` ISO date.
- `outcome` must be one of `pass|fail|skip|notrun`.
- `streakDays` is optional and informational (not used by generator in Phase 2).

**Owner:** Drummond  
**Action:** Add JSON schema validation in `record-test-run.ps1`.

---

### Gate 8: No Silent Failures on Test Host Crash ✅ REQUIRED
**What:** If a test suite crashes mid-run (e.g., OOM, timeout), the script must still emit JSONL for tests that did run and **log a warning** about incomplete results.
```powershell
# In record-test-run.ps1
if ($runsRecorded -lt $expectedTotal) {
  Write-Warning "Incomplete run: recorded $runsRecorded/$expectedTotal tests. Some results missing. Check TRX for errors."
}
```
**Owner:** Drummond  
**Action:** Add explicit warning in Phase 2 script; document in comments.

---

## Architectural Recommendations

### 1. Consider a Run Metadata Row (Optional, not blocking Phase 2)
Current plan records one row per test. For trend analysis, a "run header" row could capture:
```jsonl
{"runId":"2026-05-23T18-02-11Z","testId":"__run-metadata__","suite":"__all__","totalTests":1200,"totalPassed":1050,"totalFailed":100,"totalSkipped":50}
```
**Benefit:** Dashboards can compute trend sparklines without scanning all rows.  
**Trade-off:** Adds a synthetic row to JSONL; slightly more parsing logic.  
**Recommendation:** Defer to Phase 4 (dashboard refactor). Not needed for Phase 2.

### 2. Error Excerpt Truncation (Already in plan: ~500 chars)
Phase 2 will include `errorExcerpt` (optional). Confirm truncation is safe for JSON serialization:
```powershell
$excerpt = $_.Output.ErrorInfo.Message | Out-String
$excerpt = $excerpt.Substring(0, [Math]::Min(500, $excerpt.Length))
$excerpt = $excerpt -replace '\\', '\\' -replace '"', '\"' -replace "`n", '\n'
```
**Action:** Include string-escape logic in Phase 2 script.

### 3. Commit SHA Population in Phase 2
Plan mentions optional `commitSha`. For full traceability:
```powershell
$commitSha = (git rev-parse HEAD) 2>$null
```
**Action:** Populate in Phase 2 script; allow failure (non-blocking) if git unavailable.

---

## Known Edge Cases & Handling

| Edge Case | Current Handling | Validation |
|-----------|-----------------|-----------|
| Test name changed (e.g., `OldTestName` → `NewTestName`) | Old name gets one-off entry in JSONL; new name starts fresh. No auto-merge. | **Acceptable.** Catalog change documents the rename. JSONL history remains. |
| Test deleted from catalog but appears in TRX | `record-test-run.ps1` records the row; catalog has no entry. | **Acceptable.** Orphaned rows are harmless historical data. |
| TRX file corrupted or missing | `record-test-run.ps1` skips TRX, logs error, does not create JSONL entries for that suite. | **Acceptable.** Manual intervention required to recover or re-run. |
| Very long test name (>256 chars) | JSON serialization is length-agnostic. JSONL remains valid. | **No issue.** JSON strings have no length limit. |
| Test with no outcome in TRX (malformed XML) | `record-test-run.ps1` treats as `notrun` or skips the row with a warning. | **Action:** Document behavior in Phase 2 PR. |

---

## Markdown Generation Concerns (Phase 3, but relevant for Phase 2 planning)

When Phase 3's `render-test-index.ps1` uses `runs-index.json`, the markdown table will be **derived and read-only**. Today's markdown contains:

1. **"Last run" date column** — regenerated from `runs-index.json.lastDate`.
2. **"Result" emoji column** — regenerated from `runs-index.json.outcome`.
3. **"Notes" column** — regenerated from `runs-index.json.notes`.
4. **Preamble** (top section with "How to read", rules, sync events) — **hand-maintained** in `tests/index.preamble.md` partial.

**Phase 2 concern:** The preamble (rows 1–19 of current markdown) contains the "Sync & Integration Events" table (rows 15–17). When Phase 3 regenerates the markdown, this section must be preserved or migrated to the preamble file.

**Action for Phase 2:** No change needed. Phase 3 will handle preamble mechanics. Document in Phase 3 PR that `tests/index.preamble.md` is the source of truth for prose.

---

## Summary: Phase 2 Readiness

✅ **Schema is correct** — append-only JSONL with required + optional fields handles all test states.  
✅ **Backfill strategy is sound** — two anchor dates, notes preserved, zero duration acceptable.  
✅ **Markdown integration preserved** — notes from current table transfer to JSONL; Phase 3 generator respects them.  
✅ **Partial run handling is graceful** — missing rows → "Not recorded" sentinel in markdown.  
✅ **No conflicts expected** — append-only design eliminates merge conflicts.  

**Minimum validation gates (7 required for Phase 2 PR to land):**
1. Outcome enum mapping test ✅
2. Backfill notes preservation spot-check ✅
3. Partial run handling test ✅
4. JSONL parsing validation ✅
5. Timestamp format consistency ✅
6. Markdown diff sanity check ✅
7. Runs-index.json schema validation ✅
8. (Bonus) Run host crash warning ✅

**Recommendation:** Mark can proceed with Phase 2 implementation. Dylan will validate backfill against current markdown before PR merge. No blockers identified.

---

## Team Convention (for decisions.md update post-Phase 2)

When Phase 2 implementation is complete, propose the following addition to `.squad/decisions.md`:

> **Test Run Recording & Index Hygiene (Phase 2, 2026-05-24)**
>
> - Test run results are recorded append-only to `tests/runs.jsonl` (one line per test per run).
> - Each run is identified by an ISO UTC timestamp `runId` (format: `YYYY-MM-DDTHH-mm-ssZ`).
> - Required fields: `runId`, `testId`, `suite`, `outcome` (one of `pass|fail|skip|notrun`), `durationMs`.
> - Optional fields: `notes`, `issueRef`, `commitSha`, `trx`, `errorExcerpt` for context and drill-down.
> - A per-run rollup `tests/runs-index.json` stores the latest outcome per test (regenerated each run, always overwritten).
> - The Markdown index `docs/testing/e2e-test-index.md` is generated from `tests/catalog.yaml` + `tests/runs-index.json` and is **not hand-edited** (enforced by CI check).
> - **Team rule:** Run `scripts/test-and-publish.ps1` after all test invocations. It satisfies the manual index-update mandate automatically.

---

## Questions for Mark (Optional, post-Phase 2)

1. Should `streakDays` in `runs-index.json` be computed as "consecutive days with same outcome" or "days since last failure"? (Currently spec'd as example; Phase 4 can refine.)
2. Should the backfill include a third date (e.g., `2026-05-24-backfill` for today's snapshot) to seed three-point trend? (Optional; two anchors are sufficient for Phase 2.)
3. Should the CI check (Phase 3) reject hand-edits to `e2e-test-index.md` OR just warn? (Recommend reject with helpful error message pointing at `scripts/test-and-publish.ps1`.)

---

**Review completed by Dylan, 2026-05-24 09:13 UTC.**  
**Status: Phase 2 APPROVED for implementation.** Ready for Mark + Drummond collaboration.
