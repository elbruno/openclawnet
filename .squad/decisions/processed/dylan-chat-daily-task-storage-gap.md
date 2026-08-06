# Chat daily-task flow — storage target gap

**Reviewer:** Dylan (Tester)  
**Date:** 2026-05-25  
**Status:** Backend support needed for deterministic verification.

---

## Gap

The chat flow can create a recurring job with the `schedule` tool, but the current backend contract only persists job `Name`, `Prompt`, and `CronExpression`. There is no explicit field for an output storage target or default-storage path derived from the chat name.

## Impact

The E2E test can verify the job is recurring at `0 9 * * *` and that the prompt references the chat title and default storage location. It cannot, however, prove the backend saved results to a specific storage path unless that path is exposed as a first-class job setting or response field.

## Recommendation

Add an explicit storage/output field to the job creation or schedule-tool contract (for example `outputPath` or `storageKey`) if the team wants deterministic automated verification of the save location.
