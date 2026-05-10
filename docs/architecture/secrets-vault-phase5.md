# Architecture Proposal: Secrets Vault Phase 5

| Field | Value |
|---|---|
| **Status** | DRAFT |
| **Date** | 2026-05-08 |
| **Owner** | Mark (Lead Architect) |
| **Requested by** | Bruno Capuano |
| **Related** | `docs/architecture/secrets-vault-lifecycle-phase4.md`, `docs/architecture/secrets-vault-acl-phase2.md` |

> **Scope:** CLI/ops validation, live Azure Key Vault strategy, audit recovery runbook, and explicit exclusions. Phase 5 completes the operational hardening and production-readiness foundation established in Phase 4.

---

## Executive Summary

**Phase 4 delivered:** Lifecycle semantics (versioning, rotation, soft-delete/purge, audit hash-chain) with E2E test coverage, manual testing procedures, and video production guidance.

**Phase 5 focus:** Operational tooling and production deployment patterns to make Phase 4's lifecycle features production-ready. This includes CLI validation commands, Azure Key Vault live deployment strategy, audit recovery procedures, and operational runbooks.

**What Phase 5 is NOT:**
- **Admin UI Phase B** remains a separate initiative (Mark's Phase A–C roadmap)
- **ACL Phase 2** (deny/grant semantics) remains independent
- **New backend adapters** (e.g., HashiCorp Vault, AWS Secrets Manager)

---

## Goals

1. **CLI Operational Validation:** Extend CLI with health checks, version diff, and audit forensics commands
2. **Azure Key Vault Production Strategy:** Document live AKV deployment patterns, key rotation coordination, and failover semantics
3. **Audit Recovery Runbook:** Provide step-by-step procedures for recovering from audit chain corruption or secret purge incidents
4. **Production Hardening:** Document cache tuning, rotation grace periods, and observability integration
5. **Explicit Non-Goals:** Clearly defer Admin UI Phase B, ACL Phase 2, and additional backends

---

## 1) CLI Operational Validation

### 1.1 New CLI Commands

Extend the standalone `vault-cli` binary with additional operational validation commands.

**Commands to add:**

#### `vault health`
```bash
vault-cli health
```

**Purpose:** Verify secrets vault subsystem health across all configured backends.

**Output:**
```json
{
  "status": "healthy",
  "backends": [
    {
      "type": "SQLite",
      "status": "healthy",
      "secretCount": 42,
      "auditChainValid": true
    },
    {
      "type": "AzureKeyVault",
      "status": "healthy",
      "vaultUrl": "https://openclaw-prod.vault.azure.net/",
      "connectivity": "ok"
    }
  ],
  "warnings": []
}
```

**Failure modes:**
- Backend unreachable → `status: "degraded"`, specific backend `status: "unhealthy"`
- Audit chain broken → `auditChainValid: false`, warning message
- Secrets count mismatch (SQLite vs AKV) → warning in `warnings[]`

#### `vault audit verify --verbose`
```bash
vault-cli audit-verify --verbose
```

**Purpose:** Extended audit chain verification with detailed output.

**Output (success):**
```
✓ Verified 1,247 audit rows
✓ Hash chain intact (no tampering detected)
✓ Genesis row: 0000000000000000000000000000000000000000000000000000000000000000
✓ Latest row: a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0u1v2w3x4y5z6
✓ Time span: 2026-01-15 to 2026-05-08 (113 days)
```

**Output (failure):**
```
✗ Hash chain broken at row 1,042
  Expected PreviousRowHash: a1b2c3d4...
  Actual PreviousRowHash:   b2c3d4e5...
  
✗ Possible causes:
  - Direct database manipulation
  - Audit row deletion or reordering
  - Clock skew during migration

→ See runbook: docs/runbooks/audit-recovery.md
```

#### `vault version-diff <secret-name> <version1> <version2>`
```bash
vault-cli version-diff db-password 5 7
```

**Purpose:** Show metadata diff between two versions (no plaintext).

**Output:**
```
Secret: db-password

Version 5:
  CreatedAt: 2026-04-10T14:30:00Z
  IsCurrent: false
  SupersededAt: 2026-04-15T09:15:00Z

Version 7:
  CreatedAt: 2026-04-20T11:00:00Z
  IsCurrent: true
  SupersededAt: null

Δ Time between versions: 10 days
Δ Rotations skipped: 1 (version 6 exists)
```

**Rationale:** Helps operators understand version history without exposing plaintext.

#### `vault audit export --since <date> --format json|csv`
```bash
vault-cli audit-export --since 2026-05-01 --format csv > audit-may.csv
```

**Purpose:** Export audit logs for external SIEM/compliance systems.

**Output (CSV):**
```csv
Timestamp,CallerId,SecretName,Operation,Outcome,ToolId,AgentId
2026-05-01T08:00:00Z,tool:analyzer,db-password,Resolve,Success,,,
2026-05-01T09:30:00Z,cli:admin,api-key,Rotate,Success,,,
```

**Rationale:** Phase 4 audit is local-only; export enables integration with enterprise audit systems.

### 1.2 CLI Exit Codes

Standardize exit codes for automation:

| Exit Code | Meaning |
|---|---|
| `0` | Success |
| `1` | General error |
| `2` | Audit chain broken |
| `3` | Backend unreachable |
| `4` | Secret not found |
| `5` | Permission denied (future ACL integration) |

---

## 2) Azure Key Vault Production Strategy

### 2.1 Deployment Model

Phase 4 architecture doc specified AKV adapter semantics but not deployment patterns. Phase 5 defines how to deploy OpenClawNet with live Azure Key Vault integration.

**Deployment Options:**

#### Option A: Hybrid (SQLite for audit, AKV for secrets)
**Configuration:**
```json
{
  "Vault": {
    "Backend": "AzureKeyVault",
    "AzureKeyVault": {
      "VaultUrl": "https://openclaw-prod.vault.azure.net/",
      "TenantId": "...",
      "ManagedIdentityClientId": "..."
    },
    "AuditBackend": "SQLite",
    "AuditStorePath": "/var/openclaw/audit.db"
  }
}
```

**Rationale:**
- AKV handles secrets with enterprise-grade HSM backing
- Local SQLite audit provides tamper-evident hash-chain
- Simpler failover (audit doesn't depend on AKV availability)

**Recommended for:** Production deployments

#### Option B: Full AKV (secrets + AKV audit logs)
**Configuration:**
```json
{
  "Vault": {
    "Backend": "AzureKeyVault",
    "AzureKeyVault": {
      "VaultUrl": "https://openclaw-prod.vault.azure.net/",
      "EnableNativeAudit": true
    },
    "AuditBackend": "None"
  }
}
```

**Rationale:**
- Rely entirely on AKV's built-in audit logs (Azure Monitor)
- No local audit storage required
- Trade-off: no local hash-chain tamper detection

**Recommended for:** Azure-native deployments where all audit flows to Azure Monitor

#### Option C: SQLite-only (dev/test)
**Configuration:**
```json
{
  "Vault": {
    "Backend": "SQLite",
    "SQLiteStorePath": "/var/openclaw/secrets.db"
  }
}
```

**Rationale:**
- Phase 1-4 default
- Suitable for local development and CI/CD testing

**Recommended for:** Non-production environments

### 2.2 Azure Key Vault Rotation Coordination

**Challenge:** When rotating a secret in AKV, how does OpenClawNet detect and reflect the new version?

**Phase 5 Strategy:**

1. **Polling (Baseline):**
   - Gateway polls AKV every N minutes (`Vault:AzureKeyVault:VersionPollIntervalMinutes`, default: 5)
   - Detects new versions via `GetPropertiesOfSecretVersions(name)`
   - Updates local cache `IsCurrent` pointer

2. **Event Grid (Future):**
   - Subscribe to AKV Event Grid events (`Microsoft.KeyVault.SecretNewVersionCreated`)
   - Gateway receives webhook, invalidates cache immediately
   - **Not implemented in Phase 5** (defer to Phase 6 or operational hardening sprint)

**Recommended configuration (Phase 5 MVP):**
```json
{
  "Vault": {
    "AzureKeyVault": {
      "VersionPollIntervalMinutes": 5,
      "EnableEventGridWebhook": false
    }
  }
}
```

### 2.3 AKV Failover Semantics

**Scenario:** AKV becomes unreachable (network partition, throttling, Azure outage).

**Phase 5 Behavior:**

- **Cache hits:** Serve from local cache (TTL: `Vault:Cache:TtlSeconds`, default 120)
- **Cache misses:** Return HTTP 503 (Service Unavailable) with `Retry-After` header
- **Audit recording:** Continue recording audit rows locally (eventual consistency when AKV recovers)

**Configuration:**
```json
{
  "Vault": {
    "Cache": {
      "TtlSeconds": 120,
      "ExtendTtlOnFailure": true,
      "MaxExtendedTtlSeconds": 600
    }
  }
}
```

**When `ExtendTtlOnFailure: true`:**
- If AKV is unreachable, extend cache TTL up to `MaxExtendedTtlSeconds` (10 minutes)
- Provides graceful degradation during transient AKV outages

**Rationale:** Prevents total service disruption during AKV availability incidents.

---

## 3) Audit Recovery Runbook

### 3.1 Scenario 1: Audit Chain Corruption Detected

**Symptom:** `vault audit verify` reports hash mismatch.

**Root Causes:**
- Direct database UPDATE/DELETE outside Gateway
- Audit row reordering during migration
- Clock skew or timestamp manipulation

**Recovery Procedure:**

1. **Stop all Gateway instances:**
   ```bash
   systemctl stop openclaw-gateway
   ```

2. **Isolate corrupted database:**
   ```bash
   cp /var/openclaw/audit.db /var/openclaw/audit.db.corrupted
   ```

3. **Identify first broken row:**
   ```bash
   vault-cli audit-verify --verbose
   # Output: "✗ Hash chain broken at row 1,042"
   ```

4. **Options:**

   **Option A: Restore from backup (recommended)**
   ```bash
   # Restore last known-good backup
   cp /backups/audit.db.2026-05-07 /var/openclaw/audit.db
   vault-cli audit-verify  # Should pass
   ```

   **Option B: Rebuild chain from row N onward (advanced)**
   ```bash
   # Recompute hashes for rows >= 1042
   # Future repair tooling, if approved: vault-cli audit-rebuild --from-row 1042
   ```
   ⚠️ **Warning:** Only use if you can verify row integrity manually

   **Option C: Truncate and continue (last resort)**
   ```bash
   # Delete all rows >= 1042, continue from last good row
   sqlite3 /var/openclaw/audit.db "DELETE FROM SecretAccessAudit WHERE Id >= 1042;"
   vault-cli audit-verify  # Should pass, but history lost
   ```

5. **Restart Gateway:**
   ```bash
   systemctl start openclaw-gateway
   ```

6. **Post-incident:**
   - Retain `/var/openclaw/audit.db.corrupted` for forensics
   - Review access logs to identify unauthorized DB access
   - Enable stricter file permissions: `chmod 600 /var/openclaw/audit.db`

### 3.2 Scenario 2: Secret Accidentally Purged

**Symptom:** Secret was purged but is still needed.

**Root Cause:** Operator ran `vault purge` or `DELETE /api/secrets/{name}/purge` by mistake.

**Recovery Procedure:**

1. **Check backup retention:**
   ```bash
   # List recent backups
   ls -lh /backups/secrets.db.*
   ```

2. **Restore from backup:**
   ```bash
   # Stop Gateway
   systemctl stop openclaw-gateway
   
   # Restore database to point before purge
   cp /backups/secrets.db.2026-05-08-0800 /var/openclaw/secrets.db
   
   # Verify secret exists
   sqlite3 /var/openclaw/secrets.db "SELECT Name FROM Secrets WHERE Name = 'db-password';"
   
   # Restart Gateway
   systemctl start openclaw-gateway
   ```

3. **If no backup exists:**
   - Secret is permanently lost (purge is irreversible)
   - Re-create secret with a new value through the Gateway `PUT /api/secrets/{name}` endpoint.
   - Update all dependent services with new secret

**Prevention:**
- Enable soft-delete grace period: `Vault:Retention:SoftDeleteDays = 30`
- Require confirmation for purge: `vault-cli purge <name> --force` or Gateway `X-Confirm-Purge`.
- Restrict purge to admin-only role (future ACL Phase 2)

### 3.3 Scenario 3: Version History Mismatch (SQLite vs AKV)

**Symptom:** SQLite shows `[1, 2, 3]` versions, but AKV shows `[1, 2, 3, 4]`.

**Root Cause:** External rotation in AKV (e.g., manual Azure Portal rotation, Terraform apply).

**Recovery Procedure:**

1. **Sync versions from AKV:**
   ```bash
   # Future sync tooling, if approved: vault-cli sync-from-akv --secret db-password --dry-run
   # Output: "Would sync version 4 from AKV to local metadata"
   
   # Future sync tooling, if approved: vault-cli sync-from-akv --secret db-password
   # Output: "✓ Synced version 4 metadata"
   ```

2. **Verify sync:**
   ```bash
   vault-cli list-versions db-password
   # Output: [1, 2, 3, 4]
   ```

**Rationale:** Phase 5 acknowledges that AKV can be rotated externally; provide tooling to reconcile metadata.

**Configuration:**
```json
{
  "Vault": {
    "AzureKeyVault": {
      "AutoSyncVersions": true,
      "SyncIntervalMinutes": 15
    }
  }
}
```

When `AutoSyncVersions: true`, Gateway automatically polls AKV for new versions and updates local metadata.

---

## 4) Production Hardening

### 4.1 Cache Tuning

Phase 4 introduced cache invalidation on rotation. Phase 5 documents production cache tuning.

**Configuration:**
```json
{
  "Vault": {
    "Cache": {
      "Enabled": true,
      "TtlSeconds": 120,
      "MaxEntries": 10000,
      "EvictionPolicy": "LRU"
    }
  }
}
```

**Guidelines:**

| Scenario | Recommended TTL |
|---|---|
| High-frequency resolution (tools) | 120–300s |
| Low-frequency resolution (config) | 600–1800s |
| Zero-trust / paranoid mode | 0 (disabled) |

**Cache invalidation triggers:**
- Secret rotation → invalidate all versions of that secret
- Secret deletion → invalidate all versions
- Secret recovery → invalidate (force re-fetch)

### 4.2 Rotation Grace Period

Phase 4 introduced rotation but did not define grace period semantics.

**Phase 5 Semantics:**

**Grace Period:** After rotation, old version remains accessible for a configurable grace window.

**Configuration:**
```json
{
  "Vault": {
    "RotationGracePeriodMinutes": 5
  }
}
```

**Behavior:**
- Version N is current
- Operator rotates to version N+1 at T=0
- For T=0 to T=5min: both version N and N+1 are valid
- At T=5min: version N is superseded (no longer accessible via default resolve)
- After T=5min: only explicit versioned resolve can access version N

**Rationale:** Allows in-flight tool executions to complete with old version before hard cutoff.

**Implementation note:** Store `SupersededAt + GracePeriod` as `EffectiveSupersededAt`. Resolve logic checks `NOW() < EffectiveSupersededAt` to allow grace period access.

### 4.3 Observability Integration

**Metrics to expose (Prometheus/OpenTelemetry):**

| Metric | Type | Description |
|---|---|---|
| `vault_secrets_total` | Gauge | Total secrets count |
| `vault_resolve_requests_total` | Counter | Total resolve calls |
| `vault_resolve_cache_hits_total` | Counter | Cache hit count |
| `vault_resolve_cache_misses_total` | Counter | Cache miss count |
| `vault_rotations_total` | Counter | Total rotations |
| `vault_audit_chain_valid` | Gauge | 1=valid, 0=broken |
| `vault_backend_reachable` | Gauge | 1=reachable, 0=unreachable |

**Logging:**
- Rotation events → INFO level
- Cache invalidations → DEBUG level
- Audit chain verification → INFO (pass) / ERROR (fail)
- Backend connectivity issues → WARN

---

## 5) Explicit Exclusions (Deferred)

### 5.1 Admin UI Phase B

**Status:** NOT in Phase 5 scope.

**Rationale:** Admin UI is a separate product initiative (Mark's Phase A–C roadmap). Phase 5 focuses on CLI/ops tooling and backend hardening.

**When it ships:** Phase B will provide web UI for:
- Secret CRUD
- Version browsing
- Audit log viewing
- Rotation UI

**Phase 5 deliverable:** CLI commands (`vault health`, `vault audit verify`) provide equivalent functionality for ops/headless environments.

### 5.2 ACL Phase 2 (Deny/Grant Semantics)

**Status:** NOT in Phase 5 scope.

**Rationale:** ACL Phase 2 introduces deny rules, approval workflows, and role-based access. It's orthogonal to lifecycle/ops hardening.

**Coordination point:** Phase 5 audit export (`vault audit export`) will include ACL-related outcomes once Phase 2 ships.

**Phase 5 deliverable:** Ensure audit schema can accommodate ACL reason codes (already done in Phase 4 schema design).

### 5.3 Additional Backend Adapters

**Status:** NOT in Phase 5 scope.

**Backends NOT added:**
- HashiCorp Vault
- AWS Secrets Manager
- Google Secret Manager

**Rationale:** Phase 4 delivered SQLite (full-featured) and AKV (cloud-native). Additional backends can be added as separate initiatives once Phase 5 ops patterns are proven.

**Phase 5 deliverable:** Document backend adapter interface (`ISecretsStore`) for future contributors.

---

## 6) Implementation Handoffs

### 6.1 CLI Commands → Irving (Backend Infrastructure)

**Tasks:**
- Implement `vault health` command
- Implement `vault audit verify --verbose`
- Implement `vault version-diff`
- Implement `vault audit export`

**Acceptance criteria:**
- All commands pass unit + E2E tests
- Exit codes documented and tested
- Help text (`--help`) covers all options

### 6.2 Azure Key Vault Strategy → Drummond (Platform Hardening)

**Tasks:**
- Document hybrid deployment (SQLite audit + AKV secrets)
- Implement AKV version polling (`VersionPollIntervalMinutes`)
- Implement cache failover (`ExtendTtlOnFailure`)
- Test AKV throttling and partition scenarios

**Acceptance criteria:**
- Deployment guide with ARM/Bicep templates
- Failover behavior documented with test results
- AKV sync tested against live Azure Key Vault

### 6.3 Audit Recovery Runbooks → Dylan (Testing) + Ricken (Documentation)

**Tasks:**
- Write detailed runbooks for 3 scenarios (corruption, purge, version mismatch)
- Create synthetic corruption fixtures for testing
- Validate recovery procedures end-to-end

**Acceptance criteria:**
- Runbooks pass peer review
- All recovery procedures tested on live data
- Runbooks linked from main vault docs

### 6.4 Production Hardening → Milchick (Operations)

**Tasks:**
- Document cache tuning guidelines
- Implement rotation grace period (`RotationGracePeriodMinutes`)
- Define observability metrics (Prometheus format)

**Acceptance criteria:**
- Cache tuning tested under load
- Grace period validated with concurrent tool executions
- Metrics exposed via `/metrics` endpoint

---

## 7) Test Strategy

### 7.1 Unit Tests

- CLI command parsing and exit codes
- AKV version polling logic (mocked)
- Audit chain rebuild algorithm
- Cache TTL extension logic

### 7.2 Integration Tests

- `vault health` against live SQLite + AKV
- `vault audit verify` on clean and corrupted fixtures
- `vault audit export` format validation
- AKV failover with simulated network partition

### 7.3 Manual Validation

- Run all recovery runbooks on test environment
- Validate rotation grace period with real tools
- Test cache tuning under load (vegeta/k6)

---

## 8) Success Criteria

Phase 5 is **complete** when:

1. ✅ All CLI commands implemented and documented
2. ✅ Azure Key Vault hybrid deployment guide published
3. ✅ Audit recovery runbooks tested and validated
4. ✅ Production hardening (cache tuning, metrics) documented
5. ✅ All exclusions (Admin UI, ACL Phase 2) explicitly documented
6. ✅ Handoffs to Irving/Dylan/Drummond/Ricken/Milchick accepted

Phase 5 is **NOT complete** until:

- All new CLI commands have E2E tests
- AKV failover behavior is tested under realistic conditions
- Recovery runbooks are validated on live data

---

## 9) Timeline Estimate

| Milestone | Duration | Owner |
|---|---|---|
| CLI commands implementation | 1 week | Irving |
| AKV deployment guide + polling | 1 week | Drummond |
| Audit recovery runbooks | 3 days | Dylan + Ricken |
| Production hardening docs | 3 days | Milchick |
| Integration testing | 1 week | Dylan |
| **Total** | **3 weeks** | Team |

---

## 10) Ratification

**Status:** DRAFT (awaiting team review)

**Review checklist:**
- [ ] Bruno Capuano (Product Owner) — scope approval
- [ ] Irving (Backend) — CLI commands feasibility
- [ ] Drummond (Platform) — AKV deployment strategy
- [ ] Dylan (Testing) — test coverage adequacy
- [ ] Ricken (Docs) — runbook clarity
- [ ] Milchick (Ops) — production hardening completeness

---

## Related Documents

- `docs/architecture/secrets-vault-lifecycle-phase4.md` — Phase 4 lifecycle semantics
- `docs/architecture/secrets-vault-acl-phase2.md` — ACL Phase 2 (deferred)
- `docs/architecture/secrets-vault-admin-ui.md` — Admin UI roadmap (deferred)
- `docs/testing/secrets-vault-phase4-e2e.md` — Phase 4 test coverage
- `docs/runbooks/audit-recovery.md` — (To be created in Phase 5)

---

**Next Steps:**
1. Review and ratify this document
2. Create `.squad/decisions/inbox/mark-vault-phase5-scope.md` with accepted scope
3. Begin implementation handoffs to Irving/Drummond/Dylan/Ricken/Milchick
