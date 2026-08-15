# Release Guidance

**Last Updated:** 2026-08-06  
**Current Stable:** 674dbbd (feat: replacement for PR #205)  
**API Status:** Stable (IAgentOrchestrator, streaming, tools, skills)

## Scope

### ✅ In Scope
- **Source code:** Maintained on GitHub at https://github.com/elbruno/openclawnet
- **GitHub Releases:** Tag-gated (git tag → GitHub Release created automatically via workflow)
- **Package versions:** Pinned in `Directory.Build.props` and per-project `.csproj` files
  - **Microsoft.Agents.AI:** 1.17.0 (with Harness availability)
  - **Aspire.Hosting.Testing:** 13.4.6 (for AspireHostFixture integration)
  - **.NET:** 10.0+
- **Testing:** Unit, Integration, Playwright E2E with environment-dependent blockers documented
- **Documentation:** Public README, setup guides, architecture docs, manuals, demos

### ❌ Out of Scope
- **NuGet Publishing:** OpenClaw packages are **NOT** published to nuget.org. This is a sample/reference platform for learning Aspire + AI Agents + .NET 10. Users should clone and build locally or fork for their own scenarios.
- **Internal Package Feeds:** No private feed integration.
- **Pre-release Channels:** No alpha/beta NuGet feeds.

---

## GitHub Release Process (Tag-Gated)

### Trigger
Create an annotated git tag on `main`:

```bash
git tag -a v1.0.0 -m "Release: v1.0.0 - Initial stable release"
git push origin v1.0.0
```

### Workflow
`.github/workflows/release.yml` is configured to:
1. Listen for `push` events on tags matching `v*.*.*`
2. Extract version from tag name
3. Generate release notes from commit history since previous tag
4. Create GitHub Release with:
   - Release title: "Release: v1.0.0"
   - Release body: Auto-generated changelog
   - Asset: None (source-only release)

### Outcome
- GitHub Release created at https://github.com/elbruno/openclawnet/releases/tag/v1.0.0
- No NuGet package published
- No automatic deployment triggered

---

## Package Versions (Current)

### Core Runtime
| Package | Version | Scope | Notes |
|---------|---------|-------|-------|
| `Microsoft.Agents.AI` | 1.17.0 | Agent Framework | Harness available; no full migration yet |
| `Microsoft.Agents.Core` | 1.7.129 | AI Models | Stable |
| `Microsoft.Extensions.AI` | 10.8.3 | Model abstraction | Stable |

### .NET & Framework
| Package | Version | Scope | Notes |
|---------|---------|-------|-------|
| **.NET SDK** | 10.0+ | Runtime | Required |
| `Aspire.Hosting.Testing` | 13.4.6 | Test orchestration | AspireHostFixture integration |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.10 | Web testing | Stable |

### Testing
| Package | Version | Scope | Notes |
|---------|---------|-------|-------|
| `xunit` | 2.9.3 | Test framework | Stable |
| `Xunit.SkippableFact` | 1.5.61 | Conditional tests | Used for environment-dependent blockers |
| `Microsoft.Playwright` | 1.61.0 | E2E browser | Requires Playwright browsers |
| `WireMock.Net` | 2.13.0 | HTTP mocking | Tool test isolation |

### Domain
| Package | Version | Scope | Notes |
|---------|---------|-------|-------|
| `OllamaSharp` | 5.4.30 | Local LLM client | Ollama provider |
| `Aspire.Hosting` | 13.4.6 | Orchestration | AppHost & container management |

See `Directory.Build.props` and individual `.csproj` files for the complete dependency tree.

---

## Harness & AspireHostFixture Terminology

### `AspireHostFixture` (Current)
The primary test fixture for integration testing in OpenClaw:

```csharp
// Usage pattern (Aspire.Hosting.Testing)
public class ChatTests : IAsyncLifetime
{
    private AspireHostFixture _fixture;

    public async Task InitializeAsync()
    {
        _fixture = await AspireHostFixture.BuildAsync();
        // _fixture.AppHost is initialized and running
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task ChatEndpoint_ReturnsOk()
    {
        var client = _fixture.CreateHttpClient("gateway");
        var response = await client.PostAsync("/api/chat", ...);
        Assert.NotNull(response);
    }
}
```

**Location:** `tests/OpenClawNet.IntegrationTests/`  
**Status:** ✅ Active, stable  
**Microsoft.Agents.AI Integration:** Framework available in 1.17.0; gradual adoption as Harness patterns solidify.

### "Harness" (Microsoft.Agents.AI)
The **Harness** is the newer agent execution abstraction in `Microsoft.Agents.AI 1.17.0+`. It provides:
- Unified tool invocation pipeline
- Built-in approval flow
- Streaming event model
- Multi-turn orchestration

**Status:** 🟡 Available but **NOT fully migrated**. Current implementation uses `ChatClientAgent` + `IAIContextProvider` pattern. Harness adoption is planned for future phases.

**Why No Migration Yet:**
- Existing `DefaultAgentRuntime` + `ModelClientChatClientAdapter` pattern is stable and battle-tested
- Harness migration requires careful refactoring of streaming, tool approval, and skill injection
- Will target next major release or sprint

---

## Preserved Behaviors

### HTTP Approval Flow
✅ **Tool approval via HTTP** is preserved:
- When tool requires approval, agent pauses and returns `{ "type": "tool_approval_required", "toolName": "..." }`
- Client displays approval UI (optional)
- Client POSTs approval decision back to endpoint
- Execution resumes

**Files:** `src/OpenClawNet.Gateway/Endpoints/ToolApprovalEndpoints.cs`

### Streaming (NDJSON)
✅ **HTTP NDJSON streaming** is preserved:
- Each token, tool start/end, and completion is a separate JSON line
- Browser receives tokens in real-time with low latency
- Blazor Chat.razor processes events as they arrive
- No framing overhead

**Files:** `src/OpenClawNet.Gateway/Endpoints/ChatStreamEndpoints.cs`

### Persistence (SQLite + Storage)
✅ **Conversation & session persistence** is preserved:
- All messages stored in SQLite (default) or Azure SQL (cloud)
- Session history is queryable and resumable
- Skills, tools, and preferences are persistent

**Files:** `src/OpenClawNet.Storage/`, `src/OpenClawNet.Storage.Azure/`

---

## Known Environment-Dependent Test Blockers

Tests tagged with `[SkippableFact]` or conditional logic (using `Xunit.SkippableFact`) are skipped if environment requirements are not met:

| Blocker | Impact | Trigger | Mitigation |
|---------|--------|---------|-----------|
| **Ollama not running** | Local model tests | Missing `ollama serve` | `docker run ollama/ollama serve` or `ollama serve` in terminal |
| **Docker not available** | Aspire container tests | No Docker Desktop | Install Docker Desktop; for Linux, ensure Docker daemon is running |
| **Playwright browsers missing** | E2E browser tests | First-run Playwright | `pwsh -Command { & "$env:USERPROFILE\.playwright\install.ps1" }` or `playwright install` |
| **Azure subscription missing** | Azure provider tests | No credentials | Skipped by default; requires `AZURE_SUBSCRIPTION_ID` + `AZURE_CLIENT_ID` + `AZURE_CLIENT_SECRET` + `AZURE_TENANT_ID` |
| **GitHub Copilot auth missing** | GitHub Copilot provider tests | No GitHub token | Skipped by default; requires `GITHUB_TOKEN` with Copilot scope |
| **Port conflicts (5010, 5011, etc.)** | Aspire AppHost binding | Process already listening | Check `netstat -ano -p tcp` (Windows) or `lsof -i :5010` (Unix) |
| **Playwright timing flake** | Browser automation race | Environment slowness | Increased timeout thresholds in `xunit.runner.json` |

### How Tests Handle Blockers

```csharp
// Example: Ollama-dependent test
[SkippableFact]
public async Task OllamaProvider_WithLocalModel_Responds()
{
    Skip.IfNot(Environment.GetEnvironmentVariable("OLLAMA_AVAILABLE") == "true", 
               "Ollama service not available");
    
    // Test code...
}
```

**Configuration:** `tests/OpenClawNet.IntegrationTests/xunit.runner.json` controls skip messages and retry logic.

---

## Testing & Validation

### Local Build & Test
```bash
# Prerequisites: .NET 10 SDK, Docker Desktop, Ollama
dotnet build OpenClawNet.slnx
dotnet test tests/OpenClawNet.IntegrationTests --no-build

# With Playwright E2E
dotnet test tests/OpenClawNet.PlaywrightTests --no-build

# All tests
dotnet test --no-build
```

### CI/CD
- **GitHub Actions:** Runs on every push and PR to `main`
- **Test matrix:** Windows, macOS, Linux (if configured)
- **Skips:** Azure and GitHub Copilot tests in CI (credentials not available)

---

## Documentation & Migration

### Public Docs (No Breaking Changes)
- `README.md` — Updated with release link and version badge
- `SETUP.md` — Points to prerequisites, installation, and first chat
- `docs/architecture/agent-runtime.md` — Current architecture (default runtime + AspireHostFixture)
- `docs/manuals/` — Step-by-step guides remain stable

### Harness Adoption (Future)
- When migration begins, a new `docs/migration/HARNESS-MIGRATION.md` will be created
- Will document:
  - Phased approach
  - API changes (if any)
  - Testing strategy
  - Rollback plan

---

## Support & Questions

- **Issues:** https://github.com/elbruno/openclawnet/issues
- **Discussions:** https://github.com/elbruno/openclawnet/discussions
- **Discord:** [Microsoft Foundry Community](https://aka.ms/ai-discord/dotnet) (.NET channel)

---

## Version History

| Release | Date | Notes |
|---------|------|-------|
| v1.0.0 | 2026-08 | Stable; tag-based release; NuGet out of scope; Harness available but not migrated |
