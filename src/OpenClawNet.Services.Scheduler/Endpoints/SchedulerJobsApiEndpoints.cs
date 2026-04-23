using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Services.Scheduler.Endpoints;

public static class SchedulerJobsApiEndpoints
{
    public static void MapSchedulerJobsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/scheduler/jobs").WithTags("Scheduler");

        group.MapGet("/", async (IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var jobs = await db.Jobs
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();

            var dtos = jobs.Select(j => new JobSummaryDto(
                j.Id, j.Name, j.Status.ToString().ToLowerInvariant(), j.NextRunAt, j.LastRunAt,
                j.IsRecurring, j.CronExpression, j.Prompt, j.CreatedAt, j.AgentProfileName)).ToList();

            return Results.Ok(dtos);
        })
        .WithName("ListJobs");

        group.MapGet("/{id:guid}", async (Guid id, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs
                .Include(j => j.Runs.OrderByDescending(r => r.StartedAt).Take(50))
                .FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();

            var dto = new JobDetailDto(
                job.Id, job.Name, job.Status.ToString().ToLowerInvariant(), job.NextRunAt, job.LastRunAt,
                job.IsRecurring, job.CronExpression, job.Prompt, job.CreatedAt,
                job.AgentProfileName,
                job.Runs.Select(r => new JobRunDto(r.Id, r.Status, r.StartedAt, r.CompletedAt, r.Result, r.Error)).ToList()
            );
            return Results.Ok(dto);
        })
        .WithName("GetJob");

        group.MapPost("/{id:guid}/trigger", async (
            Guid id,
            IDbContextFactory<OpenClawDbContext> dbFactory,
            IHttpClientFactory httpClientFactory,
            ArtifactStorageService artifactStorage,
            ILogger<JobSummaryDto> logger) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(id);
            if (job is null) return Results.NotFound();
            if (JobStatusTransitions.IsTerminal(job.Status))
                return Results.Conflict(new { error = $"Cannot trigger a job in '{job.Status.ToString().ToLowerInvariant()}' state." });

            logger.LogInformation("Manual trigger for job: {Name} ({Id})", job.Name, job.Id);

            var run = new JobRun { JobId = job.Id, Status = "running" };
            db.JobRuns.Add(run);
            job.LastRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // Fire-and-forget — don't block the HTTP response
            _ = Task.Run(async () =>
            {
                using var timeoutCts = new CancellationTokenSource();
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(300)); // 5-minute timeout for chat invocation

                try
                {
                    var client = httpClientFactory.CreateClient("gateway");
                    var sessionId = Guid.NewGuid();
                    var response = await client.PostAsJsonAsync("/api/chat/", new { sessionId, message = job.Prompt, agentProfileName = job.AgentProfileName }, timeoutCts.Token);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<GatewayChatResponse>(timeoutCts.Token);

                    await using var db2 = await dbFactory.CreateDbContextAsync();
                    var run2 = await db2.JobRuns.FindAsync(run.Id);
                    if (run2 is not null)
                    {
                        run2.Status = "completed";
                        run2.Result = result?.Content;
                        run2.CompletedAt = DateTime.UtcNow;
                        await db2.SaveChangesAsync();

                        // Auto-capture artifact
                        try
                        {
                            await artifactStorage.CreateArtifactFromJobRunAsync(run2);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to create artifact for JobRun {RunId}", run2.Id);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning("Manual trigger timed out after 300s for job: {Name}", job.Name);
                    try
                    {
                        await using var db2 = await dbFactory.CreateDbContextAsync();
                        var run2 = await db2.JobRuns.FindAsync(run.Id);
                        if (run2 is not null)
                        {
                            run2.Status = "failed";
                            run2.Error = "Job execution timed out after 300 seconds";
                            run2.CompletedAt = DateTime.UtcNow;
                            await db2.SaveChangesAsync();

                            // Auto-capture error artifact
                            try
                            {
                                await artifactStorage.CreateArtifactFromJobRunAsync(run2);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to create error artifact for JobRun {RunId}", run2.Id);
                            }
                        }
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Manual trigger failed for job: {Name}", job.Name);
                    try
                    {
                        await using var db2 = await dbFactory.CreateDbContextAsync();
                        var run2 = await db2.JobRuns.FindAsync(run.Id);
                        if (run2 is not null)
                        {
                            run2.Status = "failed";
                            run2.Error = ex.Message;
                            run2.CompletedAt = DateTime.UtcNow;
                            await db2.SaveChangesAsync();

                            // Auto-capture error artifact
                            try
                            {
                                await artifactStorage.CreateArtifactFromJobRunAsync(run2);
                            }
                            catch (Exception ex2)
                            {
                                logger.LogError(ex2, "Failed to create error artifact for JobRun {RunId}", run2.Id);
                            }
                        }
                    }
                    catch { }
                }
            });

            return Results.Accepted(null, new { runId = run.Id, jobId = job.Id, status = "triggered" });
        })
        .WithName("TriggerJob");

        // --- Lifecycle action endpoints ---

        group.MapPost("/{id:guid}/start", async (Guid id, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            // Recalculate NextRunAt so a Draft job (e.g. one created from a template) starts on its real schedule.
            return await TransitionJobAsync(id, JobStatus.Active, dbFactory, recalculateNextRun: true);
        })
        .WithName("StartJob")
        .WithDescription("Transition a Draft job to Active so the scheduler polls it.");

        group.MapPost("/{id:guid}/pause", async (Guid id, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            return await TransitionJobAsync(id, JobStatus.Paused, dbFactory, recalculateNextRun: false);
        })
        .WithName("PauseJob")
        .WithDescription("Temporarily suspend an Active job. Can be resumed later.");

        group.MapPost("/{id:guid}/resume", async (Guid id, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            return await TransitionJobAsync(id, JobStatus.Active, dbFactory, recalculateNextRun: true);
        })
        .WithName("ResumeJob")
        .WithDescription("Resume a Paused job. Recalculates NextRunAt from now for recurring jobs.");

        group.MapPost("/{id:guid}/cancel", async (Guid id, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            return await TransitionJobAsync(id, JobStatus.Cancelled, dbFactory, recalculateNextRun: false);
        })
        .WithName("CancelJob")
        .WithDescription("Permanently cancel a job. This is a terminal state.");
    }

