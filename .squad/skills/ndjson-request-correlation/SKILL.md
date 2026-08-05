# NDJSON Request/Response Correlation Pattern

@extracted: 2026-04-27, petey, from tool approval mid-stream implementation  
@validated-by: petey (high), irving (high)

**Pattern:** Correlating asynchronous HTTP POST requests with pending server-side operations initiated by a streaming NDJSON response.

**Context:** OpenClawNet chat streaming uses HTTP NDJSON (`POST /api/chat/stream`) instead of SignalR. When the server needs user input mid-stream (e.g., tool approval), it emits a correlation ID in the stream, pauses, and waits for a separate HTTP POST carrying that ID.

---

## Architecture

### Server-Side Flow

1. **Generate a fresh correlation ID** (typically a `Guid`).
2. **Register the pending request** in a coordinator service (e.g., `IToolApprovalCoordinator`) with a `TaskCompletionSource<TDecision>` keyed by the ID.
3. **Emit the ID in the NDJSON stream** (e.g., `{ "type": "tool_approval", "requestId": "3a7f..." }`).
4. **Await the TaskCompletionSource** in the streaming handler.
5. **Separate HTTP endpoint** receives the POST with the correlation ID, looks up the TCS, and sets its result.
6. **Streaming handler unblocks** and continues.

### Client-Side Flow

1. **Receive NDJSON event** with correlation ID.
2. **Display UI** (e.g., Approve/Deny buttons) with the ID stored in component state.
3. **On user action**, POST to the resolution endpoint with the correlation ID and decision payload.
4. **Continue processing** the stream.

---

## Type Safety Contract

**Critical:** The correlation ID must have **identical types** on both sides of the POST:

- **Client payload:** `{ requestId: Guid, ... }`
- **Server DTO:** `public Guid RequestId { get; init; }`

**Common pitfall:** Storing the ID as a `string` on the client (e.g., `reqId.ToString()`) but expecting a `Guid` on the server. JSON serialization will produce a string value, causing deserialization failure and breaking the correlation.

**Fix:** Use the same type (`Guid`) everywhere. If you must store as a string for Blazor component parameter binding, parse it back to `Guid` before POSTing.

---

## Example: Tool Approval Flow

### Server: Emit approval request

**File:** `src/OpenClawNet.Agent/DefaultAgentRuntime.cs` (line ~480-494)

```csharp
var requestId = Guid.NewGuid();
var approvalTask = _approvalCoordinator.RequestApprovalAsync(requestId, cancellationToken);

yield return new AgentStreamEvent
{
    Type = AgentStreamEventType.ToolApprovalRequest,
    ToolName = toolCall.Name,
    RequestId = requestId  // Guid
};

var decision = await approvalTask;  // Blocks until resolved
```

### Server: Resolution endpoint

**File:** `src/OpenClawNet.Gateway/Endpoints/ToolApprovalEndpoints.cs` (line ~21-48)

```csharp
app.MapPost("/api/chat/tool-approval", (
    ToolApprovalDecisionRequest body,  // { Guid RequestId, bool Approved, bool RememberForSession }
    IToolApprovalCoordinator coordinator) =>
{
    var resolved = coordinator.TryResolve(body.RequestId, new ApprovalDecision(...));
    return resolved ? Results.Ok(...) : Results.NotFound(...);
});
```

### Coordinator: Pending request registry

**File:** `src/OpenClawNet.Agent/ToolApproval/ToolApprovalCoordinator.cs` (line ~13-70)

```csharp
private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ApprovalDecision>> _pending = new();

public Task<ApprovalDecision> RequestApprovalAsync(Guid requestId, CancellationToken cancellationToken)
{
    var tcs = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pending.TryAdd(requestId, tcs);
    return tcs.Task;
}

public bool TryResolve(Guid requestId, ApprovalDecision decision)
{
    if (!_pending.TryRemove(requestId, out var tcs)) return false;
    return tcs.TrySetResult(decision);
}
```

