# 🎤 Session 4 Speaker Script — Deploy, Operate & Scale

**Duration:** 60–75 minutes (flexible for demos)  
**Flow:** Welcome -> What's new -> [DEMO 1: Skills] -> Secrets -> [DEMO 2: Vault] -> Jobs -> [DEMO 3: Jobs] -> Transition -> Deploy -> [DEMO 4: Deploy] -> Observe -> Automate -> Secure -> Extend -> Operate at scale -> Q&A

**Demo Philosophy:** Live demos *immediately after* each main topic — when context is fresh. Each demo is 1–2 min with fallback screenshots ready.

---

## 0:00–5:00 | 1) Welcome and session goals

**Speaker Notes:**
- Welcome everyone, introduce co-presenters
- Frame Session 4: "We're moving from 'what's new' to 'how do we run this in production'"
- Show agenda slide — highlight **live demo moments** after skills/secrets/jobs/deploy
- Set expectations: "Demos are live — if something breaks, we'll show you the fallback and keep moving"

**Talking Points:**
- "Today's session is about bridging the gap between features and operations"
- "You'll see four live demos as we go — not saved for the end"
- "Goal: by the end, you'll have a mental model for deploying and operating agent workloads"

---

## 5:00–9:00 | 2) What's new in OpenClaw .NET

**Speaker Notes:**
- Quick recap of the three big features: file-based skills, secrets vault, job scheduling
- Position these as **operational enablers**, not just features
- Use slides to show high-level benefits (versioning, security, reliability)

**Talking Points:**
- "File-based skills: your agent behavior becomes a reviewable asset"
- "Secrets vault: centralized secret management with rotation built in"
- "Job scheduling: move from ad-hoc scripts to reliable automation"
- "Together, these are the foundation for production-grade agent platforms"

---

## 9:00–13:00 | 3) File-based skills

**Speaker Notes:**
- Show skill folder structure on slide
- Explain promotion flow: dev → stage → prod with version tags
- Emphasize code review and ownership model
- **Tee up the demo:** "Let's see this in action — I'll edit a skill file and reload it live"

**Talking Points:**
- "Skills are now in `skills/` folder — versioned like any other code"
- "Each skill has a clear owner and promotion path"
- "This makes rollback easy and rollout safe"
- "You can test skills in dev before deploying to production"

---

## 🎬 13:00–15:00 | DEMO 1: File-Based Skills (Live)

**Demo Setup Requirements:**
- ✅ Aspire AppHost running (`aspire run` in terminal)
- ✅ OpenClaw .NET agent service reachable (health endpoint green)
- ✅ Sample skill file ready: `skills/demo/weather-lookup.md`
- ✅ Editor open (VS Code or similar) with skill file visible

**Demo Steps:**
1. Show current skill file content (30 sec)
2. Make a visible edit: change prompt instruction or add a comment (20 sec)
3. Save file and trigger skill reload via API or hot-reload mechanism (30 sec)
4. Execute the skill via agent endpoint and show updated behavior (40 sec)

**Speaker Notes During Demo:**
- "Here's our skill file — it's just markdown with tool definitions"
- "I'm changing the instruction here... save... and now we reload"
- "Notice the agent picks up the change immediately — no redeployment needed"
- "In production, this would be a PR review + merge, then a controlled rollout"

**Talking Points:**
- "Skills are hot-reloadable for fast iteration"
- "In production, you'd gate this with approval workflows"
- "File-based means git history = skill history"

**Fallback if Demo Fails:**
- Show pre-recorded GIF or screenshot sequence of the edit → reload → execution flow
- Say: "We have a connectivity hiccup — here's what you'd see" and walk through the screenshot
- Total fallback time: 30 sec

---

## 15:00–19:00 | 4) Secrets Vault

**Speaker Notes:**
- Explain the vault-backed configuration model
- Contrast with hardcoded secrets in appsettings.json
- Show slide: secrets flow (developer references name → vault resolves value at runtime)
- Mention rotation and audit benefits
- **Tee up the demo:** "Let's add a secret to the vault and watch the app use it"

