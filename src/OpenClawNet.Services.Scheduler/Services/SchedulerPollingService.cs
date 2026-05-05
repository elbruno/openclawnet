using Cronos;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Services.Scheduler.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using System.Net.Http.Json;

namespace OpenClawNet.Services.Scheduler;

public sealed class SchedulerPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SchedulerSettingsService _settings;
    private readonly SchedulerRunState _runState;
    private readonly ILogger<SchedulerPollingService> _logger;

    public SchedulerPollingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        SchedulerSettingsService settings,
        SchedulerRunState runState,
        ILogger<SchedulerPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _runState = runState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = _settings.GetSettings();
        _logger.LogInformation("Scheduler started — poll={Poll}s, maxConcurrent={Max}, timeout={Timeout}s",
            cfg.PollIntervalSeconds, cfg.MaxConcurrentJobs, cfg.JobTimeoutSeconds);

        // Reclaim runs that were left in 'running' state by a previous process.
        // The trigger endpoint and dispatcher both fire-and-forget Task.Run blocks,
        // so any in-flight run is permanently lost when the Scheduler restarts.
        // Without this sweep, the JobRun row stays at status="running" indefinitely
        // and the UI shows a perpetual spinner. See bug: "stuck Running for 15+ min
        // after Aspire restart".
        try { await ReclaimOrphanedRunsAsync(_scopeFactory, _logger, stoppingToken); }
        catch (Exception ex) { _logger.LogError(ex, "Orphan-run reclaim failed at startup"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessDueJobsAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error processing scheduled jobs"); }

            try
            {
                var pollInterval = TimeSpan.FromSeconds(_settings.GetSettings().PollIntervalSeconds);
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessDueJobsAsync(CancellationToken ct)
    {
        List<(Guid Id, string Name, string Prompt, bool IsRecurring, string? Cron, DateTime? StartAt, DateTime? EndAt, string? TimeZone, string? AgentProfileName)> dueJobs;

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var now = DateTime.UtcNow;
            var jobs = await db.Jobs
                .Where(j => j.Status == JobStatus.Active 
                    && j.TriggerType == TriggerType.Cron 
                    && j.NextRunAt <= now)
                .ToListAsync(ct);

            if (jobs.Count == 0) return;

            // Pre-load the set of job IDs that currently have a running instance
            var jobIdsWithRunningInstance = await db.JobRuns
                .Where(r => r.Status == "running")
                .Select(r => r.JobId)
                .Distinct()
                .ToListAsync(ct);
            var runningSet = new HashSet<Guid>(jobIdsWithRunningInstance);

            foreach (var j in jobs)
            {
                // Respect StartAt: skip jobs whose schedule hasn't started yet
                if (j.StartAt.HasValue && now < j.StartAt.Value)
                {
                    j.NextRunAt = j.StartAt.Value;
                    continue;
                }

                // Respect EndAt: deactivate expired schedules
                if (j.EndAt.HasValue && now > j.EndAt.Value)
                {
                    j.Status = JobStatus.Completed;
                    j.NextRunAt = null;
                    _logger.LogInformation("Job '{Name}' expired (EndAt={EndAt})", j.Name, j.EndAt);
                    continue;
                }

                // Concurrency control: skip if already running and concurrent runs are not allowed
                if (!j.AllowConcurrentRuns && runningSet.Contains(j.Id))
                {
                    _logger.LogWarning(
                        "Skipping job '{Name}' ({Id}) — already running and AllowConcurrentRuns is false",
                        j.Name, j.Id);

                    // Still advance NextRunAt for recurring jobs so we don't re-trigger immediately
                    if (j.IsRecurring && !string.IsNullOrEmpty(j.CronExpression))
                        j.NextRunAt = CalculateNextRun(j.CronExpression, now, j.EndAt, j.TimeZone);

                    continue;
                }

                j.LastRunAt = now;

                if (j.IsRecurring && !string.IsNullOrEmpty(j.CronExpression))
                {
                    j.NextRunAt = CalculateNextRun(j.CronExpression, now, j.EndAt, j.TimeZone);

                    // If no next run (schedule exhausted), mark completed
                    if (j.NextRunAt is null)
                    {
                        j.Status = JobStatus.Completed;
                        _logger.LogInformation("Job '{Name}' has no more scheduled runs", j.Name);
                    }
                }
                else
                {
                    // One-shot job: will be dispatched now, mark as completed after execution
                    j.NextRunAt = null;
                }
            }
            await db.SaveChangesAsync(ct);

            dueJobs = jobs
                .Where(j => j.LastRunAt == now) // Only dispatch jobs that were actually marked as running
                .Select(j => (j.Id, j.Name, j.Prompt, j.IsRecurring, j.CronExpression, j.StartAt, j.EndAt, j.TimeZone, j.AgentProfileName))
                .ToList();
        }

        if (dueJobs.Count == 0) return;

        _logger.LogInformation("Dispatching {Count} due job(s)", dueJobs.Count);

        var cfg = _settings.GetSettings();
        using var semaphore = new SemaphoreSlim(cfg.MaxConcurrentJobs, cfg.MaxConcurrentJobs);

        var tasks = dueJobs.Select(job =>
            ExecuteWithSemaphoreAsync(semaphore, job.Id, job.Name, job.Prompt, job.IsRecurring, job.Cron, job.AgentProfileName, cfg.JobTimeoutSeconds, ct)
        ).ToList();

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Uses Cronos to calculate the next occurrence from a cron expression.
    /// Supports both 5-field (standard) and 6-field (with seconds) formats.
    /// </summary>
    internal static DateTime? CalculateNextRun(string cronExpression, DateTime fromUtc, DateTime? endAt, string? timeZone)
    {
        try
        {
            // Detect 6-field (seconds) vs 5-field format
            var fieldCount = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var format = fieldCount >= 6
                ? CronFormat.IncludeSeconds
                : CronFormat.Standard;

            var cron = CronExpression.Parse(cronExpression, format);

            // Resolve timezone for local-time evaluation
            TimeZoneInfo tz = TimeZoneInfo.Utc;
            if (!string.IsNullOrEmpty(timeZone))
            {
                try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
                catch (TimeZoneNotFoundException) { /* fall back to UTC */ }
            }

            var next = cron.GetNextOccurrence(fromUtc, tz);

            // Respect EndAt bound
            if (next.HasValue && endAt.HasValue && next.Value > endAt.Value)
                return null;

            return next;
        }
        catch (CronFormatException)
        {
            // Invalid expression — return null to mark schedule exhausted
            return null;
        }
    }

    private async Task ExecuteWithSemaphoreAsync(
        SemaphoreSlim semaphore, Guid jobId, string name, string prompt,
        bool isRecurring, string? cron, string? agentProfileName, int timeoutSeconds, CancellationToken outerCt)
    {
        await semaphore.WaitAsync(outerCt);
        _runState.Increment();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var scope = _scopeFactory.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            var artifactStorage = scope.ServiceProvider.GetRequiredService<ArtifactStorageService>();
            await using var db = await dbFactory.CreateDbContextAsync(outerCt);

            var run = new JobRun { JobId = jobId, Status = "running" };
            db.JobRuns.Add(run);
            await db.SaveChangesAsync(outerCt);

            _logger.LogInformation("Executing job: {Name} ({Id})", name, jobId);
            try
            {
                var client = _httpClientFactory.CreateClient("gateway");
                var response = await client.PostAsJsonAsync($"/api/jobs/{jobId}/execute",
                    new { }, timeoutCts.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<JobExecutionResponse>(timeoutCts.Token);

                run.Status = "completed";
                run.Result = result?.Output;
                run.CompletedAt = DateTime.UtcNow;
                run.TokensUsed = result?.TokensUsed;
                _logger.LogInformation("Job completed: {Name} (Tokens: {Tokens})", name, result?.TokensUsed ?? 0);
            }
            catch (OperationCanceledException) when (!outerCt.IsCancellationRequested)
            {
                run.Status = "failed";
                run.Error = $"Job timed out after {timeoutSeconds}s";
                run.CompletedAt = DateTime.UtcNow;
                _logger.LogWarning("Job timed out: {Name} ({Id})", name, jobId);
            }
            catch (Exception ex)
            {
                run.Status = "failed";
                run.Error = ex.Message;
                run.CompletedAt = DateTime.UtcNow;
                _logger.LogError(ex, "Job failed: {Name}", name);
            }

            var job = await db.Jobs.FindAsync(new object[] { jobId }, outerCt);
            if (job is not null)
            {
                // One-shot jobs transition to Completed regardless of run outcome
                if (!isRecurring || string.IsNullOrEmpty(cron))
                    job.Status = JobStatus.Completed;
            }

            await db.SaveChangesAsync(outerCt);

            // Auto-capture artifact after run completes
            try
            {
                await artifactStorage.CreateArtifactFromJobRunAsync(run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create artifact for JobRun {RunId}", run.Id);
            }
        }
        finally
        {
            _runState.Decrement();
            semaphore.Release();
        }
    }

    /// <summary>
    /// Reclaims any <c>JobRun</c> rows left in <c>"running"</c> state by a previous
    /// process. Called once at startup. Safe to run on a fresh DB (no-op).
    /// Exposed as <c>internal static</c> so unit tests can verify the behaviour
    /// without standing up a full <c>BackgroundService</c>.
    /// </summary>
    internal static async Task ReclaimOrphanedRunsAsync(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var orphans = await db.JobRuns
            .Where(r => r.Status == "running")
            .ToListAsync(ct);

        if (orphans.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var run in orphans)
        {
            run.Status = "failed";
            run.Error = "Scheduler restarted while run was in progress; the in-flight task was lost.";
            run.CompletedAt = now;
        }
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Reclaimed {Count} orphaned JobRun row(s) left in 'running' state by previous process.", orphans.Count);
    }
}

internal sealed record JobExecutionResponse(
    bool Success,
    Guid? RunId,
    string? Output,
    int TokensUsed,
    int DurationMs,
    bool WasDryRun);
