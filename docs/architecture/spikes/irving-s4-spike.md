# S4 Spike — GitHub Insights → External Dashboard

**Author:** Irving (Backend Dev)
**Date:** 2026-05-06
**Status:** Spike (read-only investigation — no code changes)

## 1. Existing GitHub tool (S2)

- `src/OpenClawNet.Tools.GitHub/GitHubTool.cs:12-225`
- `src/OpenClawNet.Tools.GitHub/GitHubClientFactory.cs:6-34`
- `src/OpenClawNet.Tools.GitHub/IGitHubClientFactory.cs:1-10`
- `src/OpenClawNet.Tools.GitHub/GitHubToolServiceCollectionExtensions.cs:6-13`

`GitHubTool : ITool` is **read-only** Octokit-based, action-multiplexed (`summary`, `list_issues`, `list_pulls`, `list_commits`, `get_repo`, `get_file`). `RequiresApproval = false`.

`IGitHubClientFactory` registered as singleton via `AddGitHubTool()`; reads `GitHub:ApiBaseUrl` / `GITHUB_API_BASE_URL` in its constructor.

## 2. Tool registration pattern

- `src/OpenClawNet.Tools.Core/ToolsServiceCollectionExtensions.cs:8-20`
- `src/OpenClawNet.Gateway/Program.cs:187-238, 313-327`
- `src/OpenClawNet.Tools.Core/ToolRegistry.cs:5-22`

DI pattern: `services.AddSingleton<ITool, X>()` or concrete singleton + `ITool` projection. `AddTool<TTool>()` is the framework helper.

Tool catalog enumerated from `IToolRegistry.GetAllTools()` in `DefaultAgentRuntime` (`src/OpenClawNet.Agent/DefaultAgentRuntime.cs:190-245`) after Gateway registers every `IEnumerable<ITool>` into the registry.

## 3. Approval flow

- `src/OpenClawNet.Agent/DefaultAgentRuntime.cs:480-760`
- `src/OpenClawNet.Agent/ToolApproval/ToolApprovalCoordinator.cs:11-94`
- `src/OpenClawNet.Gateway/Endpoints/ToolApprovalEndpoints.cs:19-52`
- `src/OpenClawNet.Gateway/Endpoints/ChatStreamEndpoints.cs:101-150`

Stream coalesces `FunctionCallContent` by `CallId` (`DefaultAgentRuntime.cs:484-556`) so one logical tool call → one approval prompt. Then emits `ToolApprovalRequest`, waits on `_approvalCoordinator.RequestApprovalAsync(...)`, resolves via `POST /api/chat/tool-approval`.

`ToolApprovalCoordinator` stores pending requests + remembered approvals per session. Stream emits `tool_approval` / `tool_approval_resolved` events.

## 4. Test-dashboard target

- `docs/landing/index.html:14-18, 203-209, 238-243`
- `docs/analysis/e2e-tool-integration-gaps.md:155-193`

The dashboard is **static GitHub Pages content**. Landing page links to `./test-dashboard/`. The publish script copies files into `docs/test-dashboard/` and commits/pushes.

**Simplest viable S4 approach:** GitHub-write tool that updates/commits a JSON file (e.g. `docs/test-dashboard/metrics.json`) in the **public repo** via GitHub API. No write API or webhook exists in the dashboard itself.

This means S4's tool **mutates the public mirror repo directly** via PAT — not the plan repo. Need to think about source-of-truth implications: do we instead write to `plan repo / docs/test-dashboard/metrics.json` and let the sync workflow propagate? **Recommend the latter** — keeps the source-of-truth flip honored.

## 5. HttpClient / Polly / retry conventions

- `src/OpenClawNet.ServiceDefaults/Extensions.cs:23-51` (Aspire defaults: `AddStandardResilienceHandler()` + service discovery for all `HttpClient`s)
- `src/OpenClawNet.Gateway/Program.cs:124, 195, 204, 241-257, 334-339` (typed/named clients)

Standard pattern is Aspire service defaults: global resilience handler, configured from `Resilience` appsettings. Local tools use typed/named clients (`AddHttpClient<WebTool>()`, `AddHttpClient("shell-service", ...)`) rather than custom Polly per tool.

## 6. Configuration pattern

- `src/OpenClawNet.Gateway/Program.cs:65-156, 197, 255-256`
- `src/OpenClawNet.Agent/AgentServiceCollectionExtensions.cs:13-54`
- `src/OpenClawNet.Gateway/appsettings.json:13-68`
- `src/OpenClawNet.AppHost/appsettings.json:9-17`

Options bound via `Configure<T>(GetSection(...))` or `Configure<T>(opts => ...)`. Examples: `Model`, `Tools:Web`, `Slack`, `ToolApproval`, `SkillsImport`, `Resilience`.

GitHub Copilot options bound in code (`GitHubCopilotOptions` via `Program.cs:148-155`) — no matching `GitHubCopilot` section in `appsettings.json` shown. GitHub tool uses `GitHub:ApiBaseUrl` env/config + `GITHUB_TOKEN` secret.

## Blockers / open questions for Mark

- Should S4's write target be the **plan repo** (`docs/test-dashboard/metrics.json` → propagated by sync workflow) or the **public repo** directly? **Strong recommend: plan repo, let sync handle propagation** to keep the source-of-truth flip honored.
- Should the new dashboard tool be **MCP-first** or keep a legacy `ITool` facade for compatibility?
- What JSON schema should `metrics.json` use, and who owns the allowlist/repo/branch policy?
- The dashboard is static — does writing JSON actually re-render charts on user view, or do we also need to bump a cache-buster query string?
