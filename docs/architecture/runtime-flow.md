# Agent Runtime Flow

## Initialization Flow (Aspire Orchestration)

```
User: aspire run
    │
    ▼
AppHost.cs (DistributedApplication builder)
    │
    ├── Configure Gateway service (Control Plane)
    │   ├── Health check endpoint: /health
    │   ├── External HTTP endpoints enabled
    │   ├── Register channel manager
    │   └── Start webhook listener
    │
    ├── Configure Web service (Control UI + WebChat)
    │   ├── Health check endpoint: /health
    │   ├── WaitFor(Gateway)
    │   └── External HTTP endpoints enabled
    │
    ├── Register all services in DI (Gateway Program.cs)
    │   ├── Storage (SQLite + EF Core) — incl. ModelProviderDefinition, AgentProfile entities
    │   ├── Agent provider (`RuntimeAgentProvider` routes to active `IAgentProvider`)
    │   ├── Agent runtime components
    │   ├── Tool framework + individual tools (including Browser)
    │   ├── Skills loader (bundle → local → workspace precedence) via `FileSkillLoader`
    │   ├── Memory + embeddings services
    │   ├── HTTP streaming endpoints (NDJSON for real-time chat via POST /api/chat/stream)
    │   ├── Channel manager (Teams adapter registered)
    │   └── Webhook endpoints
    │
    └── Start both services in parallel
        │
        ▼
    Aspire Dashboard available at https://localhost:15100
```

---

## Bootstrap File Loading Flow

At the start of every agent session, workspace bootstrap files are loaded and injected into the system prompt:

```
Session creation request (POST /api/sessions or channel message)
    │
    ▼
SessionManager.CreateSessionAsync(workspacePath)
    │
    ▼
WorkspaceLoader.LoadBootstrapFilesAsync(workspacePath)
    │
    ├── Try load: {workspacePath}/AGENTS.md
    │   ├── Found → parse markdown → store as AgentPersona
    │   └── Not found → skip (no error)
    │
    ├── Try load: {workspacePath}/SOUL.md
    │   ├── Found → parse markdown → store as AgentValues
    │   └── Not found → skip (no error)
    │
    └── Try load: {workspacePath}/USER.md
        ├── Found → parse markdown → store as UserProfile
        └── Not found → skip (no error)
    │
    ▼
BootstrapContext stored in session metadata
    │
    ▼
First PromptComposer.ComposeAsync() call:
    ├── [AGENTS.md content]  ← agent persona/behavior
    ├── [SOUL.md content]    ← values and constraints
    ├── [USER.md content]    ← user preferences
    ├── Active skills
    ├── Session summary (if any)
    └── Conversation history + user message
```

---

## Standard Chat Flow

```
User Message
    │
    ▼
Gateway API (POST /api/chat)
    │
    ▼
ProviderResolver.ResolveAsync(agentProfileName)
    │
    ├──▶ Resolve AgentProfile (if AgentProfileName specified)
    │      ├── Load AgentProfile from DB
    │      ├── Resolve ModelProviderDefinition → ResolvedProviderConfig
    │      └── Apply instructions + tool filter
    │
    ▼
RuntimeModelSettings.Update(resolvedConfig)
    │   ← Syncs DB-based definition to the runtime
    │   ← Sets endpoint, model, apiKey, deploymentName
    │
    ▼
AgentOrchestrator.ProcessAsync()  [Public boundary - stable API]
    │
    └──▶ IAgentRuntime.ExecuteAsync()  [Internal runtime abstraction]
           │
           ├── Store user message in DB
           ├── Load conversation history
           ├── Check if compaction needed (>20 messages)
           │     └── If yes → ISummaryService.SummarizeAsync() (Level 1 compaction)
           │
           ▼
           PromptComposer.ComposeAsync()
           │
           ├── Bootstrap context (AGENTS.md + SOUL.md + USER.md)
           ├── Session summary (if available)
           └── Conversation history + user message
           │
           ▼  ── First model call (via Agent Framework) ──
           ChatClientAgent.RunStreamingAsync(messages)
           │
           ├── AgentSkillsProvider.InvokingAsync()
           │     └── Advertises available skills to the model
           │
           └──▶ ModelClientChatClientAdapter.GetStreamingResponseAsync()
                  │
                  └──▶ IAgentProvider.CompleteAsync()
                         [Via RuntimeAgentProvider → uses endpoint from RuntimeModelSettings]
           │
           ├── If response has tool_calls ──▶ Tool Loop
           │       │
           │       ├── IToolExecutor.ExecuteAsync()
           │       │     ├── Check approval policy
           │       │     ├── Execute tool (FileSystem / Shell / Web / Browser / Scheduler)
           │       │     └── Return structured result
           │       ├── Append tool results to messages
           │       └── Call model again via adapter directly (history has skills context)
           │             (up to 10 iterations)
           │
           └── Final response (no tool calls)
                   │
                   ├── Store assistant message in DB
                   └── Return to IAgentOrchestrator
    │
    └──▶ Return to Gateway → UI
```

