namespace OpenClawNet.Storage.Entities;

/// <summary>Lifecycle states for a scheduled job.</summary>
public enum JobStatus
{
    /// <summary>Job created but not yet activated. Editable. Not polled by scheduler.</summary>
    Draft = 0,
    /// <summary>Scheduler will poll and execute this job on schedule.</summary>
    Active = 1,
    /// <summary>Temporarily suspended. Not polled. Can be resumed.</summary>
    Paused = 2,
    /// <summary>Permanently stopped by user. Terminal state.</summary>
    Cancelled = 3,
    /// <summary>Schedule exhausted or one-shot completed. Terminal state.</summary>
    Completed = 4,
    /// <summary>Hidden from default UI lists but preserved for audit. Terminal.</summary>
    Archived = 5
}
