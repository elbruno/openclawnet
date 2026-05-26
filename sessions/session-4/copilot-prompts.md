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

## Prompt 4 — Deployment decision checklist

```text
Given this Aspire application, generate a deployment decision checklist that compares managed container runtime vs Kubernetes for this workload, including observability, scaling, security, and operational ownership tradeoffs.
```

