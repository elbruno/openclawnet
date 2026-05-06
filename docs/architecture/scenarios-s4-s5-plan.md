# E2E Scenarios S4 & S5 — Architecture Brief

**Author:** Mark (Lead Architect)  
**Date:** 2026-05-06  
**Requested by:** Bruno Capuano

---

## Deliverable 1 — Verification of S1/S2/S3 in Plan Repo

### S1: Auto Chat Title (✅ Shipped)

**Evidence found:**
- `src/OpenClawNet.Gateway/Services/ChatNamingService.cs` — LLM-based title generation service using `IModelClient`; generates 5–8 word session titles from recent messages.
- `src/OpenClawNet.Gateway/Endpoints/ChatEndpoints.cs:104` — `POST /api/chat/{id}/auto-rename` endpoint wired to `ChatNamingService`.
- `src/OpenClawNet.Web/Components/Pages/Chat.razor` — UI triggers auto-rename via `PostAsync("api/chat/{sessionId}/auto-rename")`.
- `src/OpenClawNet.Storage/ConversationStore.cs` + `IConversationStore.cs` — session title persistence.
- **Tests:** `tests/OpenClawNet.UnitTests/Gateway/ChatEndpointProfileTests.cs` covers the endpoint; no dedicated E2E test found for the full auto-rename journey (minor gap — low risk since endpoint is simple POST→LLM→persist).

### S2: GitHub Repo Insights (✅ Shipped)

**Evidence found:**
- `src/OpenClawNet.Tools.GitHub/GitHubTool.cs` — Full `ITool` implementation with actions: `summary`, `list_issues`, `list_pulls`, `list_commits`, `get_repo`, `get_file`.
- `src/OpenClawNet.Tools.GitHub/IGitHubClientFactory.cs` + `GitHubClientFactory.cs` — Hermetic testing pattern via injectable base URL (`GitHub:ApiBaseUrl` config or `GITHUB_API_BASE_URL` env var) enabling WireMock.
- `src/OpenClawNet.Tools.GitHub/GitHubToolServiceCollectionExtensions.cs` — DI registration.
- `src/OpenClawNet.Gateway/Resources/JobTemplates/github-issue-triage.json` — pre-built job template.
- **Tests:** `tests/OpenClawNet.UnitTests/Tools/GitHubToolTests.cs` — unit tests with mocked `IGitHubClient`; Summary action shape validated. No integration test using WireMock found (minor gap — could add WireMock round-trip).

### S3: Scheduled Jobs from Chat (✅ Shipped)

**Evidence found:**
- `src/OpenClawNet.Tools.Scheduler/SchedulerTool.cs` — `ITool` with actions: `create`, `list`, `cancel`, `start`, `pause`, `resume`. Supports cron expressions and one-time ISO 8601 triggers.
- `src/OpenClawNet.Gateway/Services/SmartScheduleParser.cs` — NLP→cron conversion via LLM.
- `src/OpenClawNet.Services.Scheduler/` — Dedicated Aspire service with polling, Blazor dashboard, settings.
- `src/OpenClawNet.Storage/Entities/ScheduledJob.cs`, `JobRun.cs`, `JobStatus.cs` — full persistence model.
- `src/OpenClawNet.Gateway/Endpoints/JobScheduleEndpoints.cs`, `JobEndpoints.cs`, `ScheduleEndpoints.cs` — API surface.
- **Tests:** `tests/OpenClawNet.IntegrationTests/JobScheduleEndpointsTests.cs`, `Tools/JobToolE2ETests.cs` (deterministic with scriptable model), `tests/OpenClawNet.UnitTests/Scheduler/` (7+ test files covering cron eval, polling, orphan reclaim, settings). Coverage is strong.

---

## Deliverable 2 — S4 Architecture: GitHub Insights → External Dashboard

### User Journey

