---
name: "dotnet-package-version-overrides"
description: "Use Directory.Build.targets PackageReference updates for repo-wide .NET package alignment"
domain: "dotnet-build"
confidence: "high"
source: "earned"
---

## Context

When a .NET repo mixes explicit package versions with `Version="*"` and starts failing restore with NU1605 downgrade errors, the fastest low-churn recovery is often a repo-root override rather than editing every project file.

## Pattern

- Add a `Directory.Build.targets` file at the repo root if one does not exist.
- Normalize shared package families with `PackageReference Update="Package.Id" Version="X.Y.Z"` entries.
- Prefer pinning the highest transitively required stable version across the affected build path.
- Use this for cross-repo package drift (for example `Microsoft.Extensions.*`, ASP.NET Core test packages, EF Core support packages), not for one-off project-specific dependencies.

## Why it works

`Directory.Build.targets` is imported after the individual project file items are loaded, so `PackageReference Update="..."` can override both explicit versions and wildcard references in one place. That avoids mass csproj churn and reduces the chance of future skew between sibling projects.

## Validation checklist

- Re-run the exact restore path that was failing.
- Build the affected solution or project with errors-only console logging.
- If the repo uses Aspire, verify `aspire start` succeeds after the package alignment and shut it down with `aspire stop`.

## Example

```xml
<Project>
  <ItemGroup>
    <PackageReference Update="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
    <PackageReference Update="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.8" />
  </ItemGroup>
</Project>
```

## Anti-patterns

- Editing dozens of `.csproj` files when the problem is repo-wide version skew.
- Pinning a lower direct version than a transitive dependency already requires.
- Verifying only `dotnet restore` and skipping the real build or Aspire startup path.