**Talking Points:**
- "Developers never see production secrets — only secret names"
- "Vault handles rotation without code changes"
- "Audit trail: who accessed what secret and when"
- "Separation of duties: devs write code, operators manage secrets"

---

## 🎬 19:00–21:00 | DEMO 2: Secrets Vault (Live)

**Demo Setup Requirements:**
- ✅ Aspire AppHost running with vault integration enabled
- ✅ Vault UI or CLI accessible (e.g., `dotnet user-secrets` or Azure Key Vault portal)
- ✅ Sample application configured to read secret: `DemoApiKey`
- ✅ Application health endpoint or log output visible

**Demo Steps:**
1. Show vault UI or CLI — list existing secrets (20 sec)
2. Add a new secret: `DemoApiKey = "secret-value-12345"` (30 sec)
3. Restart or hot-reload the app to pick up the new secret (30 sec)
4. Show app log or API response proving secret was resolved (40 sec)

**Speaker Notes During Demo:**
- "Here's our vault — these are the secrets the app can access"
- "I'm adding a new secret called DemoApiKey... done"
- "Now the app reloads and pulls the secret at startup"
- "See in the logs: 'DemoApiKey resolved successfully' — but we never logged the value"

**Talking Points:**
- "App never hardcodes the secret — it's always fetched from vault"
- "If we rotate the secret, the app picks it up on next restart"
- "No code changes needed for rotation"

**Fallback if Demo Fails:**
- Show pre-recorded screenshot of vault UI with secret addition
- Show screenshot of app logs with "secret resolved" message
- Say: "Vault is being temperamental — here's the flow you'd see"
- Total fallback time: 30 sec

---

## 21:00–25:00 | 5) Job scheduling

**Speaker Notes:**
- Explain job types: recurring (cron-like) and one-time (on-demand)
- Show operational visibility: status, last run, next run, failure history
- Emphasize reliability patterns: retries, backoff, dead-letter queues
- **Tee up the demo:** "Let's create a job and watch its metadata update in real time"

**Talking Points:**
- "Jobs move agent workflows from manual triggers to scheduled automation"
- "Recurring jobs: think daily data refresh or weekly reports"
- "One-time jobs: think 'run this skill at 3 AM once'"
- "Operational visibility: you know when it ran, if it failed, and why"

---

## 🎬 25:00–27:00 | DEMO 3: Job Scheduling (Live)

**Demo Setup Requirements:**
- ✅ Aspire AppHost running with job scheduler service active
- ✅ Job management UI or API accessible (e.g., `/jobs` endpoint or dashboard)
- ✅ Sample job definition ready: recurring every 1 min or on-demand
- ✅ Job execution logs or status page visible

**Demo Steps:**
1. Show job management UI or API — list existing jobs (20 sec)
2. Create a new job: "Demo job — runs every 1 min" (30 sec)
3. Wait for first execution and show status update: "Last run: 2 sec ago, Status: Success" (40 sec)
4. Show job metadata: next run time, execution history (30 sec)

**Speaker Notes During Demo:**
- "Here's our job scheduler — currently running jobs listed here"
- "I'm creating a new recurring job: 'Demo job' — runs every minute"
- "Job is now scheduled... waiting for first execution... there it goes!"
- "See the metadata: last run, status, next scheduled run — full operational visibility"

**Talking Points:**
- "This is the foundation for unattended agent automation"
- "You can see execution history and failure patterns"
- "Jobs can trigger skills, run maintenance tasks, or generate reports"

**Fallback if Demo Fails:**
- Show pre-recorded screenshot of job creation form
- Show screenshot of job status page with execution history
- Say: "Scheduler is lagging — here's what you'd see when creating and monitoring a job"
- Total fallback time: 30 sec

---

## 27:00–29:00 | 6) Transition: from features to readiness

