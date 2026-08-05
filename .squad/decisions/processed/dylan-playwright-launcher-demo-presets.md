# Review: Spectre.Console launcher for Playwright demo runs

**Reviewer:** Dylan (Tester)  
**Date:** 2026-05-25

## Recommendation

Approve only as a thin preset launcher over existing demo contracts.

## Constraints

- Use existing metadata, not new launcher-specific tags:
  - demo flow = `DemoLive`
  - normal UI E2E = `Category=E2E`
  - tool-heavy cases still honor `ToolApproval` / `RequiresModel`
  - demo tests stay in `tests\OpenClawNet.PlaywrightTests\Demos\` and follow `*AttachedTests`
- Keep these as presets, not free-form:
  - pacing: fast / default / slow / recording
  - headed vs demo-visible mode
  - attached-demo vs standard regression suite
- Allow only narrow free-form overrides:
  - URLs / ports when Aspire describe differs
  - optional advanced slowmo value
  - explicit test filter only behind an advanced/escape hatch
- Preserve visible step-by-step execution:
  - launcher should mirror step order, not replace test flow
  - each step needs live status text and the last successful step
  - headed mode must remain the default for demos
- Surface failure modes cleanly:
  - Aspire not ready / missing resources
  - Playwright startup or driver issues
  - hidden-marker wait mismatch (`Attached` vs `Visible`)
  - auth/config problems like 401 / missing model creds
  - test assertion failure vs environment skip must be distinct

## Notes

- Do not move Aspire lifecycle ownership into the launcher.
- Do not change CI/regression suite behavior.
- Keep repeatability higher than configurability.
