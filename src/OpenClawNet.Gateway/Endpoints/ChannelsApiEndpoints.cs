using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

public static class ChannelsApiEndpoints
{
    public static void MapChannelsApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/channels").WithTags("Channels");

        // GET /api/channels — list of channels (one per JobId that has artifacts) with last-run summary
        group.MapGet("/", async (
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            [FromQuery] int? limit,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            // Get all jobs that have artifacts
            var jobsWithArtifacts = await db.JobRunArtifacts
                .GroupBy(a => a.JobId)
                .Select(g => new
                {
                    JobId = g.Key,
                    LastArtifactDate = g.Max(a => a.CreatedAt),
                    ArtifactCount = g.Count()
                })
                .OrderByDescending(x => x.LastArtifactDate)
                .Take(limit ?? 50)
                .ToListAsync();

            var jobIds = jobsWithArtifacts.Select(j => j.JobId).ToList();
            var jobs = await db.Jobs
                .Where(j => jobIds.Contains(j.Id))
                .ToDictionaryAsync(j => j.Id);

            var channels = jobsWithArtifacts.Select(j => new ChannelSummaryDto(
                j.JobId,
                jobs.TryGetValue(j.JobId, out var job) ? job.Name : "Unknown Job",
                j.LastArtifactDate,
                j.ArtifactCount
            )).ToList();

            return Results.Ok(channels);
        })
        .WithName("ListChannels");

        // GET /api/channels/{jobId} — channel detail (job metadata + recent runs + artifact counts)
        group.MapGet("/{jobId:guid}", async (
            Guid jobId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            var job = await db.Jobs.FindAsync(jobId);
            if (job is null)
                return Results.NotFound();

            // Get recent runs with artifact counts
            var runs = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.StartedAt)
                .Take(20)
                .Select(r => new
                {
                    r.Id,
                    r.Status,
                    r.StartedAt,
                    r.CompletedAt,
                    ArtifactCount = db.JobRunArtifacts.Count(a => a.JobRunId == r.Id)
                })
                .ToListAsync();

            var detail = new ChannelDetailDto(
                job.Id,
                job.Name,
                job.Status.ToString().ToLowerInvariant(),
                job.Prompt,
                runs.Select(r => new ChannelRunSummaryDto(
                    r.Id,
                    r.Status,
                    r.StartedAt,
                    r.CompletedAt,
                    r.ArtifactCount
                )).ToList()
            );

            return Results.Ok(detail);
        })
        .WithName("GetChannelDetail");

        // GET /api/channels/{jobId}/view — channel detail with all artifacts for Razor pages
        group.MapGet("/{jobId:guid}/view", async (
            Guid jobId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            var job = await db.Jobs.FindAsync(jobId);
            if (job is null)
                return Results.NotFound();

            // Fetch ALL artifacts across all runs for this job
            var artifacts = await db.JobRunArtifacts
                .Where(a => a.JobId == jobId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var artifactDtos = artifacts.Select(a => new ArtifactForViewDto(
                a.Id,
                a.JobRunId,
                a.ArtifactType.ToString().ToLowerInvariant(),
                a.Title,
                a.ContentInline,
                a.ContentPath,
                a.ContentSizeBytes,
                a.MimeType,
                a.CreatedAt
            )).ToList();

            // Failed runs (most-recent first) — surface error + stack on the
            // Channels detail page so the user is not staring at an empty cell.
            // We include the full Error text persisted by JobExecutor (ex.ToString()).
            var failedRuns = await db.JobRuns
                .Where(r => r.JobId == jobId && r.Status == "failed")
                .OrderByDescending(r => r.StartedAt)
                .Take(10)
                .Select(r => new FailedRunForViewDto(
                    r.Id,
                    r.Status,
                    r.StartedAt,
                    r.CompletedAt,
                    r.Error,
                    r.Result,
                    r.ExecutedByAgentProfile))
                .ToListAsync();

            var viewDto = new ChannelDetailViewDto(
                job.Id,
                job.Name,
                artifactDtos,
                failedRuns
            );

            return Results.Ok(viewDto);
        })
        .WithName("GetChannelDetailView");

        // GET /api/channels/{jobId}/runs/{runId} — all artifacts for a single run
        group.MapGet("/{jobId:guid}/runs/{runId:guid}", async (
            Guid jobId,
            Guid runId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            [FromServices] ArtifactStorageService artifactStorage,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            var run = await db.JobRuns
                .Where(r => r.Id == runId && r.JobId == jobId)
                .FirstOrDefaultAsync();

            if (run is null)
                return Results.NotFound();

            var artifacts = await db.JobRunArtifacts
                .Where(a => a.JobRunId == runId)
                .OrderBy(a => a.Sequence)
                .ThenBy(a => a.CreatedAt)
                .ToListAsync();

            var artifactDtos = new List<ArtifactDto>();
            foreach (var artifact in artifacts)
            {
                string? contentPreview = null;
                
                // For non-file artifacts, include content preview (first 500 chars)
                if (artifact.ArtifactType != JobRunArtifactKind.File)
                {
                    var fullContent = await artifactStorage.GetArtifactContentAsync(artifact);
                    contentPreview = fullContent.Length > 500 
                        ? fullContent.Substring(0, 500) + "..." 
                        : fullContent;
                }

                artifactDtos.Add(new ArtifactDto(
                    artifact.Id,
                    artifact.ArtifactType.ToString().ToLowerInvariant(),
                    artifact.Title,
                    contentPreview,
                    artifact.ContentSizeBytes,
                    artifact.MimeType,
                    artifact.CreatedAt
                ));
            }

            return Results.Ok(new RunArtifactsDto(
                run.Id,
                run.JobId,
                run.Status,
                run.StartedAt,
                run.CompletedAt,
                artifactDtos
            ));
        })
        .WithName("GetRunArtifacts");

        // GET /api/channels/{jobId}/runs/{runId}/artifacts/{artifactId}/content — download artifact content
        group.MapGet("/{jobId:guid}/runs/{runId:guid}/artifacts/{artifactId:guid}/content", async (
            Guid jobId,
            Guid runId,
            Guid artifactId,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            [FromServices] ArtifactStorageService artifactStorage,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            var artifact = await db.JobRunArtifacts
                .Where(a => a.Id == artifactId && a.JobRunId == runId && a.JobId == jobId)
                .FirstOrDefaultAsync();

            if (artifact is null)
                return Results.NotFound();

            var content = await artifactStorage.GetArtifactContentAsync(artifact);
            var contentType = artifact.MimeType ?? "text/plain";

            return Results.Content(content, contentType);
        })
        .WithName("GetArtifactContent");

        // POST /api/channels/{jobId}/artifacts — for Phase 1.1 tool integration
        group.MapPost("/{jobId:guid}/artifacts", async (
            Guid jobId,
            [FromBody] CreateArtifactRequest request,
            [FromServices] IDbContextFactory<OpenClawDbContext> dbFactory,
            [FromServices] ArtifactStorageService artifactStorage,
            HttpContext httpContext) =>
        {
            if (!IsLoopbackRequest(httpContext))
                return Results.StatusCode(403);

            await using var db = await dbFactory.CreateDbContextAsync();

            // Find the most recent run for this job
            var latestRun = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefaultAsync();

            if (latestRun is null)
                return Results.BadRequest(new { error = "No runs found for this job" });

            // Get next sequence number
            var maxSequence = await db.JobRunArtifacts
                .Where(a => a.JobRunId == latestRun.Id)
                .MaxAsync(a => (int?)a.Sequence) ?? -1;

            var artifact = await artifactStorage.CreateArtifactAsync(
                jobId,
                latestRun.Id,
                Enum.Parse<JobRunArtifactKind>(request.Type, ignoreCase: true),
                request.Title,
                request.Content,
                maxSequence + 1
            );

            return Results.Created($"/api/channels/{jobId}/runs/{latestRun.Id}/artifacts/{artifact.Id}", 
                (object)new { artifactId = artifact.Id });
        })
        .WithName("CreateArtifact");
    }

    private static bool IsLoopbackRequest(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        return remoteIp?.IsIPv4MappedToIPv6 == true 
            ? remoteIp.MapToIPv4().ToString() == "127.0.0.1"
            : remoteIp?.ToString() == "127.0.0.1" || remoteIp?.ToString() == "::1";
    }
}

