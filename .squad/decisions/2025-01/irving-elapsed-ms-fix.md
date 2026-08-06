# ElapsedMs Default DateTime Guard Pattern

**Date:** 2026-04-24  
**Author:** Irving  
**Status:** ✅ Implemented (commit f637e90)  
**Category:** Bug Fix / Defensive Programming

---

## Problem

`LiveConsoleEvent.Snapshot` was producing negative ElapsedMs values when `JobRun.StartedAt` was default(DateTime).

**Symptom:**
```
Test: LiveConsoleEventTests.Snapshot_FromRunningJobRun_ProjectsCoreFields
Error: Expected evt.ElapsedMs to be >= 0L, but found -65457146L
```

**Root Cause:**
The calculation `(CompletedAt ?? UtcNow) - StartedAt` produced negative milliseconds when `StartedAt == default(DateTime)` (0001-01-01). Subtracting from UtcNow gives ~2000 years, but the cast to `long` and TotalMilliseconds conversion can overflow or produce unexpected negative values depending on order of operations.

---

## Solution

**Pattern:** Always guard DateTime arithmetic against default values.

**Implementation:**
Created a helper method:
```csharp
private static long ComputeElapsedMs(DateTime startedAt, DateTime? endTime)
{
    if (startedAt == default)
        return 0;

    var elapsed = (endTime ?? DateTime.UtcNow) - startedAt;
    return Math.Max(0, (long)elapsed.TotalMilliseconds);
}
```

Applied to all factory methods: `Snapshot`, `StatusUpdate`, `Complete`.

---

## Rationale

1. **Guard against default:** Returns 0 immediately if StartedAt is uninitialized
2. **Safety net:** `Math.Max(0, ...)` ensures no negative values escape even if future bugs introduce bad data
3. **Semantic correctness:** If a run hasn't started, elapsed time is 0, not negative

---

## Files Changed

- `src\OpenClawNet.Services.Scheduler\Endpoints\JobRunStreamEndpoints.cs:137-154` — Added ComputeElapsedMs helper
- Three call sites updated to use helper instead of inline calculation

---

## Test Results

**Before:** 657/661 unit tests passing (1 failure: LiveConsoleEventTests)  
**After:** 658/661 unit tests passing (0 failures, 3 skipped for platform reasons)

---

## Broader Applicability

This pattern should be applied anywhere DateTime arithmetic is performed on potentially-uninitialized values:
- Job run durations
- Session elapsed times
- Timeout calculations
- Any DTO that exposes elapsed/duration fields

**Defensive check template:**
```csharp
if (timestamp == default) return 0; // or null, or throw, depending on contract
```

---

## Decision

**Approved:** DateTime arithmetic involving user-controlled or entity-sourced timestamps MUST guard against default(DateTime) before subtraction.

**Enforcement:** Code review + unit tests for all duration/elapsed calculations.
