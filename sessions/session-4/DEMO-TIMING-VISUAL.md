# Session 4 Demo Timing Flow (Visual)

```
SESSION 4: DEPLOY, OPERATE & SCALE (60–75 min)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

0:00 ┃ 🎤 WELCOME & GOALS (5 min)
     ┃ • Frame session: "features → operations"
     ┃ • Highlight 4 live demo moments
     ┃ • Set expectations: "demos are live, fallback ready"
     ┃
5:00 ┃ 📊 WHAT'S NEW (4 min)
     ┃ • File-based skills, secrets vault, job scheduling
     ┃ • Position as "operational enablers"
     ┃
9:00 ┃ 📦 FILE-BASED SKILLS (4 min)
     ┃ • Skill folder structure, promotion flow, ownership
     ┃ • Tee up demo: "Let's edit a skill live..."
     ┃
     ┃ ┌─────────────────────────────────────────────────┐
13:00┃ │ 🎬 DEMO 1: SKILLS (2 min)                       │
     ┃ │ 1. Show skill file (30s)                        │
     ┃ │ 2. Edit skill instruction (20s)                 │
     ┃ │ 3. Reload skill (30s)                           │
     ┃ │ 4. Execute & show updated behavior (40s)        │
     ┃ │ Fallback: demo1-skill-edit.png (30s)            │
     ┃ └─────────────────────────────────────────────────┘
     ┃
15:00┃ 🔐 SECRETS VAULT (4 min)
     ┃ • Vault-backed config, separation of duties
     ┃ • Rotation and audit benefits
     ┃ • Tee up demo: "Let's add a secret..."
     ┃
     ┃ ┌─────────────────────────────────────────────────┐
19:00┃ │ 🎬 DEMO 2: VAULT (2 min)                        │
     ┃ │ 1. Show vault UI (20s)                          │
     ┃ │ 2. Add new secret (30s)                         │
     ┃ │ 3. App picks up secret (30s)                    │
     ┃ │ 4. Verify secret in use (40s)                   │
     ┃ │ Fallback: demo2-vault-secret.png (30s)          │
     ┃ └─────────────────────────────────────────────────┘
     ┃
21:00┃ ⏰ JOB SCHEDULING (4 min)
     ┃ • Recurring + one-time jobs, operational visibility
     ┃ • Reliability patterns: retries, backoff
     ┃ • Tee up demo: "Let's create a job..."
     ┃
     ┃ ┌─────────────────────────────────────────────────┐
25:00┃ │ 🎬 DEMO 3: JOBS (2 min)                         │
     ┃ │ 1. Show job management UI (20s)                 │
     ┃ │ 2. Create new job (30s)                         │
     ┃ │ 3. Watch first execution (40s)                  │
     ┃ │ 4. Show execution history (30s)                 │
     ┃ │ Fallback: demo3-job-status.png (30s)            │
     ┃ └─────────────────────────────────────────────────┘
     ┃
27:00┃ 🔄 TRANSITION (2 min)
     ┃ • Bridge from features to operations
     ┃ • Introduce: Deploy → Observe → Automate → Secure → Extend → Scale
     ┃
29:00┃ 🚀 DEPLOY WITH ASPIRE (5 min)
     ┃ • Deployment options: Container Apps, AKS, VM/container-host
     ┃ • Selection criteria: managed vs. control, cost, scale
     ┃ • Tee up demo: "Let's check deployment readiness..."
     ┃
     ┃ ┌─────────────────────────────────────────────────┐
34:00┃ │ 🎬 DEMO 4: DEPLOY (2 min)                       │
     ┃ │ 1. Run `aspire describe` (30s)                  │
     ┃ │ 2. Show readiness checks (30s)                  │
     ┃ │ 3. (Optional) Show deployment manifest (30s)    │
     ┃ │ 4. (Optional) Show deployed resources (40s)     │
     ┃ │ Fallback: demo4-deploy-readiness.png (30s)      │
     ┃ └─────────────────────────────────────────────────┘
     ┃
36:00┃ 👁️  OBSERVE (4 min)
     ┃ • Health, logs, traces, metrics
     ┃ • Actionable alerts (not dashboard noise)
     ┃
40:00┃ 🤖 AUTOMATE (4 min)
     ┃ • Scheduled operational tasks
     ┃ • Automated checks with guardrails
     ┃
44:00┃ 🛡️  SECURE (4 min)
     ┃ • Vault-first secrets, least privilege
     ┃ • Approval boundaries for risky tools
     ┃
48:00┃ 🔧 EXTEND (3 min)
     ┃ • Skill lifecycle: author → review → test → promote
     ┃ • Governance and quality validation
     ┃
51:00┃ 📈 OPERATE AT SCALE (4 min)
     ┃ • Capacity planning, fault handling
     ┃ • Safe rollout patterns: canary, phased enablement
     ┃
55:00┃ 💬 Q&A & WRAP-UP (5 min)
     ┃ • Recap: features → demos → operational practices
     ┃ • Share resources: slides, repo, docs
     ┃ • Open Q&A
     ┃
60:00┃ END (or +5–15 min buffer for Q&A spillover)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

TOTAL DEMO TIME: 8 min (4 demos × 2 min each)
TOTAL SESSION: 60 min (base) + 5–15 min (buffer) = 60–75 min

FALLBACK STRATEGY:
• If demo fails: 30 sec fallback (vs. 2 min live) → saves 1.5 min per demo
• If all demos fail: saves ~6 min total → reallocate to Q&A
• Screenshot locations: sessions/session-4/fallback-screenshots/

DEMO PHILOSOPHY:
"Live demos with fallback ready" > "perfect demos or nothing"
```

