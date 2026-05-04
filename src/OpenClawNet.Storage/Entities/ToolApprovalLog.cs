namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Audit log row for a tool-approval decision. Concept-review §4a — adopted to
/// give "who approved what, when" visibility for governance and demos.
/// Written from <c>ToolApprovalCoordinator.TryResolve</c> and on auto-deny timeouts.
/// </summary>
public sealed class ToolApprovalLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The original approval request id surfaced to the UI as <c>requestId</c>.</summary>
    public Guid RequestId { get; set; }

    /// <summary>Conversation / chat session that requested the tool.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Tool's fully qualified name (e.g. <c>shell.exec</c>).</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Agent profile that initiated the call.</summary>
    public string? AgentProfileName { get; set; }

    /// <summary>true=approved, false=denied (whether by user click or timeout).</summary>
    public bool Approved { get; set; }

    /// <summary>true if the user ticked "remember for this session".</summary>
    public bool RememberForSession { get; set; }

    /// <summary>How the decision was reached.</summary>
    public ApprovalDecisionSource Source { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>How a tool-approval decision was reached.</summary>
public enum ApprovalDecisionSource
{
    /// <summary>User clicked Approve/Deny in the UI.</summary>
    User = 0,
    /// <summary>Approval prompt timed out and was auto-denied.</summary>
    Timeout = 1,
    /// <summary>Resolved from a previously remembered session approval.</summary>
    SessionMemory = 2,
}
