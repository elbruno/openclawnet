# Session 4 Resource Guide
**Prepared for:** Ricken (DevRel/Writer)  
**Prepared by:** Petey (Agent Platform Specialist)  
**Date:** 2026-05-26  
**Purpose:** Reference document for Session 4 content — links, code examples, and architecture patterns

---

## File-Based Skills & Microsoft Agent Framework

### Official Links
- **Microsoft Agent Framework Documentation**: https://learn.microsoft.com/agent-framework/overview/
- **Get Started with MAF**: https://learn.microsoft.com/agent-framework/get-started/
- **Agent Skills Specification (agentskills.io)**: https://agentskills.io
- **MAF Agent Skills**: https://learn.microsoft.com/en-us/microsoft/agents/agent-skills
- **Microsoft Agent Framework GitHub**: https://github.com/microsoft/agents

### Code Example: File-Based Skill Structure

**Frontmatter Pattern (`SKILL.md`)**:
```markdown
---
name: pirate-voice
description: Rewrites every answer in the voice of a salty 18th-century pirate.
---
You are a salty 18th-century pirate. Speak in pirate dialect at all times.

- Open with "Arrr" or "Ahoy".
- Use words like matey, scallywag, ye, yer, plunder, landlubber.
- Keep the underlying facts correct, but wrap them in nautical metaphors.
- End every reply with "Yarrr!"
```

**Spec-Compliant Fields (agentskills.io)**:
- **Required**: `name`, `description`
- **Optional**: `license`, `metadata` (vendor extensions like `metadata.openclawnet.category`)
- **Storage Layout**: `{skill-name}/SKILL.md` + optional `scripts/`, `references/`, `assets/` subdirectories

### MAF Integration Pattern

**Progressive Disclosure Flow** (4 stages):
1. **Advertise** — MAF reads frontmatter, adds skill summary to system prompt
2. **Load** — Agent selects skill, MAF loads full content into context
3. **Read Resource** — Agent requests specific reference files from `references/`
4. **Run Script** — Agent invokes executable in `scripts/` (deferred to future phase)

### Architecture: Skill Loading Pipeline

```
┌──────────────────────────────────────────────────────┐
│  UI/API                                              │
│  ↓                                                   │
│  OpenClawNetSkillsProvider (thin decorator)         │
│  • Layer attribution (system / installed / agent)   │
│  • Per-agent enablement filtering                   │
│  • Structured logging                               │
│  ↓                                                   │
│  Microsoft Agent Framework AgentSkillsProvider       │
│  • agentskills.io spec parsing                      │
│  • Progressive disclosure                           │
│  • YAML frontmatter + Markdown body                 │
│  ↓                                                   │
│  Agent Runtime                                       │
└──────────────────────────────────────────────────────┘
```

**Storage Layout** (3-tier):
```
C:\openclawnet\skills\
├── system\           # Ships with app, read-only (e.g., doc-processor, memory)
├── installed\        # Imported from github/awesome-copilot, shared across agents
├── agents\{name}\    # Per-agent overrides or custom skills
└── .quarantine\      # Imports pending approval (security gate)
```

**Precedence**: `agents/{name}/skills/` > `installed/` > `system/`

---

## Secrets Vault

### Official Links
- **Azure Key Vault Documentation**: https://learn.microsoft.com/azure/key-vault/
- **Azure Key Vault .NET SDK**: https://learn.microsoft.com/dotnet/api/azure.security.keyvault.secrets
- **Managed Identity Overview**: https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview
- **ASP.NET Core Configuration**: https://learn.microsoft.com/aspnet/core/fundamentals/configuration/

### Code Example: IVault / ISecretsStore Usage Pattern

**Interface Definition**:
```csharp
namespace OpenClawNet.Storage;

/// <summary>Audited runtime secret resolution facade.</summary>
public interface IVault
{
    Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default);
}

public sealed record VaultCallerContext(
    VaultCallerType CallerType,
    string CallerId,
    string? SessionId = null);

public enum VaultCallerType
{
    Tool,
    Configuration,
    Cli,
    System
}
```

**ISecretsStore Implementation Pattern**:
```csharp
namespace OpenClawNet.Storage;

/// <summary>
/// CRUD over the encrypted Secrets table. Plaintext values never round-trip to
/// disk; the implementation handles DataProtection encryption transparently.
/// </summary>
public interface ISecretsStore
{
    /// <summary>Returns the plaintext value, or null when the secret is not present.</summary>
    Task<string?> GetAsync(string name, CancellationToken ct = default);
    
    /// <summary>Insert or update a secret. The plaintext is encrypted before persistence.</summary>
    Task SetAsync(string name, string value, string? description = null, CancellationToken ct = default);
    
    /// <summary>Soft-deletes a secret by name.</summary>
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);
    
    /// <summary>Lists secret names + descriptions (no values returned, by design).</summary>
    Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default);
}
```

