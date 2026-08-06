# Dylan — PR #207 Test-Gate Review

**Date:** 2026-08-06  
**PR:** #207 `feat: replacement for PR #205 — squad meta, package upgrades, Dylan regression fixes`  
**Branch:** `mark/pr205-replacement` → `main`  
**Author:** @elbruno / Mark (Lead Architect) co-authored with Copilot  
**Decision:** **REJECT — do not merge until blockers resolved**

---

## Evidence Summary

### Branch / Base Verification ✓
- Base: `main` (correct)  
- Merge base confirmed as `12dedb9` (current main HEAD)
- **GitHub API reports `mergeable: CONFLICTING`** — must be resolved with explicit rebase/force-push before merge
- `git merge-tree --write-tree main FETCH_HEAD` returned a clean tree (no content conflicts found locally), so the CONFLICTING status may be a stale GitHub cache, but the PR owner must force-push a rebase to clear it definitively

### CI Status ✗
- `statusCheckRollup: []` — **zero CI checks attached to this PR**
- The claimed validation ("1082 passed, 0 failed") is author-asserted and cannot be independently confirmed from CI artifacts
- No required status checks means the merge gate is purely advisory

### Regression #1: `DisableCaching` (K-D-1) ✓ FIXED
- **Source diff verified:** `OpenClawNetSkillsProvider.GetMafProviderOptions()` changed from `new() { DisableCaching = true }` to `new()` — correct, since MAF 1.17 removed the property
- **Test diff verified:** `Build_DisableCachingTrue_OnEveryBuild` → `Build_MafProviderOptions_ReturnsDefaultOptions`; assertion changed from `.DisableCaching.Should().BeTrue()` to `.Should().NotBeNull()` — correct and consistent

### Regression #2: `AppInsightsAuditSink` SecretName ✓ FIXED
- **Source diff verified:** `telemetry.Properties["SecretName"] = secretName;` added at correct position in `RecordAsync`
- **Test run verified:** `AppInsightsAuditSinkTests.RecordAsync_WritesInnerAuditAndTracksEvent` **PASSED** in reproduced run

### Azure Unit Test Run ✓ 12/12
```
dotnet restore tests\OpenClawNet.UnitTests.Azure -r win-x64  → exit 0
dotnet build   tests\OpenClawNet.UnitTests.Azure --no-restore → exit 0 (0 errors)
dotnet test    tests\OpenClawNet.UnitTests.Azure --filter "Category!=Live"
  Passed: 12 / Failed: 0 / Skipped: 0
  (previously: 11/12 — RecordAsync_WritesInnerAuditAndTracksEvent was failing)
```

### Full `OpenClawNet.UnitTests` (1082 claim) ✗ CANNOT REPRODUCE
- Build of `OpenClawNet.UnitTests` on fresh worktree fails:
  ```
  MSB3923: Failed to download copilot-win32-x64-1.0.36-0.tgz from npmjs.org
  ---> TLS HandshakeFailure
  ```
- This is an **environment-dependent** issue (npmjs.org unreachable via TLS in CI/local agent environment)
- Same blocker affects `main` on a fresh build; only works on `main` because pre-built `OpenClawNet.Models.GitHubCopilot` artifacts are cached from a prior run
- The author's 1082/0 claim is credible given both fixes are correctly implemented, but it cannot be reproduced here

---

## Blockers Preventing Approval

| # | Blocker | Severity |
|---|---------|----------|
| 1 | GitHub `mergeable: CONFLICTING` not cleared — PR requires rebase + force-push | **MERGE-BLOCKING** |
| 2 | Zero CI checks — no automated pipeline ran on the PR branch head | **POLICY-BLOCKING** |
| 3 | Full UnitTests (1082) unverifiable in this environment (TLS to npmjs.org) | **EVIDENCE GAP** |

---

## Recommended Path to Approval

1. **Rebase** `mark/pr205-replacement` on current `main` and force-push to clear the CONFLICTING state
2. **Ensure CI workflow triggers** — at minimum the build+unit-test step must pass and attach a status check
3. If CI remains unavailable: share a build log artifact from a clean machine confirming 1082/0

---

## Non-blocking Observations

- NU1608 Humanizer.Core version mismatch (IntegrationTests) — pre-existing, not introduced by this PR
- `PackageVersionRegressionTests.cs` is a new guard test (good, guards issue #202) — not independently run here but correct in structure
- `AgentProfileEndpoints.cs` endpoint removal and `DocumentPipelineTests.cs` SkippableFact changes look appropriate for ASP.NET Core 10 compatibility — out of scope for this review but no objections

---

*Reviewed by Dylan (Test Engineer) — 2026-08-06*