---

## Demo Success Indicators

**DEMO 1: Skills**
- ✅ Skill file visible on screen share (large font)
- ✅ Reload completes within 5 sec
- ✅ Agent response clearly shows edit took effect

**DEMO 2: Vault**
- ✅ Vault UI visible on screen share
- ✅ Secret addition completes quickly (< 5 sec)
- ✅ App logs show "secret resolved" (not actual value)

**DEMO 3: Jobs**
- ✅ Job creation completes within 5 sec
- ✅ First execution happens within 60 sec (or trigger manually)
- ✅ Status updates visible (refresh if needed)

**DEMO 4: Deploy**
- ✅ `aspire describe` completes within 10 sec
- ✅ Output is readable on screen share (zoom if needed)
- ✅ All health checks show green (explain if dev environment)

---

## Pre-Session Checklist Summary

**30 min before:**
- [ ] Start Aspire AppHost → verify dashboard green
- [ ] Check agent service health endpoint → 200 OK
- [ ] Load sample skill file → `skills/demo/weather-lookup.md`
- [ ] Verify vault connectivity → `dotnet user-secrets list`
- [ ] Create demo job definition → ready to schedule
- [ ] Build deployment artifacts → `dotnet build` + `aspire deploy --dry-run`
- [ ] Save fallback screenshots → 4 files in `fallback-screenshots/`

**5 min before:**
- [ ] Open terminals/browser tabs → dashboard, logs, job UI
- [ ] Test demo flow once → takes ~3 min
- [ ] Reset demo state → remove test artifacts

**During session:**
- [ ] Monitor Aspire dashboard for service health
- [ ] If demo fails: acknowledge, show fallback, keep moving

---

## Timing Breakdown

| Category | Time | % of Session |
|----------|------|--------------|
| **Welcome + What's New** | 9 min | 15% |
| **Feature Deep-Dives (Skills/Vault/Jobs)** | 12 min | 20% |
| **Live Demos (4 × 2 min)** | 8 min | 13% |
| **Transition** | 2 min | 3% |
| **Deploy** | 5 min | 8% |
| **Operations (Observe/Automate/Secure/Extend/Scale)** | 19 min | 32% |
| **Q&A** | 5 min | 8% |
| **Total** | 60 min | 100% |
| **Buffer (optional)** | +5–15 min | |

**Key Insight:** Demos take only 13% of session time but deliver 40%+ of engagement and learning reinforcement.
