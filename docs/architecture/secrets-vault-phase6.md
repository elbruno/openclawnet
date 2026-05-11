# Architecture Proposal: Secrets Vault Phase 6 (Proposed)

| Field | Value |
|---|---|
| **Status** | PROPOSED / FUTURE (not active) |
| **Date** | 2026-05-08 |
| **Owner** | Mark (Lead Architect) |
| **Input from** | Ricken (DevRel), Team discussions |
| **Related** | Phase 5 (ops), Phase 4 (lifecycle), Admin UI, ACL candidates |

> **Scope:** Candidate enhancements after the merged Phase 5 work. This is **not committed work**; rather, a collection of proposed features to evaluate if Bruno chooses to activate a Phase 6.

---

## Executive Summary

**Phase 5 completed the agreed implementation scope:** CLI operational validation, Azure Key Vault production strategy, audit recovery runbooks, and operational hardening. The vault is implementation-complete for the current plan.

**Phase 6 vision:** Post-Phase-5 quality-of-life and operational enhancements, including:
- **Admin UI Phase B:** Enhanced reveal/rotate/audit UX with admin safeguards and feature flags
- **ACL Phase 2:** Per-agent or per-tool access boundaries with policy enforcement and migration strategies
- **Live Azure Key Vault CI validation:** Credentialed test-vault workflow with safety gates
- **Operational automation:** Scheduled audit verification, backup/restore drills, and runbook execution
- **Operator tooling extensions:** Audit export, version diff/status refinements, sync/import workflows
- **Product showcase follow-through:** Video and demo content for external documentation (if scoped)

**What Phase 6 is NOT:**
- **No Phase 5 reopening:** Phase 5 lifecycle features are merged and validated for the agreed scope.
- **No LLM exposure:** Plaintext secrets are never exposed to tool results or agent reasoning.
- **No purge-ease:** Purge operations remain explicit, audited, and operator-controlled.
- **No live Azure credentials required for local tests:** Default test suite remains air-gapped.

---

## Candidate Scope

### 1. Admin UI Phase B: Advanced Lifecycle & Audit UX

**Rationale:** Phase 5 CLI and ops runbooks serve operator workflows; Phase 6 polishes the admin UI for day-to-day secret lifecycle management.

**Proposed features:**
- **Reveal/Rotate UX:** Toggle-able plaintext display with audit-logged reveals; one-click rotate workflows with confirmation dialogs.
- **Audit Explorer:** Timeline view of secret access, rotation, and purge events with filtering by date, caller type, and secret name.
- **Admin Safeguards:** Session-based access tokens (not perpetual); time-limited reveals (e.g., 5-minute window); dual-control for sensitive operations (purge requires secondary approval).
- **Feature Flags:** UI toggles for reveal, rotate, purge behind `Feature:AllowAdminReveal`, `Feature:AllowAdminRotate`, etc., enabling phased rollout.

**Explicit non-goals:**
- No LLM-callable UI endpoints; UI is browser-only.
- No plaintext secret caching in UI state; values fetched on-demand and cleared immediately.
- No export of plaintext secrets to CSV/JSON/clipboard without explicit operator intent (and audit logging).

**Acceptance:**
- [ ] Reveal button is toggle-able; plaintext display includes audit-log entry with reveal timestamp.
- [ ] Rotate button triggers workflow: generate new value, store versioned, notify operator, mark old version deprecated.
- [ ] Audit explorer queries `SecretAccessAudit` and renders timeline.
- [ ] Feature flags disable reveal/rotate/purge per deployment.
- [ ] Session tokens expire after operator inactivity; re-auth required.

---

### 2. ACL Phase 2: Per-Agent and Per-Tool Access Boundaries

**Rationale:** The current vault provides runtime secret resolution and auditability. ACL Phase 2 would formalize which tools or agents can request specific secrets.

**Proposed approach:**
- **Per-Tool Bindings:** Tools declare required secrets at registration: `GoogleWorkspaceTool.RequiredSecrets = { "Google/ClientSecret", "Google/RefreshToken" }`.
- **Per-Agent Policies:** Agents can be scoped to subsets of tools and thus subsets of secrets (e.g., agent-public can use Slack, but not Admin/MasterKey).
- **Policy Enforcement:** On `IVault.GetAsync()`, check if caller tool/agent has ACL entry for the requested secret. Throw `SecretAccessDeniedException` if not.
- **Audit Integration:** Every denied access is logged alongside allowed accesses for forensics.
- **Migration Path:** Start ACL Phase 2 in "audit mode" (log policy matches and misses but do not enforce), then flip to "enforce mode" after rollout.

