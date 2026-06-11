---
marp: true
title: "OpenClawNet — Session 4: Deploy, Operate & Scale"
description: "From new OpenClaw .NET capabilities to production-ready operations"
theme: openclaw
paginate: true
size: 16:9
footer: "OpenClawNet · Session 4 · Deploy, Operate & Scale"
---

<!-- _class: lead -->

# OpenClawNet
## Session 4 — Deploy, Operate & Scale

**Microsoft Reactor Series · ~60 min · Intermediate .NET**

> *From what's new to production-grade operations.*

<br>

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft  
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft  
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

---

## Session flow (today)

1. Welcome and session goals
2. What's new in OpenClaw .NET
3. File-based skills
4. Secrets Vault
5. Job scheduling
6. Transition to production readiness
7. Deploy with Aspire
8. Observe
9. Automate
10. Secure
11. Extend (skills)
12. Operate at scale
13. Live demo walkthrough
14. Q&A and wrap-up

---

<!-- _class: lead -->

# 1) Welcome and session goals

---

## Session goals

- Understand the three major new capabilities in OpenClaw .NET
- Connect these features to production-readiness practices
- Use Aspire deployment guidance as the deployment baseline
- Walk through an end-to-end operational flow you can apply immediately

---

# 2) What's new in OpenClaw .NET

---

<!-- _class: lead -->

# 3) File-based skills

---

## File-based skills overview

**What changed:** Skills are now first-class files in your repo

```text
skills/
  finance/
    reconciliation.md
    invoice-validation.md
  support/
    triage.md
    escalation.md
```

**Benefits:**
- Versionable with Git (track changes, review PRs)
- Promote across environments: dev → stage → prod
- Clear ownership per domain/team
- Testable before deployment

---

## Skill file structure + frontmatter

Each skill file contains:

```yaml
---
name: "invoice-validation"
description: "Validate invoice data against accounting rules"
version: "1.2.0"
owner: "finance-team"
tools: ["database", "email", "calculator"]
approval_required: false
---

# Invoice Validation Skill
You are an invoice validation assistant...
```

**Frontmatter drives:** tool binding, approval gates, version tracking

---

## Skill lifecycle diagram

```text
┌─────────────┐      ┌──────────────┐      ┌─────────────┐
│  Developer  │─────▶│  Git Commit  │─────▶│  CI Review  │
└─────────────┘      └──────────────┘      └─────────────┘
                                                   │
                                                   ▼
                           ┌────────────────────────────────┐
                           │   Skill Registry (Runtime)     │
                           ├────────────────────────────────┤
                           │  - Load on startup             │
                           │  - Hot reload on file change   │
                           │  - Bind tools via MAF          │
                           │  - Track version/owner         │
                           └────────────────────────────────┘
                                       │
                                       ▼
                           ┌────────────────────┐
                           │  Agent Execution   │
                           └────────────────────┘
```

---

## MAF integration + tool binding

Skills integrate with Microsoft Agent Framework:

```csharp
// Skill loader reads frontmatter
var skill = SkillLoader.Load("skills/finance/invoice-validation.md");

// MAF binds tools from frontmatter
var agent = new AgentBuilder()
    .WithSkill(skill)
    .WithTools(skill.RequestedTools) // ["database", "email", "calculator"]
    .WithApprovalPolicy(skill.ApprovalRequired)
    .Build();
```

**Result:** Declarative skill definition + runtime enforcement

