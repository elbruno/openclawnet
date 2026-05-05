using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// NDJSON streaming endpoint that follows the currently-active run for a job.
/// Automatically switches to the next run when one completes.
/// </summary>
public static class JobStreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int PollDelayMs = 1000;
    private const int MaxStreamSeconds = 60 * 60; // 1 hour hard cap

    public static void MapJobStreamEndpoints(this WebApplication app)
    {
        // GET /api/jobs/{jobId}/stream — stream events from whichever run is currently active
        app.MapGet("/api/jobs/{jobId:guid}/stream", async (
            Guid jobId,
            HttpContext httpContext,
            IDbContextFactory<OpenClawDbContext> dbFactory,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.ContentType = "application/x-ndjson";
            httpContext.Response.Headers.CacheControl = "no-cache";

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            
            var job = await db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

            if (job is null)
            {
                await WriteLine(httpContext, JobStreamEvent.NotFound(jobId), cancellationToken);
                return;
            }

            var startedAtUtc = DateTime.UtcNow;
            Guid? currentRunId = null;
            var lastSequence = -1;
            string? lastStatus = null;

            while (!cancellationToken.IsCancellationRequested
                && (DateTime.UtcNow - startedAtUtc).TotalSeconds < MaxStreamSeconds)
            {
                // Find the most recent "running" run, or fallback to the most recent completed run
                JobRun? activeRun;
                await using (var dbCtx = await dbFactory.CreateDbContextAsync(cancellationToken))
                {
                    activeRun = await dbCtx.JobRuns
                        .AsNoTracking()
                        .Where(r => r.JobId == jobId)
                        .OrderByDescending(r => r.StartedAt)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (activeRun is null)
                {
                    await WriteLine(httpContext, JobStreamEvent.NoRuns(jobId), cancellationToken);
                    try { await Task.Delay(PollDelayMs, cancellationToken); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                // If we switched to a new run, reset state
                if (currentRunId != activeRun.Id)
                {
                    currentRunId = activeRun.Id;
                    lastSequence = -1;
                    lastStatus = null;
                    await WriteLine(httpContext, JobStreamEvent.RunSwitch(activeRun), cancellationToken);
                }

                // Fetch new events for the active run
                List<JobRunEvent> newEvents;
                await using (var dbCtx = await dbFactory.CreateDbContextAsync(cancellationToken))
                {
                    newEvents = await dbCtx.Set<JobRunEvent>()
                        .AsNoTracking()
                        .Where(e => e.JobRunId == currentRunId && e.Sequence > lastSequence)
                        .OrderBy(e => e.Sequence)
                        .ToListAsync(cancellationToken);
                }

                foreach (var ev in newEvents)
                {
                    await WriteLine(httpContext, JobStreamEvent.FromEvent(ev), cancellationToken);
                    lastSequence = ev.Sequence;
                }

                // Check for status changes
                var statusChanged = !string.Equals(activeRun.Status, lastStatus, StringComparison.Ordinal);
                if (statusChanged)
                {
                    await WriteLine(httpContext, JobStreamEvent.StatusUpdate(activeRun), cancellationToken);
                    lastStatus = activeRun.Status;
                }

                try { await Task.Delay(PollDelayMs, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }

            await WriteLine(httpContext, JobStreamEvent.Timeout(), cancellationToken);
        })
        .WithName("StreamJob")
        .WithDescription("NDJSON stream that follows the currently active run for a job, automatically switching runs");
    }

    private static async Task WriteLine(HttpContext ctx, JobStreamEvent evt, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(evt, JsonOpts);
        await ctx.Response.WriteAsync(line + "\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}

public sealed record JobStreamEvent
{
    public required string Type { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid? JobId { get; init; }
    public Guid? RunId { get; init; }
    public string? Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? Sequence { get; init; }
    public string? Kind { get; init; }
    public string? ToolName { get; init; }
    public string? Message { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public int? DurationMs { get; init; }

    internal static JobStreamEvent NotFound(Guid jobId) => new()
    {
        Type = "not_found",
        JobId = jobId,
        Message = $"Job {jobId} not found."
    };

    internal static JobStreamEvent NoRuns(Guid jobId) => new()
    {
        Type = "no_runs",
        JobId = jobId,
        Message = "Waiting for first run..."
    };

    internal static JobStreamEvent RunSwitch(JobRun run) => new()
    {
        Type = "run_switch",
        JobId = run.JobId,
        RunId = run.Id,
        Status = run.Status,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Message = $"Now following run {run.Id}"
    };

    internal static JobStreamEvent StatusUpdate(JobRun run) => new()
    {
        Type = "status",
        RunId = run.Id,
        Status = run.Status,
        Result = run.Result,
        Error = run.Error
    };

    internal static JobStreamEvent FromEvent(JobRunEvent ev) => new()
    {
        Type = "event",
        Timestamp = ev.Timestamp,
        Sequence = ev.Sequence,
        Kind = ev.Kind,
        ToolName = ev.ToolName,
        Message = ev.Message,
        DurationMs = ev.DurationMs
    };

    internal static JobStreamEvent Timeout() => new()
    {
        Type = "timeout",
        Message = "Stream timeout reached"
    };
}