**Design sketch:**
```sql
-- New table: ToolSecretACL
CREATE TABLE ToolSecretACL (
    ToolName TEXT NOT NULL,           -- e.g., "GoogleWorkspaceTool"
    SecretPattern TEXT NOT NULL,       -- e.g., "Google/*" or exact "Google/ClientSecret"
    AccessLevel TEXT NOT NULL,         -- 'Read', 'ReadWrite' (future: 'Admin')
    CreatedAt TEXT NOT NULL,
    CreatedBy TEXT NOT NULL            -- operator who added the binding
);

-- New column: SecretAccessAudit.Granted (bool)
-- TRUE = access allowed and returned, FALSE = access denied by ACL
```

**Migration considerations:**
- Existing tools have no declared secret bindings; start with "allow-all" ACL entries for backward compatibility.
- CLI commands: `vault tool-bind GoogleWorkspaceTool Google/ClientSecret` and `vault acl list --tool GoogleWorkspaceTool`.

**Explicit non-goals:**
- No agent-level fine-grained RBAC (e.g., read-only vs. read-write per secret); Phase 2 is tooling scope only.
- No dynamic ACL updates at runtime (no `vault acl grant` during agent execution).

---

### 3. Live Azure Key Vault CI Validation

**Rationale:** Phase 5 defines Azure Key Vault integration strategy; Phase 6 closes the loop with credentialed CI validation.

**Proposed workflow:**
- **Optional CI Job:** Separate GitHub Actions workflow (e.g., `.github/workflows/vault-azure-ci.yml`) that runs only on demand or after Phase 5 merges.
- **Credentialed Test Vault:** Dedicated Azure test vault (ephemeral or reused) provisioned per CI run with GitHub Actions service principal.
- **Safety Gates:**
  - Gate 1: Workflow requires explicit approval before accessing Azure (prevent accidental credential leakage).
  - Gate 2: Only `test-*` secrets are created; no production secrets touched.
  - Gate 3: Workflow cleans up test secrets after assertions.
  - Gate 4: Workflow runs isolated; does not affect default test suite.
- **Skip Behavior:** If Azure credentials are not available, workflow exits with skip status (not failure).

**Acceptance:**
- [ ] CI job creates test-vault-001 via managed identity or service principal.
- [ ] Test suite runs against live AzureKeyVaultSecretsStore adapter.
- [ ] All test secrets are prefixed `test-*` and cleaned up after job.
- [ ] Job logs are audit-friendly; no plaintext secrets in logs.
- [ ] Workflow skips gracefully if AZURE_* env vars are not set.

---

### 4. Operational Automation

**Rationale:** Phase 5 provides manual runbooks; Phase 6 automates common operations via scheduled tasks and CLI enhancements.

**Proposed capabilities:**
- **Scheduled Audit Verification:** Background job (e.g., daily at 2 AM UTC) runs `vault audit-verify`, stores result summary, alerts operator on failure.
- **Backup/Restore Drills:** Monthly automated test of vault backup and restore procedures; exercises full recovery path without touching production (uses test vault).
- **Health Check Cadence:** Automated `vault health` checks every 5 minutes; triggers PagerDuty/Slack alert if status degrades.
- **Purge Recovery Runbook Automation:** Interactive CLI guide for recovering a recently purged secret (requires manual confirmation at each step).
- **Rotation Reminder Reports:** Weekly email/dashboard showing secrets approaching rotation deadline (based on `LastRotatedAt` and configured TTL).

**Integration points:**
- Kubernetes CronJobs (if production uses K8s) or systemd timers (if bare metal).
- Observability: send health check results to Application Insights / Prometheus for dashboarding.

---

### 5. Additional Operator Tooling

**Rationale:** Extend CLI with quality-of-life commands for daily operations and auditing.

**Proposed commands:**
- **`vault audit export --from DATE --to DATE --format json --output audit.json`**  
  Export audit log slice for compliance reporting, excluding plaintext secrets.

- **`vault version diff <secret-name> <version-a> <version-b>`**  
  Compare two versions of a secret (metadata diff: timestamps, rotation notes, creator); never display plaintext diffs.

- **`vault status --detailed`**  
  Extended vault status: secret count, oldest/newest audit entry, purged-secrets recovery available, cache hit rate, last rotation date.

- **`vault sync --from <backend> --to <backend>`**  
  One-way sync of secret list from source backend to destination (e.g., SQLite → Azure Key Vault test vault) for validation workflows.

