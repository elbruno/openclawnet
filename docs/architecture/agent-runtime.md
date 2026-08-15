# Agent Runtime Architecture

## Overview

The **agent runtime** (`OpenClawNet.Agent` project) orchestrates the complete AI interaction loop. It is the execution engine that takes user messages, orchestrates model invocation, executes tools, and returns responses.

**Key principle:** The public boundary is `IAgentOrchestrator` (stable, user-facing). The internal implementation uses `IAgentRuntime` (abstraction point for future frameworks like Microsoft Agent Framework, which is already integrated).

---

## Key Interfaces

### `IAgentOrchestrator` (Public API)

The stable public interface that Gateway and other consumers interact with:

```csharp
public interface IAgentOrchestrator
{
    /// Non-streaming chat interaction
    Task<ChatResponse> ProcessAsync(ChatRequest request, CancellationToken ct);
    
    /// Streaming response with real-time token delivery
    IAsyncEnumerable<ChatResponseChunk> ProcessStreamAsync(ChatRequest request, CancellationToken ct);
}
```

### `IAgentRuntime` (Internal Abstraction)

The internal runtime abstraction that separates the public API from implementation details. This enables future integration with different agent frameworks without breaking the public `IAgentOrchestrator` interface.

```csharp
public interface IAgentRuntime
{
    /// Non-streaming execution
    Task<AgentContext> ExecuteAsync(AgentContext context, CancellationToken ct);
    
    /// Streaming execution with event-based progress
    IAsyncEnumerable<AgentStreamEvent> ExecuteStreamAsync(AgentContext context, CancellationToken ct);
}
```

---

## Default Implementation: `DefaultAgentRuntime`

The current implementation uses **Microsoft Agent Framework** (`Microsoft.Agents.AI` v1.1.0) as its execution engine.

### Architecture Diagram

```
User Request
    │
    ▼
IAgentOrchestrator.ProcessAsync() / ProcessStreamAsync()
    │
    ▼
DefaultAgentRuntime.ExecuteAsync() / ExecuteStreamAsync()
    │
    ├── Store user message in conversation store
    ├── Load conversation history from DB
    ├── Check if context compaction needed (>20 messages)
    │   └── If yes: ISummaryService.SummarizeAsync()
    │
    ▼
PromptComposer.ComposeAsync()
    ├── Bootstrap context (AGENTS.md + SOUL.md + USER.md)
    ├── Session summary (if available from prior compaction)
    ├── Available skills advertisement
    └── Conversation history + user message
    │
    ▼  ── First Model Call (via Agent Framework) ──
    ChatClientAgent.RunStreamingAsync()
    │
    ├── AgentSkillsProvider.InvokingAsync()
    │   └── Injects skill summaries into messages
    │
    └──▶ ModelClientChatClientAdapter.GetStreamingResponseAsync()
             │
             └──▶ IAgentProvider.StreamAsync()
                  [RouteToken: RuntimeAgentProvider → active provider]
    │
    ├── Response has tool_calls?
    │   ├── Yes → Tool Loop (up to 10 iterations)
    │   │   ├── IToolExecutor.ExecuteAsync()
    │   │   │   ├── Validate tool call against registry
    │   │   │   ├── Check approval policy
    │   │   │   └── Execute (FileSystem / Shell / Web / Browser / Scheduler)
    │   │   ├── Append tool result to messages
    │   │   └── Call model again via adapter directly
    │   │       (no skill re-injection; context already has skills)
    │   │
    │   └── No → Final response
    │
    ▼
Store assistant message in DB
    │
    ▼
Return to IAgentOrchestrator → Gateway → UI
```

### Component Roles

#### `ModelClientChatClientAdapter`

Bridges OpenClawNet's `IModelClient` interface to Agent Framework's `IChatClient` interface:

```csharp
public class ModelClientChatClientAdapter : IChatClient
{
    // Wraps IModelClient.StreamAsync() as IChatClient.GetStreamingResponseAsync()
    // Translates between model-neutral OpenClawNet types and Agent Framework types
}
```

**Why it exists:** Allows any model provider (Ollama, Azure, Foundry, FoundryLocal, GitHub Copilot) to work with Agent Framework without needing native SDK support.

#### `ChatClientAgent`

Agent Framework's `AIAgent` implementation. Wraps:
- An `IChatClient` (the model)
- A list of `IAIContextProvider` instances (like `AgentSkillsProvider`)

```csharp
var agentOptions = new ChatClientAgentOptions
{
    AIContextProviders = [agentSkillsProvider, /* other providers */]
};
var agent = new ChatClientAgent(chatClient, agentOptions);
```

On each invocation, context providers can enrich messages before the model call. This enables progressive skill disclosure.

#### `AgentSkillsProvider`

Implements the **Agent Skills specification** (agentskills.io) as an `IAIContextProvider`:

```csharp
public class AgentSkillsProvider : IAIContextProvider
{
    public async Task InvokingAsync(AIContextProviderInvokingEventArgs e)
    {
        // Advertise available skills by prepending skill summaries to messages
        // Full skill content is loaded only when agent selects a skill
    }
}
```

**Skill loading precedence:** Workspace skills → Local skills → Bundle skills.

#### `ToolAIFunction`

Wraps an `ITool` instance as an Agent Framework `AIFunction`:

```csharp
public class ToolAIFunction : AIFunction
{
    // Describes the tool's name, description, input schema, output schema
    // Agent Framework uses this metadata to decide when to call the tool
}
```

This makes OpenClawNet tools discoverable to the model's tool-calling pipeline.

#### `DefaultPromptComposer`

Assembles the complete system prompt and conversation context:

```csharp
var fullPrompt = await _promptComposer.ComposeAsync(new PromptRequest
{
    BootstrapContext = bootstrapContext,        // AGENTS.md + SOUL.md + USER.md
    SessionSummary = summary,                   // If context compaction occurred
    AvailableSkills = skillsList,              // Advertised by AgentSkillsProvider
    ConversationHistory = recentMessages,      // Full fidelity messages
    UserMessage = currentUserMessage
});
```

---

## Execution Flow: Two-Phase Strategy

### Phase 1: First Model Call (with Skill Context)

```
DefaultAgentRuntime.ExecuteAsync() calls
    │
    ▼
ChatClientAgent.RunStreamingAsync(messages)
    │
    ▼
For each IAIContextProvider (including AgentSkillsProvider):
    │
    ├── AgentSkillsProvider.InvokingAsync() fires
    │   ├── Reads skills directory
    │   ├── Prepends skill summaries to messages
    │   └── Model sees: "You have skills for [file-system, web-search, ...]"
    │
    ▼
ModelClientChatClientAdapter.GetStreamingResponseAsync() is called
    │
    ▼
IAgentProvider.StreamAsync() (via RuntimeAgentProvider)
```

**Result:** Model receives full skill context on the first turn. It knows what capabilities are available.

### Phase 2: Tool Iterations (No Skill Re-injection)

```
After first response, if model called a tool:
    │
    ▼
Tool Loop (iteration 1...N, max 10)
    │
    ├── IToolExecutor.ExecuteAsync(toolCall)
    │   └── Execute the tool and get result
    │
    ├── Append tool result to messages
    │   (Message history now contains full skill context from Phase 1)
    │
    ▼
Call model again via ModelClientChatClientAdapter.GetStreamingResponseAsync() directly
    │   (Skips ChatClientAgent and AgentSkillsProvider)
    │
    ▼
IAgentProvider.StreamAsync()
```

**Result:** Subsequent iterations avoid re-injecting skill summaries (already in history). Efficiency: the model sees skill context exactly once per turn.

---

## Streaming Pipeline

Real-time token delivery via HTTP NDJSON stream:

