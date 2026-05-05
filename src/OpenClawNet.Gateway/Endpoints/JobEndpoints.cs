using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs");

        group.MapGet("/", async (IDbContextFactory<OpenClawDbContext> dbFactory, bool? includeArchived) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var query = db.Jobs.AsQueryable();
            // Concept-review §4b: archived jobs are hidden by default to keep the list focused.
            if (includeArchived != true)
            {
                query = query.Where(j => j.Status != JobStatus.Archived);
            }
            var jobs = await query
                .OrderByDescending(j => j.CreatedAt)
                .Take(50)
                .ToListAsync();

            var dtos = jobs.Select(j => ToDto(j)).ToList();
            return Results.Ok(dtos);
        })
        .WithName("ListJobs");

        group.MapPost("/", async (CreateJobRequest request, IDbContextFactory<OpenClawDbContext> dbFactory, IAgentProfileStore profileStore) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Prompt))
                return Results.BadRequest(new { error = "Name and Prompt are required." });

            await using var db = await dbFactory.CreateDbContextAsync();

            var isRecurring = !string.IsNullOrEmpty(request.CronExpression);

            DateTime? nextRun = request.RunAt?.ToUniversalTime();
            if (nextRun is null && !isRecurring)
                nextRun = DateTime.UtcNow.AddHours(1);

            var resolvedAgentProfileName = await ResolveAgentProfileNameAsync(request.AgentProfileName, profileStore);

            var job = new ScheduledJob
            {
                Name = request.Name,
                Prompt = request.Prompt,
                CronExpression = request.CronExpression,
                NextRunAt = nextRun,
                IsRecurring = isRecurring,
                Status = JobStatus.Draft,
                StartAt = request.StartAt?.ToUniversalTime(),
                EndAt = request.EndAt?.ToUniversalTime(),
                TimeZone = request.TimeZone,
                NaturalLanguageSchedule = request.NaturalLanguageSchedule,
                AllowConcurrentRuns = request.AllowConcurrentRuns,
                AgentProfileName = resolvedAgentProfileName,
                SourceTemplateName = request.SourceTemplateName
            };

            db.Jobs.Add(job);
            await db.SaveChangesAsync();

            return Results.Created($"/api/jobs/{job.Id}", ToDto(job));
        })
        .WithName("CreateJob")
        .WithDescription("Create a new scheduled job");

        group.MapGet("/{jobId:guid}", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs
                .Include(j => j.Runs.OrderByDescending(r => r.StartedAt))
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is null) return Results.NotFound();

            return Results.Ok(new JobDetailDto
            {
                Id = job.Id,
                Name = job.Name,
                Prompt = job.Prompt,
                Status = job.Status.ToString().ToLowerInvariant(),
                IsRecurring = job.IsRecurring,
                CronExpression = job.CronExpression,
                NextRunAt = job.NextRunAt,
                LastRunAt = job.LastRunAt,
                CreatedAt = job.CreatedAt,
                StartAt = job.StartAt,
                EndAt = job.EndAt,
                TimeZone = job.TimeZone,
                NaturalLanguageSchedule = job.NaturalLanguageSchedule,
                AllowConcurrentRuns = job.AllowConcurrentRuns,
                AgentProfileName = job.AgentProfileName,
                SourceTemplateName = job.SourceTemplateName,
                Runs = job.Runs.Select(r => new JobRunDto
                {
                    Id = r.Id,
                    Status = r.Status,
                    Result = r.Result,
                    Error = r.Error,
                    StartedAt = r.StartedAt,
                    CompletedAt = r.CompletedAt,
                    TokensUsed = r.TokensUsed,
                    InputSnapshotJson = r.InputSnapshotJson,
                    ExecutedByAgentProfile = r.ExecutedByAgentProfile
                }).ToList()
            });
        })
        .WithName("GetJob");

        group.MapPut("/{jobId:guid}", async (Guid jobId, CreateJobRequest request, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Prompt))
                return Results.BadRequest(new { error = "Name and Prompt are required." });

            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            if (!JobStatusTransitions.IsEditable(job.Status))
                return Results.Conflict(new { error = $"Job cannot be edited in '{job.Status.ToString().ToLowerInvariant()}' state. Only Draft or Paused jobs are editable." });

            job.Name = request.Name;
            job.Prompt = request.Prompt;
            job.CronExpression = request.CronExpression;
            job.IsRecurring = !string.IsNullOrEmpty(request.CronExpression);
            job.StartAt = request.StartAt?.ToUniversalTime();
            job.EndAt = request.EndAt?.ToUniversalTime();
            job.TimeZone = request.TimeZone;
            job.NaturalLanguageSchedule = request.NaturalLanguageSchedule;
            job.AllowConcurrentRuns = request.AllowConcurrentRuns;
            job.AgentProfileName = request.AgentProfileName;
            // SourceTemplateName is intentionally not editable via PUT — it's a
            // creation-time lineage marker, not a user-controlled property.

            await db.SaveChangesAsync();

            return Results.Ok(ToDto(job));
        })
        .WithName("UpdateJob")
        .WithDescription("Update an existing scheduled job (only when Draft or Paused)");

        // PATCH /{jobId} — partial-update for safe fields (Name, Prompt). Allowed in
        // ANY status because this is for inline UX affordances (e.g. the rename
        // pencil on /jobs) where re-keying the schedule is undesirable. Only the
        // explicitly-included fields are touched; everything else is preserved.
        group.MapPatch("/{jobId:guid}", async (Guid jobId, JobPatchRequest request, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            var changed = false;

            if (request.Name is not null)
            {
                var trimmed = request.Name.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    return Results.BadRequest(new { error = "Name cannot be empty." });
                if (!string.Equals(job.Name, trimmed, StringComparison.Ordinal))
                {
                    job.Name = trimmed;
                    changed = true;
                }
            }

            if (request.Prompt is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                    return Results.BadRequest(new { error = "Prompt cannot be empty." });
                if (!string.Equals(job.Prompt, request.Prompt, StringComparison.Ordinal))
                {
                    job.Prompt = request.Prompt;
                    changed = true;
                }
            }

            if (changed)
                await db.SaveChangesAsync();

            return Results.Ok(ToDto(job));
        })
        .WithName("PatchJob")
        .WithDescription("Partially update a job (Name and/or Prompt) regardless of status. Used by inline-rename UX.");

        group.MapDelete("/{jobId:guid}", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            db.Jobs.Remove(job);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteJob");

        // ═══════════════════════════════════════════════════════════════
        // Job Execution Endpoints
        // ═══════════════════════════════════════════════════════════════

        group.MapPost("/{jobId:guid}/execute", async (
            Guid jobId,
            JobExecutionRequest? request,
            JobExecutor executor,
            CancellationToken ct) =>
        {
            var result = await executor.ExecuteJobAsync(
                jobId,
                request?.InputParameters,
                dryRun: false,
                ct);

            if (!result.IsSuccess)
            {
                return result.Error?.Contains("not found") == true
                    ? Results.NotFound(new { error = result.Error })
                    : Results.BadRequest(new { error = result.Error });
            }

            return Results.Ok(new JobExecutionResponse
            {
                Success = result.IsSuccess,
                RunId = result.RunId,
                Output = result.Output,
                TokensUsed = result.TokensUsed,
                DurationMs = (int)result.Duration.TotalMilliseconds,
                WasDryRun = result.WasDryRun
            });
        })
        .WithName("ExecuteJob")
        .WithDescription("Execute a job immediately with optional input parameter overrides");

        group.MapPost("/{jobId:guid}/dry-run", async (
            Guid jobId,
            JobExecutionRequest? request,
            JobExecutor executor,
            CancellationToken ct) =>
        {
            var result = await executor.ExecuteJobAsync(
                jobId,
                request?.InputParameters,
                dryRun: true,
                ct);

            if (!result.IsSuccess)
            {
                return result.Error?.Contains("not found") == true
                    ? Results.NotFound(new { error = result.Error })
                    : Results.BadRequest(new { error = result.Error });
            }

            return Results.Ok(new JobExecutionResponse
            {
                Success = result.IsSuccess,
                RunId = result.RunId,
                Output = result.Output,
                TokensUsed = result.TokensUsed,
                DurationMs = (int)result.Duration.TotalMilliseconds,
                WasDryRun = result.WasDryRun
            });
        })
        .WithName("DryRunJob")
        .WithDescription("Execute a job without persisting a JobRun (for testing prompts)");

        // Run-now: trigger an immediate execution of the job's prompt without altering
        // the schedule. Reuses JobExecutor (the same path used by /execute and the
        // scheduler when a tick fires) so behavior is identical to a scheduled run.
        // Distinct route from /execute so frontends and operators can express intent
        // ("run this on demand right now") clearly in logs and audit history.
        group.MapPost("/{jobId:guid}/run-now", async (
            Guid jobId,
            JobExecutor executor,
            CancellationToken ct) =>
        {
            var result = await executor.ExecuteJobAsync(
                jobId,
                null,
                dryRun: false,
                ct);

            if (!result.IsSuccess)
            {
                return result.Error?.Contains("not found") == true
                    ? Results.NotFound(new { error = result.Error })
                    : Results.BadRequest(new { error = result.Error });
            }

            return Results.Ok(new JobExecutionResponse
            {
                Success = result.IsSuccess,
                RunId = result.RunId,
                Output = result.Output,
                TokensUsed = result.TokensUsed,
                DurationMs = (int)result.Duration.TotalMilliseconds,
                WasDryRun = result.WasDryRun
            });
        })
        .WithName("RunJobNow")
        .WithDescription("Trigger an immediate one-shot execution of the job, independent of its schedule");

        // ═══════════════════════════════════════════════════════════════
        // Job State Transition Endpoints
        // ═══════════════════════════════════════════════════════════════

        group.MapPost("/{jobId:guid}/start", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            if (!JobStatusTransitions.IsAllowed(job.Status, JobStatus.Active))
            {
                return Results.Conflict(new
                {
                    error = $"Cannot start job in '{job.Status.ToString().ToLowerInvariant()}' state. " +
                            $"Valid source states: Draft."
                });
            }

            JobStatusChangeRecorder.RecordTransition(db, job, JobStatus.Active, reason: "start endpoint");
            await db.SaveChangesAsync();

            return Results.Ok(ToDto(job));
        })
        .WithName("StartJob")
        .WithDescription("Activate a Draft job (Draft → Active)");

        group.MapPost("/{jobId:guid}/pause", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            if (!JobStatusTransitions.IsAllowed(job.Status, JobStatus.Paused))
            {
                return Results.Conflict(new
                {
                    error = $"Cannot pause job in '{job.Status.ToString().ToLowerInvariant()}' state. " +
                            $"Valid source states: Active."
                });
            }

            JobStatusChangeRecorder.RecordTransition(db, job, JobStatus.Paused, reason: "pause endpoint");
            await db.SaveChangesAsync();

            return Results.Ok(ToDto(job));
        })
        .WithName("PauseJob")
        .WithDescription("Pause an Active job (Active → Paused)");

        group.MapPost("/{jobId:guid}/resume", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            if (!JobStatusTransitions.IsAllowed(job.Status, JobStatus.Active))
            {
                return Results.Conflict(new
                {
                    error = $"Cannot resume job in '{job.Status.ToString().ToLowerInvariant()}' state. " +
                            $"Valid source states: Paused."
                });
            }

            JobStatusChangeRecorder.RecordTransition(db, job, JobStatus.Active, reason: "resume endpoint");
            await db.SaveChangesAsync();

            return Results.Ok(ToDto(job));
        })
        .WithName("ResumeJob")
        .WithDescription("Resume a Paused job (Paused → Active)");

        // Concept-review §4b: explicit archive endpoint moves a Completed/Cancelled job to Archived.
        group.MapPost("/{jobId:guid}/archive", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            if (!JobStatusTransitions.IsAllowed(job.Status, JobStatus.Archived))
            {
                return Results.Conflict(new
                {
                    error = $"Cannot archive job in '{job.Status.ToString().ToLowerInvariant()}' state. " +
                            $"Only Completed or Cancelled jobs can be archived."
                });
            }

            JobStatusChangeRecorder.RecordTransition(db, job, JobStatus.Archived, reason: "archive endpoint");
            await db.SaveChangesAsync();

            return Results.Ok(ToDto(job));
        })
        .WithName("ArchiveJob")
        .WithDescription("Archive a Completed or Cancelled job");

        // Concept-review §4b: status-change history view.
        group.MapGet("/{jobId:guid}/history", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var rows = await db.Set<JobDefinitionStateChange>()
                .Where(c => c.JobId == jobId)
                .OrderByDescending(c => c.ChangedAt)
                .Take(200)
                .Select(c => new
                {
                    c.Id,
                    From = c.FromStatus.ToString(),
                    To = c.ToStatus.ToString(),
                    c.Reason,
                    c.ChangedBy,
                    c.ChangedAt,
                })
                .ToListAsync();
            return Results.Ok(rows);
        })
        .WithName("GetJobStatusHistory");

        // Concept-review §4b/UX: single-button "Create & Activate" path used by demo
        // template galleries — atomic create-in-Active-state instead of create-then-start.
        group.MapPost("/from-template/{templateName}/activate",
            async (string templateName, CreateJobRequest request, IDbContextFactory<OpenClawDbContext> dbFactory, IAgentProfileStore profileStore) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Prompt))
                return Results.BadRequest(new { error = "Name and Prompt are required." });

            await using var db = await dbFactory.CreateDbContextAsync();

            var isRecurring = !string.IsNullOrEmpty(request.CronExpression);
            DateTime? nextRun = request.RunAt?.ToUniversalTime();
            if (nextRun is null && !isRecurring)
                nextRun = DateTime.UtcNow.AddHours(1);

            var resolvedAgentProfileName = await ResolveAgentProfileNameAsync(request.AgentProfileName, profileStore);

            var job = new ScheduledJob
            {
                Name = request.Name,
                Prompt = request.Prompt,
                CronExpression = request.CronExpression,
                NextRunAt = nextRun,
                IsRecurring = isRecurring,
                Status = JobStatus.Draft, // recorder will move it to Active
                StartAt = request.StartAt?.ToUniversalTime(),
                EndAt = request.EndAt?.ToUniversalTime(),
                TimeZone = request.TimeZone,
                NaturalLanguageSchedule = request.NaturalLanguageSchedule,
                AllowConcurrentRuns = request.AllowConcurrentRuns,
                AgentProfileName = resolvedAgentProfileName,
                SourceTemplateName = templateName,
            };

            db.Jobs.Add(job);
            JobStatusChangeRecorder.RecordTransition(db, job, JobStatus.Active,
                reason: $"create-and-activate from template '{templateName}'");
            await db.SaveChangesAsync();

            return Results.Created($"/api/jobs/{job.Id}", ToDto(job));
        })
        .WithName("CreateAndActivateJobFromTemplate");

        // ═══════════════════════════════════════════════════════════════
        // Job Stats and Runs Endpoints
        // ═══════════════════════════════════════════════════════════════

        group.MapGet("/{jobId:guid}/stats", async (Guid jobId, IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            var runs = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .ToListAsync();

            var totalRuns = runs.Count;
            var completedRuns = runs.Count(r => r.Status == "completed");
            var failedRuns = runs.Count(r => r.Status == "failed");
            var totalTokens = runs.Where(r => r.TokensUsed.HasValue).Sum(r => r.TokensUsed!.Value);
            var avgTokens = completedRuns > 0
                ? runs.Where(r => r.Status == "completed" && r.TokensUsed.HasValue)
                       .Average(r => r.TokensUsed!.Value)
                : 0;

            var avgDurationMs = completedRuns > 0
                ? runs.Where(r => r.Status == "completed" && r.CompletedAt.HasValue)
                       .Average(r => (r.CompletedAt!.Value - r.StartedAt).TotalMilliseconds)
                : 0;

            var successRate = totalRuns > 0 ? (double)completedRuns / totalRuns : 0.0;
            var lastRunAt = runs.OrderByDescending(r => r.StartedAt).FirstOrDefault()?.StartedAt;

            return Results.Ok(new JobStatsResponse
            {
                JobId = jobId,
                TotalRuns = totalRuns,
                CompletedRuns = completedRuns,
                FailedRuns = failedRuns,
                SuccessRate = successRate,
                TotalTokensUsed = totalTokens,
                AverageTokensPerRun = (int)avgTokens,
                AverageDurationMs = (int)avgDurationMs,
                LastRunAt = lastRunAt
            });
        })
        .WithName("GetJobStats")
        .WithDescription("Get aggregated statistics for a job");

        group.MapGet("/{jobId:guid}/runs", async (
            Guid jobId,
            int? limit,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            var pageSize = Math.Min(limit ?? 50, 100);
            var runs = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.StartedAt)
                .Take(pageSize)
                .Select(r => new JobRunDto
                {
                    Id = r.Id,
                    Status = r.Status,
                    Result = r.Result,
                    Error = r.Error,
                    StartedAt = r.StartedAt,
                    CompletedAt = r.CompletedAt,
                    TokensUsed = r.TokensUsed,
                    InputSnapshotJson = r.InputSnapshotJson,
                    ExecutedByAgentProfile = r.ExecutedByAgentProfile
                })
                .ToListAsync();

            return Results.Ok(runs);
        })
        .WithName("GetJobRuns")
        .WithDescription("Get paginated list of job runs (default 50, max 100)");

        // Per-run event timeline (persisted, survives restart). See JobRunEvent.
        group.MapGet("/{jobId:guid}/runs/{runId:guid}/events", async (
            Guid jobId,
            Guid runId,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var run = await db.JobRuns.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId);
            if (run is null) return Results.NotFound();

            var events = await db.JobRunEvents
                .Where(e => e.JobRunId == runId)
                .OrderBy(e => e.Sequence)
                .Select(e => new JobRunEventDto
                {
                    Id = e.Id,
                    Sequence = e.Sequence,
                    Timestamp = e.Timestamp,
                    Kind = e.Kind,
                    ToolName = e.ToolName,
                    ArgumentsJson = e.ArgumentsJson,
                    ResultJson = e.ResultJson,
                    Message = e.Message,
                    DurationMs = e.DurationMs,
                    TokensUsed = e.TokensUsed
                })
                .ToListAsync();

            return Results.Ok(events);
        })
        .WithName("GetJobRunEvents")
        .WithDescription("Get the persisted event timeline for a single job run.");

        group.MapGet("/{jobId:guid}/runs/latest", async (
            Guid jobId,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            var latestRun = await db.JobRuns
                .Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefaultAsync();

            if (latestRun is null)
                return Results.NotFound(new { error = "No runs found for this job." });

            var eventCount = await db.JobRunEvents
                .Where(e => e.JobRunId == latestRun.Id)
                .CountAsync();

            return Results.Ok(new JobRunDetailDto
            {
                Id = latestRun.Id,
                JobId = latestRun.JobId,
                Status = latestRun.Status,
                Result = latestRun.Result,
                Error = latestRun.Error,
                StartedAt = latestRun.StartedAt,
                CompletedAt = latestRun.CompletedAt,
                TokensUsed = latestRun.TokensUsed,
                InputSnapshotJson = latestRun.InputSnapshotJson,
                ExecutedByAgentProfile = latestRun.ExecutedByAgentProfile,
                EventCount = eventCount
            });
        })
        .WithName("GetLatestJobRun")
        .WithDescription("Get the most recent run for a job with event count.");

        group.MapGet("/{jobId:guid}/runs/{runId:guid}", async (
            Guid jobId,
            Guid runId,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var run = await db.JobRuns.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId);
            if (run is null) return Results.NotFound();

            var eventStats = await db.JobRunEvents
                .Where(e => e.JobRunId == runId)
                .GroupBy(e => 1)
                .Select(g => new
                {
                    TotalEvents = g.Count(),
                    ToolCallCount = g.Count(e => e.Kind == "tool_call"),
                    ErrorCount = g.Count(e => e.Kind == "error")
                })
                .FirstOrDefaultAsync();

            return Results.Ok(new JobRunDetailDto
            {
                Id = run.Id,
                JobId = run.JobId,
                Status = run.Status,
                Result = run.Result,
                Error = run.Error,
                StartedAt = run.StartedAt,
                CompletedAt = run.CompletedAt,
                TokensUsed = run.TokensUsed,
                InputSnapshotJson = run.InputSnapshotJson,
                ExecutedByAgentProfile = run.ExecutedByAgentProfile,
                EventCount = eventStats?.TotalEvents ?? 0,
                ToolCallCount = eventStats?.ToolCallCount ?? 0,
                ErrorEventCount = eventStats?.ErrorCount ?? 0
            });
        })
        .WithName("GetJobRunDetail")
        .WithDescription("Get full detail for a single run including event statistics.");

        group.MapGet("/{jobId:guid}/runs/{runId:guid}/logs", async (
            Guid jobId,
            Guid runId,
            string? format,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var run = await db.JobRuns.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId);
            if (run is null) return Results.NotFound();

            var job = await db.Jobs.FindAsync(jobId);
            var events = await db.JobRunEvents
                .Where(e => e.JobRunId == runId)
                .OrderBy(e => e.Sequence)
                .ToListAsync();

            var fileName = $"job-run-{runId:N}.{(format == "json" ? "json" : "txt")}";
            var contentType = format == "json" ? "application/json" : "text/plain";

            string content;
            if (format == "json")
            {
                var logData = new
                {
                    jobId,
                    jobName = job?.Name ?? "Unknown",
                    runId,
                    status = run.Status,
                    startedAt = run.StartedAt,
                    completedAt = run.CompletedAt,
                    tokensUsed = run.TokensUsed,
                    result = run.Result,
                    error = run.Error,
                    executedByAgentProfile = run.ExecutedByAgentProfile,
                    inputSnapshotJson = run.InputSnapshotJson,
                    events = events.Select(e => new
                    {
                        sequence = e.Sequence,
                        timestamp = e.Timestamp,
                        kind = e.Kind,
                        toolName = e.ToolName,
                        message = e.Message,
                        durationMs = e.DurationMs,
                        tokensUsed = e.TokensUsed,
                        argumentsJson = e.ArgumentsJson,
                        resultJson = e.ResultJson
                    }).ToList()
                };
                content = JsonSerializer.Serialize(logData, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Job Run Log Export");
                sb.AppendLine($"==================");
                sb.AppendLine($"Job: {job?.Name ?? "Unknown"} ({jobId})");
                sb.AppendLine($"Run ID: {runId}");
                sb.AppendLine($"Status: {run.Status}");
                sb.AppendLine($"Started: {run.StartedAt:O}");
                if (run.CompletedAt.HasValue)
                    sb.AppendLine($"Completed: {run.CompletedAt:O}");
                if (run.TokensUsed.HasValue)
                    sb.AppendLine($"Tokens: {run.TokensUsed}");
                if (!string.IsNullOrEmpty(run.ExecutedByAgentProfile))
                    sb.AppendLine($"Agent Profile: {run.ExecutedByAgentProfile}");
                sb.AppendLine();

                if (!string.IsNullOrEmpty(run.Result))
                {
                    sb.AppendLine("Result:");
                    sb.AppendLine(run.Result);
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(run.Error))
                {
                    sb.AppendLine("Error:");
                    sb.AppendLine(run.Error);
                    sb.AppendLine();
                }

                if (events.Any())
                {
                    sb.AppendLine($"Events ({events.Count}):");
                    sb.AppendLine("---");
                    foreach (var evt in events)
                    {
                        sb.AppendLine($"[{evt.Sequence}] {evt.Timestamp:O} - {evt.Kind}");
                        if (!string.IsNullOrEmpty(evt.ToolName))
                            sb.AppendLine($"    Tool: {evt.ToolName}");
                        if (!string.IsNullOrEmpty(evt.Message))
                            sb.AppendLine($"    Message: {evt.Message}");
                        if (evt.DurationMs.HasValue)
                            sb.AppendLine($"    Duration: {evt.DurationMs}ms");
                        if (evt.TokensUsed.HasValue)
                            sb.AppendLine($"    Tokens: {evt.TokensUsed}");
                        sb.AppendLine();
                    }
                }

                content = sb.ToString();
            }

            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(content),
                contentType,
                fileName);
        })
        .WithName("DownloadJobRunLogs")
        .WithDescription("Download run logs as text or JSON file (format=txt|json).");

        // Built-in job templates (read-only, embedded at build time).
        group.MapGet("/templates", ([Microsoft.AspNetCore.Mvc.FromServices] OpenClawNet.Gateway.Services.JobTemplates.JobTemplatesProvider provider) =>
            Results.Ok(provider.GetAll()))
            .WithName("ListJobTemplates")
            .WithDescription("List all built-in job templates that can be used to seed a new job.");

        group.MapGet("/templates/{id}", (string id, [Microsoft.AspNetCore.Mvc.FromServices] OpenClawNet.Gateway.Services.JobTemplates.JobTemplatesProvider provider) =>
        {
            var t = provider.Get(id);
            return t is null ? Results.NotFound() : Results.Ok(t);
        })
        .WithName("GetJobTemplate")
        .WithDescription("Get a single built-in job template by id.");

        // GET /api/jobs/{jobId}/runs/{runId}/tool-calls — returns all tool calls for a run with args/results/errors
        group.MapGet("/{jobId:guid}/runs/{runId:guid}/tool-calls", async (
            Guid jobId,
            Guid runId,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var run = await db.JobRuns.FindAsync(runId);
            if (run is null || run.JobId != jobId)
                return Results.NotFound();

            // ToolCallRecords are linked via SessionId (which is the RunId for job executions)
            var toolCalls = await db.ToolCalls
                .Where(tc => tc.SessionId == runId)
                .OrderBy(tc => tc.ExecutedAt)
                .Select(tc => new ToolCallDto
                {
                    Id = tc.Id,
                    ToolName = tc.ToolName,
                    Arguments = tc.Arguments,
                    Result = tc.Result,
                    Success = tc.Success,
                    DurationMs = tc.DurationMs,
                    ExecutedAt = tc.ExecutedAt
                })
                .ToListAsync();

            return Results.Ok(new JobRunToolCallsResponse
            {
                JobId = jobId,
                RunId = runId,
                ToolCalls = toolCalls,
                TotalCount = toolCalls.Count,
                SuccessCount = toolCalls.Count(tc => tc.Success),
                FailureCount = toolCalls.Count(tc => !tc.Success),
                TotalDurationMs = toolCalls.Sum(tc => tc.DurationMs)
            });
        })
        .WithName("GetJobRunToolCalls")
        .WithDescription("Returns all tool calls for a job run with arguments, results, and error details. Critical for debugging tool invocation issues.");

        // GET /api/jobs/{jobId}/runs/{runId}/artifacts — returns all artifacts for a run
        group.MapGet("/{jobId:guid}/runs/{runId:guid}/artifacts", async (
            Guid jobId,
            Guid runId,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var run = await db.JobRuns.FindAsync(runId);
            if (run is null || run.JobId != jobId)
                return Results.NotFound();

            var artifacts = await db.JobRunArtifacts
                .Where(a => a.JobRunId == runId)
                .OrderBy(a => a.Sequence)
                .Select(a => new JobRunArtifactDto
                {
                    Id = a.Id,
                    Sequence = a.Sequence,
                    ArtifactType = a.ArtifactType.ToString().ToLowerInvariant(),
                    Title = a.Title,
                    MimeType = a.MimeType,
                    ContentSizeBytes = a.ContentSizeBytes,
                    ContentInline = a.ContentInline,
                    ContentPath = a.ContentPath,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            return Results.Ok(new JobRunArtifactsResponse
            {
                JobId = jobId,
                RunId = runId,
                Artifacts = artifacts,
                TotalCount = artifacts.Count,
                TotalSizeBytes = artifacts.Sum(a => a.ContentSizeBytes)
            });
        })
        .WithName("GetJobRunArtifacts")
        .WithDescription("Returns all artifacts produced by a job run.");

        // GET /api/jobs/{jobId}/state-history — returns job status audit trail
        group.MapGet("/{jobId:guid}/state-history", async (
            Guid jobId,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var job = await db.Jobs.FindAsync(jobId);
            if (job is null)
                return Results.NotFound();

            var history = await db.JobStateChanges
                .Where(sc => sc.JobId == jobId)
                .OrderByDescending(sc => sc.ChangedAt)
                .Select(sc => new JobStateChangeDto
                {
                    Id = sc.Id,
                    FromStatus = sc.FromStatus.ToString().ToLowerInvariant(),
                    ToStatus = sc.ToStatus.ToString().ToLowerInvariant(),
                    Reason = sc.Reason,
                    ChangedBy = sc.ChangedBy,
                    ChangedAt = sc.ChangedAt
                })
                .ToListAsync();

            return Results.Ok(new JobStateHistoryResponse
            {
                JobId = jobId,
                JobName = job.Name,
                CurrentStatus = job.Status.ToString().ToLowerInvariant(),
                History = history
            });
        })
        .WithName("GetJobStateHistory")
        .WithDescription("Returns the audit trail of job status transitions.");

        // ═══════════════════════════════════════════════════════════════
        // Job Channel Configuration Endpoints (Phase 2A Story 5)
        // ═══════════════════════════════════════════════════════════════

        group.MapGet("/{jobId:guid}/channels", async (
            Guid jobId,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            var configs = await db.JobChannelConfigurations
                .Where(c => c.JobId == jobId)
                .OrderBy(c => c.ChannelType)
                .ToListAsync();

            var dtos = configs.Select(c => new
            {
                Id = c.Id,
                JobId = c.JobId,
                ChannelType = c.ChannelType,
                IsEnabled = c.IsEnabled,
                ChannelConfig = c.ChannelConfig,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            }).ToList();

            return Results.Ok(dtos);
        })
        .WithName("GetJobChannelConfigs")
        .WithDescription("Get all channel configurations for a job.");

        group.MapPost("/{jobId:guid}/channels", async (
            Guid jobId,
            ChannelConfigsRequest request,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            if (job is null) return Results.NotFound();

            // Remove existing configs
            var existingConfigs = await db.JobChannelConfigurations
                .Where(c => c.JobId == jobId)
                .ToListAsync();
            db.JobChannelConfigurations.RemoveRange(existingConfigs);

            // Add new configs
            foreach (var channel in request.Channels)
            {
                var configJson = channel.WebhookUrl is not null
                    ? JsonSerializer.Serialize(new { webhookUrl = channel.WebhookUrl })
                    : "{}";

                var config = new JobChannelConfiguration
                {
                    JobId = jobId,
                    ChannelType = channel.ChannelType,
                    IsEnabled = channel.IsEnabled,
                    ChannelConfig = configJson,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.JobChannelConfigurations.Add(config);
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { message = "Channel configurations saved successfully." });
        })
        .WithName("SaveJobChannelConfigs")
        .WithDescription("Save or update channel configurations for a job.");

        group.MapDelete("/{jobId:guid}/channels/{channelType}", async (
            Guid jobId,
            string channelType,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var config = await db.JobChannelConfigurations
                .FirstOrDefaultAsync(c => c.JobId == jobId && c.ChannelType == channelType);

            if (config is null) return Results.NotFound();

            db.JobChannelConfigurations.Remove(config);
            await db.SaveChangesAsync();

            return Results.Ok(new { message = $"Channel configuration for {channelType} removed." });
        })
        .WithName("DeleteJobChannelConfig")
        .WithDescription("Remove a specific channel configuration from a job.");
    }

    /// <summary>
    /// If the caller didn't specify an AgentProfileName, snapshot the current
    /// default profile name onto the job. This makes the choice visible in the
    /// UI ("Agent Profile" column / detail page) and stable across later
    /// changes to which profile is marked default. Returns null only if the
    /// store somehow has no default available — JobExecutor still has its own
    /// runtime-settings fallback for that edge case.
    /// </summary>
    /// <summary>
    /// Snapshots the default agent profile name onto a job at creation time when
    /// the request omits an explicit name. Used by both <c>POST /api/jobs</c>,
    /// <c>POST /api/jobs/from-template/.../activate</c>, and the
    /// <c>POST /api/demos/.../setup</c> endpoints so every job created through a
    /// supported entry-point has a stable, visible profile reference.
    /// </summary>
    internal static async Task<string?> ResolveAgentProfileNameAsync(
        string? requested,
        IAgentProfileStore profileStore,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested;

        try
        {
            var defaultProfile = await profileStore.GetDefaultAsync(ct);
            return defaultProfile?.Name;
        }
        catch
        {
            // Defensive: never block job creation on profile-store hiccups.
            return null;
        }
    }

    private static JobDto ToDto(ScheduledJob j) => new()
    {
        Id = j.Id,
        Name = j.Name,
        Prompt = j.Prompt,
        Status = j.Status.ToString().ToLowerInvariant(),
        IsRecurring = j.IsRecurring,
        CronExpression = j.CronExpression,
        NextRunAt = j.NextRunAt,
        LastRunAt = j.LastRunAt,
        CreatedAt = j.CreatedAt,
        StartAt = j.StartAt,
        EndAt = j.EndAt,
        TimeZone = j.TimeZone,
        NaturalLanguageSchedule = j.NaturalLanguageSchedule,
        AllowConcurrentRuns = j.AllowConcurrentRuns,
        AgentProfileName = j.AgentProfileName,
        SourceTemplateName = j.SourceTemplateName
    };
}

/// <summary>
/// Partial-update payload for <c>PATCH /api/jobs/{id}</c>. Only the
/// explicitly-included fields are applied; nulls mean "do not touch".
/// Limited to safe, user-facing fields that are valid in any job state.
/// </summary>
public sealed record JobPatchRequest
{
    public string? Name { get; init; }
    public string? Prompt { get; init; }
}

public sealed record CreateJobRequest
{
    public required string Name { get; init; }
    public required string Prompt { get; init; }
    public string? CronExpression { get; init; }
    public DateTime? RunAt { get; init; }
    public DateTime? StartAt { get; init; }
    public DateTime? EndAt { get; init; }
    public string? TimeZone { get; init; }
    public string? NaturalLanguageSchedule { get; init; }
    public bool AllowConcurrentRuns { get; init; }
    public string? AgentProfileName { get; init; }
    /// <summary>Optional lineage marker for jobs created from a built-in template/demo.</summary>
    public string? SourceTemplateName { get; init; }
}

public record JobDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Prompt { get; init; }
    public required string Status { get; init; }
    public bool IsRecurring { get; init; }
    public string? CronExpression { get; init; }
    public DateTime? NextRunAt { get; init; }
    public DateTime? LastRunAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartAt { get; init; }
    public DateTime? EndAt { get; init; }
    public string? TimeZone { get; init; }
    public string? NaturalLanguageSchedule { get; init; }
    public bool AllowConcurrentRuns { get; init; }
    public string? AgentProfileName { get; init; }
    public string? SourceTemplateName { get; init; }
}