```
User: "Give me insights on repos elbruno/openclawnet and elbruno/phi3"
Agent: [calls GitHubTool.summary for each repo] → presents markdown table
User: "Push this to my dashboard"
Agent: [calls DashboardPublisherTool] → HTTP POST to external dashboard
Agent: "✅ Published to dashboard. View: https://dashboard.example.com/view/abc123"
```

**Implementation status:** User-facing documentation available at [`docs/tools/dashboard-publisher.md`](../../tools/dashboard-publisher.md)

### New Tool: `DashboardPublisherTool`

| Aspect | Detail |
|--------|--------|
| **Project** | `src/OpenClawNet.Tools.Dashboard/` (new project, mirrors `OpenClawNet.Tools.GitHub` structure) |
| **Class** | `DashboardPublisherTool : ITool` |
| **Name** | `"dashboard_publish"` |
| **Category** | `"integration"` |

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "title": { "type": "string", "description": "Dashboard card title" },
    "insights": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "repo": { "type": "string" },
          "openIssues": { "type": "integer" },
          "openPRs": { "type": "integer" },
          "stars": { "type": "integer" },
          "lastPush": { "type": "string", "format": "date-time" },
          "summary": { "type": "string" }
        },
        "required": ["repo"]
      }
    },
    "format": { "type": "string", "enum": ["card", "table", "chart"], "default": "card" }
  },
  "required": ["title", "insights"]
}
```

**Output:** `ToolResult { Success = true, Output = "Published. Dashboard URL: {url}" }` or error with HTTP status + body excerpt.

### Dashboard Endpoint Contract

| HTTP | Detail |
|------|--------|
| **Method** | `POST` |
| **URL** | Configured via `Dashboard:BaseUrl` + `/api/v1/insights` |
| **Auth** | `Authorization: Bearer {api-key}` — key from `Dashboard:ApiKey` in config |
| **Content-Type** | `application/json` |

**Request Body:**

```json
{
  "title": "Multi-repo Insights 2026-05-06",
  "source": "openclawnet",
  "generatedAt": "2026-05-06T14:30:00Z",
  "insights": [ /* same as tool input insights array */ ]
}
```

**Response:** `201 Created` with `{ "id": "abc123", "viewUrl": "https://..." }`

### Reuse vs Extend Existing GitHub Tool

**Recommendation: Reuse S2's `GitHubTool` as-is.** The dashboard tool is a *consumer* of GitHub data, not a producer. The agent orchestrator naturally chains: GitHubTool gathers data → DashboardPublisherTool publishes it. No modification to `GitHubTool` needed. The agent's multi-tool-call capability handles sequencing.

### Configuration

**`appsettings.json` section:**

```json
{
  "Dashboard": {
    "BaseUrl": "https://dashboard.example.com",
    "ApiKey": "",
    "TimeoutSeconds": 30
  }
}
```

**Options type:** `DashboardOptions` (in `OpenClawNet.Tools.Dashboard` project)

```csharp
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}
```

Bound via `services.Configure<DashboardOptions>(config.GetSection(DashboardOptions.SectionName))` and injected as `IOptions<DashboardOptions>`.

### Approval Flow

**Recommendation: YES — require user approval.** Publishing to an external system is a side-effectful, non-reversible action. Consistent with the existing `IToolApprovalPolicy` pattern. Set `RequiresApproval = true` in `ToolMetadata`. The existing tool-approval card UX handles it seamlessly.

### Test Strategy

| Layer | Approach |
|-------|----------|
| **Unit** | Mock `HttpMessageHandler`; verify correct JSON body shape + auth header |
| **Integration** | WireMock server simulating dashboard API; verify round-trip POST → 201 response parsing |
| **E2E** | `GatewayE2EFactory` + scriptable model client that returns a tool call → validates full agent→tool→external pipeline |
| **GitHub data** | Reuse `IGitHubClientFactory` hermetic pattern (existing) |

### Telemetry & Retry

- **Polly:** Standard retry (2 attempts, exponential backoff) + circuit breaker. Configured via `Microsoft.Extensions.Http.Resilience` pipeline (already used in `ServiceDefaults`).
- **ILogger structured fields:** `DashboardUrl`, `ResponseStatus`, `ElapsedMs`, `InsightCount`.
- **Activity source:** `OpenClawNet.Tools.Dashboard` for distributed tracing.

---

## Deliverable 3 — S5 Architecture: Gmail + Calendar Assistant

### User Journey

```
User: "Summarize my unread Gmail"
Agent: [calls GmailSummarizeTool] → returns bullet-point summary of unread emails
User: "Schedule a meeting tomorrow at 10am with the team"
Agent: [calls CalendarCreateEventTool] → creates Google Calendar event
Agent: "✅ Created: 'Team Meeting' — May 7, 2026 10:00–11:00 AM. Invite sent to team@example.com"
```

### New Tools

#### `GmailSummarizeTool`

| Aspect | Detail |
|--------|--------|
| **Project** | `src/OpenClawNet.Tools.GoogleWorkspace/` (new project) |
| **Class** | `GmailSummarizeTool : ITool` |
| **Name** | `"gmail_summarize"` |
| **Category** | `"communication"` |
| **RequiresApproval** | `false` (read-only) |

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "maxResults": { "type": "integer", "default": 10, "description": "Max emails to summarize" },
    "query": { "type": "string", "description": "Gmail search query (optional, default: is:unread)" },
    "labelIds": { "type": "array", "items": { "type": "string" }, "description": "Filter by label IDs" }
  }
}
```

