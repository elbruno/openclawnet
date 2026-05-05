using Microsoft.EntityFrameworkCore;
using OpenClawNet.Models.Abstractions;
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
    private const string MarkdownSummaryJobName = "URL Markdown Summary";

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

        group.MapPost("/markdown-summary/setup", SetupMarkdownSummaryAsync)
            .WithName("SetupMarkdownSummary")
            .WithDescription("Creates a recurring job that converts a URL to Markdown via the markdown_convert tool and produces a brief AI summary of the page content.");

        group.MapGet("/markdown-summary/status", GetMarkdownSummaryStatusAsync)
            .WithName("GetMarkdownSummaryStatus")
            .WithDescription("Returns the current status of the URL → Markdown summary demo job.");

        return app;
    }

    private static async Task<IResult> SetupDocPipelineAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        IAgentProfileStore profileStore,
        DocPipelineSetupRequest? request = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Multi-instance demo: every POST creates a new JobDefinition. Server-side
        // dedup on Name only — if "Document Processing Pipeline" already exists we
        // append " (2)", " (3)", … so the user always lands on a fresh, fully editable
        // job. SourceTemplateName retains lineage for audit/reporting.
        var uniqueName = await GenerateUniqueJobNameAsync(db, DocPipelineJobName);

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
            Name = uniqueName,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
            Status = JobStatus.Draft,
            NextRunAt = now.AddSeconds(5),  // first run in 5 seconds (after user starts the job)
            StartAt = now,
            EndAt = now.AddMinutes(durationMinutes),
            AllowConcurrentRuns = false,
            NaturalLanguageSchedule = $"Every {intervalSeconds} seconds for {durationMinutes} minutes",
            SourceTemplateName = DocPipelineJobName,
            AgentProfileName = await JobEndpoints.ResolveAgentProfileNameAsync(null, profileStore)
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
            Message = $"Document processing pipeline '{job.Name}' created in Draft state. Start it from the Jobs list to run every {intervalSeconds}s for {durationMinutes} minutes."
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
        IAgentProfileStore profileStore,
        WebsiteWatcherSetupRequest? request = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var uniqueName = await GenerateUniqueJobNameAsync(db, WebsiteWatcherJobName);

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
            Name = uniqueName,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
            Status = JobStatus.Draft,
            NextRunAt = now.AddSeconds(10),
            StartAt = now,
            AllowConcurrentRuns = false,
            NaturalLanguageSchedule = "Every 15 minutes",
            SourceTemplateName = WebsiteWatcherJobName,
            AgentProfileName = await JobEndpoints.ResolveAgentProfileNameAsync(null, profileStore)
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
            Message = $"Website watcher '{job.Name}' created in Draft state. Start it from the Jobs list to check {url} every 15 minutes and log changes to {logPath}."
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
        IAgentProfileStore profileStore,
        FolderHealthSetupRequest? request = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var uniqueName = await GenerateUniqueJobNameAsync(db, FolderHealthJobName);

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
            Name = uniqueName,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
            Status = JobStatus.Draft,
            NextRunAt = NextDailyNineUtc(now),
            StartAt = now,
            AllowConcurrentRuns = false,
            NaturalLanguageSchedule = "Daily at 09:00 UTC",
            SourceTemplateName = FolderHealthJobName,
            AgentProfileName = await JobEndpoints.ResolveAgentProfileNameAsync(null, profileStore)
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
            Message = $"Folder health report '{job.Name}' created in Draft state. Start it from the Jobs list to run daily at 09:00 UTC against {folder}."
        });
    }

    private static async Task<IResult> GetFolderHealthStatusAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory)
    {
        return await GetDemoJobStatusAsync(dbFactory, FolderHealthJobName);
    }

    // ── URL → Markdown Summary demo ────────────────────────────────────────────

    private static async Task<IResult> SetupMarkdownSummaryAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        IAgentProfileStore profileStore,
        MarkdownSummaryRequest? request = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var uniqueName = await GenerateUniqueJobNameAsync(db, MarkdownSummaryJobName);

        var url = string.IsNullOrWhiteSpace(request?.Url) ? "https://elbruno.com" : request!.Url!.Trim();
        var intervalMinutes = request?.IntervalMinutes is > 0 ? request.IntervalMinutes!.Value : 60;

        // Cron: every N minutes, on the minute. Cap at 1440 (1 day) for safety.
        var safeMinutes = Math.Min(intervalMinutes, 1440);
        var cronExpression = safeMinutes >= 60
            ? $"0 */{Math.Max(1, safeMinutes / 60)} * * *"
            : $"*/{safeMinutes} * * * *";

        var now = DateTime.UtcNow;

        var prompt =
            "Convert the page at " + url + " to Markdown using the `markdown_convert` tool (pass `url` = \"" + url + "\").\n" +
            "Then produce a concise summary of the page in 3-5 bullet points covering:\n" +
            "  • What the page is about (title + topic).\n" +
            "  • Key sections or headings.\n" +
            "  • Any notable links, calls-to-action, or recent updates visible in the content.\n" +
            "Format the response as Markdown:\n" +
            "  ## Summary of " + url + "\n" +
            "  - bullet 1\n  - bullet 2\n  ...\n" +
            "Keep the entire response under 250 words. If the markdown_convert tool fails, report the error verbatim — do not retry.";

        var job = new ScheduledJob
        {
            Name = uniqueName,
            Prompt = prompt,
            CronExpression = cronExpression,
            IsRecurring = true,
            Status = JobStatus.Draft,
            NextRunAt = now.AddSeconds(10),
            StartAt = now,
            AllowConcurrentRuns = false,
            NaturalLanguageSchedule = safeMinutes >= 60
                ? $"Every {Math.Max(1, safeMinutes / 60)} hour(s)"
                : $"Every {safeMinutes} minutes",
            SourceTemplateName = MarkdownSummaryJobName,
            AgentProfileName = await JobEndpoints.ResolveAgentProfileNameAsync(null, profileStore)
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return Results.Created($"/api/jobs/{job.Id}", new MarkdownSummaryResponse
        {
            JobId = job.Id,
            Name = job.Name,
            Url = url,
            CronExpression = cronExpression,
            IntervalMinutes = safeMinutes,
            StartsAt = now,
            Message = $"URL → Markdown summary '{job.Name}' created in Draft state. Start it from the Jobs list to fetch {url}, convert it to Markdown, and generate a summary every {safeMinutes} minutes."
        });
    }

    private static async Task<IResult> GetMarkdownSummaryStatusAsync(
        IDbContextFactory<OpenClawDbContext> dbFactory)
    {
        return await GetDemoJobStatusAsync(dbFactory, MarkdownSummaryJobName);
    }

    private static DateTime NextDailyNineUtc(DateTime now)
    {
        var today9 = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0, DateTimeKind.Utc);
        return today9 > now ? today9 : today9.AddDays(1);
    }

    /// <summary>
    /// Generates a job name that is unique across the <c>Jobs</c> table by appending
    /// " (N)" when collisions exist. The first instance keeps the bare template name;
    /// subsequent instances become "Name (2)", "Name (3)", etc. Comparison is case-
    /// insensitive to match SQLite's default NOCASE collation behaviour for ASCII.
    /// </summary>
    /// <remarks>
    /// This is a server-side dedup convenience — the user can rename the job freely
    /// after creation. Computed via a single SELECT (existing names) rather than a
    /// retry loop, so concurrent setups in the same process won't collide as long as
    /// EF's change tracker hasn't been bypassed. Two truly concurrent HTTP setups
    /// could in theory both pick the same suffix; SQLite has no UNIQUE on Jobs.Name
    /// so the duplicate would still persist — acceptable for a demo endpoint.
    /// </remarks>
    internal static async Task<string> GenerateUniqueJobNameAsync(OpenClawDbContext db, string baseName)
    {
        var existing = await db.Jobs
            .Where(j => j.Name == baseName || j.Name.StartsWith(baseName + " ("))
            .Select(j => j.Name)
            .ToListAsync();

        if (existing.Count == 0) return baseName;
        if (!existing.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            return baseName;

        // Find the smallest N >= 2 such that "{baseName} (N)" is not taken.
        var taken = new HashSet<int>();
        foreach (var name in existing)
        {
            if (name.Length <= baseName.Length + 3) continue;
            if (!name.StartsWith(baseName + " (", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith(')')) continue;
            var inner = name.Substring(baseName.Length + 2, name.Length - baseName.Length - 3);
            if (int.TryParse(inner, out var n) && n >= 2) taken.Add(n);
        }

        for (var i = 2; ; i++)
        {
            if (!taken.Contains(i)) return $"{baseName} ({i})";
        }
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

// --- URL → Markdown Summary DTOs ---

public sealed record MarkdownSummaryRequest
{
    /// <summary>Absolute http/https URL to convert and summarize.</summary>
    public string? Url { get; init; }

    /// <summary>How often to re-run the summary, in minutes. Defaults to 60.</summary>
    public int? IntervalMinutes { get; init; }
}

public sealed record MarkdownSummaryResponse
{
    public Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }
    public required string CronExpression { get; init; }
    public int IntervalMinutes { get; init; }
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
