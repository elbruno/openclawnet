# Jobs Architecture

**Author:** Ripley (Lead / Architect)  
**Date:** 2026-04-28  
**Status:** Living Document

---

## Overview

Jobs are scheduled or triggered units of agent work in OpenClawNet. They represent the automation primitive that binds agent execution to time-based schedules, manual triggers, or external webhooks.

A Job is defined by:
- **Name** and **Prompt** (the instruction for the agent)
- **AgentProfileName** (which agent personality/configuration to use)
- **Schedule** (cron expression, one-shot datetime, or manual/webhook trigger)
- **Lifecycle state** (Draft → Active → Paused/Cancelled/Completed)
- **Input parameters** (JSON for template substitution in prompts)
- **Output tracking** (last result, for chaining and debugging)

---

## Domain Model

### ScheduledJob Entity

Primary entity representing a job definition.

```csharp
public sealed class ScheduledJob
{
    // Identity
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Prompt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Scheduling
    public string? CronExpression { get; set; }
    public bool IsRecurring { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? StartAt { get; set; }  // Effective start (null = immediate)
    public DateTime? EndAt { get; set; }    // Expiry (null = no end)
    public string? TimeZone { get; set; }   // IANA timezone (null = UTC)
    public string? NaturalLanguageSchedule { get; set; }

    // Lifecycle
    public JobStatus Status { get; set; }  // Draft, Active, Paused, Cancelled, Completed
    public bool AllowConcurrentRuns { get; set; }

    // Execution Config
    public string? AgentProfileName { get; set; }  // FK to AgentProfile
    public string? InputParametersJson { get; set; }  // Template substitution vars
    public string? LastOutputJson { get; set; }       // Last successful run result

    // Triggering
    public TriggerType TriggerType { get; set; }  // Manual, Cron, OneShot, Webhook
    public string? WebhookEndpoint { get; set; }   // For webhook-triggered jobs

    // Relations
    public List<JobRun> Runs { get; set; } = [];
}
```

