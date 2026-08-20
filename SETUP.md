# OpenClaw .NET Setup

This repository keeps setup guidance in the `docs/manuals` folder.

- [Prerequisites](docs/manuals/00-prerequisites.md)
- [Local installation](docs/manuals/01-local-installation.md)

Quick start:

1. Install the .NET 10 SDK and required local dependencies from the prerequisites guide.
2. Configure environment settings and secrets as described in the local installation guide.
3. Restore and build the solution:

   ```powershell
   dotnet restore OpenClawNet.slnx
   dotnet build OpenClawNet.slnx --no-restore
   ```

For a runtime-specific project build, use the same runtime identifier for both
restore and build. Do not pass a runtime identifier when building the solution;
solution-level RID builds are not supported by the .NET SDK.

```powershell
dotnet restore OpenClawNet.slnx --runtime win-x64
dotnet build tests/OpenClawNet.UnitTests/OpenClawNet.UnitTests.csproj `
  --no-restore --runtime win-x64
```

If `NETSDK1047` appears after switching between these modes, repeat the matching
restore command with `--force-evaluate` before building.
