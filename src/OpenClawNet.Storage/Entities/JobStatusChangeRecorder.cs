using Microsoft.EntityFrameworkCore;

namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Helper that persists a <see cref="JobDefinitionStateChange"/> row each time
/// a <see cref="ScheduledJob"/>'s status moves between two states. Concept-review §4b
/// — adopted as the single chokepoint for status-history audit, so we never end up
/// with status changes that aren't reflected in the audit log.
/// </summary>
public static class JobStatusChangeRecorder
{
    /// <summary>
    /// Mutates <paramref name="job"/>.Status to <paramref name="to"/> and appends an
    /// audit row to <paramref name="db"/>. Caller is responsible for SaveChangesAsync.
    /// Throws <see cref="InvalidOperationException"/> if the transition isn't allowed.
    /// </summary>
    public static void RecordTransition(
        OpenClawDbContext db,
        ScheduledJob job,
        JobStatus to,
        string? reason = null,
        string? changedBy = null)
    {
        var from = job.Status;
        if (from == to) return; // no-op — don't pollute the audit log with non-changes

        if (!JobStatusTransitions.IsAllowed(from, to))
        {
            throw new InvalidOperationException(
                $"Job state transition '{from}' → '{to}' is not allowed.");
        }

        job.Status = to;
        db.Set<JobDefinitionStateChange>().Add(new JobDefinitionStateChange
        {
            JobId = job.Id,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            ChangedBy = changedBy,
            ChangedAt = DateTime.UtcNow,
        });
    }
}