```
User request: POST /api/chat/stream { message: "..." }
    │
    ▼
ChatStreamEndpoints
    ├── ProviderResolver.ResolveAsync(agentProfileName)
    ├── RuntimeModelSettings.Update(resolvedConfig)
    └── IAgentOrchestrator.ProcessStreamAsync()
    │
    ▼
DefaultAgentRuntime.ExecuteStreamAsync() yields AgentStreamEvent
    │
    ├── AgentStreamEvent.Type = "content" → token chunk
    │   │   { "type": "content", "content": "The quick..." }
    │
    ├── AgentStreamEvent.Type = "tool_start" → tool invocation begins
    │   │   { "type": "tool_start", "toolName": "FileSystem", "toolInput": {...} }
    │
    ├── AgentStreamEvent.Type = "tool_end" → tool execution complete
    │   │   { "type": "tool_end", "toolResult": "..." }
    │
    └── AgentStreamEvent.Type = "complete" → interaction done
        │   { "type": "complete" }
    │
    ▼
Gateway yields each event as NDJSON line
    │
    ├── NDJSON over HTTP (no framing overhead)
    ├── Browser receives each line immediately (low latency)
    └── Blazor Chat.razor processes events in real-time
```

---

## Request Context: `AgentContext` & `AgentRequest`

### `AgentRequest`

User-facing request structure:

```csharp
public class AgentRequest
{
    public string SessionId { get; set; }           // Current session
    public string? AgentProfileName { get; set; }   // Optional agent profile to use
    public string Message { get; set; }              // User message
    public string? WorkspacePath { get; set; }       // Optional workspace override
    public CancellationToken CancellationToken { get; set; }
}
```

> **Note on `AgentProfileName` resolution.** The chat endpoints accept an explicit profile
> name, but if the named profile is not `Kind = Standard` the runtime silently falls back to
> the default Standard profile. This guarantees `System` and `ToolTester` profiles cannot
> leak into chat conversations. `System` profiles are resolved by name from internal
> services (e.g. `SchedulerHelpersEndpoints.ResolveDefaultProfileAsync` prefers
> `Kind = System`); `ToolTester` profiles are resolved by the Tool Test endpoints
> (`POST /api/tools/{name}/test` with `mode=probe`). See
> [`design/tool-test-design.md`](../design/tool-test-design.md) for details.

### `AgentContext`

Internal runtime context (passed through execution):

```csharp
public class AgentContext
{
    public string SessionId { get; set; }
    public ChatSession Session { get; set; }
    public string UserMessage { get; set; }
    public List<ChatMessage> Messages { get; set; }      // Full conversation
    public BootstrapContext? BootstrapContext { get; set; }
    public string? SessionSummary { get; set; }
    public List<ToolCall> ToolCalls { get; set; }        // From model response
    public string FinalResponse { get; set; }             // Assistant response
    // ... additional metadata
}
```

---

## Context Compaction

Prevents context window overflow by summarizing old messages:

```
Active conversation reaches 20+ messages
    │
    ▼
IAgentRuntime detects: Messages.Count > SummarizationThreshold (20)
    │
    ▼
ISummaryService.SummarizeAsync(oldestMessages)
    │
    ├── Take oldest 10 messages (SummaryBatchSize)
    ├── Build summarization prompt: "Summarize this conversation concisely..."
    ├── Call IAgentProvider.CompleteAsync() (not streaming)
    └── Return: compact summary text (e.g., "User asked about .NET APIs...")
    │
    ▼
Store SessionSummary entity in DB
    │
    ▼
Remove old messages from active context
    │
    ▼
Next PromptComposer.ComposeAsync():
    ├── [Bootstrap context: AGENTS.md, SOUL.md, USER.md]
    ├── [Session summary: "Earlier, user asked about ..."]
    ├── [Recent messages: 10–20 at full fidelity]
    └── [Current user message]
```

