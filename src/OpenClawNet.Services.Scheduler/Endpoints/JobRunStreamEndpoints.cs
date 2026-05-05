using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Services.Scheduler.Endpoints;

/// <summary>
/// NDJSON live-console endpoint for a single in-flight <see cref="JobRun"/>.
/// Polls the database (JobRun row + appended JobRunEvents) on a short cadence
/// and pushes incremental updates to the browser following the NDJSON pattern
/// established by <c>/api/chat/stream</c>. SignalR is intentionally NOT used.
/// </summary>
public static class JobRunStreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int PollDelayMs = 1000;
    private const int MaxStreamSeconds = 60 * 30; // 30 min hard cap

    public static void MapJobRunStreamEndpoints(this WebApplication app)
    {
        app.MapGet("/api/scheduler/jobs/{jobId:guid}/runs/{runId:guid}/stream", async (
            Guid jobId,
            Guid runId,
            HttpContext httpContext,
            IDbContextFactory<OpenClawDbContext> dbFactory,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.ContentType = "application/x-ndjson";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var startedAtUtc = DateTime.UtcNow;
            var lastSequence = -1;
            string? lastStatus = null;
            string? lastResult = null;
            string? lastError = null;
            var sentSnapshot = false;

            while (!cancellationToken.IsCancellationRequested
                && (DateTime.UtcNow - startedAtUtc).TotalSeconds < MaxStreamSeconds)
            {
                JobRun? run;
                List<JobRunEvent> newEvents;
                await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
                {
                    run = await db.JobRuns
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, cancellationToken);

                    if (run is null)
                    {
                        await WriteLine(httpContext, LiveConsoleEvent.NotFound(runId), cancellationToken);
                        return;
                    }

                    newEvents = await db.Set<JobRunEvent>()
                        .AsNoTracking()
                        .Where(e => e.JobRunId == runId && e.Sequence > lastSequence)
                        .OrderBy(e => e.Sequence)
                        .ToListAsync(cancellationToken);
                }

                if (!sentSnapshot)
                {
                    await WriteLine(httpContext, LiveConsoleEvent.Snapshot(run), cancellationToken);
                    sentSnapshot = true;
                    lastStatus = run.Status;
                    lastResult = run.Result;
                    lastError = run.Error;
                }

                foreach (var ev in newEvents)
                {
                    await WriteLine(httpContext, LiveConsoleEvent.FromEvent(ev), cancellationToken);
                    lastSequence = ev.Sequence;
                }

                var statusChanged = !string.Equals(run.Status, lastStatus, StringComparison.Ordinal)
                                    || !string.Equals(run.Result, lastResult, StringComparison.Ordinal)
                                    || !string.Equals(run.Error, lastError, StringComparison.Ordinal);
                if (statusChanged)
                {
                    await WriteLine(httpContext, LiveConsoleEvent.StatusUpdate(run), cancellationToken);
                    lastStatus = run.Status;
                    lastResult = run.Result;
                    lastError = run.Error;
                }

                if (!string.Equals(run.Status, "running", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteLine(httpContext, LiveConsoleEvent.Complete(run), cancellationToken);
                    return;
                }

                try { await Task.Delay(PollDelayMs, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        })
        .WithName("StreamJobRun")
        .WithDescription("NDJSON live-console for a single JobRun. Polls DB for events + status changes until the run terminates.");
    }

    private static async Task WriteLine(HttpContext ctx, LiveConsoleEvent evt, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(evt, JsonOpts);
        await ctx.Response.WriteAsync(line + "\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}

/// <summary>
/// Wire-format DTO for a single line on the NDJSON live-console stream.
/// One of: <c>snapshot</c> | <c>event</c> | <c>status</c> | <c>complete</c> | <c>not_found</c>.
/// </summary>
public sealed record LiveConsoleEvent
{
    public required string Type { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid? RunId { get; init; }
    public string? Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public long? ElapsedMs { get; init; }
    public int? Sequence { get; init; }
    public string? Kind { get; init; }
    public string? ToolName { get; init; }
    public string? Message { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public int? DurationMs { get; init; }
    public int? TokensUsed { get; init; }

    /// <summary>
    /// Computes elapsed milliseconds for a run, guarding against uninitialized/default StartedAt.
    /// Returns 0 if StartedAt is default(DateTime), otherwise the duration from StartedAt to endTime.
    /// </summary>
    private static long ComputeElapsedMs(DateTime startedAt, DateTime? endTime)
    {
        if (startedAt == default)
            return 0;

        var elapsed = (endTime ?? DateTime.UtcNow) - startedAt;
        return Math.Max(0, (long)elapsed.TotalMilliseconds);
    }

    internal static LiveConsoleEvent Snapshot(JobRun run) => new()
    {
        Type = "snapshot",
        RunId = run.Id,
        Status = run.Status,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        ElapsedMs = ComputeElapsedMs(run.StartedAt, run.CompletedAt),
        Result = run.Result,
        Error = run.Error
    };

    internal static LiveConsoleEvent StatusUpdate(JobRun run) => new()
    {
        Type = "status",
        RunId = run.Id,
        Status = run.Status,
        ElapsedMs = ComputeElapsedMs(run.StartedAt, run.CompletedAt),
        Result = run.Result,
        Error = run.Error
    };

    internal static LiveConsoleEvent FromEvent(JobRunEvent ev) => new()
    {
        Type = "event",
        Timestamp = ev.Timestamp,
        Sequence = ev.Sequence,
        Kind = ev.Kind,
        ToolName = ev.ToolName,
        Message = ev.Message,
        DurationMs = ev.DurationMs,
        TokensUsed = ev.TokensUsed
    };

    internal static LiveConsoleEvent Complete(JobRun run) => new()
    {
        Type = "complete",
        RunId = run.Id,
        Status = run.Status,
        CompletedAt = run.CompletedAt ?? DateTime.UtcNow,
        ElapsedMs = ComputeElapsedMs(run.StartedAt, run.CompletedAt),
        Result = run.Result,
        Error = run.Error
    };

    internal static LiveConsoleEvent NotFound(Guid runId) => new()
    {
        Type = "not_found",
        RunId = runId,
        Message = $"Run {runId} not found."
    };
}
