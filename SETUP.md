# OpenClaw .NET Setup

This repository keeps setup guidance in the `docs/manuals` folder.

- [Prerequisites](docs/manuals/00-prerequisites.md)
- [Local installation](docs/manuals/01-local-installation.md)
- [Windows Playwright Setup](docs/SETUP-PLAYWRIGHT-WINDOWS.md) - Fix for binary blocking issues

Quick start:

1. Install the .NET 10 SDK and required local dependencies from the prerequisites guide.
2. Configure environment settings and secrets as described in the local installation guide.
3. **On Windows:** If you encounter Playwright binary blocking issues, see [Windows Playwright Setup](docs/SETUP-PLAYWRIGHT-WINDOWS.md)
4. Build the solution with `dotnet build OpenClawNet.slnx`.
