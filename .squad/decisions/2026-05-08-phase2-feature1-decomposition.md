# 2026-05-08: Mark — Phase 2 Feature 1 Decomposition (Multi-Channel Adapters)

**Author:** Mark (Lead Architect)  
**Date:** 2026-05-08  
**Status:** 🟢 Approved, Ready for Sprint  
**Related:** Phase 2 Scope Proposal (2026-05-08), Feature 1 kickoff

---

## Executive Summary

Phase 2 Feature 1 (Multi-Channel Delivery Adapters: Teams, Slack, Generic Webhook) has been decomposed into **9 story cards** totaling **52 story points** over **~6.5 dev days** (8–10 calendar days with 3–4 team members).

**Critical Path:** Stories 1 → 3 → {2, 4, 5, 6} → 7 → 8 → 9  
**MVP Unblock:** Generic Webhook (Story 2) — no auth, ships by Day 1 afternoon  
**Demo Ready:** All 3 adapters + audit trail by end of Day 3  

---

## Story Cards (9 total, 52 pts)

| # | Title | Owner | Pts | Deps | Status |
|---|-------|-------|-----|------|--------|
| 1 | Adapter Factory & Registry | Irving | 5 | none | Ready |
| 2 | Generic Webhook Adapter (MVP) | Irving | 5 | S1 | Ready |
| 3 | Job-to-Channel Routing Data Model | Irving | 4 | none | Ready |
| 4 | Delivery Service (Fire-and-Forget) | Irving | 6 | S1,S3 | Ready |
| 5 | Channel Selection UI | Helly | 6 | S3 | Ready |
| 6 | Job Executor Integration | Irving | 5 | S4,S3 | Ready |
| 7 | Teams Proactive Message Adapter | Irving | 7 | S1,S2 | Ready |
| 8 | Slack Webhook Adapter | Irving | 6 | S1,S2 | Ready |
| 9 | Testing & Demo Prep | Dylan | 8 | S2–8 | Ready |

**Key decisions:** Hardcoded factory (no plugins), fire-and-forget delivery, audit trail via `AdapterDeliveryLog` entity.

---

## Parallelization & Timeline

### Day 1
- **Irving (Morning):** S1 (Factory, 2–3h) → S3 (Data Model, 2–3h)
- **Irving (Afternoon):** S2 (Webhook, 3h) → S4 (Service, 2–3h)
- **Helly (Afternoon):** S5 (UI, 3h) after S3 ready

### Day 2
- **Irving (Morning):** S6 (Integration, 2h) → S7 (Teams, 3h)
- **Irving (Afternoon):** S8 (Slack, 3h)
- **Dylan (Full Day):** S9 (Testing, 4h)

### Day 3
- **Team:** Integration testing, demo validation

---

## Key Questions Answered

**Q1: Minimal MVP adapter?**  
→ Generic Webhook (S2): no auth, HTTP POST, ships Day 1 afternoon, unblocks demo.

**Q2: Helly & Irving parallelization?**  
→ Irving must complete S1 + S3 first (4–5h), then Helly can start S5 in parallel with Irving's S2/S4/S7/S8.

**Q3: New project or existing?**  
→ Add to existing: `OpenClawNet.Channels/Adapters/` (webhook, slack), `OpenClawNet.Gateway/Services/` (factory, service), `OpenClawNet.Adapters.Teams/` (outbound impl). No new projects needed.

**Q4: Test strategy?**  
→ Unit: 80% coverage, mocked HttpClient. Integration: job execution → audit trail. Manual: 3 jobs across Teams/Slack/Webhook with live validation. Dylan owns story 9.

---

## Blockers & Unknowns

| Risk | Mitigation |
|------|-----------|
| **Teams conversation ref not stored** | Store during inbound bot message; retrieve from profile for MVP |
| **Background task queue missing** | Use `Task.Run()` + DB logging for MVP; optimize later |
| **Session 5 demo date conflict** | S1–S6 must complete by Day 2 EOD; S7/S8 by Day 3 EOD |
| **Slack webhook config undefined** | Add to `appsettings.Development.json`; document in README |

---

## Effort & Owner Assignment

| Owner | Stories | Total Pts | Duration |
|-------|---------|-----------|----------|
| Irving | 1,2,3,4,6,7,8 | 38 pts | ~5 days |
| Helly | 5 | 6 pts | ~1 day |
| Dylan | 9 | 8 pts | ~1 day |
| **Team** | **All** | **52 pts** | **~6.5 days** |

---

## Recommended Kickoff (Next 48 Hours)

**Tomorrow morning:**
1. Irving: S1 (Factory) — 2–3h
2. Irving: S3 (Data Model + migration) — 2–3h
3. Helly: Begin S5 research (wait for S3 PR)

**Tomorrow afternoon:**
1. Irving: S2 (Webhook adapter) — 3h
2. Irving: S4 (Delivery service) — 2–3h
3. Helly: S5 (UI implementation) — 3h

**Day 2:** Irving → S6 + S7, Dylan → S9 setup

**Day 3:** Final integration + demo validation

---

## Full Decomposition

See `PHASE2_FEATURE1_DECOMPOSITION.md` in repo root for complete story details: success criteria, notes, implementation guidance, and acceptance tests.

---

## Sign-Off

✅ **Approved for execution**  
🎯 **MVP (Generic Webhook) unblocks by Day 1 EOD**  
📅 **All adapters demo-ready by Day 3 EOD**  
🚀 **Session 5 demo achievable with this timeline**
