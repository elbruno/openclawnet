# Dylan inbox — headed attached-demo rerun rule

## Proposed decision

For attached Playwright demos that target an already-running Aspire app:

1. prebuild the Playwright test project before starting or attaching to Aspire,
2. run the demo with `dotnet test --no-build --no-restore`,
3. and treat `data-testid="assistant-message-complete"` as a hidden DOM sentinel that must be awaited with `WaitForSelectorState.Attached`.

## Why

- Rebuilding while attached to a live Aspire graph caused repeatable DLL copy/file-lock failures.
- The hidden assistant completion marker never becomes visible, so `Visible` waits create false timeouts even when the response is complete.
- Using the no-build attached-demo flow made the headed Chromium window visible again, which is the desired presenter/demo experience.

## Evidence

- 2026-05-22 BrowseAndSchedule rerun launched visible Chromium successfully after switching to prebuild + `--no-build --no-restore`.
- The previous timeout on `assistant-message-complete` was removed by waiting for `Attached`.
- The remaining failure moved to a genuine runtime `HTTP 401`, proving the browser-startup/waiting path was no longer the active blocker.
