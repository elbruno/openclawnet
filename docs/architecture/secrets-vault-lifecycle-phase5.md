# Architecture Proposal: Secrets Vault Operations (Phase 5)

| Field | Value |
|---|---|
| **Status** | IMPLEMENTATION TRACK |
| **Date** | 2026-05-09 |
| **Owner** | Ricken (DevRel) |
| **Requested by** | Bruno Capuano, Mark (Architecture) |
| **Related** | `docs/architecture/secrets-vault-lifecycle-phase4.md`, `docs/architecture/secrets-vault-phase5.md` |

> **Scope:** Phase 5 captures post-Phase-4 operational features and testing surface. Phase 4 is merged; the current CLI implementation lives in `src/OpenClawNet.Cli.Vault`.

---

## Goals

1. **Operationalize Phase 4 lifecycle** — translate Phase 4 design (versioning, rotation, soft-delete, audit hash-chain) into end-to-end CLI and UI workflows.
2. **Establish comprehensive testing** — unit tests + integration tests + extended E2E scenarios covering edge cases and cross-backend behavior.
3. **Provide admin surface** — `vault-cli` commands for operators to perform rotation, recovery, audit verification, and lifecycle management without UI.
4. **Cross-link documentation** — Phase 4 manual testing guides + video scripts + Phase 5 CLI reference form a cohesive operator runbook.

## Non-Goals

