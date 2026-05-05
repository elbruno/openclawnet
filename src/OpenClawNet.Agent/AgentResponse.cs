using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Agent;

public sealed record AgentResponse
{
    public required string Content { get; init; }
    public IReadOnlyList<ToolResult> ToolResults { get; init; } = [];
    public int ToolCallCount { get; init; }
    public int TotalTokens { get; init; }
}

public sealed record AgentStreamEvent
{
    public AgentStreamEventType Type { get; init; }
    public string? Content { get; init; }
    public string? ToolName { get; init; }
    public string? ToolDescription { get; init; }
    public ToolResult? ToolResult { get; init; }
    public bool IsComplete { get; init; }

    /// <summary>
    /// Correlation id for a <see cref="AgentStreamEventType.ToolApprovalRequest"/> event.
    /// Echoed back by the client when POSTing the approval/denial decision.
    /// </summary>
    public Guid? RequestId { get; init; }

    /// <summary>
    /// JSON-serialized arguments the model wants to call the tool with.
    /// Surfaced in the approval card so the user can review what's about to run.
    /// </summary>
    public string? ToolArgsJson { get; init; }

    /// <summary>
    /// UTC instant at which the approval prompt will auto-deny if no decision arrives.
    /// Null = no timeout (legacy indefinite wait). Concept-review §4a/UX.
    /// </summary>
    public DateTime? ApprovalExpiresAt { get; init; }
    
    // Phase B: Tool approval resolution fields
    public bool? Approved { get; init; }
    public string? DecisionSource { get; init; }
    public DateTime? DecidedAt { get; init; }
}

public enum AgentStreamEventType
{
    ContentDelta,
    ToolApprovalRequest,
    ToolApprovalResolved,
    ToolCallStart,
    ToolCallComplete,
    Complete,
    Error
}
