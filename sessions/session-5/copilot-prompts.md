# Session 5 Copilot Prompts

**OpenClawNet — Channels, Browser & Events**

Prompts organized by stage. Use these during the live session for GitHub Copilot Chat or inline completions.

---

## Stage 1: Teams Integration

### Prompt 1 — Add a Typing Indicator

**Context:** `OpenClawNetBot.cs` is open, cursor in `OnMessageActivityAsync`.

**Prompt:**
> *"Before calling ProcessAsync, send a typing activity to Teams so the user sees 'OpenClawNet is typing...' while the agent works. Use `turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing })`."*

**Expected:** Copilot inserts the typing activity send before the orchestrator call, matching the existing async pattern.

---

### Prompt 2 — Add Slash Command Support

**Context:** `OpenClawNetBot.cs` open.

**Prompt:**
> *"Add handling for the `/help` slash command in OnMessageActivityAsync. If the message starts with `/help`, reply with a formatted list of available tools from IToolRegistry instead of calling the orchestrator. Inject IToolRegistry in the constructor."*

**Expected:** Copilot adds a guard clause checking for `/help`, injects `IToolRegistry`, and formats the tool manifest as a Teams card or plain text.

---

### Prompt 3 — Persist Session Map to Database

**Context:** `OpenClawNetBot.cs` open, `IDbContextFactory` available in codebase.

**Prompt:**
> *"The _sessionMap ConcurrentDictionary loses Teams conversation mappings on restart. Refactor it to persist session mappings in the database using OpenClawDbContext. Add a TeamsConversation entity with ConversationId (string PK) and SessionId (Guid)."*

**Expected:** Copilot adds a new entity, migration, and replaces the in-memory dictionary with DB reads/writes.

---

## Stage 2: Browser Tool

### Prompt 4 — Add wait-for-selector Action

**Context:** `BrowserTool.cs` open, cursor after the `fill` case in the switch.

**Prompt:**
> *"Add a `wait-for-selector` action to BrowserTool. It should navigate to the URL, wait up to 5 seconds for the CSS selector to appear using `WaitForSelectorAsync`, then return the element's inner text. If the selector doesn't appear within the timeout, return a descriptive failure message."*

**Expected:** Copilot adds a new `WaitForSelectorAsync` private method following the existing helper pattern (RequireUrl, separate private method, ToolResult.Ok/Fail).

---

### Prompt 5 — Add Scrollable Extract with Pagination

**Context:** `BrowserTool.cs` open.

**Prompt:**
> *"The current extract-text action is limited to 5,000 characters. Add a `page` parameter (integer, default 1) that extracts a different 5,000-character chunk. Return the page number and total length in the result so the agent knows whether to request more pages."*

**Expected:** Copilot adds pagination logic with offset math, updates the ToolMetadata schema to include the page parameter.

---

### Prompt 6 — Convert Screenshot to Base64

**Context:** `BrowserTool.cs` open, ScreenshotAsync method visible.

**Prompt:**
> *"Instead of saving the screenshot to a temp file, return it as a base64-encoded PNG data URI so the caller gets the image inline without needing filesystem access. Use `ScreenshotAsync` with no path argument to get the byte array, then convert with Convert.ToBase64String."*

**Expected:** Copilot rewrites ScreenshotAsync to return `data:image/png;base64,...` inline in the ToolResult.

---

## Stage 3: Webhook Events

### Prompt 7 — Add Webhook Signature Verification

**Context:** `WebhookEndpoints.cs` open.

**Prompt:**
> *"Add HMAC-SHA256 signature verification to the webhook endpoint. Read a `WebhookSecret` from configuration, compute `HMAC-SHA256(requestBody, secret)`, and compare it to the `X-Webhook-Signature` header. Return 401 if the signature doesn't match. If the header is missing and no secret is configured, allow the request (opt-in security)."*

**Expected:** Copilot adds header reading, HMAC computation using `System.Security.Cryptography.HMACSHA256`, constant-time comparison, and configuration binding.

---

### Prompt 8 — Fire-and-Forget Webhook Mode

**Context:** `WebhookEndpoints.cs` open.

**Prompt:**
> *"Add an optional `async` query parameter to the webhook endpoint. When `?async=true`, enqueue the agent run using a BackgroundService queue (IBackgroundTaskQueue pattern from ASP.NET Core) and return 202 Accepted immediately with the session ID, rather than waiting for the agent to complete."*

**Expected:** Copilot adds the query parameter, a `IBackgroundTaskQueue` interface, channel-based implementation, and hosted service pattern.

---

### Prompt 9 — GitHub Webhook Payload Parser

**Context:** `WebhookEndpoints.cs` open.

**Prompt:**
> *"Add a specialized route `POST /api/webhooks/github/{event}` that parses GitHub webhook payloads (push, pull_request, issues events) and formats a human-friendly message for the agent. For a push event, extract the repository name, pusher, and list of changed files. For pull_request, extract PR title, author, and action (opened/merged/closed)."*

**Expected:** Copilot adds a GitHub-specific endpoint with typed DTOs for each event type and a message formatter.

---

## Bonus — Architecture Extensions

### Prompt 10 — Slack Adapter

**Context:** `IBotAdapter.cs` open, `TeamsAdapter.cs` visible for reference.

**Prompt:**
> *"Using TeamsAdapter as a reference, implement a SlackAdapter that handles Slack Events API payloads. Use SlackNet NuGet package. It should handle `app_mention` events, extract the user message from the event text, call IAgentOrchestrator, and post the response to Slack using the SlackApiClient."*

**Expected:** Copilot generates a Slack adapter following the IBotAdapter pattern, handling Slack's challenge handshake and event dispatch.

---

### Prompt 11 — Unit Test for BrowserTool

**Context:** `tests/OpenClawNet.UnitTests/` directory, existing test files visible.

**Prompt:**
> *"Write unit tests for BrowserTool.ExecuteAsync. Since Playwright can't be mocked easily, write tests that verify the routing logic: (1) unknown action returns failure, (2) missing url returns failure, (3) missing selector on click action returns failure. Use a real BrowserTool with a NullLogger and call ExecuteAsync with crafted ToolInput objects."*

**Expected:** Copilot generates 3+ `[Fact]` tests covering the fast-fail paths that don't require a browser.

---

## Quick Prompts (Inline)

These are for inline Copilot completions during the session:

1. Start typing `protected override async Task OnMembersAddedAsync` → Copilot completes the welcome message handler
2. Type `"wait-for-selector" =>` in the switch statement → Copilot suggests the action stub
3. Type `// Extract all links from the page` inside ExtractTextAsync → Copilot suggests `page.EvaluateAsync` with link extraction logic
4. Type `var hmac = new HMACSHA256` → Copilot suggests the full signature computation block
