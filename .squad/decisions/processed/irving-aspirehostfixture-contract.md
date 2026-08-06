# Decision: AspireHostFixture Contract — Local-First E2E Execution

**Author:** Irving (Backend Dev)  
**Date:** 2026-05-25  
**Status:** READY FOR COORDINATOR SYNTHESIS  
**Scope:** `tests/OpenClawNet.PlaywrightTests/` — local-first E2E fixture design

---

## Problem Statement

The team currently has two separate Aspire integration strategies:

| Class | Mode | Start? | Stop? | Who uses it |
|---|---|---|---|---|
| `AppHostFixture` | In-process (`DistributedApplicationTestingBuilder`) | Always | Always | CI regression tests |
| `AttachedAspireTestBase` | External attach | Never | Never | Demo-live tests only |
| `PlaywrightDemoLauncher` | External probe only | Never | Never | Demo launcher |

There is no fixture that supports **local-first E2E execution**: detect whether Aspire is already running, attach if it is, start it if it isn't, and clean up only what the fixture itself started. This causes friction for developers who run E2E tests locally against a live Aspire stack.

---

## Contract: `AspireHostFixture` (revised — local-first mode)

### Core State

```csharp
public sealed class AspireHostFixture : IAsyncLifetime
{
    // True when Aspire was already running before InitializeAsync.
    // Used to gate teardown — we never stop what we didn't start.
    private bool _aspireWasPreExisting;

    // True when fixture successfully started Aspire itself.
    private bool _startedByFixture;

    // Aspire child process when _startedByFixture == true.
    private Process? _aspireProcess;

    // Playwright resources (always owned by this fixture).
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    // Resolved endpoints (set in InitializeAsync, consumed by tests).
    public string WebBaseUrl { get; private set; } = string.Empty;
    public string GatewayBaseUrl { get; private set; } = string.Empty;
    public string SchedulerBaseUrl { get; private set; } = string.Empty;

    // True when all resources are up and Playwright is ready.
    public bool IsReady { get; private set; }

    // Human-readable reason when IsReady == false.
    public string? StartupSkipReason { get; private set; }

    public IBrowser Browser =>
        _browser ?? throw new InvalidOperationException("Fixture not initialized");
}
```

---

## Detection Strategy

### Step 1 — Probe `aspire describe`

Run `aspire describe --format Json` with a **30-second timeout** (matches existing pattern in `AttachedAspireTestBase` and `PlaywrightDemoLauncher`).

Extract `resources[]` → find entries whose `displayName` matches `web`, `gateway`, `scheduler`. Prefer `https://` URLs; fall back to `http://`.

**Pre-existing = running** when all three resources have valid, non-empty URLs AND HTTP health checks pass (see Step 3).

**Robustness rules for `aspire describe` parsing:**

- Strip any non-JSON prefix/suffix before the outer `{` / `}` — the Aspire CLI occasionally emits startup banners before the JSON.
- Tolerate missing `scheduler` resource gracefully: set `SchedulerBaseUrl = string.Empty` and continue; scheduler-dependent tests use `Skip.IfNot(SchedulerBaseUrl != string.Empty, ...)`.
- If JSON parse fails entirely, treat as "not running" (do not throw).

### Step 2 — Env-var override (escape hatch)

If `OPENCLAW_WEB_URL`, `OPENCLAW_GATEWAY_URL` are both set to non-empty non-placeholder values, skip `aspire describe` entirely and use those URLs directly. Set `_aspireWasPreExisting = true`, `_startedByFixture = false`.

This matches `AttachedAspireTestBase.ResolveAspireAsync` and gives CI environments a clean injection point.

### Step 3 — HTTP health check

After resolving URLs (from describe or env vars), probe `{url}/health` for `web`, `gateway`, and `scheduler` with:
- Individual request timeout: 5 seconds
- Overall wait deadline: **2 minutes** (matches existing `WaitForEndpointReadyAsync` in `AppHostFixture`)
- Poll interval: 1 second

Only declare Aspire "running" after health checks pass. This prevents attaching to an Aspire instance that reported `Running` state but whose HTTP listeners aren't ready yet.

---

## Start-Only-When-Down

```
if (aspireIsPreExisting) {
    _aspireWasPreExisting = true;
    // Skip to Playwright init
} else {
    StartAspireProcess();
    _startedByFixture = true;
    PollUntilDescribeReturnsResources(timeout: 3 minutes);
    PollHealthEndpoints(timeout: 2 minutes);
}
```

### Starting Aspire

Use `Process.Start` with `aspire start src\OpenClawNet.AppHost` from the repo root.

- `UseShellExecute = false`
- `RedirectStandardOutput = true` / `RedirectStandardError = true`
- Do **not** kill the process directly in error paths — issue `aspire stop` instead.

**Poll loop after start:**

```
deadline = now + 3 minutes
interval = 5 seconds

while (now < deadline) {
    result = await RunAspireDescribeAsync(timeout: 30s);
    urls = ParseUrls(result);
    if (urls.AllPresent) break;
    await Task.Delay(interval);
}

if (!urls.AllPresent) throw TimeoutException("Aspire did not surface resources within 3 minutes.");
```

