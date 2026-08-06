# Decision: Package Stabilization — Wildcard Pin Strategy (2026-08-06)

**Author:** Irving (Backend/Packaging Lead)  
**PR:** [#208](https://github.com/elbruno/openclawnet/pull/208)  
**Status:** Ready for merge

---

## Decision Made

**Pinned all 27 `Version="*"` wildcards to latest stable; deferred 7 upgrades explicitly.**

All wildcard references are now deterministic. No speculative or major-version upgrades were included.

---

## Deferred Upgrades — Reasons

| Package | Current → Available | Reason | Action Needed |
|---|---|---|---|
| `GitHub.Copilot.SDK` | `0.3.0 → 1.0.9` | Major version (0.x → 1.x) | API review; separate PR |
| `ModelContextProtocol` | `1.3.0 → 2.1.0` | Major version; central MCP infra | Dedicated upgrade PR |
| `SixLabors.ImageSharp` | `3.1.12 → 4.0.0` | Major version; known breaking API | Breaking change analysis needed |
| `Azure.AI.OpenAI` | `2.9.0-beta.1 → GA 2.1.0` | GA is a feature downgrade; beta has newer API surface | Intentional — leave as-is until GA catches up |
| `AngleSharp` | `1.7.0 → 1.7.1` | Not available on `azure-default` feed (NU1103) | Re-check when feed is updated |
| `MudBlazor` | `9.7.0 → 9.8.0` | Minor UI bump; no tests failing | Low priority; can be batched |
| `ElBruno.MarkItDotNet` | `0.6.1 → 0.9.1` | Pre-1.0; possible breaking changes | Verify changelog before upgrading |

---

## Key Technical Decision

**Feed validation is mandatory before committing version bumps.**  
The `azure-default` private NuGet feed does not always mirror nuget.org immediately. `AngleSharp 1.7.1` exists on nuget.org but caused NU1103 on the actual restore. The correct workflow: edit → restore → if NU1103, revert that package only.

---

## Build Commands (for future PRs touching these packages)

```powershell
dotnet restore tests/OpenClawNet.UnitTests --runtime win-x64
dotnet build tests/OpenClawNet.UnitTests -r win-x64 --no-restore -p:CopilotSkipCliDownload=true
dotnet test tests/OpenClawNet.UnitTests -r win-x64 --no-build -p:CopilotSkipCliDownload=true --filter "FullyQualifiedName~PackageVersionRegressionTests"
```