    private static async Task<IResult> TransitionJobAsync(
        Guid id, JobStatus targetStatus, IDbContextFactory<OpenClawDbContext> dbFactory, bool recalculateNextRun)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(id);
        if (job is null) return Results.NotFound();

        if (!JobStatusTransitions.IsAllowed(job.Status, targetStatus))
        {
            return Results.Conflict(new
            {
                error = $"Cannot transition from '{job.Status.ToString().ToLowerInvariant()}' to '{targetStatus.ToString().ToLowerInvariant()}'."
            });
        }

        job.Status = targetStatus;

        // When resuming a recurring job, recalculate NextRunAt from now
        if (recalculateNextRun && job.IsRecurring && !string.IsNullOrEmpty(job.CronExpression))
        {
            job.NextRunAt = SchedulerPollingService.CalculateNextRun(
                job.CronExpression, DateTime.UtcNow, job.EndAt, job.TimeZone);
        }

        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            id = job.Id,
            status = job.Status.ToString().ToLowerInvariant()
        });
    }

    private sealed record GatewayChatResponse(string Content, int ToolCallCount, int TotalTokens);
}

public sealed record JobSummaryDto(
    Guid Id, string Name, string Status, DateTime? NextRunAt, DateTime? LastRunAt,
    bool IsRecurring, string? CronExpression, string Prompt, DateTime CreatedAt, string? AgentProfileName);

public sealed record JobDetailDto(
    Guid Id, string Name, string Status, DateTime? NextRunAt, DateTime? LastRunAt,
    bool IsRecurring, string? CronExpression, string Prompt, DateTime CreatedAt,
    string? AgentProfileName,
    List<JobRunDto> Runs);

public sealed record JobRunDto(Guid Id, string Status, DateTime StartedAt, DateTime? CompletedAt, string? Result, string? Error);
