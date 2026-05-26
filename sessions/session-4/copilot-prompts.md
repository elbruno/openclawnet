# 🤖 Session 4 Copilot Prompts

## Prompt 1 — File-based skill guardrails

```text
Create a new file-based skill for incident triage. Include frontmatter (name, description, tags), clear tool usage boundaries, and explicit "do not" instructions for destructive actions.
```

## Prompt 2 — Vault-backed configuration

```text
Refactor this settings flow so secrets come from the configured secrets vault provider, while non-sensitive config remains in standard settings. Keep the same runtime behavior and add clear startup validation errors for missing secrets.
```

## Prompt 3 — Scheduled job definition

```text
Generate a recurring job definition that runs every weekday at 9:00 AM, executes an agent prompt for daily status summary, stores run metadata, and returns a concise failure reason when execution does not succeed.
```

## Prompt 4 — Transition to production readiness

```text
Given these three capabilities (file-based skills, secrets vault, and job scheduling), create a production-readiness transition checklist with technical actions, owners, and success criteria for each capability.
```

## Prompt 5 — Deployment decision checklist

```text
Given this Aspire application, generate a deployment decision checklist that compares managed container runtime vs Kubernetes for this workload, including observability, scaling, security, and operational ownership tradeoffs.
```

## Prompt 6 — Observability baseline

```text
Create an observability baseline for this distributed app with required health checks, log categories, trace boundaries, and actionable alert rules. Include what to avoid to reduce noise.
```

## Prompt 7 — Secure + extend governance

```text
Design a governance policy for extending file-based skills in production: review process, security checks, packaging/versioning, rollback rules, and promotion gates.
```

## Prompt 8 — Operate at scale playbook

```text
Create an operate-at-scale playbook for this workload covering resilience patterns, capacity planning, progressive rollout, and cost/performance controls tied to telemetry.
```