**Output:** Markdown-formatted bullet list of emails (sender, subject, snippet, date).

#### `CalendarCreateEventTool`

| Aspect | Detail |
|--------|--------|
| **Class** | `CalendarCreateEventTool : ITool` |
| **Name** | `"calendar_create_event"` |
| **Category** | `"communication"` |
| **RequiresApproval** | `true` (creates external resource) |

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "title": { "type": "string", "description": "Event title" },
    "startTime": { "type": "string", "format": "date-time", "description": "Start time ISO 8601" },
    "endTime": { "type": "string", "format": "date-time", "description": "End time ISO 8601" },
    "attendees": { "type": "array", "items": { "type": "string" }, "description": "Email addresses" },
    "description": { "type": "string" },
    "location": { "type": "string" }
  },
  "required": ["title", "startTime"]
}
```

### Provider Abstraction

```csharp
// Mirrors IGitHubClientFactory pattern
public interface IGoogleWorkspaceClientFactory
{
    IGmailClient CreateGmailClient();
    ICalendarClient CreateCalendarClient();
}

public interface IGmailClient
{
    Task<IReadOnlyList<EmailSummary>> GetUnreadAsync(string? query, int maxResults, CancellationToken ct);
}

public interface ICalendarClient
{
    Task<CalendarEventResult> CreateEventAsync(CreateEventRequest request, CancellationToken ct);
}
```

Factory implementation wraps the Google APIs SDK (`Google.Apis.Gmail.v1`, `Google.Apis.Calendar.v3`) and is injected via DI — tests substitute with mocks.

### OAuth 2.0 Flow

**Recommendation: Web application flow (authorization code).** Rationale:
- OpenClawNet is a Blazor Server app running in a browser context — the web flow is natural.
- Redirect URI: `https://localhost:{port}/api/auth/google/callback` (dev) / production equivalent.
- Consent screen prompts user once; refresh token stored for subsequent calls.

**Scopes (minimized):**
- `https://www.googleapis.com/auth/gmail.readonly` — read-only Gmail access
- `https://www.googleapis.com/auth/calendar.events` — create/edit calendar events (not full calendar admin)

### Token Storage

| Aspect | Recommendation |
|--------|---------------|
| **Abstraction** | New `IOAuthTokenStore` interface in `OpenClawNet.Storage` |
| **Implementation** | `SqliteOAuthTokenStore` — stores in existing SQLite DB, new `OAuthToken` entity |
| **Encryption** | DPAPI (`DataProtectionProvider`) for encryption at rest on Windows; ASP.NET Core Data Protection on Linux |
| **Schema** | `OAuthTokens` table: `Id`, `Provider` (e.g., "google"), `UserId`, `AccessToken` (encrypted), `RefreshToken` (encrypted), `ExpiresAt`, `Scopes`, `CreatedAt`, `UpdatedAt` |

