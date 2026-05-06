# Diagram — Tool approval sequence

> Mirrored from [`docs/architecture/20260425-concept-review.md`](../20260425-concept-review.md) §4a.

```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant W as Web (Chat)
    participant G as Gateway /api/chat/stream
    participant R as DefaultAgentRuntime
    participant S as IToolResultSanitizer
    participant A as IToolApprovalAuditor
    participant M as MCP Tool
    participant DB as ToolApprovalLog (SQLite)

    U->>W: Prompt
    W->>G: POST NDJSON stream
    G->>R: Run agent loop
    R-->>G: stream { type: "approval", expiresAt }
    G-->>W: NDJSON line
    W-->>U: Approval card with countdown

    alt User approves before timeout
        U->>W: Approve
        W->>G: POST /api/tools/approval (Approve)
        G->>R: Resolve approval
        R->>M: Invoke tool
        M-->>R: Raw result
        R->>S: Sanitize
        S-->>R: Cleaned result
        R-->>G: stream tool message
        R->>A: Append (Approved, ChangedBy=User)
        A->>DB: INSERT row
    else User denies
        U->>W: Deny
        W->>G: POST /api/tools/approval (Deny)
        G->>R: Resolve approval
        R-->>G: stream "denied" message
        R->>A: Append (Denied, ChangedBy=User)
        A->>DB: INSERT row
    else No response within timeout
        Note over R: CancellationTokenSource.CancelAfter fires
        R-->>G: stream "denied (timeout)" message
        R->>A: Append (Denied, ChangedBy=Timeout)
        A->>DB: INSERT row
    end
```

## Resolution precedence

When the runtime decides whether a tool call needs human approval, it
checks (highest to lowest priority):

1. Tool-level override on the agent profile.
2. `McpServerDefinition.DefaultRequireApproval` (server-level default).
3. Agent profile default.
4. Tool metadata `RequiresApproval`.

First non-null wins.
