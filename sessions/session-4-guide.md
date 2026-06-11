# Session 4 Guide — Deploy, Operate & Scale

## Objective

Run Session 4 with a complete progression from new capabilities to operations at scale, with **live demos immediately after each main topic** (skills, secrets, jobs, deploy).

**Demo Flow:**
1. File-based skills → **DEMO 1: Edit skill live**
2. Secrets Vault → **DEMO 2: Add secret, app picks it up**
3. Job scheduling → **DEMO 3: Create job, watch metadata**
4. Deploy with Aspire → **DEMO 4: aspire describe, show readiness**

Primary deployment reference: <https://aspire.dev/deployment/>

---

## Recommended Agenda (60–75 min)

| Time | Section | Duration |
|------|---------|----------|
| 0:00–5:00 | Welcome and goals | 5 min |
| 5:00–9:00 | What's new in OpenClaw .NET | 4 min |
| 9:00–13:00 | File-based skills | 4 min |
| **13:00–15:00** | **DEMO 1: Skills** | **2 min** |
| 15:00–19:00 | Secrets Vault | 4 min |
| **19:00–21:00** | **DEMO 2: Vault** | **2 min** |
| 21:00–25:00 | Job scheduling | 4 min |
| **25:00–27:00** | **DEMO 3: Jobs** | **2 min** |
| 27:00–29:00 | Transition to readiness | 2 min |
| 29:00–34:00 | Deploy with Aspire | 5 min |
| **34:00–36:00** | **DEMO 4: Deploy** | **2 min** |
| 36:00–40:00 | Observe | 4 min |
| 40:00–44:00 | Automate | 4 min |
| 44:00–48:00 | Secure | 4 min |
| 48:00–51:00 | Extend (skills) | 3 min |
| 51:00–55:00 | Operate at scale | 4 min |
| 55:00–60:00 | Q&A and wrap-up | 5 min |

**Total:** 60 min (demos included)  
**Buffer:** 5–15 min for Q&A spillover or demo delays

---

## Before You Start: Pre-Session Checklist

### 30 Minutes Before Session

**Aspire Services:**
- [ ] Start Aspire AppHost: `aspire run` in terminal
- [ ] Verify dashboard accessible: `http://localhost:15888` (or configured port)
- [ ] Check all services green: agent service, job scheduler, vault integration

**Agent Service:**
- [ ] Health endpoint responding: `curl http://localhost:5000/health` → 200 OK
- [ ] Sample skill loaded: `skills/demo/weather-lookup.md` exists and is valid
- [ ] Logs visible in terminal or Aspire dashboard

**Secrets Vault:**
- [ ] Vault connectivity verified: `dotnet user-secrets list` or Azure Key Vault portal access
- [ ] Demo secret name chosen: `DemoApiKey` (not yet added — will add during demo)
- [ ] Application configured to read vault secrets at startup

**Job Scheduler:**
- [ ] Job management UI or API accessible: `/jobs` endpoint or dashboard
- [ ] Sample job definition ready: "Demo Job — runs every 1 min"
- [ ] Job execution logs visible

**Deployment Artifacts:**
- [ ] Build successful: `dotnet build` completes without errors
- [ ] Aspire deployment manifest generated: `aspire deploy --dry-run` succeeds
- [ ] (Optional) Azure subscription configured if doing live deploy

**Fallback Plan:**
- [ ] Screenshots saved in `sessions/session-4/fallback-screenshots/`:
  - `demo1-skill-edit.png` (skill file edit → reload → execution)
  - `demo2-vault-secret.png` (vault UI with secret added)
  - `demo3-job-status.png` (job status page with execution history)
  - `demo4-deploy-readiness.png` (`aspire describe` output or Azure portal)
- [ ] Fallback screenshots open in separate folder for quick access

### 5 Minutes Before Session

**Terminals & Browser:**
- [ ] Terminal 1: Aspire AppHost logs running
- [ ] Terminal 2: Agent service logs visible
- [ ] Terminal 3: Job scheduler logs (if separate)
- [ ] Browser Tab 1: Aspire dashboard (`http://localhost:15888`)
- [ ] Browser Tab 2: Job management UI or API docs
- [ ] Browser Tab 3: Azure portal (if live deploy planned)

**Editor:**
- [ ] VS Code open with `skills/demo/weather-lookup.md` ready to edit
- [ ] Editor visible on screen share (large font for audience)

**Final Checks:**
- [ ] Run demo flow once: skill edit → vault add → job create → aspire describe (takes ~3 min)
- [ ] Verify all services still healthy after test run
- [ ] Reset demo state: remove demo secret, delete demo job, revert skill edits

---

## Live Demo Walkthroughs

### DEMO 1: File-Based Skills (13:00–15:00 | 2 min)

**Context:** Immediately after explaining file-based skills. Audience understands theory — now show practice.

**Demo Goal:** Edit a skill file, reload it, and show updated behavior in action.

**Talking Points (before demo):**
- "Let's see this in action — I'll edit a skill file and reload it live"
- "This is the dev workflow — in production, this would be a PR review + merge"