**Speaker Notes:**
- Bridge from "what's new" to "how do we operate this"
- Introduce the operational flow: Deploy → Observe → Automate → Secure → Extend → Operate at scale
- Emphasize: "Features matter most when they improve day-2 operations"

**Talking Points:**
- "We've seen the features — now let's talk about running them in production"
- "Deployment model, observability, security, and scale are not afterthoughts"
- "Next: deployment with Aspire, then a live deploy demo"

---

## 29:00–34:00 | 7) Deploy with Aspire

**Speaker Notes:**
- Reference Aspire deployment page: <https://aspire.dev/deployment/>
- Show target options: Container Apps, AKS, VM/container-host
- Explain selection criteria: managed vs. control, cost, scale, existing infra
- Show slide: deployment decision tree
- **Tee up the demo:** "Let's run `aspire describe` and show what deployment readiness looks like"

**Talking Points:**
- "Aspire gives you one app model — deployment target is a config choice"
- "Container Apps: fully managed, best for new projects"
- "AKS: more control, good for existing Kubernetes teams"
- "VM/container-host: maximum control, legacy integration scenarios"
- "Aspire validates readiness before you deploy"

---

## 🎬 34:00–36:00 | DEMO 4: Deploy Readiness (Live)

**Demo Setup Requirements:**
- ✅ Aspire AppHost project with deployment manifest configured
- ✅ Terminal ready to run `aspire describe` or equivalent command
- ✅ Deployment artifacts ready (container images built, manifests generated)
- ✅ Optional: Azure subscription configured for live deploy (if time allows)

**Demo Steps:**
1. Run `aspire describe` in terminal to show app topology (30 sec)
2. Show readiness checks: health endpoints, dependencies, configuration (30 sec)
3. (Optional) Run `aspire deploy` or show Azure portal with deployed resources (40 sec)
4. Show post-deploy health: services green, traces flowing (20 sec)

**Speaker Notes During Demo:**
- "Running `aspire describe` — this shows our app topology and dependencies"
- "All services are healthy, dependencies resolved, configuration validated"
- "If we had time, we'd run `aspire deploy` and watch it provision resources in Azure"
- "Post-deploy: health checks green, traces flowing, ready for traffic"

**Talking Points:**
- "Aspire validates everything before deployment — no surprises"
- "Deployment is declarative — same command for dev, stage, prod"
- "Post-deploy observability is automatic — health, logs, traces"

**Fallback if Demo Fails:**
- Show pre-recorded screenshot of `aspire describe` output
- Show screenshot of Azure portal with deployed Container Apps
- Say: "Network is slow — here's what a successful deploy looks like"
- Total fallback time: 30 sec

---

## 36:00–40:00 | 8) Observe

**Speaker Notes:**
- Baseline observability: health, readiness, liveness
- Distributed tracing for request flows, tool invocations, job executions
- Logs + metrics + traces = operational narrative
- Actionable alerts: latency spikes, failure rate thresholds, quota breaches

**Talking Points:**
- "Observability is built in — not bolted on"
- "Traces show the full flow: user request → skill execution → tool call → response"
- "Alerts should be actionable — not dashboard noise"
- "Use observability to understand cost and performance patterns"

---

## 40:00–44:00 | 9) Automate

**Speaker Notes:**
- Scheduled jobs for recurring platform tasks: data refresh, report generation, cleanup
- Automated operational checks: drift detection, stale resource cleanup, failure recovery
- Automated release steps with guardrails: health checks, rollback triggers
- Idempotent automation: safe to retry, safe to run multiple times

**Talking Points:**
- "Automation reduces toil and improves reliability"
- "Jobs can run operational checks: 'Are secrets expiring soon?'"
- "Automated deployments with health checks and rollback policies"
- "Automation should be observable — you need to know when it runs and if it fails"

---

## 44:00–48:00 | 10) Secure