public sealed record JobDetailDto : JobDto
{
    public List<JobRunDto> Runs { get; init; } = [];
}

public sealed record JobRunDto
{
    public Guid Id { get; init; }
    public required string Status { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? TokensUsed { get; init; }
    /// <summary>Snapshot of input parameters used for this run (JSON).</summary>
    public string? InputSnapshotJson { get; init; }
    /// <summary>Agent profile name used to execute this run (auditing).</summary>
    public string? ExecutedByAgentProfile { get; init; }
}

/// <summary>Extended run detail with event statistics.</summary>
public sealed record JobRunDetailDto
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public required string Status { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? TokensUsed { get; init; }
    public string? InputSnapshotJson { get; init; }
    public string? ExecutedByAgentProfile { get; init; }
    public int EventCount { get; init; }
    public int ToolCallCount { get; init; }
    public int ErrorEventCount { get; init; }
}

/// <summary>One row from the persisted event timeline for a single JobRun.</summary>
public sealed record JobRunEventDto
{
    public Guid Id { get; init; }
    public int Sequence { get; init; }
    public DateTime Timestamp { get; init; }
    public required string Kind { get; init; }
    public string? ToolName { get; init; }
    public string? ArgumentsJson { get; init; }
    public string? ResultJson { get; init; }
    public string? Message { get; init; }
    public int? DurationMs { get; init; }
    public int? TokensUsed { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// Execution DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record JobExecutionRequest
{
    public Dictionary<string, object>? InputParameters { get; init; }
}

public sealed record JobExecutionResponse
{
    public bool Success { get; init; }
    public Guid? RunId { get; init; }
    public string? Output { get; init; }
    public int TokensUsed { get; init; }
    public int DurationMs { get; init; }
    public bool WasDryRun { get; init; }
}

public sealed record JobStatsResponse
{
    public Guid JobId { get; init; }
    public int TotalRuns { get; init; }
    public int CompletedRuns { get; init; }
    public int FailedRuns { get; init; }
    public double SuccessRate { get; init; }
    public int TotalTokensUsed { get; init; }
    public int AverageTokensPerRun { get; init; }
    public int AverageDurationMs { get; init; }
    public DateTime? LastRunAt { get; init; }
}

public sealed record ToolCallDto
{
    public Guid Id { get; init; }
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public string? Result { get; init; }
    public bool Success { get; init; }
    public double DurationMs { get; init; }
    public DateTime ExecutedAt { get; init; }
}

public sealed record JobRunToolCallsResponse
{
    public Guid JobId { get; init; }
    public Guid RunId { get; init; }
    public List<ToolCallDto> ToolCalls { get; init; } = [];
    public int TotalCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public double TotalDurationMs { get; init; }
}

public sealed record JobRunArtifactDto
{
    public Guid Id { get; init; }
    public int Sequence { get; init; }
    public required string ArtifactType { get; init; }
    public string? Title { get; init; }
    public string? MimeType { get; init; }
    public long ContentSizeBytes { get; init; }
    public string? ContentInline { get; init; }
    public string? ContentPath { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record JobRunArtifactsResponse
{
    public Guid JobId { get; init; }
    public Guid RunId { get; init; }
    public List<JobRunArtifactDto> Artifacts { get; init; } = [];
    public int TotalCount { get; init; }
    public long TotalSizeBytes { get; init; }
}

public sealed record JobStateChangeDto
{
    public Guid Id { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public string? Reason { get; init; }
    public string? ChangedBy { get; init; }
    public DateTime ChangedAt { get; init; }
}

public sealed record JobStateHistoryResponse
{
    public Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string CurrentStatus { get; init; }
    public List<JobStateChangeDto> History { get; init; } = [];
}

// ── Channel Configuration DTOs (Phase 2A Story 5) ──

public sealed record ChannelConfigsRequest
{
    public required List<ChannelItem> Channels { get; init; }
}

public sealed record ChannelItem
{
    public required string ChannelType { get; init; }
    public bool IsEnabled { get; init; }
    public string? WebhookUrl { get; init; }
}

