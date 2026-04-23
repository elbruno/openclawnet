using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Pre-configured demo scenarios that showcase OpenClawNet capabilities.
/// Each demo sets up a scheduled job with sensible defaults — one POST to start, one GET to check.
/// </summary>
public static class DemoEndpoints
{
    private const string DocPipelineJobName = "Document Processing Pipeline";
    private const string WebsiteWatcherJobName = "Website Watcher";
    private const string FolderHealthJobName = "Folder Health Report";

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/demos").WithTags("Demos");

        group.MapPost("/doc-pipeline/setup", SetupDocPipelineAsync)
            .WithName("SetupDocPipeline")
            .WithDescription("Creates a pre-configured scheduled job that scans a folder for documents and generates summary reports.");

        group.MapGet("/doc-pipeline/status", GetDocPipelineStatusAsync)
            .WithName("GetDocPipelineStatus")
            .WithDescription("Returns the current status of the document processing pipeline demo, including latest run results.");

        group.MapPost("/website-watcher/setup", SetupWebsiteWatcherAsync)
            .WithName("SetupWebsiteWatcher")
            .WithDescription("Creates a recurring job that fetches a URL every 15 minutes and writes a one-line change log when the page changes.");

        group.MapGet("/website-watcher/status", GetWebsiteWatcherStatusAsync)
            .WithName("GetWebsiteWatcherStatus")
            .WithDescription("Returns the current status of the website-watcher demo job.");

        group.MapPost("/folder-health/setup", SetupFolderHealthAsync)
            .WithName("SetupFolderHealth")
            .WithDescription("Creates a recurring job that runs daily at 09:00 UTC, summarizing a folder and writing a markdown report.");

        group.MapGet("/folder-health/status", GetFolderHealthStatusAsync)
            .WithName("GetFolderHealthStatus")
            .WithDescription("Returns the current status of the folder-health demo job.");

