namespace OpenClawNet.Storage.Entities;

public sealed class ScheduledJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? CronExpression { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Draft;
    public bool IsRecurring { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Schedule effective start (null = immediately).</summary>
    public DateTime? StartAt { get; set; }

    /// <summary>Schedule expiry (null = no end).</summary>
    public DateTime? EndAt { get; set; }

    /// <summary>IANA timezone, e.g. "America/New_York" (null = UTC).</summary>
    public string? TimeZone { get; set; }

    /// <summary>Original natural-language input for reference.</summary>
    public string? NaturalLanguageSchedule { get; set; }

    /// <summary>
    /// When false (default), the scheduler skips a trigger if the job already has
    /// a run with Status == "running". When true, overlapping runs are allowed.
    /// </summary>
    public bool AllowConcurrentRuns { get; set; }

    /// <summary>Agent profile to use when executing this job.</summary>
    public string? AgentProfileName { get; set; }

    /// <summary>JSON dictionary for parameterized prompt template substitution.</summary>
    public string? InputParametersJson { get; set; }

    /// <summary>Result from last successful run (for chaining/debugging).</summary>
    public string? LastOutputJson { get; set; }

    /// <summary>How this job is triggered (Cron, OneShot, Webhook, Manual).</summary>
    public TriggerType TriggerType { get; set; } = TriggerType.Manual;

    /// <summary>Webhook endpoint for external triggers (null if not webhook-triggered).</summary>
    public string? WebhookEndpoint { get; set; }

    /// <summary>
    /// Id of the built-in template (or demo) this job was instantiated from, when
    /// applicable. Pure traceability — the job is fully editable and is NOT linked
    /// back to the template at runtime. Null for jobs created from scratch.
    /// </summary>
    public string? SourceTemplateName { get; set; }

    public List<JobRun> Runs { get; set; } = [];
}