> **RuntimeModelSettings sync:** The key addition is the `RuntimeModelSettings.Update()` step before the orchestrator runs. This bridges the DB-stored `ModelProviderDefinition` to the runtime, ensuring `IAgentProvider.CreateChatClient()` receives the correct endpoint and credentials from the definition — not just the DI-registered defaults.

---

## Streaming Flow

For real-time token streaming via HTTP NDJSON:

```
Chat.razor (Blazor) ──POST /api/chat/stream──▶ Gateway
                           (HTTP NDJSON)          │
                                                  ▼
                                    ProviderResolver.ResolveAsync(profileName)
                                                  │
                                                  ▼
                                    RuntimeModelSettings.Update(resolvedConfig)
                                       ← endpoint, model, apiKey synced
                                                  │
                                                  ▼
                                    AgentOrchestrator.ProcessAsync()
                                                  │
                                                  ▼
                                    IAgentProvider.StreamAsync()
                                       [uses endpoint from RuntimeModelSettings]
                                                  │
                                         ┌────────┼─────────┐
                                         ▼        ▼         ▼
                                    content  tool_start  complete
                                         │        │         │
                                         ├────────┼─────────┤
                                         ▼
                            NDJSON stream (one event per line)
                            {"type":"content","content":"..."}
                            {"type":"tool_start","toolName":"..."}
                            {"type":"complete"}
```

Each message is a separate HTTP request. Errors surface as HTTP status codes (e.g., 400 for validation, 500 for server errors), not hidden in frames.

> **Note:** Both `ChatStreamEndpoints` and `ChatEndpoints` perform the same `ProviderResolver → RuntimeModelSettings.Update()` sync before invoking the orchestrator. This ensures streaming and non-streaming chat flows use the same resolved endpoint from the `ModelProviderDefinition`.

---

## Isolated Session Flow

Isolated sessions provide sandboxed execution with no cross-contamination:

```
Request: Create isolated session (e.g., scheduled job or webhook trigger)
    │
    ▼
SessionManager.CreateIsolatedSessionAsync(options)
    │
    ├── Allocate new session ID (no relationship to other sessions)
    ├── Set InheritSkills = false (clean skill state)
    ├── Set InheritMemory = false (no prior conversation history)
    ├── Load bootstrap files from specified workspace (or none)
    └── Set TTL (auto-cleanup on timeout)
    │
    ▼
Agent executes in isolation:
    ├── No access to history of other sessions
    ├── Tool calls scoped to the isolated workspace
    ├── Memory writes do not affect main session store
    └── Context compaction within the isolated session only
    │
    ▼
Session completes or times out:
    ├── Final result returned to caller (job, webhook, etc.)
    ├── Session marked as archived
    └── Resources released (memory cleared, TTL expired)
```

---

## Context Compaction Flow

