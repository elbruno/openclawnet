namespace OpenClawNet.Agent.ToolApproval;

/// <summary>
/// Records every approval decision (user click, timeout auto-deny, session-memory hit)
/// to the <c>ToolApprovalLogs</c> table. Concept-review §4a — adopted for governance
/// and demo storytelling. Implementations must be best-effort: a logging failure must
/// never fail the parent tool call.
/// </summary>
public interface IToolApprovalAuditor
{
    Task RecordAsync(ToolApprovalAuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable description of one approval decision passed to <see cref="IToolApprovalAuditor"/>.
/// </summary>
public sealed record ToolApprovalAuditEntry(
    Guid RequestId,
    Guid SessionId,
    string ToolName,
    string? AgentProfileName,
    bool Approved,
    bool RememberForSession,
    ToolApprovalAuditSource Source,
    string? ToolArgsJson = null);

/// <summary>How the recorded decision was reached.</summary>
public enum ToolApprovalAuditSource
{
    User = 0,
    Timeout = 1,
    SessionMemory = 2,
}
