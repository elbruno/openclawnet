# Irving — Package upgrade build fix

**Date:** 2026-05-22T17:30:54.290-04:00
**Owner:** Irving

## Decision

Use repo-root package overrides in `C:\src\openclawnet\Directory.Build.targets` to align shared .NET package families instead of editing dozens of individual `.csproj` files.

## Why

- The failing restore path was caused by version skew, not missing package references.
- This repo mixes explicit versions with `Version="*"` references, so per-project edits would create unnecessary churn and leave the drift easy to reintroduce.
- `Directory.Build.targets` lets us pin the shared families once, after project items load, which is the safest place to normalize versions across the solution.

## Applied versions

- `Aspire.Hosting.Testing` → `13.2.4`
- `Microsoft.AspNetCore.*` test/runtime packages implicated in the build path → `10.0.8`
- `Microsoft.EntityFrameworkCore.*` packages used by storage/tests → `10.0.8`
- `Microsoft.Extensions.*` packages involved in the downgrade chain → `10.0.8`
- `Microsoft.Playwright` → `1.52.0` repo-wide for consistency

## Validation

- `dotnet restore tests\OpenClawNet.PlaywrightTests\OpenClawNet.PlaywrightTests.csproj -v minimal`
- `dotnet build OpenClawNet.slnx -v minimal '-clp:ErrorsOnly;Summary'`
- `dotnet build src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj -v minimal '-clp:ErrorsOnly;Summary'`
- `aspire start --apphost C:\src\openclawnet\src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj`
- `aspire stop`
