namespace OpenClawNet.Storage.Entities;

public static class JobStatusTransitions
{
    private static readonly HashSet<(JobStatus From, JobStatus To)> _allowed = new()
    {
        (JobStatus.Draft, JobStatus.Active),
        (JobStatus.Draft, JobStatus.Cancelled),
        (JobStatus.Active, JobStatus.Paused),
        (JobStatus.Active, JobStatus.Cancelled),
        (JobStatus.Active, JobStatus.Completed),
        (JobStatus.Paused, JobStatus.Active),
        (JobStatus.Paused, JobStatus.Cancelled),
        // Phase 1 (concept-review §4b): archive lifecycle for cleanup of terminal jobs.
        (JobStatus.Completed, JobStatus.Archived),
        (JobStatus.Cancelled, JobStatus.Archived),
    };

    public static bool IsAllowed(JobStatus from, JobStatus to) =>
        _allowed.Contains((from, to));

    public static bool IsTerminal(JobStatus status) =>
        status is JobStatus.Completed or JobStatus.Cancelled or JobStatus.Archived;

    public static bool IsEditable(JobStatus status) =>
        status is JobStatus.Draft or JobStatus.Paused;

    /// <summary>Whether a job in this status should be hidden from default UI lists.</summary>
    public static bool IsHiddenByDefault(JobStatus status) =>
        status is JobStatus.Archived;
}
