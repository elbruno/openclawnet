# Decision: Package Phase 2 — MudBlazor 9.8.0, AngleSharp 1.7.1 (2026-08-06)

**Author:** Irving (Backend/Packaging Lead)
**PR:** [#212](https://github.com/elbruno/openclawnet/pull/212)
**Status:** MERGEABLE / CLEAN. Do not merge without reviewer sign-off.

---

## Decision Made

Upgraded MudBlazor (minor) and AngleSharp (patch) as the only eligible compatible
upgrades remaining from PR #208's deferred list. All other deferred items remain
deferred with the same reasons.

## AngleSharp Feed Note

At PR #208 time, `AngleSharp 1.7.1` returned NU1103 from `azure-default` feed.
As of 2026-08-06 afternoon the feed serves it. Probed with `dotnet restore` before
committing. If CI also shows NU1103, that means the feed intermittently lags — revert
to 1.7.0 and re-open the issue.

## Deferred Upgrades (still in effect)

| Package | Reason |
|---|---|
| `Azure.AI.OpenAI 2.9.0-beta.1` | GA `2.1.0` would be a feature downgrade |
| `GitHub.Copilot.SDK 0.3.0 → 1.0.9` | Major version; API change review needed |
| `ModelContextProtocol 1.3.0 → 2.1.0` | Major version; central infra |
| `SixLabors.ImageSharp 3.1.12 → 4.0.0` | Major version; known breaking API |
| `ElBruno.MarkItDotNet 0.6.1 → 0.9.1` | Pre-1.0; verify changelog before proceeding |
