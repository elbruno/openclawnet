namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Audit log row recording one transition of a <see cref="ScheduledJob.Status"/>.
/// Concept-review §4b — adopted to support compliance, debugging, and demo storytelling.
/// Written by the job-status endpoints whenever <see cref="JobStatusTransitions"/> approves a change.
/// </summary>
public sealed class JobDefinitionStateChange
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK → <see cref="ScheduledJob.Id"/>.</summary>
    public Guid JobId { get; set; }

    public JobStatus FromStatus { get; set; }
    public JobStatus ToStatus { get; set; }

    /// <summary>Optional user-provided reason for the change.</summary>
    public string? Reason { get; set; }

    /// <summary>User or system identifier that initiated the change.</summary>
    public string? ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public ScheduledJob? Job { get; set; }
}