### Refresh Token Handling

- On every API call, check `ExpiresAt` with 5-minute buffer.
- If expired, use refresh token to obtain new access token via Google's token endpoint.
- If refresh fails (revoked), surface error to user: "Google authorization expired — please re-authorize."
- Store updated tokens atomically (access + expiry + optional new refresh token).

### Per-User vs Per-Installation

**Recommendation: Single-user (per-installation) for v1.** OpenClawNet's current model is single-user local-first. One Google account linked per installation. Future multi-user can promote to per-user via the `UserId` field already in the schema.

### Configuration

```json
{
  "GoogleWorkspace": {
    "ClientId": "",
    "ClientSecret": "",
    "RedirectUri": "https://localhost:5001/api/auth/google/callback",
    "Scopes": ["gmail.readonly", "calendar.events"]
  }
}
```

### Drummond Security Review Checklist

The following MUST be reviewed by Drummond before merge:

1. **OAuth client secret storage** — must not be in appsettings.json committed to git; use user-secrets or env vars
2. **Token encryption implementation** — verify DPAPI/DataProtection usage, key rotation strategy
3. **Refresh token storage** — encrypted at rest, no plaintext in logs
4. **Scope minimization** — confirm `gmail.readonly` + `calendar.events` are minimal sufficient
5. **Token revocation endpoint** — user can de-authorize from UI
6. **Redirect URI validation** — no open redirect; strict match only
7. **PKCE** — confirm authorization code flow uses PKCE (RFC 7636)
8. **Rate limiting** — Google API quota handling; no credential leakage in error messages
9. **Audit logging** — token grant/refresh/revoke events logged (no token values)

### Test Strategy

| Layer | Approach |
|-------|----------|
| **Unit** | Mock `IGoogleWorkspaceClientFactory` returning fake `IGmailClient` / `ICalendarClient` |
| **Integration** | In-memory factory implementations; verify tool input parsing → client call → output formatting |
| **E2E** | `GatewayE2EFactory` + scriptable model + mock factory. Validates full chat→tool→response pipeline |
| **NEVER** | Hit live Gmail/Calendar in CI. All external calls mocked. |

---

## Deliverable 4 — Work Item Decomposition

### S4: GitHub Insights → External Dashboard (4 stories)

| # | Title | Owner | Size | Dependencies |
|---|-------|-------|------|--------------|
| S4-1 | Create `OpenClawNet.Tools.Dashboard` project + `DashboardPublisherTool` implementation | Irving | M | None |
| S4-2 | `DashboardOptions` IOptions binding + appsettings section + HttpClient registration with Polly | Irving | S | None |
| S4-3 | Tool approval integration (`RequiresApproval=true`) + telemetry structured logging | Helly | S | S4-1 |
| S4-4 | Unit + integration tests (WireMock dashboard stub, scriptable model E2E) | Dylan | M | S4-1, S4-2 |
| S4-5 | Documentation: tool contract doc + update `docs/architecture/components.md` | Irving | S | S4-1 |

### S5: Gmail + Calendar Assistant (7 stories)

| # | Title | Owner | Size | Dependencies |
|---|-------|-------|------|--------------|
| S5-1 | Create `OpenClawNet.Tools.GoogleWorkspace` project + `IGoogleWorkspaceClientFactory` abstraction | Petey | M | None |
| S5-2 | `GmailSummarizeTool` implementation with mock-friendly factory | Petey | M | S5-1 |
| S5-3 | `CalendarCreateEventTool` implementation + approval flow | Petey | M | S5-1 |
| S5-4 | OAuth 2.0 web flow endpoint (`/api/auth/google/callback`) + PKCE | Petey | L | S5-1 |
| S5-5 | `IOAuthTokenStore` + `SqliteOAuthTokenStore` + EF migration + encryption | Helly | M | S5-4 |
| S5-6 | Drummond security review: OAuth secrets, token encryption, scope audit | Drummond | M | S5-4, S5-5 |
| S5-7 | Unit + integration + E2E tests (mock Google clients, token refresh scenarios) | Dylan | L | S5-2, S5-3, S5-5 |

