# Session 4 Demo Choreography — Summary

**Date:** 2026-05-26  
**Updated by:** Milchick (Educational Media Producer)  
**Status:** Ready for review

---

## What Changed

Updated Session 4 materials to align with Bruno's feedback: **live demos immediately after each main topic** (skills, secrets, jobs, deploy) instead of one big demo at the end.

**Files Updated:**
1. `docs/sessions/session-4/speaker-script.md` — Added demo timing, speaker notes, setup requirements, fallback plans
2. `docs/sessions/session-4-guide.md` — Added pre-session checklist, demo walkthroughs, troubleshooting guide
3. `.squad/agents/milchick/history.md` — Appended Session 4 learnings
4. `.squad/decisions/inbox/milchick-session4-demos.md` — Full decision document with rationale

---

## New Demo Flow

**Before:** One 7-min demo at end (50:00–57:00)  
**After:** Four 2-min demos immediately after each topic:

| Time | Demo | What We Show |
|------|------|--------------|
| **13:00–15:00** | **DEMO 1: Skills** | Edit skill file → reload → execute → show updated behavior |
| **19:00–21:00** | **DEMO 2: Vault** | Add secret → app picks it up → verify in logs/API |
| **25:00–27:00** | **DEMO 3: Jobs** | Create recurring job → watch metadata/status update |
| **34:00–36:00** | **DEMO 4: Deploy** | `aspire describe` → show topology/health → (optional) deployed resources |

**Total demo time:** 8 min (vs. 7 min before, but distributed throughout)  
**Session length:** 60–75 min (60 min base + 5–15 min buffer for Q&A/delays)

---

## Key Features

### 1. Pre-Session Checklist (Critical!)

**30 minutes before session:**
- Start Aspire AppHost, verify all services healthy
- Load sample skill file: `skills/demo/weather-lookup.md`
- Verify vault connectivity (dotnet user-secrets or Azure Key Vault)
- Create demo job ready to schedule
- Build deployment artifacts (container images, manifests)
- **Save fallback screenshots** (one per demo)

**5 minutes before session:**
- Open terminals/browser tabs (Aspire dashboard, logs, job UI)
- Test demo flow once (takes ~3 min)
- Reset demo state (remove demo artifacts from test run)

### 2. Fallback Strategy (If Demo Fails)

**For each demo:**
- Pre-cached screenshot saved in `sessions/session-4/fallback-screenshots/`
- If demo fails: acknowledge quickly, show screenshot, narrate flow, keep moving
- Fallback time: 30 sec (vs. 2 min live) → saves 1.5 min per failed demo

**If all demos fail:** saves ~6 min total → reallocate to Q&A

### 3. Speaker Notes Per Section

**Each section now includes:**
- **Talking points:** Key messages to deliver
- **Speaker notes:** What to say before/during/after demo
- **Demo steps:** Step-by-step instructions (30–40 sec per step)
- **Watch for:** What to monitor during demo (service health, timing, output)
- **Fallback:** What to do if demo fails

### 4. Demo Walkthroughs in Session Guide

**Each demo includes:**
- **Context:** Why this demo matters (reinforces which concept)
- **Demo goal:** What the audience should learn
- **Steps:** Detailed walkthrough with timing
- **Talking points:** What to say during each step
- **Watch for:** Critical success indicators
- **Fallback:** Pre-cached screenshot + narration script

---

## Why This Matters

**Benefits:**
- ✅ Demos reinforce learning while context is fresh (not 30 min later at end)
- ✅ Smaller demos = less cognitive load for audience
- ✅ Fallback strategy = session continues even if demos fail
- ✅ Realistic timing accounts for delays and Q&A spillover
- ✅ Pre-session checklist prevents surprises

**Trade-offs:**
- ⚠️ Requires 30 min pre-session setup (vs. 5 min for slides-only)
- ⚠️ Demo failures require quick decision-making (switch to fallback)
- ⚠️ Fallback screenshots must be created before session

---

## Next Steps (Before Session)

**Required (Before First Session 4 Run):**
1. [ ] Create fallback screenshots:
   - `sessions/session-4/fallback-screenshots/demo1-skill-edit.png`
   - `sessions/session-4/fallback-screenshots/demo2-vault-secret.png`
   - `sessions/session-4/fallback-screenshots/demo3-job-status.png`
   - `sessions/session-4/fallback-screenshots/demo4-deploy-readiness.png`

2. [ ] Create sample skill file: `skills/demo/weather-lookup.md` (or verify exists)

3. [ ] Test pre-session checklist once (verify 30 min is enough time)

4. [ ] Practice demo transitions: slide → demo → slide (should be seamless)

**Recommended:**
- [ ] Record practice session to check timing (aim for 60 min with buffer)
- [ ] Test fallback narration (should take ~30 sec per demo)
- [ ] Verify Aspire services start reliably (test on target machine)

---

## Questions for Bruno/Mark

1. **Fallback screenshots:** Should we create these during next test run, or use existing screenshots if available?
2. **Sample skill file:** Does `skills/demo/weather-lookup.md` exist, or should we create it?
3. **Live deploy (DEMO 4):** Should we attempt live deploy to Azure, or just show `aspire describe`? (Live deploy adds risk but is more impressive)
4. **Session length:** 60 min or 75 min? (60 min with tight timing, 75 min with comfortable Q&A buffer)

---

## Files to Review

**Speaker Script:**
- `docs/sessions/session-4/speaker-script.md` (comprehensive, ~5000 words with demo details)

**Session Guide:**
- `docs/sessions/session-4-guide.md` (operational checklist, demo walkthroughs, troubleshooting)

**Decision Document:**
- `.squad/decisions/inbox/milchick-session4-demos.md` (full rationale, timing breakdown, dependencies)

---

## Demo Philosophy

> "Live demos with fallback ready" > "perfect demos or nothing"

**Key principle:** Acknowledge failures gracefully, show fallback, keep moving. Audience values authenticity over perfection.

---

**Next:** Review updated files, create fallback screenshots, test pre-session checklist timing.