**Steps:**
1. **Show skill file** (30 sec)
   - Open `skills/demo/weather-lookup.md` in VS Code
   - Read aloud one instruction or tool definition
   - "This skill looks up weather for a given city"

2. **Edit skill** (20 sec)
   - Change prompt instruction: "Always include temperature in Celsius AND Fahrenheit"
   - Save file (Ctrl+S)
   - "Just saved — now let's reload"

3. **Reload skill** (30 sec)
   - Trigger skill reload: API call (`POST /skills/reload`) or hot-reload mechanism
   - Show terminal output: "Skill 'weather-lookup' reloaded successfully"

4. **Execute skill** (40 sec)
   - Call agent with prompt: "What's the weather in Seattle?"
   - Show response: includes both Celsius and Fahrenheit (proving edit worked)
   - "See — updated behavior without redeploying the entire app"

**Watch For:**
- Skill file is readable on screen share (large font)
- Reload completes within 5 sec (if slower, explain "normally instant")
- Agent response clearly shows the edit took effect

**Fallback (if skill reload fails):**
- Show `fallback-screenshots/demo1-skill-edit.png`
- Say: "We have a hot-reload hiccup — here's what you'd see: edit, save, reload, and the agent immediately uses the new behavior"
- **Time saved:** 30 sec

---

### DEMO 2: Secrets Vault (19:00–21:00 | 2 min)

**Context:** Immediately after explaining secrets vault. Audience understands separation of duties — now show secret addition flow.

**Demo Goal:** Add a secret to the vault and show the app resolve it at runtime.

**Talking Points (before demo):**
- "Let's add a secret to the vault and watch the app use it"
- "Developers never see production secrets — only secret names"

**Steps:**
1. **Show vault UI** (20 sec)
   - Open vault UI (dotnet user-secrets or Azure Key Vault portal)
   - List existing secrets: "ConnectionString", "ApiKey", etc.
   - "Here are the secrets our app can access"

2. **Add new secret** (30 sec)
   - Add: `DemoApiKey = "secret-value-12345"`
   - Command: `dotnet user-secrets set "DemoApiKey" "secret-value-12345"` (or via portal)
   - Show confirmation: "Secret 'DemoApiKey' added"

3. **App picks up secret** (30 sec)
   - Restart or hot-reload agent service (if hot-reload supported)
   - Show app logs: "DemoApiKey resolved successfully" (value NOT logged)
   - "App fetched the secret at startup — but we never hardcoded it"

4. **Verify secret in use** (40 sec)
   - Call API endpoint that uses `DemoApiKey`: `GET /api/demo`
   - Show response: API call succeeded (proving secret was used)
   - "In production, operators rotate secrets in vault — app picks up changes on restart"

**Watch For:**
- Vault UI is visible on screen share
- Secret addition completes quickly (< 5 sec)
- App logs clearly show "secret resolved" (not the actual value)

**Fallback (if vault connection fails):**
- Show `fallback-screenshots/demo2-vault-secret.png`
- Say: "Vault is being slow — here's the flow: add secret in vault UI, app restarts, logs show 'secret resolved', and API calls work"
- **Time saved:** 30 sec

---

### DEMO 3: Job Scheduling (25:00–27:00 | 2 min)

**Context:** Immediately after explaining job scheduling. Audience understands recurring + one-time jobs — now show job creation and status tracking.

**Demo Goal:** Create a recurring job and watch its metadata update in real time.

**Talking Points (before demo):**
- "Let's create a job and watch its metadata update in real time"
- "This is the foundation for unattended agent automation"

**Steps:**
1. **Show job management UI** (20 sec)
   - Open job management UI or API docs: `/jobs` endpoint
   - List existing jobs: "Data refresh — runs daily", "Report generator — runs weekly"
   - "Here are our currently scheduled jobs"

2. **Create new job** (30 sec)
   - Create: "Demo Job — runs every 1 min"
   - Payload: `{ "name": "Demo Job", "schedule": "*/1 * * * *", "skill": "weather-lookup" }`
   - Submit job via UI or API call: `POST /jobs`
   - Show confirmation: "Job 'Demo Job' created, next run in 60 sec"

3. **Watch first execution** (40 sec)
   - Wait for first execution (or trigger manually if time-sensitive)
   - Show status update: "Last run: 2 sec ago, Status: Success"
   - Refresh job page to show updated metadata

4. **Show execution history** (30 sec)
   - Click into job details: execution history table
   - Show: run timestamp, duration, status, logs
   - "Full operational visibility — you know when it ran, if it failed, and why"

**Watch For:**
- Job creation completes within 5 sec
- First execution happens within 60 sec (or trigger manually)
- Status updates are visible without manual refresh (or refresh explicitly)

**Fallback (if job scheduler is slow):**
- Show `fallback-screenshots/demo3-job-status.png`
- Say: "Scheduler is lagging — here's what you'd see: create job, job executes on schedule, status page shows last run, next run, and execution history"
- **Time saved:** 30 sec

---

### DEMO 4: Deploy Readiness (34:00–36:00 | 2 min)