        return app;
    }

    private static async Task<IResult> SetupDocPipelineAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        DocPipelineSetupRequest? request = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Check if a doc-pipeline demo job already exists in a non-terminal state
        var existing = await db.Jobs
            .Where(j => j.Name == DocPipelineJobName
                && j.Status != JobStatus.Cancelled
                && j.Status != JobStatus.Completed)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return Results.Conflict(new
            {
                error = "A document processing pipeline job already exists.",
                jobId = existing.Id,
                message = "Use GET /api/demos/doc-pipeline/status to check progress, or delete the existing job first via DELETE /api/jobs/{id}."
            });
        }

        var folderPath = request?.FolderPath ?? @"C:\src\openclawnet-plan\docs\sampleDocs";
        var intervalSeconds = request?.IntervalSeconds ?? 30;
        var durationMinutes = request?.DurationMinutes ?? 5;

        var now = DateTime.UtcNow;
        var prompt = $"""
            Check the folder {folderPath} for any documents. For each document found, describe it and generate a brief summary based on the filename. List all documents found with their sizes. Then check if a 'processed' subfolder exists in the same directory.
            """;

        // Build a cron expression for sub-minute intervals (Cronos 6-field format: seconds)
        var cronExpression = intervalSeconds switch
        {
            <= 0 or > 3600 => "*/30 * * * * *",            // default: every 30s
            _ => $"*/{intervalSeconds} * * * * *"
        };

        var job = new ScheduledJob
        {
            Name = DocPipelineJobName,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
            Status = JobStatus.Draft,
            NextRunAt = now.AddSeconds(5),  // first run in 5 seconds (after user starts the job)
            StartAt = now,
            EndAt = now.AddMinutes(durationMinutes),
            AllowConcurrentRuns = false,
            NaturalLanguageSchedule = $"Every {intervalSeconds} seconds for {durationMinutes} minutes"
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return Results.Created($"/api/jobs/{job.Id}", new DocPipelineSetupResponse
        {
            JobId = job.Id,
            Name = job.Name,
            FolderPath = folderPath,
            CronExpression = cronExpression,
            StartsAt = now,
            EndsAt = now.AddMinutes(durationMinutes),
            IntervalSeconds = intervalSeconds,
            Message = $"Document processing pipeline created in Draft state. Start it from the Jobs list to run every {intervalSeconds}s for {durationMinutes} minutes."
        });
    }

    private static async Task<IResult> GetDocPipelineStatusAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory)
    {
        return await GetDemoJobStatusAsync(dbFactory, DocPipelineJobName);
    }

    // ── Website Watcher demo ────────────────────────────────────────────────────

    private static async Task<IResult> SetupWebsiteWatcherAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        WebsiteWatcherSetupRequest? request = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var existing = await db.Jobs
            .Where(j => j.Name == WebsiteWatcherJobName
                && j.Status != JobStatus.Cancelled
                && j.Status != JobStatus.Completed)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return Results.Conflict(new
            {
                error = "A website-watcher job already exists.",
                jobId = existing.Id,
                message = "Use GET /api/demos/website-watcher/status to check progress, or DELETE /api/jobs/{id} to remove it."
            });
        }

        var url = string.IsNullOrWhiteSpace(request?.Url) ? "https://example.com" : request!.Url!.Trim();
        var logPath = string.IsNullOrWhiteSpace(request?.LogPath)
            ? @"docs\watch-log.txt"
            : request!.LogPath!.Trim();

        const string cronExpression = "*/15 * * * *";
        var now = DateTime.UtcNow;

        var prompt =
            "Use the `web.fetch` tool to GET " + url + ".\n" +
            "Compute a SHA-256 hash of the response body.\n" +
            "If a previous hash exists in `" + logPath + "`, compare. If it differs (or no log exists yet),\n" +
            "append a single line to `" + logPath + "` via `filesystem.write_file` in the form:\n" +
            "\"<UTC ISO-8601 timestamp> changed <hash-prefix-12>\".\n" +
            "Otherwise do nothing. Be terse — one line of output max.";

        var job = new ScheduledJob
        {
            Name = WebsiteWatcherJobName,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
            Status = JobStatus.Draft,
            NextRunAt = now.AddSeconds(10),
            StartAt = now,
            AllowConcurrentRuns = false,
            NaturalLanguageSchedule = "Every 15 minutes"
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return Results.Created($"/api/jobs/{job.Id}", new WebsiteWatcherSetupResponse
        {
            JobId = job.Id,
            Name = job.Name,
            Url = url,
            LogPath = logPath,
            CronExpression = cronExpression,
            StartsAt = now,
            Message = $"Website watcher created in Draft state. Start it from the Jobs list to check {url} every 15 minutes and log changes to {logPath}."
        });
    }

    private static async Task<IResult> GetWebsiteWatcherStatusAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory)
    {
        return await GetDemoJobStatusAsync(dbFactory, WebsiteWatcherJobName);
    }

    // ── Folder Health Report demo ──────────────────────────────────────────────

    private static async Task<IResult> SetupFolderHealthAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        FolderHealthSetupRequest? request = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var existing = await db.Jobs
            .Where(j => j.Name == FolderHealthJobName
                && j.Status != JobStatus.Cancelled
                && j.Status != JobStatus.Completed)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return Results.Conflict(new
            {
                error = "A folder-health-report job already exists.",
                jobId = existing.Id,
                message = "Use GET /api/demos/folder-health/status to check progress, or DELETE /api/jobs/{id} to remove it."
            });
        }

        var folder = string.IsNullOrWhiteSpace(request?.FolderPath)
            ? @"C:\src\openclawnet-plan\docs"
            : request!.FolderPath!.Trim();

        const string cronExpression = "0 9 * * *";
        var now = DateTime.UtcNow;

        var prompt =
            "Daily folder health report for " + folder + ".\n" +
            "1. Use `filesystem.list_dir` to enumerate the folder.\n" +
            "2. Compute: total file count, total size in bytes (human-readable), and the most recently modified file.\n" +
            "3. Build a markdown report titled \"# Folder Health — " + folder + "\" with a table summarising those metrics.\n" +
            "4. Save the report via `filesystem.write_file` to `docs/folder-health-{date}.md` where {date}\n" +
            "   is today's UTC date in YYYY-MM-DD form.\n" +
            "Keep the report under 30 lines.";

        var job = new ScheduledJob
        {
            Name = FolderHealthJobName,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
            Status = JobStatus.Draft,
            NextRunAt = NextDailyNineUtc(now),
            StartAt = now,
            AllowConcurrentRuns = false,
            NaturalLanguageSchedule = "Daily at 09:00 UTC"
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return Results.Created($"/api/jobs/{job.Id}", new FolderHealthSetupResponse
        {
            JobId = job.Id,
            Name = job.Name,
            FolderPath = folder,
            CronExpression = cronExpression,
            StartsAt = now,
            Message = $"Folder health report created in Draft state. Start it from the Jobs list to run daily at 09:00 UTC against {folder}."
        });
    }

    private static async Task<IResult> GetFolderHealthStatusAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory)
    {
        return await GetDemoJobStatusAsync(dbFactory, FolderHealthJobName);
    }

    private static DateTime NextDailyNineUtc(DateTime now)
    {
        var today9 = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0, DateTimeKind.Utc);
        return today9 > now ? today9 : today9.AddDays(1);
    }

    // ── Shared status helper ───────────────────────────────────────────────────

    private static async Task<IResult> GetDemoJobStatusAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory, string jobName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var job = await db.Jobs
            .Include(j => j.Runs.OrderByDescending(r => r.StartedAt))
            .Where(j => j.Name == jobName)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync();

        if (job is null)
        {
            return Results.NotFound(new
            {
                error = $"No '{jobName}' job found.",
                message = "Use the corresponding /setup endpoint to create one."
            });
        }

        var latestRun = job.Runs.FirstOrDefault();
        var completedRuns = job.Runs.Count(r => r.Status == "completed");
        var failedRuns = job.Runs.Count(r => r.Status == "failed");

        return Results.Ok(new DocPipelineStatusResponse
        {
            JobId = job.Id,
            JobStatus = job.Status.ToString().ToLowerInvariant(),
            CreatedAt = job.CreatedAt,
            StartsAt = job.StartAt,
            EndsAt = job.EndAt,
            NextRunAt = job.NextRunAt,
            LastRunAt = job.LastRunAt,
            TotalRuns = job.Runs.Count,
            CompletedRuns = completedRuns,
            FailedRuns = failedRuns,
            LatestRun = latestRun is null ? null : new DocPipelineRunInfo
            {
                RunId = latestRun.Id,
                Status = latestRun.Status,
                StartedAt = latestRun.StartedAt,
                CompletedAt = latestRun.CompletedAt,
                Result = latestRun.Result,
                Error = latestRun.Error
            }
        });
    }
}

