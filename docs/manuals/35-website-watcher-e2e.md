# 35 — Website Watcher End-to-End Validation

> **Audience:** developers validating the Jobs pipeline end-to-end.
> **Branch / PR:** anything that touches `Jobs`, `JobRuns`, `Channels`, the demo
> templates, or the `web.fetch` / Browser tools should run this E2E and the
> manual checks below before merging.

This guide documents the automated **`WebsiteWatcherE2ETests.FullFlow_CreateFromTemplate_RunGetTitle_VisibleEverywhere`**
test and the parallel set of **manual checks** that each developer should walk
through after running it.

The test exercises the full Job pipeline using the real `OpenClawNet.AppHost`:

1. Create a job from the **Website Watcher** demo template.
2. Override its prompt so the agent fetches `http://elbruno.com` and returns the
   `<title>`.
3. Trigger it through the **Scheduler** service (the production trigger path).
4. Assert it shows up everywhere it should.

---

## Prerequisites

| Requirement | Why | How to set up |
|---|---|---|
| .NET 10 preview SDK | App targets `net10.0` | Install latest preview |
| `$env:NUGET_PACKAGES="$env:USERPROFILE\.nuget\packages2"` | Repo convention | Set in any shell that runs `dotnet` |
| **Either** Azure OpenAI **or** local Ollama | Test prefers Azure for speed; falls back to Ollama | See "Pick a backend" below |
| Aspire **stopped** | Test boots its own AppHost on the same SQLite DB | `aspire stop` if it's running |

### Pick a backend

The test auto-selects the fastest backend you have configured:

| Preference | Trigger | Setup |
|---|---|---|
| **1 — Azure OpenAI (fast, cloud)** | `AZURE_OPENAI_ENDPOINT` + `AZURE_OPENAI_API_KEY` + `AZURE_OPENAI_DEPLOYMENT` set to non-placeholder values | `setx AZURE_OPENAI_ENDPOINT "https://…openai.azure.com/"` and friends, then open a fresh shell. Test will provision an `azure-openai-e2e` provider + `website-watcher-azure-e2e` Standard profile, pin the job to it, and clean both up on completion. |
| **2 — Ollama (slower, local)** | `qwen2.5:3b` pulled locally and Ollama running | `ollama serve` + `ollama pull qwen2.5:3b` (~2 GB) |
| Neither | — | Test reports `Skipped`. Not a CI regression. |

> **Skip behavior:** the test is tagged `[Trait("Category","RequiresModel")]` and uses
> `Skip.IfNot(_fixture.IsAnyToolCapableModelAvailable, ...)`. With Azure
> configured, expected runtime drops from ~30 s to ~10 s.

---

## Run the test

```powershell
$env:NUGET_PACKAGES="$env:USERPROFILE\.nuget\packages2"
dotnet test tests\OpenClawNet.PlaywrightTests `
  --filter "FullyQualifiedName~WebsiteWatcherE2ETests" `
  --logger "console;verbosity=detailed" --nologo
```

Expected runtime: **~30 seconds** once the AppHost is warm.

---

## What each step validates

The test prints a numbered tag for every step; the contract for "OK" is that
**all eight tags appear and the suite exits 0**.

| # | Tag in output | Endpoint / surface | What it proves |
|---|---|---|---|
| 1 | `[1] Created job 'Website Watcher (N)' (...)` | `POST /api/demos/website-watcher/setup` | Demo template factory produces a job in `Draft`. |
| 2 | `[2] Prompt rewritten to fetch <title>` | `PUT /api/jobs/{id}` | Job prompt is editable post-creation. |
| 3 | `[3] Triggered run …` | `POST /api/scheduler/jobs/{id}/trigger` | Scheduler accepts the trigger and returns a `runId`. |
| 3b | `[3b] Run terminal status='completed'` + agent output | `GET /api/jobs/{id}/runs` (polled) | The run reaches a terminal state and stores the agent's output. |
| 4 | `[4] Run history has 1 run(s)` | same | Run history is durable. |
| 5 | `[5] Demo helper /status returned job info` | `GET /api/demos/website-watcher/status` | Demo helper still resolves the job after the prompt swap. |
| 6 | `[6] /api/jobs list contains job (N total)` | `GET /api/jobs` | Job is discoverable from the global list. |
| 7 | `[7] /api/channels lists our job` | `GET /api/channels` | Successful run produced a channel artifact (auto-capture by `ArtifactStorageService`). |
| 8 | `[8] /api/channels/{id} → 1 run(s), 1 artifact(s)` | `GET /api/channels/{jobId}` | Channel detail returns at least one artifact. |