---

## Deliverable 5 — E2E Test Plan

All E2E tests live in `tests/OpenClawNet.E2ETests/` using the existing `GatewayE2EFactory` infrastructure.

### S1: Auto Chat Title E2E

```csharp
[Fact]
public async Task Chat_AutoRename_Generates_Title_From_Conversation()
{
    // 1. Create session, send 2 messages (via scriptable model)
    // 2. POST /api/chat/{id}/auto-rename
    // 3. Assert: 200 OK, GeneratedName is non-empty
    // 4. GET /api/chat/{id} — verify title updated in storage
}
```

### S2: GitHub Repo Insights E2E

```csharp
[Fact]
public async Task Chat_GitHubTool_Returns_Repo_Summary_Via_Agent()
{
    // 1. Configure WireMock for GitHub API (via IGitHubClientFactory override)
    // 2. Send chat message: "Summarize elbruno/openclawnet"
    // 3. Scriptable model returns tool_call for github.summary
    // 4. Assert: agent response contains repo stats from WireMock stub
}
```

### S3: Scheduled Job from Chat E2E

```csharp
[Fact]
public async Task Chat_SchedulerTool_Creates_Job_From_Natural_Language()
{
    // 1. Send chat: "Schedule a daily check at 9am"
    // 2. Scriptable model returns tool_call for schedule.create with cron
    // 3. Assert: ScheduledJob persisted in DB with correct cron expression
    // 4. Assert: tool response confirms job creation
}
```

### S4: Dashboard Publish E2E

```csharp
[Fact]
public async Task Chat_DashboardPublish_Posts_Insights_To_External_API()
{
    // 1. Start WireMock for dashboard endpoint (201 Created)
    // 2. Configure DashboardOptions with WireMock URL
    // 3. Scriptable model returns: [github.summary call] → [dashboard_publish call]
    // 4. Assert: WireMock received POST with correct JSON body + auth header
    // 5. Assert: agent response contains dashboard view URL
}
```

### S5: Gmail + Calendar E2E

```csharp
[Fact]
public async Task Chat_GmailSummarize_Returns_Email_List()
{
    // 1. Register mock IGoogleWorkspaceClientFactory (returns 3 fake emails)
    // 2. Scriptable model returns tool_call for gmail_summarize
    // 3. Assert: response contains sender/subject for all 3 emails
}

[Fact]
public async Task Chat_CalendarCreate_Creates_Event_With_Approval()
{
    // 1. Register mock IGoogleWorkspaceClientFactory
    // 2. Scriptable model returns tool_call for calendar_create_event
    // 3. Assert: approval requested (RequiresApproval=true gate)
    // 4. Simulate approval
    // 5. Assert: mock calendar client received CreateEvent call
    // 6. Assert: response confirms event created
}
```

---

## Architectural Notes

- **Pattern consistency:** Both S4 and S5 follow the `IGitHubClientFactory` hermetic pattern — injectable factories for external clients, enabling WireMock/mocks without touching live APIs.
- **Tool metadata:** Both use `ToolMetadata.RequiresApproval` for write operations (dashboard publish, calendar create) while keeping reads unapproved (GitHub summary, Gmail read).
- **Config pattern:** Each tool group gets its own `appsettings.json` section + `IOptions<T>` strongly-typed binding.
- **Polly/resilience:** Leverages existing `ServiceDefaults` resilience pipeline; tools just register named `HttpClient` instances.
- **E2E infrastructure:** The `GatewayE2EFactory` + scriptable model approach (from `JobToolE2ETests`) is the gold standard for deterministic E2E testing without LLM or network dependencies.
