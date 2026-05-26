# 🎤 Session 4 Speaker Script — Deploy, Operate & Scale

**Duration:** 60 minutes  
**Flow:** What's new -> Deploy -> Observe -> Automate -> Secure -> Extend -> Operate at scale

## 0:00–5:00 Opening + context

- Welcome and framing
- Position Session 4 as operations/production readiness
- Show agenda slide and timing

## 5:00–20:00 What’s new (Part 1)

### 1) File-based skills (5 min)
- Explain why file-based skills are better for review/versioning
- Show skill folder layout
- Emphasize ownership and promotion strategy

### 2) Secrets Vault (5 min)
- Explain secure secret retrieval model
- Contrast against appsettings/local env sprawl
- Mention rotation and auditability

### 3) Job scheduling (5 min)
- Recurring + one-time jobs
- Operational visibility (status/history/failure)
- Reliability expectations for background execution

## 20:00–45:00 Deploy-to-scale flow (Part 2)

### Deploy (7 min)
- Use Aspire deployment page as reference: <https://aspire.dev/deployment/>
- Walk through target options and selection criteria

### Observe (4 min)
- Health, logs, traces, metrics
- Alerting focused on actionable events

### Automate (4 min)
- Scheduled operational tasks
- Safe automated deployment/ops checks

### Secure (4 min)
- Vault-first secrets
- Least privilege for services and jobs

### Extend (skills) (3 min)
- Skill lifecycle with code review and versioning

### Operate at scale (3 min)
- Capacity, resilience, rollout strategy, and cost governance

## 45:00–55:00 Live demo

1. Load/update a file-based skill
2. Resolve a vault-backed secret at runtime
3. Schedule a job and inspect run metadata
4. Show deployment target choices and post-deploy observability

## 55:00–60:00 Wrap-up + Q&A

- Recap the two-part story: new capabilities + production operations
- Share links to slides/resources
- Q&A