- **`vault import --from sqlite-backup --path /backup/vault-2026-05-01.db`**  
  Restore secrets from backup database without overwriting live vault.

**Acceptance:**
- [ ] `vault audit export` returns JSON with no plaintext values.
- [ ] `vault version diff` shows metadata but never plaintext or hashes.
- [ ] `vault status` matches health check output format.
- [ ] `vault sync` is logged and requires explicit confirmation.

---

### 6. Video & Product Showcase Follow-Through (Optional)

**Rationale:** Phase 4 includes video production guidance; Phase 6 may extend with external demo content if prioritized.

**Proposed scope:**
- **"Vault Lifecycle in 5 Minutes"** demo video: set secret → rotation → audit check → recovery.
- **Product page callout:** Documentation link on main site to vault architecture and operator runbooks.
- **Getting-started guide for operators:** How to deploy vault, integrate with tools, monitor health.

**Framing:** This is *separate from implementation completeness*. Skippable if time/priority constraints apply.

---

## Explicit Non-Goals

| Non-Goal | Reason |
|----------|--------|
| **Reopening Phase 5** | Phase 5 CLI, Azure strategy, and runbooks are merged. Phase 6 only *extends* that baseline if activated. |
| **Exposing plaintext to LLM** | Core security contract unchanged: secrets never appear in ToolResult or agent reasoning context. |
| **Making purge easier** | Purge remains explicit and audited; no casual "delete all old versions" shortcuts. |
| **Requiring live Azure credentials for default tests** | Local test suite remains air-gapped; Azure CI is optional opt-in. |
| **New backends (HashiCorp, AWS Secrets Manager)** | Phase 3 established adapter pattern; Phase 6 does not add new adapters. |
| **Secret rotation without operator intent** | Rotation is operator-initiated or scheduled; no automatic rotation based on age alone. |

---

## Validation Approach (High-Level)

### Unit Tests
- ACL enforcement: verify `SecretAccessDeniedException` thrown when caller not in ACL.
- Admin UI: verify reveal/rotate endpoints respect feature flags and session tokens.

### Integration Tests
- Tool binding: register tool with secret bindings, verify `IVault.GetAsync()` checks bindings.
- Operator workflows: spin up test vault, perform audit verify → rotate → purge recovery → restore.
- Azure CI: optional job creates test vault, runs lifecycle operations, cleans up.

### E2E / Manual Tests
- Admin UI operations: open browser, authenticate, reveal secret, check audit log.
- CLI commands: `vault audit export`, `vault version diff`, `vault sync` with example data.
- Backup/restore drill: export vault, delete test secret, restore from backup, verify recovery.

### Documentation Tests
- Operator runbooks are executable as written (no steps that fail or assume unstated context).
- Getting-started guide walks a new operator through: vault deployment → tool registration → health check.

---

## Decision Gate Before Starting

**Phase 6 is conditional on Bruno's prioritization decision.** Before implementation:

1. **Which candidate features does Bruno want to activate?**
   - Admin UI Phase B: yes / no / partial
   - ACL Phase 2: yes / no / partial
   - Azure CI: yes / no
   - Operational automation: yes / no / partial
   - Operator tooling: yes / no / partial
   - Video/showcase: yes / no

2. **Sequencing:** In what order should selected items be implemented?

3. **Resourcing:** Who takes each feature (Mark, Drummond, Ricken, others)?

---

## Acceptance Criteria (Phase 6 as a Whole)

- [ ] All activated features are specified in `.squad/decisions.md` with clear scope boundaries.
- [ ] Each feature has passing unit/integration/manual tests per validation approach above.
- [ ] No Phase 5 functionality is broken or re-opened.
- [ ] Audit log continues to be append-only and tamper-evident.
- [ ] Operator runbooks remain clear, executable, and up-to-date.
- [ ] Phase 6 documentation is merged and linked from main Vault README.

---

## References

| Resource | Path |
|----------|------|
| Phase 4 Lifecycle | `docs/architecture/secrets-vault-lifecycle-phase4.md` |
| Phase 5 Lifecycle | `docs/architecture/secrets-vault-lifecycle-phase5.md` |
| Phase 5 Operations | `docs/operations/secrets-vault-phase5-ops.md` |
| Threat Model (Phase 1) | `docs/architecture/secrets-vault-threat-model.md` |
| Evolution (Phase 1) | `docs/architecture/secrets-vault-evolution.md` |
| Team Decisions | `.squad/decisions.md` |

---

**Status:** Ready for Bruno's decision on what subset to activate. No implementation work begins until scope is locked in `.squad/decisions.md`.