```
Turn N: Message count in active context exceeds threshold (default: 20)
    │
    ▼
IAgentRuntime detects: history.Count > SummarizationThreshold
    │
    ▼
ISummaryService.SummarizeAsync(oldestMessages)
    │
    ├── Take oldest SummaryBatchSize messages
    ├── Build summarization prompt:
    │   "Summarize the following conversation segment concisely..."
    ├── Call ModelClient.CompleteAsync() with summarization prompt
    └── Return: compact summary text
    │
    ▼
Store SessionSummary entity in SQLite
    │
    ▼
Remove summarized messages from active context
    │
    ▼
Next PromptComposer.ComposeAsync():
    ├── [Bootstrap context]
    ├── [Active skills]
    ├── [SessionSummary: "User is building... (summary text)"]
    ├── [Messages 21–N at full fidelity]
    └── [New user message]
```

---

## Webhook Trigger Flow

```
External system (GitHub, calendar, custom) → POST /api/webhooks/{eventType}
    │
    ▼
WebhookEndpoints.HandleAsync(eventType, payload)
    │
    ├── Validate webhook signature (HMAC or token)
    ├── Parse event payload (JSON → WebhookEvent)
    └── Determine agent task from event type mapping
    │
    ▼
SessionManager.CreateIsolatedSessionAsync(webhookOptions)
    │
    ├── Load workspace defined in webhook config
    ├── Inject event context into USER.md equivalent
    └── Set TTL for webhook-triggered session
    │
    ▼
AgentOrchestrator.ProcessAsync(webhookPrompt, session)
    │
    ├── Agent executes the configured task
    │   Example: "A new GitHub issue was opened: {title}. Analyze and suggest labels."
    │
    └── Result stored in session history
    │
    ▼
Optional: Send result back via channel
    ├── If webhook source has a registered channel → IChannel.SendMessageAsync()
    └── Or: Store result for polling via GET /api/webhooks/{jobId}/result
```

---

## Channel Message Flow

Inbound message from a channel (e.g., Microsoft Teams):

```
External platform (Teams) sends message
    │
    ▼
Teams Adapter (OpenClawNet.Adapters.Teams)
BotActivityHandler.OnMessageActivityAsync()
    │
    ├── Extract: text, sender ID, conversation reference
    └── Map to: ChannelMessage { ChannelId, UserId, Text, Metadata }
    │
    ▼
Gateway ChannelManager.RouteMessageAsync(channelMessage)
    │
    ├── Lookup or create session for (ChannelId + UserId)
    ├── Associate workspace if configured for this channel
    └── Forward to AgentOrchestrator
    │
    ▼
AgentOrchestrator.ProcessAsync(channelMessage.Text, session)
    │
    (Standard chat flow — prompt composition, model call, tool loop)
    │
    ▼
Agent response text
    │
    ▼
ChannelManager.SendResponseAsync(channelId, sessionId, responseText)
    │
    ▼
Teams Adapter: BotConnectorClient.ReplyToActivityAsync()
    │
    ▼
Message delivered to user in Teams
```

---

## Scheduler Flow

```
JobSchedulerService (BackgroundService)
    │
    ├── Poll every 30 seconds
    ├── Query: active jobs where NextRunAt <= now
    │
    ▼
    For each due job:
    ├── Create JobRun record (status: running)
    ├── Create isolated agent session (clean context, scoped workspace)
    │     └── Uses IsolatedSession — no contamination from other sessions
    ├── Resolve AgentProfile if job has `AgentProfileName`
    │     └── Applies profile's provider, instructions, and tool filter
    ├── Load job-specific workspace (if configured)
    ├── Execute via AgentOrchestrator
    │     └── Job prompt + any configured tools
    ├── Update JobRun (completed/failed, output stored)
    └── Update job NextRunAt (if recurring cron job)
    │
    ▼
Optional post-job actions:
    ├── Send result via channel (if job has a channel_id)
    └── Trigger webhook callback (if job has a callback_url)
```