**Result:** Context window stays bounded while preserving conversation semantics.

---

## Integration Points

### With `IAgentProvider` (Model Routing)

`DefaultAgentRuntime` uses `IAgentProvider.StreamAsync()` indirectly via:
1. `ModelClientChatClientAdapter` (wraps `IModelClient` as `IChatClient`)
2. `IModelClient` implementation (delegates to `RuntimeAgentProvider`)
3. `RuntimeAgentProvider` (router that picks active provider based on `RuntimeModelSettings`)

See `docs/architecture/provider-model.md` for provider details.

### With `IToolExecutor` (Tool Execution)

When the model returns a tool call:

```csharp
var toolResult = await _toolExecutor.ExecuteAsync(
    toolName: "FileSystem",
    input: { "action": "read", "path": "/path/to/file" }
);
```

See `docs/architecture/tools.md` (to be created).

### With `ISummaryService` (Context Compaction)

Before prompt composition, runtime checks if compaction is needed:

```csharp
if (context.Messages.Count > SummarizationThreshold)
{
    var summary = await _summaryService.SummarizeAsync(oldMessages);
    // Store and use summary
}
```

### With `IPromptComposer` (Prompt Assembly)

Before every model call:

```csharp
var composedMessages = await _promptComposer.ComposeAsync(
    bootstrapContext: loadedBootstrapFiles,
    sessionSummary: compactionResult,
    conversationHistory: recentMessages,
    userMessage: currentMessage
);
```

---

## Isolated Sessions

For webhooks, scheduled jobs, and clean execution contexts:

```csharp
var isolated = await sessionManager.CreateIsolatedSessionAsync(new IsolatedSessionOptions
{
    WorkspacePath = "/path/to/job/workspace",
    InheritSkills = false,   // Clean skill state
    InheritMemory = false,   // No prior history
    Ttl = TimeSpan.FromHours(1)  // Auto-cleanup
});

// Agent runs in sandboxed context; no cross-contamination
```

See `docs/architecture/runtime-flow.md` → "Isolated Session Flow" for detailed diagram.

---

## Error Handling

### Model Unavailable

If `IAgentProvider` is unavailable or returns an error:

```csharp
try
{
    var response = await _provider.StreamAsync(request);
}
catch (ModelProviderUnavailableException)
{
    // Handled by RuntimeAgentProvider fallback chain
}
```

### Tool Execution Failure

If a tool fails:

```csharp
var toolResult = await _toolExecutor.ExecuteAsync(toolCall);
// If tool threw, result.Success = false
// Tool result appended to messages; model sees the error
// Model can retry with different tool or different input
```

### Context Overflow

If conversation grows beyond compaction, oldest messages are summarized. If summary also fails, oldest messages are truncated.

---

## Key Design Principles

1. **Stable Public API:** `IAgentOrchestrator` never changes. Internal `IAgentRuntime` evolves.
2. **Framework Agnostic:** Adapter pattern (`ModelClientChatClientAdapter`) bridges any model to Agent Framework.
3. **Streaming-first:** HTTP NDJSON pipeline is native; non-streaming is a wrapper around streaming.
4. **Error Resilient:** Tool failures are graceful; model gets feedback and can retry.
5. **Context-aware:** Bootstrap files, session summaries, and skill context are always available to the model.
6. **Efficient:** Skill context injected once per turn; subsequent iterations skip re-injection.

---

## References

- **Gateway integration:** `src/OpenClawNet.Gateway/Program.cs` (line 128: `.AddAgentRuntime()`)
- **Endpoint wiring:** `src/OpenClawNet.Gateway/Endpoints/ChatEndpoints.cs`, `ChatStreamEndpoints.cs`
- **Test integration:** `tests/OpenClawNet.IntegrationTests/` (agent runtime tests)
- **Related docs:** `docs/architecture/provider-model.md`, `components.md`, `runtime-flow.md`
