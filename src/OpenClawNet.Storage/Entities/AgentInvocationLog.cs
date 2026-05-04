namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Shared telemetry record for any agent invocation — covers both <see cref="ChatSession"/>
/// turns and <see cref="JobRun"/> executions. Concept-review §4c (Option B: Sibling Model)
/// — adopted to provide unified observability without forcing chat into the job model.
/// </summary>
/// <remarks>
/// Written best-effort, asynchronously, from both code paths. A failed write here
/// must never fail the parent invocation.
/// </remarks>
public sealed class AgentInvocationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Whether this row originated from chat or a job run.</summary>
    public AgentInvocationKind Kind { get; set; }

    /// <summary>
    /// Cross-link back to the source entity:
    /// <see cref="ChatSession.Id"/> when <see cref="Kind"/> is <see cref="AgentInvocationKind.Chat"/>,
    /// <see cref="JobRun.Id"/> when <see cref="Kind"/> is <see cref="AgentInvocationKind.JobRun"/>.
    /// </summary>
    public Guid SourceId { get; set; }

    public string? AgentProfileName { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }

    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
    public int? LatencyMs { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Optional error message when the invocation failed.</summary>
    public string? Error { get; set; }
}

public enum AgentInvocationKind
{
    Chat = 0,
    JobRun = 1,
}