### Acceptance contract for the agent's output

The prompt asks the agent to return exactly one of:

```
Title: <the page title>
Title: <unavailable: short reason>
```

Both shapes are **valid** — the test treats either as PASS because the goal is
to validate **orchestration**, not network reachability of `elbruno.com`. If
the test box can't reach `elbruno.com`, the sentinel form still proves the
agent ran the correct number of tool iterations and produced a structured
answer.

The sentinel form also catches the "max iterations exhausted" regression: a
broken agent that loops returns nothing matching the contract and step 3b
fails.

---

## Manual checks to run alongside the test

After the automated test succeeds, walk these UI paths to confirm the new
features behave for a human user. File feedback as a comment on the open PR or
a new issue with label `area:jobs-pipeline`.

### Manual check A — Job ID is visible everywhere

1. Start Aspire: `aspire start src\OpenClawNet.AppHost` → choose `OpenClawNet.AppHost`.
2. Open `https://localhost:7294/jobs`.
3. **Verify:** every row shows the **Job ID** (truncated GUID) next to the
   name. Hover/click should reveal the full GUID.
4. Open a job's detail page.
5. **Verify:** the Job ID is shown in the header.
6. Open `https://localhost:7030/jobs` (Scheduler UI).
7. **Verify:** same — Job ID visible on list and detail pages.

### Manual check B — Run live-refreshes

1. In the Web UI, click **Run** on any job.
2. **Verify:** the detail page auto-refreshes every ~3 s while the status is
   `running` and stops polling once it hits a terminal state.
3. **Verify:** the agent output appears without a manual page refresh.

### Manual check C — Website Watcher template (UI flow)

1. Open `https://localhost:7294/demos`.
2. Click **Website Watcher** → fill `http://elbruno.com` → **Create**.
3. **Verify:** redirect lands on the new job's detail page.
4. Edit the prompt to:
   `Fetch http://elbruno.com using web.fetch. Return one line: "Title: <the value of the HTML <title> tag>". If web.fetch fails, return "Title: <unavailable: <reason>>".`
5. Click **Run**.
6. **Verify:** the run completes within ~60 s and the output starts with `Title:`.
7. Open `https://localhost:7294/channels`.
8. **Verify:** the job appears with at least one artifact.

### Manual check D — Channels page is reachable

1. From the channels page, click into the channel for the job created above.
2. **Verify:** the page loads (no broken `/settings` link in the side nav).
3. **Verify:** the artifact body matches the agent's `Title:` line.

---

## Failure triage

| Symptom | Likely cause | Fix |
|---|---|---|
| Test reports **Skipped** | `qwen2.5:3b` not pulled, or Ollama not running | `ollama pull qwen2.5:3b`; start `ollama serve` |
| Step 3 fails: scheduler returns 5xx | Scheduler service didn't start. Look for `health check` errors in test log | Stop any external Aspire instance, retry |
| Step 3b times out | Agent exhausted `MaxToolIterations` (currently `25`) | Inspect run via `GET /api/jobs/{id}/runs`; check for tool-loop in logs |
| Step 3b output doesn't match contract | Model regression — prompt isn't being followed | Re-pull the model or pin a different one in `AppHostFixture.cs` |
| Step 7 fails: channel doesn't appear within 15 s | `ArtifactStorageService.CreateArtifactFromJobRunAsync` regressed | Check Scheduler logs; was the run actually `completed`? |
| Step 8 returns `runs` (not `recentRuns`) | DTO renamed | Update test to match new `ChannelDetailDto` field name |

---

## Where to file feedback

- **Bug?** Open an issue on `elbruno/openclawnet-plan` with label
  `area:jobs-pipeline` and attach the full test output (or the relevant
  Aspire dashboard screenshot).
- **Behavioral question?** Comment directly on the PR that introduced the
  change.
- **Doc gap in this file?** PR welcome — keep the step numbering aligned with
  `WebsiteWatcherE2ETests.cs`.