**Context:** Immediately after explaining Aspire deployment options. Audience understands target choices — now show readiness validation.

**Demo Goal:** Run `aspire describe` to show app topology and readiness checks, then (optionally) show deployed resources.

**Talking Points (before demo):**
- "Let's run `aspire describe` and show what deployment readiness looks like"
- "Aspire validates everything before deployment — no surprises"

**Steps:**
1. **Run aspire describe** (30 sec)
   - Command: `aspire describe` (or `dotnet run --project AppHost -- describe`)
   - Show output: app topology, dependencies, health checks
   - "Here's our app: agent service, job scheduler, vault integration — all dependencies resolved"

2. **Show readiness checks** (30 sec)
   - Highlight: "Health: ✓ All services healthy"
   - Highlight: "Dependencies: ✓ Vault connected, Database ready"
   - Highlight: "Configuration: ✓ All required settings provided"
   - "Everything green — ready to deploy"

3. **(Optional) Show deployment manifest** (30 sec)
   - Command: `aspire deploy --dry-run` or show generated manifest file
   - "This is what Aspire will provision: Container Apps, Key Vault, Storage, etc."
   - "Same manifest works for dev, stage, prod — just different environments"

4. **(Optional) Show deployed resources** (40 sec)
   - If time allows and Azure subscription is configured:
   - Open Azure portal, show deployed Container Apps
   - Show health checks green, logs flowing, traces visible
   - "Post-deploy: everything running, observability automatic"

**Watch For:**
- `aspire describe` completes within 10 sec
- Output is readable on screen share (may need to zoom terminal)
- All health checks show green (if not, explain "this is a dev environment")

**Fallback (if aspire describe fails or is slow):**
- Show `fallback-screenshots/demo4-deploy-readiness.png`
- Say: "Command is hanging — here's what a successful readiness check looks like: all services healthy, dependencies resolved, configuration validated, ready to deploy"
- **Time saved:** 30 sec

---

## Key Messages to Reinforce (Throughout Session)

**After Demos:**
- "Live demos show you the dev workflow — production adds approval gates and rollback policies"
- "These features aren't just conveniences — they're operational enablers"
- "File-based skills + vault + jobs = production-grade agent operations"

**During Operational Sections:**
- Observe: "Observability is built in, not bolted on"
- Automate: "Automation reduces toil and improves reliability"
- Secure: "Security starts with secrets management"
- Extend: "Skills are code — treat them like code"
- Scale: "Scale is not just more servers — it's safe, predictable growth"

**Final Recap:**
- File-based skills turn agent behavior into reviewable assets
- Vault-backed secrets reduce operational risk and simplify rotation
- Scheduling moves agent workflows from ad-hoc to reliable automation
- Aspire gives a consistent application model while deployment target can vary
- Operations (observe/security/scale) must be designed early, not bolted on

---

## Troubleshooting & Fallback Strategy

### If Any Demo Fails

**Immediate Actions:**
1. Acknowledge quickly: "We have a [connectivity/timing/config] hiccup here"
2. Switch to fallback screenshot: "Here's what you'd see when this works"
3. Walk through screenshot: narrate the steps as if live
4. Keep moving: "Let's continue — that's the flow you'd follow"

**Total fallback time per demo:** 30 sec (vs. 2 min live)  
**Time saved if all demos fail:** ~6 min → reallocate to Q&A

### Common Issues & Quick Fixes

**Aspire services not responding:**
- Check: `curl http://localhost:15888` → if timeout, restart AppHost
- Fallback: show dashboard screenshot, explain "services would be green here"

**Skill reload not working:**
- Check: skill file syntax valid (missing bracket, invalid markdown)
- Fallback: show screenshot of successful reload

**Vault connection timeout:**
- Check: `dotnet user-secrets list` → if slow, Azure Key Vault may be throttling
- Fallback: show screenshot of secret addition in vault UI

**Job scheduler lagging:**
- Check: job execution logs for errors
- Fallback: show screenshot of job status page with execution history

**Aspire describe slow:**
- Check: container images built? (`docker images` → should see app images)
- Fallback: show screenshot of `aspire describe` output

---

## Post-Session Checklist

- [ ] Stop Aspire AppHost: `Ctrl+C` in terminal
- [ ] Clean up demo artifacts:
  - Remove demo secret: `dotnet user-secrets remove "DemoApiKey"`
  - Delete demo job: `DELETE /jobs/demo-job` or via UI
  - Revert skill edits: `git restore skills/demo/weather-lookup.md`
- [ ] Archive demo logs for reference: save terminal output to `sessions/session-4/demo-logs/`
- [ ] Update session retrospective: note what worked, what didn't, timing adjustments

---

## Links & Resources

- **Aspire Deployment Docs:** <https://aspire.dev/deployment/>
- **Repo:** <https://github.com/elbruno/openclawnet>
- **Session Materials:** `sessions/session-4/`
- **Speaker Script:** `sessions/session-4/speaker-script.md`
- **Slides:** `sessions/session-4/slides.md`