**Speaker Notes:**
- Vault-first secrets: never hardcode, always fetch at runtime
- Least privilege for runtime identities: managed identity for Azure resources
- Approval boundaries for risky tools: human-in-the-loop for destructive actions
- Security posture as part of deployment: policy gates, vulnerability scans

**Talking Points:**
- "Security starts with secrets management — vault is the foundation"
- "Least privilege: services only get access to what they need"
- "High-risk tools require approval — no fully autonomous deletion or data changes"
- "Security is part of the deployment pipeline — not an audit afterthought"

---

## 48:00–51:00 | 11) Extend (skills)

**Speaker Notes:**
- Skill lifecycle: author → review → test → promote
- Skills as packages: domain-specific skill packs owned by domain teams
- Governance: version tags, rollback capability, approval workflows
- Quality validation: prompt quality, tool usage boundaries, test coverage

**Talking Points:**
- "Skills are code — treat them like code with reviews and tests"
- "Domain teams own domain skills — clear ownership model"
- "Version tags enable rollback: if a skill breaks production, revert"
- "Skill quality matters: bad prompts lead to bad agent behavior"

---

## 51:00–55:00 | 12) Operate at scale

**Speaker Notes:**
- Capacity planning: concurrency limits, rate limits, quota management
- Fault handling: retries with exponential backoff, circuit breakers, dead-letter queues
- Safe rollout patterns: canary deployments, phased enablement, feature flags, fast rollback
- Cost and performance governance: telemetry-driven capacity decisions

**Talking Points:**
- "Scale is not just about more servers — it's about safe, predictable growth"
- "Concurrency limits prevent runaway costs and cascading failures"
- "Canary deployments: test new skills on 5% of traffic before full rollout"
- "Use telemetry to understand cost drivers and optimize"

---

## 55:00–60:00 | 13) Q&A and wrap-up

**Speaker Notes:**
- Recap the full journey: features → demos → operational practices
- Reinforce the key message: "Production-readiness is designed in, not bolted on"
- Share resources: slides, repo, Aspire deployment docs
- Open Q&A: prioritize operational questions (security, scale, cost)

**Talking Points:**
- "We've covered a lot: file-based skills, secrets vault, job scheduling, and deployment"
- "Key takeaway: these features enable production-grade agent operations"
- "Resources are in the repo — slides, demo scripts, deployment guides"
- "Questions? Let's focus on what you'd face in production"

---

## Appendix: Demo Setup Checklist (Pre-Session)

**30 min before session:**
- [ ] Start Aspire AppHost: `aspire run` in terminal, verify dashboard at `http://localhost:15888`
- [ ] Verify agent service health: `curl http://localhost:5000/health` → 200 OK
- [ ] Load sample skill: `skills/demo/weather-lookup.md` exists and is valid
- [ ] Verify vault connectivity: `dotnet user-secrets list` or Azure Key Vault access
- [ ] Create demo job: "Demo Job — runs every 1 min" via job scheduler API
- [ ] Build deployment artifacts: `dotnet build` and `aspire deploy --dry-run` succeeds
- [ ] Prepare fallback screenshots: save in `sessions/session-4/fallback-screenshots/`
  - `demo1-skill-edit.png`
  - `demo2-vault-secret.png`
  - `demo3-job-status.png`
  - `demo4-deploy-readiness.png`

**5 min before session:**
- [ ] Open terminals: Aspire dashboard, agent service logs, job scheduler logs
- [ ] Open browser tabs: Aspire dashboard, job management UI, Azure portal (if live deploy)
- [ ] Open VS Code: skill file ready to edit
- [ ] Test demo flow once: skill edit → vault add → job create → aspire describe
- [ ] Fallback screenshots open in separate folder for quick access

**During session:**
- [ ] Monitor Aspire dashboard for service health
- [ ] If demo fails: acknowledge quickly, switch to fallback, keep moving
- [ ] Total demo time: ~8 min across 4 demos (2 min each)
- [ ] Buffer time: ~5 min for Q&A spillover or demo delays
