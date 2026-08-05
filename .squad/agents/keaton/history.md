# Keaton's History

## 2026-04-24T16:42:56Z — Live Test Coverage Expansion Architecture (Workstream B Lead)

**Status:** ✅ Spawned (background agent, ~3min)

**Task:** Architect implementation plan for live test coverage expansion (Workstreams A & B, per-tool e2e harness).

**Contributions to live-test-planning session:**
- Designed two parallel workstreams: Irving's 3 critical tests (Workstream A) + per-tool e2e harness (Workstream B, 8 tests)
- Architected shared infrastructure: LiveTestFixture, WebApplicationFactory gateway, parameterized providers (Ollama + AOAI)
- Planned CI/CD: Manual dispatch workflow, provider selection, cost controls ($0 Ollama, $0.50–$1.00 AOAI per run)
- Created 17-todo implementation roadmap (4 phases: foundation → core flows → per-tool tests → CI/docs)
- Authored `keaton-live-test-plan.md` decision document (~300 lines, 17.5 KB)

**Cross-reference:** Irving (Backend/LLM) analyzed current live test gaps; both report to `keaton-live-test-plan.md` decision (merged to `.squad/decisions.md` 2026-04-24).

---

## 2026-05-01T14:30:00Z — PR #73 Follow-Up Scoping

**Status:** ✅ Complete

**Task:** Scope 4 follow-up items from PR #73 (Live Test Coverage Expansion).

## Learnings

### Three Factory Patterns (to be consolidated)

1. **`LiveOllamaWebAppFactory`** (line 19) — Proper `GatewayWebAppFactory` subclass with constructor params for model/endpoint. Used by FileSystem/Calculator/MarkItDown e2e. **This is the canonical pattern.**

2. **`LiveOllamaGatewayWebAppFactory`** (lines 147-200 in `LiveJobExecutionTests.cs`) — Internal sealed class, duplicates factory logic. **Should be deleted** — use `LiveOllamaWebAppFactory` instead.

3. **Inline `WithWebHostBuilder`** (in `LiveWebToolE2ETests`, `LiveHtmlQueryToolE2ETests`) — Per-class setup with 25+ lines duplicated. Creates `_liveFactory`/`_liveClient` fields. **Should migrate to override pattern** using `LiveOllamaWebAppFactory`.

### MCP Test Wiring Complexity

- `IMcpToolProvider` is registered via `McpServiceCollectionExtensions.AddMcp()` at `McpToolProvider.cs:24`
- Requires `IMcpServerCatalog` (Storage layer) + at least one MCP server running
- `InProcessMcpHost` manages server lifecycle — this is **substantial infrastructure** for a unit test fixture
- Current skip-stub in `LiveMcpToolTests.cs` is intentional; wiring MCP requires Bruno's architectural decision

### Browser/Shell Tool Architecture

- Both tools are **Aspire service proxies** (not standalone implementations)
- `BrowserTool` → `browser-service` via `IHttpClientFactory.CreateClient("browser-service")`
- `ShellTool` → `shell-service` via `IHttpClientFactory.CreateClient("shell-service")`
- These services are registered in `AppHost.cs` lines 20-25
- **E2E tests cannot work** without Aspire's service discovery — `GatewayWebAppFactory` doesn't start Aspire resources

### Embeddings Tool (standalone)

- Uses `ElBruno.LocalEmbeddings` (ONNX runtime) — no external service dependency
- E2E tests theoretically feasible but require ~2GB model download on first run
- Model cached to `_storage.ModelsPath/embeddings/`

---
