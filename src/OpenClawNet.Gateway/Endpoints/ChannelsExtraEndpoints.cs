using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

public static class ChannelsExtraEndpoints
{
    public static void MapChannelsExtraEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/channels").WithTags("Channels");

        // GET /api/channels/{jobId}/stats — message count, last activity, total artifacts
        group.MapGet("/{jobId:guid}/stats", async (
            Guid jobId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            var job = await db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is null)
                return Results.NotFound();

            var runCount = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .CountAsync();

            var eventCount = await db.Set<JobRunEvent>()
                .Where(e => e.Run.JobId == jobId)
                .CountAsync();

            var artifactStats = await db.JobRunArtifacts
                .Where(a => a.JobId == jobId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    TotalSizeBytes = g.Sum(a => a.ContentSizeBytes)
                })
                .FirstOrDefaultAsync();

            var lastActivity = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.StartedAt)
                .Select(r => r.StartedAt)
                .FirstOrDefaultAsync();

            return Results.Ok(new ChannelStatsDto(
                jobId,
                job.Name,
                runCount,
                eventCount,
                artifactStats?.Count ?? 0,
                artifactStats?.TotalSizeBytes ?? 0,
                lastActivity
            ));
        })
        .WithName("GetChannelStats")
        .WithDescription("Get aggregate statistics for a channel (run count, event count, artifact count/size, last activity)");

        // POST /api/channels/{jobId}/clear — wipe artifacts/runs for a channel (debugging convenience)
        group.MapPost("/{jobId:guid}/clear", async (
            Guid jobId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (job is null)
                return Results.NotFound();

            int artifactCount, eventCount, runCount;

            // ExecuteDeleteAsync is not supported by InMemory provider
            if (db.Database.ProviderName?.Contains("InMemory") == true)
            {
                // Fall back to RemoveRange for non-relational providers
                var artifacts = await db.JobRunArtifacts
                    .Where(a => a.JobId == jobId)
                    .ToListAsync();
                artifactCount = artifacts.Count;
                db.JobRunArtifacts.RemoveRange(artifacts);

                var events = await db.Set<JobRunEvent>()
                    .Where(e => e.Run.JobId == jobId)
                    .ToListAsync();
                eventCount = events.Count;
                db.Set<JobRunEvent>().RemoveRange(events);

                var runs = await db.JobRuns
                    .Where(r => r.JobId == jobId)
                    .ToListAsync();
                runCount = runs.Count;
                db.JobRuns.RemoveRange(runs);

                await db.SaveChangesAsync();
            }
            else
            {
                // Use ExecuteDeleteAsync for relational providers (faster, single SQL DELETE)
                artifactCount = await db.JobRunArtifacts
                    .Where(a => a.JobId == jobId)
                    .ExecuteDeleteAsync();

                eventCount = await db.Set<JobRunEvent>()
                    .Where(e => e.Run.JobId == jobId)
                    .ExecuteDeleteAsync();

                runCount = await db.JobRuns
                    .Where(r => r.JobId == jobId)
                    .ExecuteDeleteAsync();
            }

            return Results.Ok(new { 
                jobId, 
                runsDeleted = runCount, 
                eventsDeleted = eventCount, 
                artifactsDeleted = artifactCount 
            });
        })
        .WithName("ClearChannel")
        .WithDescription("Delete all runs, events, and artifacts for a channel (debugging/cleanup tool)");

        // GET /api/channels/{jobId}/artifacts — all artifacts for the channel (not per-run)
        group.MapGet("/{jobId:guid}/artifacts", async (
            Guid jobId,
            [FromQuery] int? limit,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            var job = await db.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is null)
                return Results.NotFound();

            var artifacts = await db.JobRunArtifacts
                .AsNoTracking()
                .Where(a => a.JobId == jobId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(limit ?? 100)
                .Select(a => new ChannelArtifactDto(
                    a.Id,
                    a.JobRunId,
                    a.Title,
                    a.ArtifactType.ToString(),
                    a.ContentSizeBytes,
                    a.CreatedAt
                ))
                .ToListAsync();

            return Results.Ok(new { jobId, jobName = job.Name, artifacts });
        })
        .WithName("GetChannelArtifacts")
        .WithDescription("Get all artifacts for a channel (across all runs), ordered by creation date");
    }

    private static bool IsLoopbackRequest(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress is null
        || System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress);
}

public sealed record ChannelStatsDto(
    Guid JobId,
    string JobName,
    int RunCount,
    int EventCount,
    int ArtifactCount,
    long TotalArtifactSizeBytes,
    DateTime? LastActivity
);

public sealed record ChannelArtifactDto(
    Guid Id,
    Guid RunId,
    string Title,
    string Kind,
    long SizeBytes,
    DateTime CreatedAt
);