// --- Website Watcher DTOs ---

public sealed record WebsiteWatcherSetupRequest
{
    public string? Url { get; init; }
    public string? LogPath { get; init; }
}

public sealed record WebsiteWatcherSetupResponse
{
    public Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }
    public required string LogPath { get; init; }
    public required string CronExpression { get; init; }
    public DateTime StartsAt { get; init; }
    public required string Message { get; init; }
}

// --- Folder Health DTOs ---

public sealed record FolderHealthSetupRequest
{
    public string? FolderPath { get; init; }
}

public sealed record FolderHealthSetupResponse
{
    public Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string FolderPath { get; init; }
    public required string CronExpression { get; init; }
    public DateTime StartsAt { get; init; }
    public required string Message { get; init; }
}

// --- Request/Response DTOs ---

public sealed record DocPipelineSetupRequest
{
    /// <summary>Folder to scan for documents. Defaults to docs/sampleDocs.</summary>
    public string? FolderPath { get; init; }

    /// <summary>How often to run, in seconds. Defaults to 30.</summary>
    public int? IntervalSeconds { get; init; }

    /// <summary>How long the pipeline stays active, in minutes. Defaults to 5.</summary>
    public int? DurationMinutes { get; init; }
}

public sealed record DocPipelineSetupResponse
{
    public Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string FolderPath { get; init; }
    public required string CronExpression { get; init; }
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public int IntervalSeconds { get; init; }
    public required string Message { get; init; }
}

public sealed record DocPipelineStatusResponse
{
    public Guid JobId { get; init; }
    public required string JobStatus { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }
    public DateTime? NextRunAt { get; init; }
    public DateTime? LastRunAt { get; init; }
    public int TotalRuns { get; init; }
    public int CompletedRuns { get; init; }
    public int FailedRuns { get; init; }
    public DocPipelineRunInfo? LatestRun { get; init; }
}

public sealed record DocPipelineRunInfo
{
    public Guid RunId { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
}