// DTOs
//
// NOTE: Property names below are part of the loopback HTTP contract consumed by
// the OpenClawNet.Channels Razor pages. Renaming requires coordinating with the
// matching ChannelSummary/ChannelDetailDto records there. JSON is case-insensitive
// but field names must still match.
public record ChannelSummaryDto(Guid JobId, string JobName, DateTime LastActivityUtc, int TotalArtifacts);

public record ChannelDetailDto(
    Guid JobId, 
    string JobName, 
    string Status, 
    string Prompt, 
    List<ChannelRunSummaryDto> RecentRuns);

public record ChannelRunSummaryDto(
    Guid RunId, 
    string Status, 
    DateTime StartedAt, 
    DateTime? CompletedAt, 
    int ArtifactCount);

// Razor-specific DTOs for ChannelDetail.razor
public record ChannelDetailViewDto(
    Guid JobId,
    string JobName,
    List<ArtifactForViewDto> Artifacts,
    List<FailedRunForViewDto>? FailedRuns = null);

public record FailedRunForViewDto(
    Guid RunId,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? Error,
    string? PartialResult,
    string? ExecutedByAgentProfile);

public record ArtifactForViewDto(
    Guid Id,
    Guid RunId,
    string ArtifactType,
    string? Title,
    string? ContentInline,
    string? ContentPath,
    long ContentSizeBytes,
    string? MimeType,
    DateTime CreatedAt);

public record RunArtifactsDto(
    Guid RunId,
    Guid JobId,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    List<ArtifactDto> Artifacts);

public record ArtifactDto(
    Guid Id,
    string Type,
    string? Title,
    string? ContentPreview,
    long SizeBytes,
    string? MimeType,
    DateTime CreatedAt);

public record CreateArtifactRequest(string Type, string? Title, string Content);