**Key Columns (PR #6 Additions):**
- `InputParametersJson` — JSON dictionary for parameterized prompts (e.g., `{"customer_id": "123"}`)
- `LastOutputJson` — Result from last successful run (enables job chaining, debugging)
- `TriggerType` — Enum: Manual, Cron, OneShot, Webhook
- `WebhookEndpoint` — Webhook path for external triggers (null if not webhook)

---

### JobRun Entity

Represents a single execution of a Job.

```csharp
public sealed class JobRun
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Status { get; set; }  // "running", "completed", "failed"
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // PR #6 Additions
    public string? InputSnapshotJson { get; set; }  // Input params at execution time
    public int? TokensUsed { get; set; }            // Total tokens (prompt + completion)
    public string? ExecutedByAgentProfile { get; set; }  // Profile name for auditing

    public ScheduledJob Job { get; set; }
}
```

**Key Columns (PR #6 Additions):**
- `InputSnapshotJson` — Snapshot of input parameters used for this run (immutable audit trail)
- `TokensUsed` — Total token consumption for cost tracking
- `ExecutedByAgentProfile` — Agent profile name used (for late-binding audit)

---

### JobStatus Enum

5-state lifecycle:

```csharp
public enum JobStatus
{
    Draft = 0,      // Not yet activated. Editable. Not polled by scheduler.
    Active = 1,     // Scheduler polls and executes on schedule.
    Paused = 2,     // Temporarily suspended. Can be resumed.
    Cancelled = 3,  // Permanently stopped by user. Terminal.
    Completed = 4   // Schedule exhausted or one-shot completed. Terminal.
}
```

**Valid Transitions** (enforced by `JobStatusTransitions`):
- Draft → Active, Cancelled
- Active → Paused, Cancelled, Completed
- Paused → Active, Cancelled

**Editable States:** Draft, Paused (configuration changes allowed)  
**Terminal States:** Cancelled, Completed (no further transitions)

---

### TriggerType Enum

How a job is initiated:

```csharp
public enum TriggerType
{
    Manual = 0,   // User-initiated via UI/API
    Cron = 1,     // Recurring schedule via cron expression
    OneShot = 2,  // Single scheduled execution (datetime-based)
    Webhook = 3   // External event/webhook trigger
}
```

---

## Execution Flow (High-Level)

1. **Job Definition Phase:**
   - User creates Job (Status = Draft)
   - Sets Prompt, AgentProfileName, Schedule/TriggerType
   - Optionally provides InputParametersJson for parameterized prompts

2. **Activation:**
   - User transitions Job to Active (Draft → Active)
   - Scheduler starts polling (for Cron/OneShot)
   - Webhook endpoint registered (for Webhook triggers)

3. **Execution:**
   - Scheduler/trigger creates JobRun (Status = "running")
   - Resolves AgentProfile (late-binding via name)
   - Merges InputParametersJson into Prompt template
   - Executes agent via MAF Workflow (see PR #7)
   - Captures Result, Error, TokensUsed, InputSnapshotJson

4. **Completion:**
   - JobRun marked "completed" or "failed"
   - Job.LastOutputJson updated (if successful)
   - Job.NextRunAt recalculated (for recurring jobs)
   - Job.Status → Completed (if schedule exhausted or one-shot)

---

## Design Rationale

### Why JSON columns for inputs/outputs?

**Flexibility.** Jobs need to support arbitrary input parameters and outputs without schema changes. JSON columns provide:
- Type-agnostic storage (strings, numbers, objects, arrays)
- Easy serialization from C# objects
- Queryable via SQLite JSON functions (if needed later)

**Alternative considered:** Separate `JobParameter` table with key-value pairs. Rejected due to added complexity for minimal benefit.

### Why TriggerType enum?

**Explicit modeling.** Trigger semantics differ significantly:
- **Cron** → recurring, scheduler-driven
- **OneShot** → single execution, scheduler-driven
- **Webhook** → external event, API-driven
- **Manual** → user action, API-driven

Enum makes trigger logic explicit and prevents invalid state combinations (e.g., cron + webhook endpoint).

### Why InputSnapshotJson in JobRun?

**Immutable audit trail.** Job inputs can change between runs (late-binding, dynamic parameters). Snapshotting ensures:
- Reproducibility (know exact inputs for a failed run)
- Debugging (compare inputs across runs)
- Compliance (audit what was executed, not just configured)

### Why ExecutedByAgentProfile in JobRun?

**Late-binding audit.** Jobs bind to AgentProfile by *name* (not ID), enabling profile updates without breaking jobs. But this means the profile config can change between runs. Capturing the name at execution time provides an audit trail of which profile was used.

**Future enhancement:** Store full profile config snapshot if compliance requires it.

---

## Migration Strategy

Schema changes are applied via `OpenClawDbContext.OnModelCreating()` and EF Core's automatic schema migration (via `SchemaMigrator`).

**New columns added in PR #6:**
- ScheduledJob: `InputParametersJson`, `LastOutputJson`, `TriggerType`, `WebhookEndpoint`
- JobRun: `InputSnapshotJson`, `TokensUsed`, `ExecutedByAgentProfile`

All new columns are **nullable** (except `TriggerType`, which defaults to `Manual`) to preserve existing data.

**Backward Compatibility:**
- Existing jobs default to `TriggerType.Manual`
- Existing job runs have null for new columns (acceptable — old runs predated token tracking)

---

## Testing

**Unit Tests (PR #6):**
- Entity validation (new columns persist/query correctly)
- TriggerType enum conversion (stored as string, round-trips correctly)
- Nullable column defaults (minimal job can be created without new fields)

**Integration Tests (PR #7+):**
- Job execution with input parameters (template substitution)
- JobRun token tracking (MAF provides usage metadata)
- Late-binding AgentProfile resolution

---

## JobRun Lifecycle & Timeout Handling

**Background:** As of commit 507537e, all job executions are wrapped in a 5-minute `CancellationTokenSource`. This prevents runs from hanging indefinitely if the agent or a tool call stalls.

### Execution Lifecycle

1. **JobRun created** — Status = `running`
2. **Execution begins** — Agent prompt is sent to the configured provider, with a 5-minute cancellation token attached
3. **Normal completion** — Status → `succeeded`, result captured in `JobRun.Result`
4. **Failure (exception/tool error)** — Status → `failed`, error message captured in `JobRun.Error`
5. **Timeout (5 minutes exceeded)** — Cancellation token fires, Status → `failed` with error message: "Execution timeout: no response after 5 minutes"

### Key Properties

- `JobRun.Status`: Enum with values `running`, `succeeded`, `failed`
- `JobRun.Error`: Stack trace or timeout message (null if successful)
- `JobRun.CompletedAt`: UTC timestamp when the run finished (null while running)

### Timeout Rationale

The 5-minute timeout was introduced to close the gap where long-hanging jobs would remain in `running` state indefinitely, blocking log inspection and retries. Now:
- Stuck runs cleanly fail with a timeout error
- The next scheduled occurrence can proceed
- Failed runs are visible in the Web UI and API with error context

The timeout is configurable in the Scheduler configuration (see `SchedulerOptions.JobTimeoutSeconds`).

---

## Future Enhancements (Post-Session 4)

1. **Job Chaining via Dependencies** — `DependsOnJobId` FK for DAG workflows
2. **Event-Driven Triggers** — Job completion emits events for downstream jobs
3. **Snapshot Versioning** — Store full AgentProfile config in JobRun for compliance
4. **MAF Workflow Integration** — Multi-step jobs as DAG workflows (MAF Workflow primitives)
5. **Template Engine** — Structured prompt templates (Liquid, Scriban) instead of JSON merge

---

## Scheduler (PR #11)

### Overview

The Scheduler is a BackgroundService (`SchedulerPollingService`) that runs in the `OpenClawNet.Services.Scheduler` project. It polls the database for active jobs with `TriggerType = Cron` and executes them when they become due according to their cron expression.

### Architecture

**Components:**

1. **SchedulerPollingService** — BackgroundService that polls every N seconds (configurable, default 30s)
2. **CronExpressionEvaluator** — Utility wrapper around the Cronos library for parsing and evaluating cron expressions
3. **SchedulerOptions** — Configuration for poll interval, concurrency limits, timeouts, and enabled state
4. **SchedulerSettingsService** — Manages runtime settings persistence
5. **SchedulerRunState** — Tracks currently running job count for monitoring

**Execution Flow:**

1. Every poll interval (default 30s), the scheduler queries for jobs where:
   - `Status = Active`
   - `TriggerType = Cron`
   - `NextRunAt <= DateTime.UtcNow`

2. For each due job:
   - Validates schedule bounds (respects `StartAt` and `EndAt`)
   - Checks concurrency (`AllowConcurrentRuns` flag)
   - Updates `LastRunAt` and recalculates `NextRunAt` using Cronos
   - Calls `POST /api/jobs/{id}/execute` on the Gateway

3. Job execution is handled by the existing `JobExecutor` service in the Gateway, which:
   - Resolves the AgentProfile
   - Merges input parameters
   - Executes the prompt via the agent runtime
   - Records the result in a JobRun entity

**Cron Expressions:**

The scheduler uses the **Cronos** library, which supports both:
- **5-field format** (standard): `minute hour day month day-of-week`
- **6-field format** (with seconds): `second minute hour day month day-of-week`

Examples:
- `0 9 * * *` — Daily at 9 AM
- `*/5 * * * *` — Every 5 minutes
- `0 0 9 * * MON` — Every Monday at 9 AM (6-field)

**Edge Cases:**

1. **Invalid Cron Expression:** If parsing fails, `NextRunAt` is set to null, marking the schedule as exhausted (job transitions to `Completed`)
2. **Paused Jobs:** Scheduler only queries `Active` jobs, so paused jobs are automatically skipped
3. **Draft Jobs:** Draft jobs are not polled (only `Active` jobs)
4. **Concurrent Runs:** If `AllowConcurrentRuns = false` and a run is already in progress, the trigger is skipped but `NextRunAt` is still advanced
5. **Schedule Expiry:** If `NextRunAt` would exceed `EndAt`, the schedule is exhausted and the job transitions to `Completed`

**Configuration:**

Settings in `SchedulerOptions` (configurable via `appsettings.json` or runtime UI):
- `PollIntervalSeconds` — How often to check for due jobs (default: 30, range: 5-3600)
- `MaxConcurrentJobs` — Max parallel executions (default: 3, range: 1-20)
- `JobTimeoutSeconds` — Execution timeout per job (default: 300, range: 10-7200)
- `Enabled` — Master on/off switch (default: true)

**Integration with Gateway:**

The scheduler calls the Gateway's `/api/jobs/{id}/execute` endpoint via HTTP, which:
- Enables clean separation of concerns (scheduler → orchestrator → executor)
- Leverages existing JobExecutor logic (no duplication)
- Provides observability (all executions go through the same code path)

**Testing:**

Unit tests cover:
- Cron parsing and evaluation (`CronExpressionEvaluatorTests`)
- Next occurrence calculation with timezones (`SchedulerPollingServiceTests`)
- Edge cases (invalid cron, schedule expiry, concurrent runs)
- Options validation and clamping (`SchedulerOptionsTests`)

---

## References

- Original Analysis: `docs/analysis/jobs-skills-and-maf-architecture.md`
- Rollout Plan: Line 1116 (PR #6 spec)
- Job State Machine: `.squad/decisions/inbox/ripley-job-state-machine.md`
- Implementation Delta: `docs/analysis/jobs-implementation-delta.md`
