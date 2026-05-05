using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenClawNet.Agent;
using OpenClawNet.Channels.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Executes scheduled jobs by resolving agent profiles, executing prompts,
/// and recording execution results as JobRuns.
/// </summary>
public sealed class JobExecutor
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly RuntimeAgentProvider _agentProvider;
    private readonly IAgentRuntime _agentRuntime;
    private readonly IAgentProfileStore _profileStore;
    private readonly RuntimeModelSettings _runtimeSettings;
    private readonly AgentInvocationLogger? _invocationLogger;
    private readonly IChannelDeliveryService? _deliveryService;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        RuntimeAgentProvider agentProvider,
        IAgentRuntime agentRuntime,
        IAgentProfileStore profileStore,
        RuntimeModelSettings runtimeSettings,
        ILogger<JobExecutor> logger,
        AgentInvocationLogger? invocationLogger = null,
        IChannelDeliveryService? deliveryService = null)
    {
        _dbFactory = dbFactory;
        _agentProvider = agentProvider;
        _agentRuntime = agentRuntime;
        _profileStore = profileStore;
        _runtimeSettings = runtimeSettings;
        _invocationLogger = invocationLogger;
        _deliveryService = deliveryService;
        _logger = logger;
    }

    /// <summary>
    /// Executes a job and records the result as a JobRun.
    /// </summary>
    /// <param name="jobId">ID of the job to execute</param>
    /// <param name="inputOverrides">Optional runtime input parameters (merged with job defaults)</param>
    /// <param name="dryRun">If true, execute but don't persist JobRun</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution result</returns>
    public async Task<JobExecutionResult> ExecuteJobAsync(
        Guid jobId,
        Dictionary<string, object>? inputOverrides = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var job = await db.Jobs.FindAsync([jobId], cancellationToken);

        if (job is null)
        {
            _logger.LogWarning("Job {JobId} not found", jobId);
            return JobExecutionResult.NotFound(jobId);
        }

        var startedAt = DateTime.UtcNow;
        JobRun? jobRun = null;

        try
        {
            // Resolve agent profile (or fall back to runtime settings)
            AgentProfile? profile = null;
            string profileName;

            if (!string.IsNullOrWhiteSpace(job.AgentProfileName))
            {
                profileName = job.AgentProfileName;
                profile = await _profileStore.GetAsync(profileName, cancellationToken);

                if (profile is null)
                {
                    _logger.LogWarning("AgentProfile '{ProfileName}' not found for job {JobId}, falling back to runtime settings",
                        profileName, jobId);
                }
            }
            else
            {
                profileName = "default";
            }

            // If no profile or not found, fall back to runtime model settings
            string provider;
            string model;
            string? instructions = null;

            if (profile is not null)
            {
                provider = profile.Provider ?? _runtimeSettings.Current.Provider;
                model = _runtimeSettings.Current.Model ?? "default";
                instructions = profile.Instructions;
                _logger.LogInformation("Using AgentProfile '{ProfileName}' (Provider: {Provider}, Model: {Model})",
                    profileName, provider, model);

                // Wave 4 PR-2 (Dallas): cron-triggered jobs cannot use AgentProfiles
                // that require tool approval — there is no human in the loop to click
                // Approve. Fail fast with a clear, actionable error rather than
                // hanging the scheduler.
                if (profile.RequireToolApproval && IsCronTriggered(job))
                {
                    var error = $"Cron jobs cannot use AgentProfiles that require tool approval. " +
                                $"Either disable approval on profile '{profileName}' or run the agent interactively.";
                    _logger.LogWarning("Refusing to run cron job {JobId} on requiring profile '{ProfileName}': {Error}",
                        jobId, profileName, error);

                    if (!dryRun)
                    {
                        var failedRun = new JobRun
                        {
                            JobId = jobId,
                            Status = "failed",
                            StartedAt = startedAt,
                            CompletedAt = DateTime.UtcNow,
                            InputSnapshotJson = "{}",
                            ExecutedByAgentProfile = profileName,
                            Error = error
                        };
                        db.JobRuns.Add(failedRun);
                        await db.SaveChangesAsync(cancellationToken);
                        return JobExecutionResult.Failed(failedRun.Id, error, dryRun);
                    }

                    return JobExecutionResult.Failed(null, error, dryRun);
                }
            }
            else
            {
                provider = _runtimeSettings.Current.Provider;
                model = _runtimeSettings.Current.Model ?? "default";
                _logger.LogInformation("Using RuntimeModelSettings (Provider: {Provider}, Model: {Model})",
                    provider, model);
            }

            // Merge input parameters
            var inputs = MergeInputParameters(job.InputParametersJson, inputOverrides);
            var inputSnapshot = JsonSerializer.Serialize(inputs);
            var prompt = SubstituteParameters(job.Prompt, inputs);

            // Prepend instructions if profile has them
            if (!string.IsNullOrWhiteSpace(instructions))
            {
                prompt = $"{instructions}\n\n{prompt}";
            }

            _logger.LogInformation("Executing job {JobId} (Profile: {Profile}, DryRun: {DryRun})", jobId, profileName, dryRun);

            // Create JobRun record (if not dry-run)
            if (!dryRun)
            {
                jobRun = new JobRun
                {
                    JobId = jobId,
                    Status = "running",
                    StartedAt = startedAt,
                    InputSnapshotJson = inputSnapshot,
                    ExecutedByAgentProfile = profileName
                };
                db.JobRuns.Add(jobRun);
                await db.SaveChangesAsync(cancellationToken);
            }

            // Execute via agent runtime
            var sessionId = Guid.NewGuid();
            var context = new AgentContext
            {
                SessionId = sessionId,
                UserMessage = prompt,
                ModelName = model
            };

            var result = await _agentRuntime.ExecuteAsync(context, cancellationToken);
            var completedAt = DateTime.UtcNow;
            var duration = completedAt - startedAt;

            // Update job and run
            if (!dryRun && jobRun is not null)
            {
                // Promote tool failures to run failure. Previously a successful
                // agent invocation that called a tool which failed (e.g.
                // markdown_convert returning a Fail ToolResult) was recorded as
                // Status="completed" with a vague paraphrase from the model in
                // the Result column ("markdown_convert tool failed: ..."). The
                // raw tool diagnostics never made it to JobRun.Error so the
                // Channel detail page's failure card stayed empty.
                //
                // If any tool returned Success=false we now flip the run to
                // Failed and persist the FULL tool diagnostics in jobRun.Error
                // (joined when there are multiple). The model's text is still
                // kept in jobRun.Result as a partial output for context.
                var failedTools = result.ToolResults.Where(r => !r.Success).ToList();
                if (failedTools.Count > 0)
                {
                    var summary = string.Join(
                        Environment.NewLine + Environment.NewLine,
                        failedTools.Select(t => $"Tool '{t.ToolName}' failed: {t.Error ?? "(no error message)"}"));

                    jobRun.Status = "failed";
                    jobRun.Error = summary;
                    jobRun.Result = result.FinalResponse;
                    jobRun.CompletedAt = completedAt;
                    jobRun.TokensUsed = result.TotalTokens;

                    job.LastRunAt = completedAt;
                    job.LastOutputJson = result.FinalResponse;

                    AppendRunEvents(db, jobRun, startedAt, completedAt, result, profileName, error: summary);
                    await db.SaveChangesAsync(cancellationToken);

                    _logger.LogWarning("Job {JobId} agent finished but {Count} tool call(s) failed; marking run Failed: {Summary}",
                        jobId, failedTools.Count, summary);

                    return JobExecutionResult.Failed(jobRun.Id, summary, dryRun);
                }

                jobRun.Status = "completed";
                jobRun.Result = result.FinalResponse;
                jobRun.CompletedAt = completedAt;
                jobRun.TokensUsed = result.TotalTokens;

                job.LastRunAt = completedAt;
                job.LastOutputJson = result.FinalResponse;

                AppendRunEvents(db, jobRun, startedAt, completedAt, result, profileName, error: null);

                await db.SaveChangesAsync(cancellationToken);

                // Concept-review §4c — record sibling-model invocation row.
                if (_invocationLogger is not null)
                {
                    _ = _invocationLogger.RecordAsync(new AgentInvocationLog
                    {
                        Kind = AgentInvocationKind.JobRun,
                        SourceId = jobRun.Id,
                        AgentProfileName = profileName,
                        Provider = job.AgentProfileName, // best-effort
                        Model = model,
                        TokensIn = null,
                        TokensOut = result.TotalTokens,
                        LatencyMs = (int)duration.TotalMilliseconds,
                        StartedAt = startedAt,
                        CompletedAt = completedAt,
                    }, CancellationToken.None);
                }

                // Phase 2 Story 6: Multi-channel delivery integration
                // After successful job completion, trigger delivery to all enabled channels.
                // Fire-and-forget pattern: job success NOT blocked by delivery failures.
                await TriggerMultiChannelDeliveryAsync(db, job, jobRun, result.FinalResponse, cancellationToken);
            }

            _logger.LogInformation("Job {JobId} executed successfully in {Duration}ms (Tokens: {Tokens})",
                jobId, duration.TotalMilliseconds, result.TotalTokens);

            return JobExecutionResult.Success(
                jobRun?.Id,
                result.FinalResponse,
                result.TotalTokens,
                duration,
                inputSnapshot,
                dryRun);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} execution failed", jobId);

            // Persist FULL exception chain (message + stack + inner exceptions)
            // so the Channels detail page and live console can surface meaningful
            // diagnostics instead of an empty cell. ex.ToString() includes the
            // Type, Message, StackTrace and any InnerException recursively —
            // this is the canonical "give me everything" rendering.
            var fullDetail = ex.ToString();

            if (!dryRun && jobRun is not null)
            {
                jobRun.Status = "failed";
                jobRun.Error = fullDetail;
                jobRun.CompletedAt = DateTime.UtcNow;
                AppendRunEvents(db, jobRun, startedAt, jobRun.CompletedAt.Value,
                    result: null, profileName: jobRun.ExecutedByAgentProfile, error: fullDetail);
                await db.SaveChangesAsync(cancellationToken);
            }

            return JobExecutionResult.Failed(jobRun?.Id, ex.Message, dryRun);
        }
    }

    /// <summary>
    /// Persists a timeline of JobRunEvents for a single run. Called once per run,
    /// after the agent finishes (successfully or with an exception). The agent
    /// runtime's <see cref="AgentContext.ExecutedToolCalls"/> and <c>ToolResults</c>
    /// are correlated by index — they are populated in lockstep inside
    /// <c>DefaultAgentRuntime.ExecuteAsync</c>, so element [i] of each refers to the
    /// same tool invocation. Payloads are truncated via <see cref="JobRunEvent.Truncate"/>.
    /// </summary>
    private static void AppendRunEvents(
        OpenClawDbContext db,
        JobRun jobRun,
        DateTime startedAt,
        DateTime completedAt,
        AgentContext? result,
        string? profileName,
        string? error)
    {
        var seq = 0;

        db.Set<JobRunEvent>().Add(new JobRunEvent
        {
            JobRunId = jobRun.Id,
            Sequence = seq++,
            Timestamp = startedAt,
            Kind = JobRunEventKind.AgentStarted,
            Message = profileName is null ? null : $"profile={profileName}"
        });

        if (result is not null)
        {
            var calls = result.ExecutedToolCalls;
            var results = result.ToolResults;
            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                var toolResult = i < results.Count ? results[i] : null;
                db.Set<JobRunEvent>().Add(new JobRunEvent
                {
                    JobRunId = jobRun.Id,
                    Sequence = seq++,
                    Timestamp = startedAt,
                    Kind = JobRunEventKind.ToolCall,
                    ToolName = call.Name,
                    ArgumentsJson = JobRunEvent.Truncate(call.Arguments),
                    ResultJson = JobRunEvent.Truncate(toolResult?.Success == false
                        ? toolResult?.Error
                        : toolResult?.Output),
                    DurationMs = toolResult is null ? null : (int)toolResult.Duration.TotalMilliseconds,
                    Message = toolResult?.Success == false ? "tool reported failure" : null
                });
            }
        }

        db.Set<JobRunEvent>().Add(new JobRunEvent
        {
            JobRunId = jobRun.Id,
            Sequence = seq,
            Timestamp = completedAt,
            Kind = error is null ? JobRunEventKind.AgentCompleted : JobRunEventKind.AgentFailed,
            Message = error ?? JobRunEvent.Truncate(result?.FinalResponse),
            DurationMs = (int)(completedAt - startedAt).TotalMilliseconds,
            TokensUsed = result?.TotalTokens
        });
    }

    /// <summary>
    /// Triggers multi-channel delivery for a completed job.
    /// Fire-and-forget pattern: never throws, logs all delivery outcomes.
    /// </summary>
    private async Task TriggerMultiChannelDeliveryAsync(
        OpenClawDbContext db,
        ScheduledJob job,
        JobRun jobRun,
        string? artifactContent,
        CancellationToken cancellationToken)
    {
        if (_deliveryService is null)
        {
            _logger.LogDebug("No IChannelDeliveryService available; skipping multi-channel delivery for job {JobId}", job.Id);
            return;
        }

        try
        {
            // Query enabled channel configurations for this job
            var channelConfigs = await db.JobChannelConfigurations
                .Where(jc => jc.JobId == job.Id && jc.IsEnabled)
                .ToListAsync(cancellationToken);

            if (channelConfigs.Count == 0)
            {
                _logger.LogInformation("No enabled channels configured for job {JobId}; skipping delivery", job.Id);
                return;
            }

            _logger.LogInformation("Triggering multi-channel delivery for job {JobId} to {Count} enabled channel(s)",
                job.Id, channelConfigs.Count);

            // Call delivery service (fire-and-forget: job success NOT blocked by delivery failures)
            // Content is job run result; artifact ID is job run ID
            var deliveryResult = await _deliveryService.DeliverAsync(
                job,
                jobRun.Id,
                artifactType: "text",
                content: artifactContent ?? "(no output)",
                cancellationToken);

            _logger.LogInformation(
                "Multi-channel delivery completed for job {JobId}: {Success}/{Total} succeeded, {Failed} failed in {Duration}ms",
                job.Id,
                deliveryResult.SuccessCount,
                deliveryResult.TotalAttempted,
                deliveryResult.FailureCount,
                deliveryResult.Duration.TotalMilliseconds);

            if (deliveryResult.FailureCount > 0)
            {
                _logger.LogWarning(
                    "Some channel deliveries failed for job {JobId}: {Failures}",
                    job.Id,
                    string.Join("; ", deliveryResult.Failures.Select(f => $"{f.ChannelType}: {f.ErrorMessage}")));
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget: catch all exceptions to ensure job completion is not blocked
            _logger.LogError(ex, "Multi-channel delivery failed for job {JobId}; job marked complete regardless", job.Id);
        }
    }

    /// <summary>
    /// A job is "cron-triggered" (i.e. unattended) when it has a recurring schedule.
    /// One-shot jobs that the user kicks off manually still go through this executor
    /// but a CronExpression is the canonical signal of background/unattended use.
    /// </summary>
    private static bool IsCronTriggered(ScheduledJob job)
        => !string.IsNullOrWhiteSpace(job.CronExpression);

    private static Dictionary<string, object> MergeInputParameters(
        string? jobInputsJson,
        Dictionary<string, object>? runtimeOverrides)
    {
        var jobInputs = string.IsNullOrWhiteSpace(jobInputsJson)
            ? new Dictionary<string, object>()
            : JsonSerializer.Deserialize<Dictionary<string, object>>(jobInputsJson) ?? new();

        if (runtimeOverrides is not null)
        {
            foreach (var (key, value) in runtimeOverrides)
            {
                jobInputs[key] = value;
            }
        }

        return jobInputs;
    }

    private static string SubstituteParameters(string template, Dictionary<string, object> parameters)
    {
        var result = template;
        foreach (var (key, value) in parameters)
        {
            result = result.Replace($"{{{key}}}", value?.ToString() ?? string.Empty);
        }
        return result;
    }
}

/// <summary>
/// Result of a job execution.
/// </summary>
public sealed record JobExecutionResult
{
    public bool IsSuccess { get; init; }
    public Guid? RunId { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public int TokensUsed { get; init; }
    public TimeSpan Duration { get; init; }
    public string? InputSnapshot { get; init; }
    public bool WasDryRun { get; init; }

    public static JobExecutionResult Success(Guid? runId, string? output, int tokens, TimeSpan duration, string? inputSnapshot, bool dryRun)
        => new()
        {
            IsSuccess = true,
            RunId = runId,
            Output = output,
            TokensUsed = tokens,
            Duration = duration,
            InputSnapshot = inputSnapshot,
            WasDryRun = dryRun
        };

    public static JobExecutionResult Failed(Guid? runId, string error, bool dryRun)
        => new()
        {
            IsSuccess = false,
            RunId = runId,
            Error = error,
            WasDryRun = dryRun
        };

    public static JobExecutionResult NotFound(Guid jobId)
        => new()
        {
            IsSuccess = false,
            Error = $"Job {jobId} not found"
        };

    public static JobExecutionResult ProfileNotFound(string profileName)
        => new()
        {
            IsSuccess = false,
            Error = $"AgentProfile '{profileName}' not found"
        };
}
