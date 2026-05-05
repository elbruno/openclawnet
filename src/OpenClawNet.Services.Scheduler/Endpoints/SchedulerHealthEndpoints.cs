using Microsoft.EntityFrameworkCore;
using OpenClawNet.Services.Scheduler.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Services.Scheduler.Endpoints;

/// <summary>
/// Diagnostics and health endpoints for the scheduler service.
/// </summary>
public static class SchedulerHealthEndpoints
{
    public static void MapSchedulerHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/scheduler/health", async (
            IDbContextFactory<OpenClawDbContext> dbFactory,
            SchedulerRunState runState) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // Get running jobs count
            var runningJobsCount = await db.Jobs
                .CountAsync(j => j.Status == JobStatus.Active);

            // Get stuck runs (running > 30 minutes)
            var thirtyMinutesAgo = DateTime.UtcNow.AddMinutes(-30);
            var stuckRuns = await db.JobRuns
                .Where(r => r.Status.ToLower() == "running" && r.StartedAt < thirtyMinutesAgo)
                .Select(r => new StuckRunInfo
                {
                    RunId = r.Id,
                    JobId = r.JobId,
                    JobName = r.Job!.Name,
                    StartedAt = r.StartedAt,
                    MinutesRunning = (int)(DateTime.UtcNow - r.StartedAt).TotalMinutes
                })
                .Take(10)
                .ToListAsync();

            // Get recent failed runs (last 10)
            var recentFailed = await db.JobRuns
                .Where(r => r.Status.ToLower() == "failed")
                .OrderByDescending(r => r.CompletedAt ?? r.StartedAt)
                .Take(10)
                .Select(r => new FailedRunInfo
                {
                    RunId = r.Id,
                    JobId = r.JobId,
                    JobName = r.Job!.Name,
                    StartedAt = r.StartedAt,
                    CompletedAt = r.CompletedAt,
                    Error = r.Error
                })
                .ToListAsync();

            // Get next scheduled jobs (next 5)
            var nextScheduled = await db.Jobs
                .Where(j => j.Status == JobStatus.Active && j.NextRunAt.HasValue)
                .OrderBy(j => j.NextRunAt)
                .Take(5)
                .Select(j => new ScheduledJobInfo
                {
                    JobId = j.Id,
                    JobName = j.Name,
                    NextRunAt = j.NextRunAt!.Value,
                    IsRecurring = j.IsRecurring,
                    CronExpression = j.CronExpression
                })
                .ToListAsync();

            // Get current running runs count
            var currentRunningRuns = await db.JobRuns
                .CountAsync(r => r.Status.ToLower() == "running");

            // Get scheduler state from the runState service
            var schedulerState = runState.GetState();

            return Results.Ok(new SchedulerHealthResponse
            {
                Status = "healthy",
                Timestamp = DateTime.UtcNow,
                SchedulerRunning = schedulerState.IsRunning,
                ActiveJobsCount = runningJobsCount,
                CurrentRunningRuns = currentRunningRuns,
                StuckRunsCount = stuckRuns.Count,
                StuckRuns = stuckRuns,
                RecentFailedRuns = recentFailed,
                NextScheduledJobs = nextScheduled,
                SchedulerState = new
                {
                    schedulerState.IsRunning,
                    schedulerState.LastPollAt,
                    schedulerState.PollingIntervalMs
                }
            });
        })
        .WithName("GetSchedulerHealth")
        .WithDescription("Get scheduler health status, stuck runs, recent failures, and next scheduled jobs.")
        .WithTags("Scheduler");
    }
}

public sealed record SchedulerHealthResponse
{
    public required string Status { get; init; }
    public DateTime Timestamp { get; init; }
    public bool SchedulerRunning { get; init; }
    public int ActiveJobsCount { get; init; }
    public int CurrentRunningRuns { get; init; }
    public int StuckRunsCount { get; init; }
    public List<StuckRunInfo> StuckRuns { get; init; } = [];
    public List<FailedRunInfo> RecentFailedRuns { get; init; } = [];
    public List<ScheduledJobInfo> NextScheduledJobs { get; init; } = [];
    public object? SchedulerState { get; init; }
}

public sealed record StuckRunInfo
{
    public Guid RunId { get; init; }
    public Guid JobId { get; init; }
    public required string JobName { get; init; }
    public DateTime StartedAt { get; init; }
    public int MinutesRunning { get; init; }
}

public sealed record FailedRunInfo
{
    public Guid RunId { get; init; }
    public Guid JobId { get; init; }
    public required string JobName { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Error { get; init; }
}

public sealed record ScheduledJobInfo
{
    public Guid JobId { get; init; }
    public required string JobName { get; init; }
    public DateTime NextRunAt { get; init; }
    public bool IsRecurring { get; init; }
    public string? CronExpression { get; init; }
}