**Usage Example (from VaultService.cs)**:
```csharp
public sealed class VaultService : IVault
{
    private readonly ISecretsStore _store;
    private readonly ISecretAccessAuditor _auditor;
    private readonly IVaultSecretRedactor _redactor;

    public async Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            await _auditor.RecordAsync("<invalid>", ctx, success: false, ct);
            throw new VaultException("Vault secret reference is invalid.");
        }

        var value = await _store.GetAsync(name, ct);
        var success = value is not null;
        await _auditor.RecordAsync(name, ctx, success, ct);

        if (value is not null)
            _redactor.TrackResolvedValue(value);

        if (!success)
            throw new VaultException("Vault secret not found or unavailable.");

        return value;
    }
}
```

### Configuration Override Hierarchy

**Resolution Chain** (first non-null wins):
1. **Azure Key Vault** — (if configured) `DefaultAzureCredential` auth
2. **Environment Variables** — `OPENCLAWNET_SECRET_<UPPER_SNAKE>`
3. **SQLite Local** — Encrypted with ASP.NET Core Data Protection

**Configuration Shape** (`appsettings.Production.json`):
```json
{
  "Vault": {
    "Backends": [ "AzureKeyVault", "Environment", "Sqlite" ]
  },
  "Storage": {
    "Azure": {
      "KeyVault": {
        "Uri": "https://openclawnet-prod.vault.azure.net/",
        "CacheTtlMinutes": 15
      }
    }
  }
}
```

### Architecture: Secrets Resolution Flow

```
Tool/Config Request
    ↓
IVault.ResolveAsync(name, context)
    ↓
ChainedSecretsStore (ordered backends)
    ├─→ AzureKeyVaultSecretsStore (read-only, DefaultAzureCredential)
    ├─→ EnvironmentSecretsStore (env vars + /run/secrets/* files)
    └─→ SecretsStore (SQLite + DataProtection, read-write)
    ↓
ISecretAccessAuditor.RecordAsync (audit trail)
    ↓
IVaultSecretRedactor.TrackResolvedValue (redact from logs)
    ↓
Return plaintext to caller
```

**Key Security Features**:
- Plaintext never persisted to SQLite (encrypted with Data Protection API)
- All access audited with caller context (tool ID, session ID, timestamp)
- Resolved values tracked for redaction in logs and tool outputs
- Azure Key Vault backed by Managed Identity (no credentials in config)

---

## Job Scheduling

### Official Links
- **Cronos Library (cron parsing)**: https://github.com/HangfireIO/Cronos
- **Quartz.NET (alternative scheduler)**: https://www.quartz-scheduler.net/
- **Azure Logic Apps (comparison)**: https://learn.microsoft.com/azure/logic-apps/

### Code Example: Job Definition Structure

**Entity Schema** (from `docs/architecture/jobs.md`):
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
}
```

### Code Example: Recurring vs One-Time Patterns

**Recurring (Cron)**:
```csharp
var recurringJob = new ScheduledJob
{
    Name = "Daily Summarizer",
    Prompt = "Summarize today's events from the logs.",
    AgentProfileName = "default-assistant",
    TriggerType = TriggerType.Cron,
    CronExpression = "0 9 * * *",  // Every day at 9 AM
    IsRecurring = true,
    Status = JobStatus.Active,
    AllowConcurrentRuns = false,
    TimeZone = "America/New_York"
};
```

**One-Time (OneShot)**:
```csharp
var oneShotJob = new ScheduledJob
{
    Name = "Quarterly Report",
    Prompt = "Generate Q1 2026 financial report.",
    AgentProfileName = "analyst",
    TriggerType = TriggerType.OneShot,
    NextRunAt = new DateTime(2026, 04, 01, 17, 0, 0, DateTimeKind.Utc),
    IsRecurring = false,
    Status = JobStatus.Active
};
```

**Cron Expression Syntax** (5-field format):
- `0 9 * * *` — Daily at 9 AM
- `*/5 * * * *` — Every 5 minutes
- `0 0 * * MON` — Every Monday at midnight
- `0 12 1 * *` — First day of month at noon

### Code Example: Scheduler Polling Pattern

**From `SchedulerPollingService.cs`**:
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var cfg = _settings.GetSettings();
    _logger.LogInformation("Scheduler started — poll={Poll}s", cfg.PollIntervalSeconds);

    while (!stoppingToken.IsCancellationRequested)
    {
        try { await ProcessDueJobsAsync(stoppingToken); }
        catch (Exception ex) { _logger.LogError(ex, "Error processing scheduled jobs"); }

        var pollInterval = TimeSpan.FromSeconds(cfg.PollIntervalSeconds);
        await Task.Delay(pollInterval, stoppingToken);
    }
}

private async Task ProcessDueJobsAsync(CancellationToken ct)
{
    var now = DateTime.UtcNow;
    var jobs = await db.Jobs
        .Where(j => j.Status == JobStatus.Active 
            && j.TriggerType == TriggerType.Cron 
            && j.NextRunAt <= now)
        .ToListAsync(ct);

    foreach (var job in jobs)
    {
        // Update last/next run times
        job.LastRunAt = now;
        var cronExpr = CronExpression.Parse(job.CronExpression);
        job.NextRunAt = cronExpr.GetNextOccurrence(now, TimeZoneInfo.Utc);

        // Execute via Gateway API: POST /api/jobs/{id}/execute
        await _httpClientFactory.CreateClient("gateway")
            .PostAsync($"/api/jobs/{job.Id}/execute", null, ct);
    }
}
```