Expose progress to console: `[AspireHostFixture] Waiting for Aspire resources... (elapsed: 45s)` on each poll tick.

---

## Ownership Boundaries

```
┌─────────────────────────────────────────────────────────────────┐
│  AspireHostFixture (this contract)                              │
│                                                                 │
│  Owns:                                                          │
│  ● aspire describe probe + URL resolution                       │
│  ● aspire start (conditional)                                   │
│  ● aspire stop (conditional — only if _startedByFixture)        │
│  ● HTTP health wait                                             │
│  ● Playwright browser lifecycle (IBrowser)                      │
│  ● Orphaned node process cleanup (pre-init + post-dispose)      │
│                                                                 │
│  Does NOT own:                                                  │
│  ● IPage lifecycle (owned by test base or individual test)      │
│  ● Timing presets / SlowMo (env vars, not fixture state)        │
│  ● Test catalog or filter selection (launcher concern)          │
│  ● aspire stop when Aspire was pre-existing                     │
└─────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────┐
│  AppHostFixture (unchanged — CI mode)      │
│                                            │
│  Owns:                                     │
│  ● In-process DistributedApplication       │
│  ● Always starts / always stops            │
│  ● No external aspire process involved     │
└────────────────────────────────────────────┘

┌────────────────────────────────────────────┐
│  AttachedAspireTestBase (unchanged)        │
│                                            │
│  Owns:                                     │
│  ● Demo-only attach                        │
│  ● Never starts / never stops Aspire       │
│  ● IPage lifecycle (one page per class)    │
└────────────────────────────────────────────┘

┌────────────────────────────────────────────┐
│  PlaywrightDemoLauncher (unchanged)        │
│                                            │
│  Owns:                                     │
│  ● dotnet test invocation                  │
│  ● Timing preset prompt                    │
│  ● Pre/post node process cleanup           │
│  ● Aspire readiness gate (abort if down)   │
│  ● Does NOT start or stop Aspire           │
└────────────────────────────────────────────┘
```

---

## Stop / Teardown Contract

```csharp
public async Task DisposeAsync()
{
    // 1. Close browser first — eliminates Playwright node processes cleanly.
    if (_browser is not null) await _browser.CloseAsync();
    _playwright?.Dispose();

    // 2. Clean up any lingering node/browser processes we may have spawned.
    CleanupOrphanedPlaywrightNodeProcesses(_fixtureStartedAt);

    // 3. Stop Aspire ONLY if this fixture started it.
    if (_startedByFixture)
    {
        await StopAspireAsync(timeout: TimeSpan.FromSeconds(30));
    }
    // If _aspireWasPreExisting == true: leave Aspire running. Do not touch it.
}
```

### `StopAspireAsync` implementation

- Run `aspire stop` via `Process.Start` (not `_aspireProcess.Kill`).
- Apply the SKILL.md rule: **always use `aspire stop`, never Ctrl+C or `Kill`**.
- 30-second timeout; if `aspire stop` itself times out, emit a warning to console but do not throw.
- After stop, attempt to drain the `_aspireProcess` stdout/stderr to avoid handle leak.

---

## Failure Handling

| Failure mode | Handling |
|---|---|
| `aspire describe` times out | Treat as "not running"; proceed to start |
| `aspire describe` JSON malformed | Treat as "not running"; proceed to start |
| `aspire start` process fails to launch | `IsReady = false`, `StartupSkipReason = ...`, do not re-throw |
| Resources don't surface within 3 minutes | `IsReady = false`, `StartupSkipReason = ...` |
| HTTP health check times out (2 min) | `IsReady = false`, `StartupSkipReason = ...` |
| Playwright init fails | Close any open browser, `IsReady = false`, `StartupSkipReason = ...` |
| `SkipException` thrown from inner code | Re-throw unchanged (standard xUnit skip passthrough) |

All failure paths must:
1. Log to `Console.WriteLine($"[AspireHostFixture] ...")` (never `Debug.WriteLine`; test output capture picks up Console).
2. Set `IsReady = false` and `StartupSkipReason` before returning — never throw from `InitializeAsync`.
3. Call `DisposeAsync` cleanup steps that are safe to run (Playwright close, node cleanup).

Tests gate on `IsReady` via:
```csharp
Skip.IfNot(fixture.IsReady, fixture.StartupSkipReason ?? "Fixture not ready");
```

---

## Timeout Table

| Operation | Timeout | Rationale |
|---|---|---|
| `aspire describe` probe | 30 s | Matches existing pattern in `AttachedAspireTestBase` and `PlaywrightDemoLauncher` |
| Wait for resources after `aspire start` | 3 min | Covers cold Docker pull + build scenarios on dev machines |
| HTTP health endpoint warm-up | 2 min | Matches existing `WaitForEndpointReadyAsync` in `AppHostFixture` |
| `aspire stop` | 30 s | CLI graceful shutdown; warn+continue on timeout |
| Playwright browser launch | 30 s (default Playwright) | No override needed |

