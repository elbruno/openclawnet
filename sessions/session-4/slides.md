---
marp: true
title: "OpenClawNet — Session 4: Deploy, Operate & Scale"
description: "What’s new in OpenClaw .NET, then deployment and operations with Aspire"
theme: openclaw
paginate: true
size: 16:9
footer: "OpenClawNet · Session 4 · Deploy, Operate & Scale"
---

<!-- _class: lead -->

# OpenClawNet
## Session 4 — Deploy, Operate & Scale

**Microsoft Reactor Series · ~60 min · Intermediate .NET**

> *From “what’s new” to production-grade operations.*

<br>

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft  
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft  
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

---

## Session structure

1. **What’s new in OpenClaw .NET**
2. **Deploy with Aspire**
3. **Observe**
4. **Automate**
5. **Secure**
6. **Extend (skills)**
7. **Operate at scale**

---

<!-- _class: lead -->

# Part 1 — What’s new

---

## 1) File-based skills

- Skills are now first-class artifacts in the repo
- Versionable, reviewable, and easy to promote across environments
- Clear ownership model for skill packs per domain/team
- Enables safer rollout: test in dev, then stage, then production

```text
skills/
  finance/
    reconciliation.md
  support/
    triage.md
```

---

## 2) Secrets Vault

- Centralized secret handling replaces ad-hoc local settings
- Runtime reads secrets through one controlled abstraction
- Separation of duties:
  - developers reference secret names
  - operators manage secret values and rotation
- Better auditability and less accidental credential leakage

---

## 3) Job scheduling

- Built-in scheduling for recurring and one-time jobs
- Agent automation for maintenance and recurring workflows
- Operationally visible: status, last run, next run, failures
- Foundation for background reliability patterns

---

## Why these 3 updates matter together

- **Skills** define agent behavior
- **Vault** protects sensitive configuration and credentials
- **Jobs** make behavior run predictably without manual triggering

> This is the bridge from “demo app” to “operable platform”.

---

<!-- _class: lead -->

# Part 2 — Deploy -> Observe -> Automate -> Secure -> Extend -> Operate at scale

---

## Deploy (Aspire deployment options)

Reference: [aspire.dev/deployment](https://aspire.dev/deployment/)

- Start with local orchestrated validation
- Choose target based on constraints:
  - Container Apps / managed container runtime
  - Kubernetes (AKS or existing cluster)
  - VM/container-host scenarios
- Keep one app model, adapt deployment target per environment

---

## Observe

- Baseline first: health, readiness, liveness
- Distributed tracing for request/tool/job flows
- Logs + metrics + traces in a single operational narrative
- Define actionable alerts (not dashboard noise)

---

## Automate

- Scheduled jobs for recurring platform tasks
- Automated operational checks (drift, stale resources, failures)
- Automated release steps with guardrails
- Automation should be idempotent and observable

---

## Secure

- Secrets from vault, not from source files
- Least privilege for runtime identities and automation actors
- Approval boundaries for risky tools/actions
- Security posture as part of deployment, not an afterthought

---

## Extend (skills)

- Add domain skills as file-based packages
- Review skills like code (PRs, ownership, changelog)
- Validate prompt quality and tool usage boundaries
- Promote with version tags and rollback capability

---

## Operate at scale

- Capacity + concurrency planning for chat and jobs
- Fault handling strategy: retries, backoff, dead-letter policies
- Safe rollout patterns: canary, phased enablement, fast rollback
- Cost and performance governance tied to real telemetry

---

## Suggested live demo flow

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

# Q&A

**OpenClaw .NET — Session 4**