### Status Tracking Metadata

**JobRun Entity** (execution history):
```csharp
public sealed class JobRun
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Status { get; set; }  // "running", "succeeded", "failed"
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? InputSnapshotJson { get; set; }  // Input params at execution time
    public int? TokensUsed { get; set; }            // Total tokens (prompt + completion)
    public string? ExecutedByAgentProfile { get; set; }
}
```

### Timeout & Retry Strategy

**Timeout Pattern** (from job executor):
```csharp
// 5-minute timeout per job execution
var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

try
{
    var result = await _agentRuntime.ExecuteAsync(prompt, linkedCts.Token);
    jobRun.Status = "succeeded";
    jobRun.Result = result;
}
catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
{
    jobRun.Status = "failed";
    jobRun.Error = "Execution timeout: no response after 5 minutes";
}
catch (Exception ex)
{
    jobRun.Status = "failed";
    jobRun.Error = ex.ToString();
}
finally
{
    jobRun.CompletedAt = DateTime.UtcNow;
}
```

**Configuration** (`SchedulerOptions`):
- `PollIntervalSeconds` — Default: 30, Range: 5-3600
- `MaxConcurrentJobs` — Default: 3, Range: 1-20
- `JobTimeoutSeconds` — Default: 300, Range: 10-7200

### Architecture: Job Execution Flow

```
SchedulerPollingService (BackgroundService)
    ↓ (every N seconds, poll active cron jobs)
Query: Status=Active, TriggerType=Cron, NextRunAt <= now
    ↓
Create JobRun (Status=running)
    ↓
POST /api/jobs/{id}/execute → Gateway
    ↓
Gateway: Resolve AgentProfile, merge InputParametersJson
    ↓
DefaultAgentRuntime.ExecuteAsync (with 5-min timeout)
    ↓
Update JobRun (Status=succeeded/failed, Result/Error, CompletedAt)
    ↓
Update Job.LastOutputJson, recalculate NextRunAt (cron)
```

---

## Deploy with Aspire

### Official Links
- **Aspire Deployment Overview**: https://learn.microsoft.com/dotnet/aspire/deployment/overview
- **Deploy to Azure Container Apps**: https://learn.microsoft.com/dotnet/aspire/deployment/azure/aca-deployment
- **azd CLI**: https://learn.microsoft.com/azure/developer/azure-developer-cli/overview
- **Azure Application Insights**: https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview
- **OpenTelemetry in .NET**: https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel

### Code Example: AppHost Topology

**From `AppHost.cs`**:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// SQLite database (local dev only)
var dbPath = builder.Configuration["OpenClawNet:ConnectionStrings:DbPath"]
    ?? Path.Combine(builder.AppHostDirectory, ".data");
var sqlite = builder.AddSqlite("openclawnet-db", databasePath: dbPath, databaseFileName: "openclawnet.db");

// External tool services (isolated)
var shellService = builder.AddProject<Projects.OpenClawNet_Services_Shell>("shell-service")
    .WithHttpHealthCheck("/health");

var browserService = builder.AddProject<Projects.OpenClawNet_Services_Browser>("browser-service")
    .WithHttpHealthCheck("/health");

// Gateway (central agent runtime)
var gateway = builder.AddProject<Projects.OpenClawNet_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(sqlite)
    .WithReference(shellService)
    .WithReference(browserService)
    .WaitFor(shellService)
    .WaitFor(browserService);

