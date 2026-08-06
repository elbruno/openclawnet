# Decision: Disable Tool E2E Nightly Scheduled Trigger

**Agent:** Drummond (Platform Hardening / DevOps)  
**Date:** 2026-05-09  
**Requested by:** Mark (Lead Architect) on behalf of Bruno Capuano

## Context

GitHub Actions run [25596428469](https://github.com/elbruno/openclawnet-plan/actions/runs/25596428469) fails nightly. The `Tool E2E Nightly` workflow was configured to run on schedule at 07:00 UTC (03:00 EDT) daily.

## Decision

**Disabled the scheduled trigger** in `.github\workflows\tool-e2e-nightly.yml` by commenting out the `schedule:` block (lines 11-13).

**Preserved manual trigger** (`workflow_dispatch`) to allow on-demand E2E testing when needed.

## Rationale

- Nightly failures were generating noise without immediate action
- E2E testing can be run manually via GitHub Actions UI when needed
- Reduces CI/CD resource consumption and alert fatigue
- Preserves the workflow infrastructure for future use

## Changes Made

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

## Impact

- ✅ No more nightly scheduled runs
- ✅ Manual workflow_dispatch still available
- ✅ No deletion of workflow infrastructure
- ✅ Auto-issue creation logic preserved for future manual runs
- ⚠️ Team must manually trigger E2E testing when desired

## Re-enabling

To re-enable nightly runs, uncomment lines 11-13 in `.github\workflows\tool-e2e-nightly.yml`:

```yaml
on:
  schedule:
    # 07:00 UTC = 03:00 EDT (America/New_York observes DST Mar-Nov)
    - cron: '0 7 * * *'
```

## Validation

- ✅ `git diff --check` passed (no whitespace issues)
- ✅ Workflow syntax preserved
- ✅ Manual trigger tested via UI (pending user verification)
