# E2E Test Index

Central catalogue of all end-to-end and integration tests in OpenClawNet.
Each row links to the test file, describes what it proves, and shows the last recorded execution result.

> **Test dashboard (live HTML):** [elbruno.github.io/openclawnet/test-dashboard](https://elbruno.github.io/openclawnet/test-dashboard/)  
> **Refresh test outputs:** run `scripts\test-and-publish.ps1` after a test run; it records runs, rebuilds `docs\test-dashboard\`, and regenerates this index in this repo before the Pages deploy stages that folder to `/test-dashboard/` on the public site.  
> **Team rule (mandatory):** Every time a test is executed OR a new E2E/integration test is added, this index must be updated in the same change.  
> **Aspire AppHost rule (mandatory):** Any test that requires the AppHost must begin from a clean Aspire state with `aspire stop`, then start the stack with `aspire start`, then run `aspire describe --format Json` to confirm Aspire is running and to discover resource endpoints before executing the test. End the test run with `aspire stop`.

---

## Sync & Integration Events

| Event | Date | Description |
|-------|------|-------------|
| Cherry-pick from public | 2026-05-23 | Synced commit 86d3ef1 (Fix: Add extensive logging to markdown_convert tool) from public repo. Resolves conflict by taking public version with comprehensive structured logging. Build verified: 0 errors, 0 warnings. Pushed to origin/main. |

---

## How to read this table

| Column | Meaning |
|--------|---------|
| **Test / Class** | Clickable link to the test file. |
| **What it proves** | One-sentence description of the test scenario. |
| **Last run** | Date of the most recent recorded execution. |
| **Result** | PASS / FAIL / SKIP / Not recorded |
| **Notes** | Short failure reason or known caveat. |

Run filters opt in to specific layers; each suite section includes the exact `dotnet test` command to use.