### Client: Receive and store

**File:** `src/OpenClawNet.Web/Components/Pages/Chat.razor` (line ~508-521)

```csharp
case "tool_approval":
    if (evt.RequestId is { } reqId && reqId != Guid.Empty)
    {
        PendingApproval = new PendingApprovalRequest(
            reqId,  // Guid (NOT reqId.ToString()!)
            evt.ToolName ?? string.Empty,
            evt.ToolDescription,
            evt.ToolArgsJson);
    }
    await InvokeAsync(StateHasChanged);
    break;
```

### Client: Submit decision

**File:** `src/OpenClawNet.Web/Components/Pages/Chat.razor` (line ~236-264)

```csharp
private async Task SubmitToolDecisionAsync(bool approved, bool rememberForSession)
{
    var pending = PendingApproval;
    if (pending is null || pending.RequestId == Guid.Empty) return;

    var client = HttpClientFactory.CreateClient("gateway");
    var payload = new
    {
        requestId = pending.RequestId,  // Guid
        approved,
        rememberForSession
    };
    await client.PostAsJsonAsync("api/chat/tool-approval", payload);
}
```

---

## Checklist for Implementing This Pattern

1. [ ] Choose a correlation ID type (prefer `Guid` for uniqueness).
2. [ ] Create a coordinator service with:
   - `ConcurrentDictionary<Guid, TaskCompletionSource<TDecision>>` for pending requests.
   - `RequestAsync(Guid id)` → registers TCS and returns Task.
   - `TryResolve(Guid id, TDecision decision)` → removes TCS and sets result.
3. [ ] In the streaming handler:
   - Generate fresh ID.
   - Register with coordinator.
   - Emit ID in NDJSON event.
   - Await the Task.
4. [ ] Create a resolution endpoint:
   - Accept DTO with `Guid RequestId` and decision payload.
   - Call `coordinator.TryResolve(requestId, decision)`.
   - Return 200 if resolved, 404 if unknown.
5. [ ] On the client:
   - Parse NDJSON event.
   - Store ID as `Guid` (not string).
   - POST to resolution endpoint with `{ requestId: Guid, ... }`.
6. [ ] Test type contract: Ensure JSON payload deserializes correctly on server.

---

## Troubleshooting

**Symptom:** POST succeeds (200 OK) but stream never unblocks.

- **Cause 1:** Type mismatch — client sends string, server expects Guid. Deserialization fails silently.
  - **Fix:** Use `Guid` everywhere. Check network tab for actual JSON payload.
- **Cause 2:** Race condition — POST arrives before `RequestAsync` registers the TCS.
  - **Fix:** Call `RequestAsync` **before** yielding the NDJSON event.
- **Cause 3:** Different correlation IDs — client stores one value, sends another.
  - **Fix:** Log requestId on both sides. Verify they match.

**Symptom:** POST returns 404.

- **Cause:** Stale UI (page refresh), server restart (TCS lost), or request already resolved.
  - **Fix:** Clear pending state on stream end/error. Display user-friendly message on 404.

---

## Related Files

- `src/OpenClawNet.Agent/DefaultAgentRuntime.cs` — approval request emission
- `src/OpenClawNet.Agent/ToolApproval/IToolApprovalCoordinator.cs` — coordinator interface
- `src/OpenClawNet.Agent/ToolApproval/ToolApprovalCoordinator.cs` — in-memory coordinator
- `src/OpenClawNet.Gateway/Endpoints/ChatStreamEndpoints.cs` — NDJSON streaming
- `src/OpenClawNet.Gateway/Endpoints/ToolApprovalEndpoints.cs` — resolution endpoint
- `src/OpenClawNet.Web/Components/Pages/Chat.razor` — client streaming + POST
- `src/OpenClawNet.Web/Components/ToolApprovalCard.razor` — approval UI