---

## Process Hygiene

### Node/browser process cleanup

Mirror the `PlaywrightDemoLauncher.CleanupOrphanedPlaywrightNodeProcesses` pattern:

- Record `_fixtureStartedAt = DateTime.UtcNow` at the top of `InitializeAsync`.
- On `DisposeAsync`, kill only node processes whose `StartTime > _fixtureStartedAt - 5s` AND whose `MainModule.FileName` contains `\playwright-driver-cache\` or `\.playwright\`.
- Never use name-based blanket kill (`Get-Process node | Stop-Process`).
- Each kill uses explicit `nodeProcess.Kill(entireProcessTree: true)` — matches launcher pattern.

### Aspire child process hygiene

- If `_startedByFixture`: call `aspire stop` (CLI), then `await _aspireProcess.WaitForExitAsync(CancellationToken)` with 10s timeout.
- If `WaitForExitAsync` times out: log warning, call `_aspireProcess.Kill(entireProcessTree: true)` as last resort only.
- Never use `taskkill /IM` or name-based blanket kills per the aspire-lifecycle SKILL.md rule.

---

## Phased Implementation Notes

### Phase 1 — Aspire detection + URL resolution (extract + harden)

Extract `TryResolveUrlsFromDescribeAsync` from `AttachedAspireTestBase` into a shared static helper class `AspireDescribeResolver`. Both `AttachedAspireTestBase` and the new fixture call it.

Hardening additions:
- Strip non-JSON prefix/suffix (banner lines).
- Prefer `https://` over `http://` URLs.
- Tolerate missing `scheduler`.
- Validate resolved URLs are parseable `Uri` instances before returning.

**Output:** `AspireDescribeResolver` static class + unit tests.

### Phase 2 — Conditional start with ownership flag

Add `AspireHostFixture` as a new class (does not replace `AppHostFixture`). Implement:
- `DetectOrStartAspireAsync()` method with `_startedByFixture` flag.
- Poll loop using `AspireDescribeResolver`.
- Console progress ticks.

**Output:** New `AspireHostFixture.cs` with Phase 1 dependency.

### Phase 3 — Conditional stop in DisposeAsync

Implement `StopAspireAsync()` gated on `_startedByFixture`. Wire into `DisposeAsync`.

**Output:** Teardown path in same file.

### Phase 4 — Browser + node process hygiene

Port `CleanupOrphanedPlaywrightNodeProcesses` from `PlaywrightDemoLauncher` into a shared helper (e.g., `PlaywrightProcessHygiene` static class in the test project).

Both `AspireHostFixture` and `PlaywrightDemoLauncher` call it. No logic duplication.

**Output:** `PlaywrightProcessHygiene` static helper + wired into both callers.

---

## Files Affected

| File | Change |
|---|---|
| `tests/OpenClawNet.PlaywrightTests/AspireHostFixture.cs` | NEW — this contract |
| `tests/OpenClawNet.PlaywrightTests/AspireDescribeResolver.cs` | NEW — extracted/hardened from `AttachedAspireTestBase` |
| `tests/OpenClawNet.PlaywrightTests/PlaywrightProcessHygiene.cs` | NEW — extracted from `PlaywrightDemoLauncher` |
| `tests/OpenClawNet.PlaywrightTests/Demos/AttachedAspireTestBase.cs` | Minor — call `AspireDescribeResolver` instead of inline |
| `src/OpenClawNet.PlaywrightDemoLauncher/Program.cs` | Minor — call `PlaywrightProcessHygiene` instead of inline |
| `tests/OpenClawNet.PlaywrightTests/AppHostFixture.cs` | No change — CI path unaffected |

---

## Constraints Honoured

- **aspire-lifecycle SKILL.md:** Never `Kill()` Aspire directly; always `aspire stop` CLI; `Kill` only as last resort after 10s drain timeout.
- **windows-compatibility SKILL.md:** Filenames safe (no colons); process kills by explicit PID; paths via `Path.Combine`.
- **decisions.md 2026-05-11:** `aspire describe` first → start only if missing → stop only what we started.
- **decisions.md (launcher thin scope):** Launcher keeps no Aspire lifecycle changes; hygiene extracted to shared helper.

---

## Open Questions for Coordinator

1. **Class name:** `AspireHostFixture` vs `LocalAspireFixture` vs `HybridAspireFixture`? Suggest `AspireHostFixture` — mirrors existing `AppHostFixture` naming convention.
2. **Scheduler tolerance:** Treat missing `scheduler` resource as warning (skip scheduler-dependent tests) or hard failure? Recommend: warning + `Skip.IfNot`.
3. **xUnit collection fixture scope:** Should `AspireHostFixture` be `ICollectionFixture` (shared across test classes) or `IClassFixture` (per class)? Recommend `ICollectionFixture` to match `AppHostFixture` pattern and avoid double-starting Aspire.
4. **`aspire start` working directory:** Repo root (matches launcher pattern) or AppHost project directory? Recommend repo root — `aspire start src\OpenClawNet.AppHost`.
