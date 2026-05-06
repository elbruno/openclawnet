# S5 Spike — Google Workspace Integration (Gmail + Calendar)

**Author:** Petey (Agent Platform Specialist)
**Date:** 2026-05-06
**Status:** Spike (read-only investigation — no code changes)

## 1. NuGet packages & .NET 10 compatibility

Use latest stable Google APIs packages on NuGet:

- `Google.Apis.Gmail.v1`
- `Google.Apis.Calendar.v3`
- `Google.Apis.Auth`
- `Google.Apis.Auth.AspNetCore3` (only if web flow is chosen — see §2)

All target `netstandard2.0`, so net10.0 consumes them cleanly. No repo-side net10 issues
discovered during the spike.

## 2. OAuth 2.0 flow choice

**Recommendation: installed-app loopback flow** (`http://127.0.0.1:<random-port>` redirect).

| Flow | Pros | Cons |
|------|------|------|
| Installed-app loopback | Best UX for desktop-style single-user Blazor Server; no public callback URL needed; PKCE built-in | Requires a desktop browser handoff for consent |
| Web server flow | Standard for multi-tenant SaaS | Requires a stable public redirect URI (not appropriate for v1 single-user) |

Consent screen lives at `https://accounts.google.com/o/oauth2/auth` — Google handles UI; our app only configures the OAuth client + scopes.

## 3. Token storage design — `IOAuthTokenStore`

Sits next to `OpenClawNet.Storage`. Provider-agnostic.

**Schema:**

| Column | Sensitive | Notes |
|--------|-----------|-------|
| `provider` | no | e.g. `google` |
| `user_id` | no | local user identifier (single-user v1 = `default`) |
| `access_token` | **yes** | encrypted at rest |
| `refresh_token` | **yes** | encrypted at rest |
| `expires_at` | no | UTC epoch |
| `scopes` | no | space-separated |
| `obtained_at` | no | UTC epoch |

**Encryption (v1):** Reuse the existing `DpapiSecretStore` / DataProtection-backed `SecretsStore` patterns already in the repo (`src/OpenClawNet.Mcp.Core/DpapiSecretStore.cs`, `src/OpenClawNet.Storage/SecretsStore.cs`). Clean seam (`IOAuthTokenProtector`) for swapping in Azure Key Vault later.

→ See `docs/security/s5-oauth-checklist.md` (Drummond) for hardening requirements.

## 4. MAF/MCP tool shape

Existing pattern (anchor: `src/OpenClawNet.Tools.GitHub/`, confirmed by Irving's spike):

- `IGitHubClientFactory` (interface, line ~1–15)
- `GitHubClientFactory : IGitHubClientFactory` (concrete, line ~6–34)
- `GitHubTool` (tool facade, line ~12–225) — exposes `ITool`, takes the factory via DI
- Registered via `services.AddSingleton<ITool, X>()` and projected through `IToolRegistry`

**S5 tools mirror this:**

- `IGoogleWorkspaceClientFactory` returning `GmailService` / `CalendarService`
- `GmailSummarizeTool` and `CalendarCreateEventTool` as facades
- Registered via `AddGoogleWorkspaceTools()` extension

## 5. Per-tool approval

Both S5 tools touch personal data. Calendar one sends real invites — they MUST go through the existing approval gate.

**Reusable, no new infrastructure needed:**

- `ToolMetadata.RequiresApproval = true` (`src/OpenClawNet.Tools.Abstractions/ToolMetadata.cs:5-13`)
- Coordinator: `src/OpenClawNet.Agent/ToolApproval/ToolApprovalCoordinator.cs:11-94`
- FunctionCallContent CallId coalescing: `src/OpenClawNet.Agent/DefaultAgentRuntime.cs:484-556` (Irving's spike)

**New UX ask for `CalendarCreateEventTool`:** the approval prompt should render meeting details (subject, start time, duration, attendee list) as a structured preview, not raw JSON. Helly should add a calendar-event template to the existing approval-prompt component.

## 6. Hermetic testing

Same factory inversion that worked for GitHub:

- Tests inject `Mock<IGoogleWorkspaceClientFactory>` returning fake service instances
- Real network never touched in CI
- WireMock optional for HTTP-level fidelity, but factory mocking is the default

## 7. Scope minimization

| Tool | Scope | Purpose |
|------|-------|---------|
| `GmailSummarizeTool` | `https://www.googleapis.com/auth/gmail.readonly` | Read unread messages, snippets, headers — never send / modify |
| `CalendarCreateEventTool` | `https://www.googleapis.com/auth/calendar.events` | Create events on user's primary calendar with invitees |

Explicitly **avoid** broader scopes:
- ❌ `https://mail.google.com/` (full mailbox)
- ❌ `https://www.googleapis.com/auth/calendar` (full calendar — read/write all calendars)

## Open questions for Mark + Drummond

- **Mark:** New project (`OpenClawNet.OAuth.Storage`) or extend `OpenClawNet.Storage`?
- **Mark:** Single `OpenClawNet.Tools.Google` project for both, or split per service?
- **Drummond:** Confirm DPAPI / DataProtection acceptable for v1 token-at-rest. (Per Drummond's checklist: yes — reuse existing `SecretsStore` pattern.)
- **Drummond:** gitleaks rules for Google `client_secret` and `refresh_token` shapes — confirmed needed in his checklist.
- **Drummond:** Sync workflow exclusions — confirmed `.gitleaks.toml` and `.github/sync-config.yml` need updates per his checklist.
- **Mark:** Calendar approval-prompt template (Helly) for v1, or render raw JSON?
