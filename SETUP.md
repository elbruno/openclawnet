# OpenClaw .NET Setup

This repository keeps setup guidance in the `docs/manuals` folder.

- [Prerequisites](docs/manuals/00-prerequisites.md)
- [Local installation](docs/manuals/01-local-installation.md)

Quick start:

1. Install the .NET 10 SDK and required local dependencies from the prerequisites guide.
2. Configure environment settings and secrets as described in the local installation guide.
3. Build the solution with `dotnet build OpenClawNet.slnx`.

---

## 📦 Release & Versioning

**This is a reference platform, not a NuGet package.**

- Source: Clone locally or fork for your projects
- Releases: Tag-gated on GitHub (git tag → GitHub Release workflow)
- Package versions: All dependencies pinned in `Directory.Build.props`
  - **Microsoft.Agents.AI:** 1.17.0 (Harness available; gradual adoption planned)
  - **Aspire.Hosting.Testing:** 13.4.6 (AspireHostFixture integration)
  - **.NET:** 10.0+

For detailed information on versioning, NuGet scope, and Harness adoption, see [**Release Guidance**](./docs/release/RELEASE-GUIDANCE.md).

---

## 🧪 Testing & Known Blockers

Integration tests use **AspireHostFixture** (Aspire.Hosting.Testing) for orchestration. Some tests may skip if your environment is missing optional dependencies:

- **Ollama not running** → Local LLM tests skipped
- **Docker not available** → Aspire container tests skipped
- **Playwright browsers missing** → E2E browser tests skipped
- **Azure credentials missing** → Azure provider tests skipped
- **GitHub token missing** → GitHub Copilot tests skipped
- **Port conflicts (5010+)** → Aspire tests may fail to bind

For detailed troubleshooting and mitigation steps, see [**Test Environment & Blockers**](./docs/architecture/TEST-ENVIRONMENT.md).

---

## 📚 Architecture & Migration

- **Agent Runtime:** See [Agent Runtime Architecture](./docs/architecture/agent-runtime.md)
- **Migration Notes:** Harness adoption is planned but not yet started. See [Migration Notes](./docs/migration/MIGRATION-NOTES.md)