- Admin UI implementation (Phase B, Mark's domain).
- External backend testing beyond design mapping (Phase 3 Azure Key Vault scope).
- Performance tuning or load testing (deferred to Phase 6 or ops phase).

---

## 1) CLI Surface

### 1.1 Command Structure

Phase 5 establishes the standalone `vault-cli` operator binary in `src/OpenClawNet.Cli.Vault`. Commands map directly to the Phase 4 store API and avoid plaintext output.

```
vault-cli list                              # List active secret metadata only
vault-cli list-versions <name>              # List version numbers for a secret
vault-cli rotate <name>                     # Create a new version from stdin
vault-cli delete <name>                     # Soft-delete a secret
vault-cli recover <name>                    # Undo soft-delete
vault-cli purge <name> --force              # Irreversible purge with explicit force flag
vault-cli audit-verify                      # Verify hash-chain integrity
```

### 1.2 Rotate Command Details

```
vault-cli rotate <name>
```

**Modes:**
- stdin — read the new value from standard input so values do not appear in shell history

**Behavior:**
1. Read the new value from stdin.
2. Call `ISecretsStore.RotateAsync`, which creates the next `SecretVersions` row atomically.
3. Output only operation status; plaintext is never printed.

**Expected output:**
```
✓ Secret 'Google/ClientSecret' rotated successfully.
```

### 1.3 List Versions Command

```
vault-cli list-versions <name>
```

**Output:**
```
Versions for 'Google/ClientSecret':
  1
  2
  3
```

### 1.4 Delete/Recover/Purge Commands

```
vault-cli delete <name>
  → Sets DeletedAt = now, PurgeAfter = now + RetentionWindow
  → Secret becomes inaccessible to non-admin callers
  → Audit: outcome = SOFT_DELETED

vault-cli recover <name>
  → Clears DeletedAt, PurgeAfter (undoes soft-delete)
  → Secret becomes accessible again
  → Audit: outcome = RECOVERED

vault-cli purge <name> --force
  → Requires confirmation (--force flag) or interactive prompt
  → Physically deletes Secrets + all SecretVersions rows
  → Non-reversible; audit rows remain for forensics
  → Audit: outcome = PURGED
```

### 1.5 Audit Verify Command

```
vault-cli audit-verify
```

**Behavior:**
1. Query `SecretAccessAudit` in deterministic chain order.
2. Recompute each row hash with `SecretAccessAuditHashChain`.
3. Return exit code 0 for valid and non-zero for tampering/corruption.

**Output on corruption:**
```
✗ Audit chain integrity check FAILED.
  First corruption at row index: 42
  Expected RowHash: abc123...
  Found RowHash:    def456...
  Row details: TimestampUtc=2026-05-08T15:30:00Z, SecretName=Slack/BotToken, Outcome=ACCESSED

  → This indicates tampering or data corruption. Do NOT trust vault state.
  → Recommended action: restore DB from last known-good backup, re-run verify.
  → Retain corrupted DB for forensics.
```

Repair is intentionally not implemented. Corruption response is restore/forensics, not silent mutation.

---

## 2) Testing Surface (Phase 5 Extension)

Phase 4 includes 7 E2E scenarios in `SecretsVaultPhase4E2ETests.cs`. Phase 5 adds extended integration tests and E2E cross-backend validation.

### 2.1 Additional Unit Tests (Vault.Core)

This remains a future CLI enhancement; the current Phase 5 CLI ships metadata list/versioning, rotate, delete, recover, purge, and audit verification.

Proposed test classes:

- `RotateAtomicityTests` — concurrent rotations, no split-brain `IsCurrent` rows
- `SoftDeleteRecoveryTests` — delete → resolve fails → recover → resolve succeeds
- `AuditHashChainTests` — corruption detection on modified rows
- `VersionResolutionTests` — resolve specific version vs. latest, fallback on missing version
- `CacheInvalidationTests` (Phase 4 extension) — rotation invalidates "latest" pointer but not specific version caches

### 2.2 Integration Tests (Vault.Integration)

This remains a future ops enhancement; the Phase 4 schema is merged and available for later reporting work.

- `AzureKeyVaultBackendMapping` — verify Phase 4 operations map correctly to AKV SDK calls (via mocked client)
- `EnvVarBackendDegradation` — confirm env var backend explicitly rejects rotate/soft-delete with clear error message
- `DockerSecretsBackendDegradation` — confirm Docker secrets backend rejects versioning with clear error message

### 2.3 E2E Scenarios (Phase 5 Extension)

Extend Phase 4's 7 scenarios with:

- **Scenario 8: Rotation under concurrent resolves** — verify new version is always returned post-rotation, no resolution inconsistency
- **Scenario 9: Soft-delete + purge lifecycle** — delete → verify NotFound → recover → verify accessible → purge → verify irreversible
- **Scenario 10: Audit chain corruption detection** — intentionally corrupt a hash → run verify → confirm detection

### 2.4 Manual Testing Runbooks (Phase 5 Extension)

Cross-reference Phase 4 manual tests with Phase 5 CLI:

- Update `docs/manual-testing/secrets-vault-phase4-manual-tests.md` to include CLI command equivalents
- Add Phase 5 CLI section: `# Testing Phase 5: CLI Commands`
  - Map each test scenario to CLI command sequence
  - Provide curl/PowerShell examples for Admin API endpoints (if Phase B Admin UI is shipping concurrently)

### 2.5 Video/Demo Scripts (Phase 5 Extension)

Cross-reference Phase 4 video scripts:

- Update `docs/testing/secrets-vault-phase4-video-scripts.md` to highlight CLI demonstrations
- Add Phase 5 section: `# Phase 5 Demonstration: Operator Workflows`
  - Rotate a secret (3 min demo)
  - Recover a deleted secret (2 min demo)
  - Verify audit chain integrity (2 min demo)

---

## 3) Documentation Cross-Links

### Phase 4 Reference Documents

These Phase 4 documents are **inputs** to Phase 5 testing/validation:

| Document | Purpose | Cross-Link |
|----------|---------|------------|
| `docs/architecture/secrets-vault-lifecycle-phase4.md` | Phase 4 design spec | Phase 5 CLI § 1.0 references all Phase 4 semantics |
| `docs/testing/secrets-vault-phase4-e2e.md` | Phase 4 E2E coverage | Phase 5 § 2.3 extends with 3 new scenarios |
| `docs/manual-testing/secrets-vault-phase4-manual-tests.md` | Phase 4 manual test runbook | Phase 5 § 2.4 adds CLI equivalents section |
| `docs/testing/secrets-vault-phase4-video-plan.md` | Phase 4 video plan | Phase 5 § 2.5 references and extends |
| `docs/testing/secrets-vault-phase4-video-scripts.md` | Phase 4 demo scripts | Phase 5 § 2.5 adds Phase 5 operator demos |

### Phase 5 Documentation to Create

- **`docs/cli/secrets-vault-phase5-cli-reference.md`** — comprehensive CLI reference with examples
- **`docs/testing/secrets-vault-phase5-integration-tests.md`** — Phase 5 extended integration test catalog
- **`docs/manual-testing/secrets-vault-phase5-operator-runbook.md`** — Phase 5 operator workflows (rotate, recover, verify)

---

## 4) CLI Implementation Approach

The current CLI framework is a minimal standalone console app in `src/OpenClawNet.Cli.Vault`.

Candidate approaches:

1. **Aspire CLI extension** — reuse Phase 4 CLI parser if Aspire has built-in command infrastructure
2. **System.CommandLine** — standalone command parser (standard .NET approach for CLI)
3. **Spectre.Console** — rich console UI for interactive CLI (supports spinners, tables, color)

**Recommendation:** Use **System.CommandLine** for parsing + **Spectre.Console** for output formatting (both have mature community adoption).

---

## 5) Follow-Up Questions

| # | Question | Impact | Status |
|---|----------|--------|--------|
| 5.1 | **CLI framework expansion** | Current implementation is a minimal console app. Should future work adopt System.CommandLine or keep the lightweight parser? | **OPEN** |
| 5.2 | **CLI framework choice** | Affects future command growth and help formatting. Who owns this decision? | **PENDING architecture review** |
| 5.3 | **Admin API endpoint surface** | CLI commands imply HTTP endpoints (for Phase B Admin UI). Should Phase 5 include REST layer or defer to Admin UI phase? | **PENDING Admin UI scope clarification** |
| 5.4 | **Audit verify --fix-if-possible** | Auto-repair of hash chain too risky? Or defer to ops phase? | **DEFERRED** |
| 5.5 | **Performance benchmarks** | Should Phase 5 include performance targets (rotation latency, audit verify time)? | **DEFERRED to ops phase** |

---

## 6) Success Criteria (Phase 5 Planning Track)

✅ **Documentation track:**
- [ ] Phase 5 overview document complete (this doc)
- [ ] Phase 5 CLI reference skeleton created with placeholders
- [ ] Phase 5 integration test catalog documented
- [ ] Phase 4 manual test / video docs cross-linked to Phase 5
- [ ] Decision log updated (`ricken-vault-phase5-docs.md`)

⏳ **Implementation track (when Phase 4 code ships):**
- [ ] Phase 5 CLI commands implemented and tested
- [ ] Phase 5 integration tests passing
- [ ] CLI reference docs populated (remove placeholders)
- [ ] Manual test runbook updated with CLI equivalents
- [ ] Video scripts updated with Phase 5 demos

---

## 7) Timeline (Planning Estimate)

**Phase 5 documentation track:** 2–3 hours (Ricken, parallel to Phase 4 implementation)  
**Phase 5 implementation track:** 2–3 weeks (pending Phase 4 completion + CLI framework choice)

---

## References

| Resource | Path |
|----------|------|
| Phase 4 architecture spec | `docs/architecture/secrets-vault-lifecycle-phase4.md` |
| Phase 4 E2E docs | `docs/testing/secrets-vault-phase4-e2e.md` |
| Phase 4 manual testing | `docs/manual-testing/secrets-vault-phase4-manual-tests.md` |
| Phase 4 video plan | `docs/testing/secrets-vault-phase4-video-plan.md` |
| Phase 4 video scripts | `docs/testing/secrets-vault-phase4-video-scripts.md` |
| Phase 1 vault design | `docs/architecture/secrets-vault-evolution.md` |
| Vault threat model | `docs/architecture/secrets-vault-threat-model.md` |

---

*Planning track document. Awaiting Phase 4 implementation review and CLI framework selection before implementation begins.*