Reference: [Microsoft Agent Framework](https://github.com/microsoft/agents)

---

## Rollout strategy: dev → stage → prod

**Phase 1: Dev environment**
- Edit skill file locally
- Test with dev data and tools
- Validate prompt quality

**Phase 2: Stage environment**
- Merge skill PR to stage branch
- Deploy to staging infra
- Run A/B tests with real scenarios

**Phase 3: Production**
- Tag skill version (e.g., `v1.2.0`)
- Deploy to prod via GitOps
- Monitor execution metrics
- Roll back if needed (revert commit)

> **Demo marker:** *DEMO: Live skill edit → reload → test execution*

---

<!-- _class: lead -->

# 4) Secrets Vault

---

## Secrets Vault architecture

```text
┌──────────────┐         ┌─────────────────┐         ┌─────────────────┐
│              │         │                 │         │                 │
│  Application │────────▶│ ISecretsProvider│────────▶│ Azure Key Vault │
│              │  inject │                 │  fetch  │                 │
└──────────────┘         └─────────────────┘         └─────────────────┘
                                  │
                                  │ (abstraction layer)
                                  │
                         ┌────────┴─────────┐
                         │                  │
                    Local Dev          Prod Runtime
                 (appsettings)     (managed identity)
```

**Flow:**
1. App requests secret by name (e.g., `"OpenAI:ApiKey"`)
2. Provider resolves from vault (or local config in dev)
3. Secret returned to app, never logged or persisted
4. Audit trail captured in Key Vault logs

---

## ISecretsProvider interface

**Contract:**

```csharp
public interface ISecretsProvider
{
    Task<string> GetSecretAsync(string secretName);
    Task<Dictionary<string, string>> GetSecretsAsync(string[] secretNames);
}
```

---

## Using secrets in agent startup

```csharp
public class AgentService
{
    private readonly ISecretsProvider _secrets;

    public AgentService(ISecretsProvider secrets) => _secrets = secrets;

    public async Task InitializeAsync()
    {
        var apiKey = await _secrets.GetSecretAsync("OpenAI:ApiKey");
        var dbConn = await _secrets.GetSecretAsync("Database:ConnectionString");
        _aiClient = new OpenAIClient(apiKey);
    }
}
```

---

## Operational security + audit trail

**Separation of duties:**

| Role | Responsibility |
|------|----------------|
| **Developer** | Reference secret name in code (`"OpenAI:ApiKey"`) |
| **Operator** | Set secret value in Key Vault, manage rotation |
| **Security Team** | Audit access logs, enforce least privilege |

**Benefits:**
- No secrets in source code or config files
- Centralized rotation (update vault, restart app)
- Full audit trail: who accessed what, when
- Managed identity eliminates credential management

**Azure Key Vault reference:** [https://learn.microsoft.com/azure/key-vault/](https://learn.microsoft.com/azure/key-vault/)

> **Demo marker:** *DEMO: Add secret to vault → app picks it up at startup*

---

<!-- _class: lead -->

# 5) Job scheduling

---

## Job scheduling overview

**Built-in scheduling for:**
- Recurring maintenance tasks (daily cleanup, weekly reports)
- One-time deferred work (post-deployment checks)
- Agent-driven automation (triage inbox, reconcile data)

**Operational visibility:**
- Status: pending, running, completed, failed
- Last run time + next scheduled run
- Execution history + failure logs
- Manual trigger capability

---

## Job lifecycle diagram

```text
┌──────────────┐
│ Job Definition│  (code or config)
└───────┬──────┘
        │
        ▼
┌──────────────────┐
│  Job Scheduler   │  (periodic timer or event-driven)
└───────┬──────────┘
        │
        ▼
┌──────────────────┐       ┌─────────────────┐
│ Job Executor     │──────▶│ Execution Metadata│
│  - Acquire lock  │       │  - Start time    │
│  - Run job logic │       │  - End time      │
│  - Release lock  │       │  - Status        │
└──────────────────┘       │  - Error logs    │
                           └─────────────────┘
```

**Key concepts:**
- **Lock:** Prevents duplicate execution (only one instance runs)
- **Metadata:** Tracks every execution for observability
- **Idempotency:** Jobs can safely retry without side effects

---

## Recurring job pattern

```csharp
[RecurringJob("daily-cleanup", Schedule = "0 2 * * *")] // 2 AM daily
public class DailyCleanupJob : IJob
{
    public async Task ExecuteAsync(JobContext context)
    {
        await CleanupTempFilesAsync();
        await ArchiveOldLogsAsync();
    }
}
```

---

## One-time deferred jobs

```csharp
// Enqueue job to run once, 5 minutes from now
await _jobScheduler.EnqueueAsync<PostDeploymentCheckJob>(
    delay: TimeSpan.FromMinutes(5),
    payload: new { DeploymentId = "abc123" }
);
```

**Idempotency:** Jobs check state before acting — safe to retry without side effects.

---

## Observability + status dashboard

**Dashboard view (conceptual):**

```text
╔═══════════════════════════════════════════════════════════════╗
║ Job Name            │ Status    │ Last Run  │ Next Run  │ Logs║
╠═══════════════════════════════════════════════════════════════╣
║ daily-cleanup       │ ✓ Success │ 2:00 AM   │ Tomorrow  │ View║
║ weekly-report       │ ⏳ Running│ Now       │ Next week │ View║
║ invoice-validation  │ ✗ Failed  │ 1:30 PM   │ Retry soon│ View║
╚═══════════════════════════════════════════════════════════════╝
```

**Metrics to track:**
- Job success/failure rate
- Average execution time
- Queue depth (pending jobs)
- Failure trends over time

---

## Reliability patterns

**Timeout handling:**

```csharp
[JobTimeout(Minutes = 10)]
public class LongRunningJob : IJob { ... }
```

**Retry with exponential backoff:**

```csharp
[RetryPolicy(MaxAttempts = 3, BackoffMultiplier = 2.0)]
public class UnreliableJob : IJob { ... }
```

**Dead-letter queue:** Failed jobs after max retries go to DLQ for manual review.

**Circuit breaker:** Pause job execution if downstream dependency is unhealthy.

> **Demo marker:** *DEMO: Create scheduled job → watch execution metadata*

---

## Why these updates matter together

- **Skills** define agent behavior
- **Vault** protects sensitive configuration and credentials
- **Jobs** make behavior run predictably without manual triggering

> This is the bridge from "demo app" to "operable platform".

---

<!-- _class: lead -->

# 6) Transition: from new features to production readiness

---

## Transition message

- New features matter most when they improve day-2 operations
- We now move from capabilities to operational architecture
- Flow we will follow:
  - Deploy -> Observe -> Automate -> Secure -> Extend -> Operate at scale

---

<!-- _class: lead -->

# 7) Deploy with Aspire

---

## Deployment target matrix

| Target | Best For | Trade-offs |
|--------|----------|------------|
| **Azure Container Apps** | Managed container runtime, auto-scaling, event-driven | Limited control over networking |
| **AKS (Kubernetes)** | Full control, existing K8s expertise, hybrid scenarios | Higher operational overhead |
| **VM / Container Host** | Legacy constraints, air-gapped, custom hardware | Manual scaling, patch management |

**Reference:** [aspire.dev/deployment](https://aspire.dev/deployment/)

**Decision factors:**
- Team expertise (K8s vs managed services)
- Networking requirements (private endpoints, VPN)
- Scale profile (steady vs bursty)
- Cost/ops trade-off

---

## Local → production workflow

```text
┌────────────────┐
│  Local Dev     │  aspire run (orchestrated containers)
└────────┬───────┘
         │
         ▼
┌────────────────┐
│  CI Build      │  dotnet build, run tests, package artifacts
└────────┬───────┘
         │
         ▼
┌────────────────┐
│  Staging       │  aspire deploy --env staging
│  Environment   │  (validation, integration tests)
└────────┬───────┘
         │
         ▼
┌────────────────┐
│  Production    │  aspire deploy --env prod
│  Environment   │  (gradual rollout, health checks)
└────────────────┘
```

**Key points:**
- Same app model, different environment config
- Aspire orchestrates local multi-service dev
- Production: individual services deployed to target infra

---

## Deployment readiness checklist (1/2)

**Infrastructure & access:**

- [ ] **Networking:** Private endpoints, firewall rules, DNS config
- [ ] **Identity:** Managed identity for Azure resources, RBAC assigned
- [ ] **Secrets:** All secrets in Key Vault, no hardcoded credentials
- [ ] **Observability:** App Insights configured, logs/metrics/traces enabled
- [ ] **Health probes:** `/health` endpoint implemented and tested

---

## Deployment readiness checklist (2/2)

**Runtime & governance:**

- [ ] **Scaling rules:** CPU/memory thresholds, min/max replicas defined
- [ ] **Backup/DR:** Data backup strategy, failover plan documented
- [ ] **Security:** Least privilege, no public admin endpoints
- [ ] **Cost governance:** Budget alerts, resource tags for tracking

---

## Health probes + distributed traces

**Health endpoint example:**

```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        var result = new {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e =>
                new { name = e.Key, status = e.Value.Status.ToString() })
        };
        await ctx.Response.WriteAsJsonAsync(result);
    }
});
```

**Distributed tracing:** Every request/tool call/job generates trace spans → view end-to-end flow in App Insights.

---

## Auto-scaling (Container Apps)

```yaml
scale:
  minReplicas: 2
  maxReplicas: 10
  rules:
    - name: http-scaling
      http:
        metadata:
          concurrentRequests: 50
```

---

## Resource limits + governance

**Per-replica limits:**

```yaml
resources:
  cpu: 1.0
  memory: 2Gi
```

- Scale based on load (CPU, memory, request count)
- Set limits to prevent runaway costs
- Load test to validate scaling behavior

---

## Cost and performance decisions

**Cost levers:**
- Replica count (scale to zero for dev environments)
- Instance size (CPU/memory per replica)
- Managed services vs self-hosted (ops time vs license cost)
- Data egress (cache frequently accessed data)

**Performance levers:**
- Connection pooling (database, HTTP clients)
- Caching layer (Redis, in-memory)
- Async/await for I/O-bound work
- Batch processing for bulk operations

**Best practice:** Measure with telemetry, optimize based on real usage patterns.

> **Demo marker:** *DEMO: `aspire describe`, show deployment readiness*

---

<!-- _class: lead -->

# 8) Observe

---

## Observability baseline

**Three pillars:**

1. **Logs:** Structured logs for debugging and audit trails
2. **Metrics:** Time-series data (request rate, error rate, latency)
3. **Traces:** Distributed tracing for request flows

**Foundation:**
- Health endpoints (`/health`, `/ready`, `/live`)
- Startup probes (is the app initialized?)
- Readiness probes (can it serve traffic?)
- Liveness probes (is it still responsive?)

---

## Distributed tracing for agent flows

**Trace example: User chat request**

```text
Trace ID: abc123...

├─ HTTP POST /api/chat (200ms)
│  ├─ Load skill from file (5ms)
│  ├─ Retrieve secret from Key Vault (15ms)
│  ├─ Call LLM API (150ms)
│  │  └─ Token usage: 120 input, 80 output
│  ├─ Execute tool: database query (25ms)
│  └─ Return response (5ms)
```

**Benefits:**
- See exact bottlenecks (which step took longest?)
- Track tool usage and token consumption
- Debug failures (where did the error occur?)

**Implementation:** Use OpenTelemetry + Azure Monitor / App Insights

---

## Actionable alerts (not dashboard noise)

**Good alerts:**
- Error rate >5% for 5 minutes → page on-call
- Job failure 3x in a row → notify team channel
- Secret expiring in <7 days → email operator

**Bad alerts (avoid):**
- CPU >50% (too noisy, not actionable)
- Any single request failure (spammy)
- Disk usage >10% (too early, not urgent)

**Alert design:**
1. Define **SLO** (service level objective): 99.9% uptime, p99 latency <500ms
2. Alert when **SLO at risk** (not arbitrary thresholds)
3. Include **runbook link** in alert (what to do next)

---

<!-- _class: lead -->

# 9) Automate

---

## Platform automation use cases

- **Drift detection:** Compare deployed config vs source-of-truth
- **Resource cleanup:** Delete old logs, temp files, stale caches
- **Health checks:** Periodic synthetic transactions
- **Data reconciliation:** Verify data consistency across systems
- **Cost optimization:** Identify idle resources, suggest rightsizing

---

## Drift detection job example

```csharp
[RecurringJob("config-drift-check", Schedule = "0 */6 * * *")]
public class ConfigDriftCheckJob : IJob
{
    public async Task ExecuteAsync(JobContext context)
    {
        var deployed = await GetDeployedConfigAsync();
        var expected = await LoadSourceOfTruthAsync();
        if (!deployed.Equals(expected))
            await NotifyOpsTeamAsync("Config drift detected!");
    }
}
```

---

## Automated operational checks

**Decision tree: When to automate?**

```text
Is the task...
├─ Repeating? (daily/weekly)
│  └─ Yes → Automate with scheduled job
│
├─ Error-prone if manual? (copy-paste mistakes)
│  └─ Yes → Automate with CI/CD or job
│
├─ Slow if manual? (>10 min of human time)
│  └─ Yes → Automate with job or script
│
└─ Otherwise → Keep manual with checklist
```

**Automation principles:**
- **Idempotent:** Safe to run multiple times
- **Observable:** Log every action, report status
- **Fail-safe:** If automation fails, alert immediately
- **Testable:** Automation code has tests like app code

---

<!-- _class: lead -->

# 10) Secure

---

## Security layers

**Defense in depth:**

```text
┌─────────────────────────────────────────────┐
│  Network Layer (firewall, private endpoints)│
├─────────────────────────────────────────────┤
│  Identity Layer (managed identity, RBAC)    │
├─────────────────────────────────────────────┤
│  Data Layer (encryption at rest/in transit) │
├─────────────────────────────────────────────┤
│  App Layer (input validation, auth checks)  │
├─────────────────────────────────────────────┤
│  Audit Layer (logs, compliance reports)     │
└─────────────────────────────────────────────┘
```

**Every layer must independently enforce security** (assume other layers can be breached).

---

## Secrets best practices

✅ **Do:**
- Store secrets in Azure Key Vault
- Use managed identity (no stored credentials)
- Rotate secrets automatically (Key Vault rotation policy)
- Audit secret access (who accessed what, when)

❌ **Don't:**
- Hardcode secrets in code or config files
- Log secrets (even accidentally in trace data)
- Share secrets between environments
- Grant broad access ("everyone can read all secrets")

---

## Least privilege for secrets

Grant only what's needed — nothing more:

```bash
az keyvault set-policy --name MyVault \
  --object-id <managed-identity-id> \
  --secret-permissions get
```

One identity · one vault · one permission scope.

---

## Tool approval policy

```yaml
# skill frontmatter
tools: ["database", "email", "file-delete"]
approval_required: true  # High-risk tools need human approval
```

```csharp
if (tool.RequiresApproval && !context.HasApproval)
{
    await RequestApprovalAsync(context, tool);
    // Execution blocked until operator approves
}
```

---

## Risky actions requiring approval

**Examples:**
- Delete records from production database
- Send emails to customers
- Modify financial data
- Deploy infrastructure changes

**Workflow:** Slack/Teams alert → operator reviews → approve or reject

---

<!-- _class: lead -->

# 11) Extend (skills)

---

## Skill extensibility model

**Skills as domain packages:**

```text
skills/
  finance/          ← Finance team owns
    invoice-validation.md
    expense-approval.md
    reconciliation.md
  support/          ← Support team owns
    triage.md
    escalation.md
  legal/            ← Legal team owns
    contract-review.md
```

**Ownership model:**
- Each skill has `owner` in frontmatter
- CODEOWNERS file enforces review by domain experts
- Skills evolve independently (different release cadence)

---

## Skill review like code

**Pull request checklist for skills:**

- [ ] Frontmatter complete (name, version, owner, tools)
- [ ] Prompt quality validated (clear instructions, examples)
- [ ] Tool usage boundaries defined (no unauthorized actions)
- [ ] Test cases written (expected inputs/outputs)
- [ ] Security review (no hardcoded secrets, safe tool usage)
- [ ] Changelog updated (what changed, why)

**Example PR review comment:**

> "This skill requests `database` tool but doesn't specify read-only. Should we add `approval_required: true` or limit to read-only queries?"

---

## Skill version promotion

```bash
# Tag and push validated skill version
git tag skill-invoice-validation-v1.2.0
git push origin skill-invoice-validation-v1.2.0

# Merge to production branch
git checkout production
git merge skill-invoice-validation-v1.2.0
```

---

## Skill rollback strategy

```bash
# Revert problematic commit
git revert abc123
git push origin production

# Or roll back to last good tag
git checkout skill-invoice-validation-v1.1.0
```

Skill file + version metadata tracked in deployment log.

---

<!-- _class: lead -->

# 12) Operate at scale

---

## Capacity + concurrency planning

**Scaling dimensions:**

```text
┌───────────────────────────────────────────┐
│  Concurrent Users                         │
│  ├─ Chat sessions: 100 concurrent         │
│  ├─ Background jobs: 20 concurrent        │
│  └─ Peak load: 2x normal (500 users)     │
└───────────────────────────────────────────┘
         │
         ▼
┌───────────────────────────────────────────┐
│  Resource Requirements                    │
│  ├─ CPU: 4 cores per 100 concurrent users │
│  ├─ Memory: 8 GB per 100 users            │
│  ├─ Database: 50 connections per replica  │
│  └─ LLM API: rate limit 100 req/min       │
└───────────────────────────────────────────┘
```

**Capacity planning steps:**
1. Measure baseline (load testing)
2. Define SLOs (latency, availability)
3. Calculate resource needs per user
4. Add headroom (2x for peak, 20% buffer)

---

## Retry with exponential backoff

```csharp
await Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt =>
            TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (ex, ts, count, ctx) =>
            _logger.LogWarning($"Retry {count} after {ts}"))
    .ExecuteAsync(() => CallExternalApiAsync());
```

---

## Dead-letter queue + circuit breaker

```csharp
if (job.FailureCount >= MaxRetries)
{
    await _deadLetterQueue.EnqueueAsync(job);
    await _alerting.NotifyAsync("Job failed after max retries", job);
}
```

**Circuit breaker:** Stop calling unhealthy dependency → fail fast → retry after cooldown.

---

## Safe rollout patterns

**Phased rollout (canary deployment):**

```text
Phase 1: 5% traffic  → Monitor for 1 hour  → No errors? Proceed
Phase 2: 25% traffic → Monitor for 1 hour  → No errors? Proceed
Phase 3: 100% traffic → Monitor for 24 hours → Success!
```

**Feature flags for skill enablement:**

```csharp
if (await _featureFlags.IsEnabledAsync("invoice-validation-skill", ctx))
    // route to new skill
else
    // fall back to previous logic
```

**Fast rollback:** Revert deployment or disable feature flag within minutes.

---

## Cost tracking by category

```text
┌─────────────────────────────────────────┐
│ Cost Category       │ Monthly $ │ Trend │
├─────────────────────────────────────────┤
│ Compute (replicas)  │ $1,200    │ ↑ 10% │
│ LLM API (tokens)    │ $800      │ ↓ 5%  │
│ Database (storage)  │ $300      │ → 0%  │
│ Networking + other  │ $200      │ → 0%  │
├─────────────────────────────────────────┤
│ Total               │ $2,500    │ ↑ 8%  │
└─────────────────────────────────────────┘
```

---

## Cost optimization + governance

**Strategies:**
- Cache frequently accessed data (reduce DB queries)
- Batch LLM requests (reduce API calls)
- Scale down non-prod environments
- Right-size instances (avoid over-provisioning)

**Governance:** Monthly cost review · budget alerts · tag by team/project

---

## 13) Live demo walkthrough

1. Show new file-based skill loaded at runtime
2. Read a secret via vault-backed configuration
3. Create and run a scheduled job
4. Deploy profile walkthrough from Aspire docs
5. Observe traces/health after deployment

---

## Session resources

- Aspire deployment docs: <https://aspire.dev/deployment/>
- Repo: <https://github.com/elbruno/openclawnet>
- Session materials: `sessions/session-4/`

---

<!-- _class: lead -->

# 14) Q&A and wrap-up

- Recap: new capabilities -> production readiness
- Share links and next steps
- Open Q&A

**OpenClaw .NET - Session 4**