// Scheduler (background job polling)
var scheduler = builder.AddProject<Projects.OpenClawNet_Services_Scheduler>("scheduler")
    .WithHttpHealthCheck("/health")
    .WithReference(sqlite)
    .WithReference(gateway)
    .WaitFor(gateway);

// Web UI
var web = builder.AddProject<Projects.OpenClawNet_Web>("web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(gateway)
    .WithReference(scheduler)
    .WaitFor(gateway)
    .WaitFor(scheduler);

builder.Build().Run();
```

**Key Patterns**:
- `.WithReference()` — Service discovery + dependency injection
- `.WaitFor()` — Startup ordering (health check dependencies)
- `.WithHttpHealthCheck("/health")` — Liveness/readiness probes
- `.WithExternalHttpEndpoints()` — Expose to external traffic (ACA ingress)

### Deployment Matrix: Container Apps vs AKS vs VMs

| Dimension                  | **Container Apps (ACA)**                            | **AKS**                                              | **VMs**                                              |
|----------------------------|-----------------------------------------------------|------------------------------------------------------|------------------------------------------------------|
| **Operational Complexity** | Low (managed, serverless)                           | Medium (cluster management, node pools)              | High (OS patching, scaling, load balancing)          |
| **Scaling**                | Auto (0-N replicas, KEDA-based)                     | Manual + HPA/KEDA                                    | Manual (VMSS autoscale)                              |
| **Networking**             | Built-in ingress + service discovery                | Ingress controller + CoreDNS                         | Load balancer + custom DNS                           |
| **Cost Model**             | Pay-per-second compute + memory                     | Pay for nodes (always-on)                            | Pay for VMs (always-on)                              |
| **Best For**               | Aspire apps, microservices, HTTP workloads          | Complex orchestration, GPU, custom CNI               | Legacy monoliths, Windows GUI apps                   |
| **SQLite Support**         | ❌ Ephemeral storage (use Azure SQL/PostgreSQL)     | ✅ With persistent volumes (but not recommended)     | ✅ Persistent disk                                   |
| **Aspire Integration**     | ✅ Native (`azd up` via Aspire.Hosting.Azure)       | ⚠️ Manual (Helm charts, deploy via kubectl)         | ❌ Not a container platform                          |
| **Health Probes**          | Native (liveness, readiness, startup)               | Native (K8s probes)                                  | Custom (load balancer health checks)                 |
| **Service Discovery**      | Built-in (Aspire service refs → ACA env vars)       | CoreDNS (K8s Services)                               | Manual (DNS records, config files)                   |

### Deployment Decision Tree

```
Start: "Where should I deploy OpenClawNet?"
    ↓
Q1: Do I need GPU workloads or custom CNI?
    Yes → AKS (Container Apps doesn't support GPU yet)
    No  → Q2

Q2: Do I have existing Kubernetes expertise and infrastructure?
    Yes → AKS (leverage existing ops workflows)
    No  → Q3

Q3: Is this a lift-and-shift of a legacy app?
    Yes → VMs (if monolith with OS dependencies)
    No  → Q4

Q4: Do I want minimal operational overhead?
    Yes → Container Apps (fully managed, auto-scale)
    No  → AKS (more control, more complexity)

Recommendation for OpenClawNet (greenfield Aspire app):
    → **Azure Container Apps** (Scenario A in azure-deployment-options-analysis.md)
```

### Local → Production Workflow

**1. Local Development**:
```bash
# Run locally via Aspire
dotnet run --project src/OpenClawNet.AppHost

# Access dashboard at http://localhost:15888
```

**2. Pre-Deployment Prep**:
```bash
# Replace SQLite with Azure SQL/PostgreSQL connection string
# Move secrets to Key Vault (enable Managed Identity)
# Add Aspire.Hosting.Azure.AppContainers NuGet package
```

**3. Deploy to Azure** (via `azd`):
```bash
# Initialize Azure Developer CLI
azd init

# Provision infrastructure + deploy
azd up

# Output: Container Apps environment + ingress URLs
```

**4. AppHost Exports to**:
- **Container Images** — One image per `AddProject<>` resource (built via Dockerfile)
- **ACA Manifests** — YAML/Bicep describing ingress, scaling, env vars, health probes
- **Service Discovery** — Aspire `.WithReference()` → ACA environment variables
- **Secrets** — `vault://` references → Key Vault integration (via Managed Identity)

### Health Probe + Distributed Trace Integration

**Health Endpoint Pattern** (all services):
```csharp
// Program.cs
app.MapHealthChecks("/health");
```

**Application Insights Integration**:
```csharp
// Program.cs (all services)
builder.AddServiceDefaults();  // Configures OTel → App Insights

// Distributed tracing via OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("OpenClawNet.*"));

// Metrics + Logs automatically exported to App Insights
```

**Container Apps Health Probes** (auto-configured by `azd`):
- **Liveness**: `GET /health` (restart container if fails)
- **Readiness**: `GET /health` (remove from load balancer if fails)
- **Startup**: `GET /health` (delay traffic until healthy)

**App Insights Dashboard** (automatic):
- Request telemetry (HTTP status, duration, dependencies)
- Exception tracking (stack traces, correlation IDs)
- Distributed traces (Gateway → Shell Service → Ollama)
- Custom metrics (job execution time, token usage)

### Architecture: Aspire → ACA Deployment Flow

```
Local: AppHost.cs (DistributedApplication)
    ↓
azd init (scaffolds azure.yaml + infra/ Bicep templates)
    ↓
azd up
    ├─→ Build: dotnet publish (each project)
    ├─→ Containerize: docker build (per AddProject resource)
    ├─→ Push: docker push (to Azure Container Registry)
    ├─→ Provision: Bicep deployment (ACA environment, SQL, Key Vault, App Insights)
    └─→ Deploy: az containerapp create/update (with service refs → env vars)
    ↓
Production: Azure Container Apps
    ├─ web (external ingress, HTTPS)
    ├─ gateway (external ingress, HTTPS)
    ├─ scheduler (internal, no ingress)
    ├─ shell-service (internal, no ingress)
    ├─ browser-service (internal, no ingress)
    └─ Azure SQL + Key Vault + App Insights (managed PaaS)
```

---

## Architecture Assumptions & Gaps Found

### Assumptions Documented
1. **MAF handles agentskills.io spec parsing** — No custom parser needed for frontmatter YAML; MAF's `AgentSkillsProvider` already implements progressive disclosure correctly.
2. **IVault is the single secrets API** — Tools, configuration, CLI all call `IVault.ResolveAsync`; backend selection (SQLite, env vars, Key Vault) is transparent.
3. **Job scheduling is cron-based** — Cronos library parses 5-field cron expressions; scheduler polls every N seconds (default 30s).
4. **Aspire AppHost exports to ACA manifests** — `azd up` handles container builds, registry pushes, Bicep deployments; health checks become ACA probes.

### Gaps Identified
1. **No official MAF capabilities reference doc** — MAF documentation is split across multiple pages; no single "capabilities matrix" page for tool binding, permissions, guardrails.
2. **Aspire deployment guide assumes Azure SQL** — SQLite works locally but is incompatible with ACA ephemeral storage; migration guide missing from Aspire docs.
3. **Job retry logic not implemented** — `JobRun` tracks failures, but no automatic retry on transient errors (future enhancement: exponential backoff + max retries).
4. **Secrets rotation not automated** — `ISecretsStore.RotateAsync` exists but no scheduler integration to auto-rotate secrets on expiry.

---

## Resource Links Summary

**Microsoft Agent Framework**:
- Overview: https://learn.microsoft.com/agent-framework/overview/
- Get Started: https://learn.microsoft.com/agent-framework/get-started/
- Agent Skills: https://learn.microsoft.com/en-us/microsoft/agents/agent-skills
- GitHub: https://github.com/microsoft/agents

**agentskills.io Spec**:
- Specification: https://agentskills.io

**Azure Key Vault**:
- Documentation: https://learn.microsoft.com/azure/key-vault/
- .NET SDK: https://learn.microsoft.com/dotnet/api/azure.security.keyvault.secrets
- Managed Identity: https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview

**Aspire Deployment**:
- Overview: https://learn.microsoft.com/dotnet/aspire/deployment/overview
- Deploy to ACA: https://learn.microsoft.com/dotnet/aspire/deployment/azure/aca-deployment
- azd CLI: https://learn.microsoft.com/azure/developer/azure-developer-cli/overview

**Observability**:
- Application Insights: https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview
- OpenTelemetry in .NET: https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel

**Job Scheduling**:
- Cronos (cron parsing): https://github.com/HangfireIO/Cronos

---

## Next Steps for Ricken

1. **Extract code examples** into slide code blocks (trim to 5–10 lines per slide for readability).
2. **Convert architecture descriptions** to ASCII diagrams (use boxes, arrows, simple formatting).
3. **Link official docs** as "Learn More" footer on relevant slides.
4. **Add "Try It" sections** for local dev (`dotnet run`, `azd up`, `/health` endpoint checks).
5. **Flag gaps** as "Future Enhancements" or "Coming Soon" callouts (e.g., job retries, secrets rotation).

---

**End of Resource Guide**
