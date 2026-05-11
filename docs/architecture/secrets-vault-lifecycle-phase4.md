# Architecture Proposal: Secrets Vault Lifecycle (Phase 4)

| Field | Value |
|---|---|
| **Status** | DRAFT (Design-only) |
| **Date** | 2026-05-09 |
| **Owner** | Drummond (Platform Hardening / DevOps) |
| **Requested by** | Bruno Capuano |
| **Related** | `docs/architecture/secrets-vault-acl-phase2.md` (Mark), `docs/architecture/secrets-vault-admin-ui.md` |

> Scope: lifecycle semantics **only** (versioning, rotation, soft-delete/purge, audit tamper-evidence), across the existing backends (SQLite, Azure Key Vault adapter, env vars, Docker secrets). No code changes in this doc.

---

## Goals

1. Provide **stable semantics** for secret lifecycle operations across backends.
2. Add **versioning** so operators can rollback and audit what was active.
3. Make **rotation atomic** from the perspective of the Vault API.
4. Adopt a **soft-delete + purge** model aligned with Azure Key Vault.
5. Make audit trails **tamper-evident** via a hash-chain.

## Non-Goals

- Designing the Admin UI (covered by MarkΓÇÖs Phase AΓÇôC doc).
- Introducing new backends.
- Azure deployment guidance (design mentions AKV semantics only).

---

## 1) Versioning

### 1.1 Data model: `SecretVersions` table

Introduce a separate table for versions, linked to the logical secret identity (by name).

**Tables (conceptual):**

- `Secrets` (logical identity)
  - `Name` (PK)
  - `CreatedAt`, `UpdatedAt`
  - `DeletedAt` *(Phase 4, see ┬º3)*
  - `PurgeAfter` *(Phase 4, see ┬º3)*

- `SecretVersions` (versioned payload)
  - `Id` (PK)
  - `SecretName` (FK ΓåÆ `Secrets.Name`)
  - `Version` (int, monotonic per secret)
  - `EncryptedValue` (ciphertext)
  - `CreatedAt`
  - `IsCurrent` (bool)
  - `SupersededAt` *(optional; convenience for auditing/ops)*

**Constraints:**

- Unique: `(SecretName, Version)`
- Exactly one current per secret: `IsCurrent = true` unique filtered index per `SecretName`

### 1.2 Vault API surface

Add version-aware resolution while preserving the Phase 1 call pattern.

**Current Phase 1 API:**
```csharp
Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default);
```

**Phase 4 extension (ISecretsStore backend interface):**
```csharp
// Version null means "latest".
Task<string?> GetAsync(string name, int? version = null, CancellationToken ct = default);

// Optional convenience (admin / ops)
Task<IReadOnlyList<int>> ListVersionsAsync(string name, CancellationToken ct = default);
```

**Behavior:**

- `ResolveAsync(name, version: null)` (Phase 1 public API) calls `GetAsync(name, null)` on the backend, which returns the latest *current* version.
- `GetAsync(name, version: X)` (ISecretsStore backend) returns that specific version (even if not current), unless the secret is purged.
- Phase 1 `IVault.ResolveAsync` signature remains unchanged; versioning is internal to `ISecretsStore` implementations.

### 1.3 Backfill strategy (existing rows ΓåÆ Version 1)

Migration backfills all existing `Secrets` rows into `SecretVersions`:

- Insert `SecretVersions(SecretName, Version=1, IsCurrent=true, CreatedAt=<Secrets.CreatedAt>, EncryptedValue=<current encrypted payload>)`
- Keep `Secrets` as the identity row; do not change secret names.

**Open question:** ≡ƒƒó (none) ΓÇö Backfill is deterministic.

---

## 2) Rotation

### 2.1 API

Rotation should create a new version and atomically switch `IsCurrent`.

```csharp
Task RotateAsync(string name, string newValue, CancellationToken ct = default);
```

### 2.2 Atomic semantics

**SQLite backend (native):**

- Single DB transaction:
  1. Read current version (and current `Version` number).
  2. Insert new `SecretVersions` row with `Version = current + 1`, `IsCurrent = true`.
  3. Mark previous current row `IsCurrent = false`, set `SupersededAt = now`.
  4. Invalidate caches.

**Azure Key Vault backend (native-ish):**

- `SetSecret` inherently creates a new version; treat it as Rotate.
- Switching ΓÇ£currentΓÇ¥ is equivalent to "latest version".

### 2.3 Cache TTL grace

Rotation interacts with in-memory caches and in-flight tool calls.

**Proposed semantics:**

- Immediately after rotation, `ResolveAsync(name)` must return the new version.
- For a short grace window, an *in-flight* caller may continue using the previously-resolved value (because they already have it). Vault cannot revoke that.

**Implementation note (design):**

- Cache entries should be version-keyed (name+version). Rotation invalidates the ΓÇ£latestΓÇ¥ pointer.

**Open question:** ≡ƒƒí Cache grace window length.
- **Recommended default:** `2 minutes` (short enough to limit exposure, long enough to survive transient retries).

### 2.4 Operator surfaces

- CLI (Phase 4):
  - `vault-cli rotate <name>`
- Admin UI (Phase B per Mark):
  - `POST /api/vault/secrets/{name}/rotate`

**Open question:** ≡ƒƒí Should rotate accept `--from-stdin` (safer) vs. prompt input.
- **Recommended default:** support both; default to `--from-stdin` in docs.

---

## 3) Soft-delete + purge

### 3.1 Schema

Mirror Azure Key VaultΓÇÖs soft-delete model locally.

- `Secrets.DeletedAt` (nullable)
- `Secrets.PurgeAfter` (nullable)

**Deletion behavior:**

- Soft-delete sets `DeletedAt = now`, `PurgeAfter = now + retentionWindow`.
- Purge physically removes `Secrets` and all `SecretVersions` (and optionally tombstone/audit entries remain).

### 3.2 Default retention

- **Local (SQLite):** 30 days grace
- **Azure Key Vault:** AKV commonly uses 90 days default (configurable). We follow AKV semantics, but keep local shorter.

**Open question:** ≡ƒƒó Local grace default.
- **Recommended default:** 30 days (as requested), configurable via `Vault:Retention:SoftDeleteDays`.

### 3.3 API

```csharp
Task DeleteAsync(string name, CancellationToken ct = default);        // soft-delete
Task RecoverAsync(string name, CancellationToken ct = default);       // undo soft-delete
Task PurgeAsync(string name, CancellationToken ct = default);         // irreversible
```

**Resolve behavior:**

- If `DeletedAt != null`, `ResolveAsync` should behave as NotFound for non-admin callers.

**Open question:** ≡ƒƒí Whether admins can resolve deleted secrets for incident response.
- **Recommended default:** NO (treat deleted as not found); require explicit `RecoverAsync` first.

### 3.4 Azure Key Vault mapping

When using the AKV backend:

- Soft-delete: map to `BeginDeleteSecret(name)`
- Purge: map to `PurgeDeletedSecret(name)`
- Recover: map to `RecoverDeletedSecret(name)`

**Open question:** ≡ƒƒí Purge permission surface.
- **Recommended default:** purge requires explicit admin-only role in UI + CLI confirmation (`--force`).

---

## 4) Audit hash-chain (tamper-evidence)

Phase 1 audit rows are append-only but not tamper-evident. Phase 4 adds a hash-chain so any deletion/reordering/editing of rows is detectable.

### 4.1 Schema additions

Add to `SecretAccessAudit`:

- `PreviousRowHash` (string)
- `RowHash` (string)

**Note:** Audit rows include all operations regardless of source (Tool, Configuration, Cli, System). The `CallerType` enum maps semantically: Configuration = URI resolver, System = background/admin operations, Tool = tool execution, Cli = CLI commands.

### 4.2 Hash algorithm

For each audit row, compute:

```
RowHash = SHA256( prev || timestamp || callerId || secretName || outcome )
```

- `prev` = previous rowΓÇÖs `RowHash`
- `timestamp` = UTC timestamp (canonical ISO-8601 string)
- `callerId` = normalized caller identity
- `secretName` = normalized secret name
- `outcome` = normalized outcome string
- Genesis row uses `prev = 64x"0"` (zero hash).

**Canonicalization (recommended):**

- UTF-8 bytes of: `${prev}|${timestamp:o}|${callerId}|${secretName}|${outcome}`
- Store `RowHash`/`PreviousRowHash` as lowercase hex.

**Open question:** ≡ƒƒí Should the chain include `Operation` and/or `ToolId/AgentId` explicitly.
- **Recommended default:** include them by embedding into `callerId` and/or `outcome` initially (no schema churn), then add explicit columns later if needed.

### 4.3 Verification CLI

Add CLI verifier:

- `vault-cli audit-verify`
  - Recomputes chain in chronological order.
  - Fails if any row hash mismatch or chain break is detected.

**Open question:** ≡ƒƒó Ordering key for verification.
- **Recommended default:** order by `TimestampUtc, Id` to ensure deterministic ordering.

---

## 5) Cross-backend semantics

Not all backends can support lifecycle features. We define ΓÇ£acceptable degradationΓÇ¥ explicitly.

Legend:
- **Native**: backend supports the operation as a first-class feature.
- **Emulated**: vault simulates behavior (often by layering metadata locally).
- **Not supported**: operation fails with a clear exception/message; operators must use backend-native controls.

| Operation | SQLite (DP + EF) | Azure Key Vault | Env vars | Docker secrets |
|---|---|---|---|---|
| Set/Create (latest) | Native | Native (`SetSecret`) | Not supported | Not supported |
| Resolve latest | Native | Native | Native | Native |
| Resolve specific version | Native | Native (AKV version ID) | Not supported | Not supported |
| List versions | Native | Native | Not supported | Not supported |
| Rotate (new version) | Native | Native (`SetSecret` creates new version) | Not supported | Not supported |
| Soft-delete | Native | Native (`BeginDeleteSecret`) | Not supported | Not supported |
| Recover deleted | Native | Native (`RecoverDeletedSecret`) | Not supported | Not supported |
| Purge | Native | Native (`PurgeDeletedSecret`) | Not supported | Not supported |
| Audit hash-chain | Native | Emulated* | Emulated* | Emulated* |

\*Emulated audit hash-chain means the **Vault** writes audit rows to the local audit store even if the secret value lives elsewhere. This is acceptable because audit integrity is a local control; it does not require backend participation.

**Open question:** ≡ƒƒí Should AKV backend rely on AKVΓÇÖs own versioning/soft-delete exclusively or also mirror metadata locally.
- **Recommended default:** rely on AKV as source of truth; store only audit locally.

---

## 6) Migration plan

### 6.1 EF migration

Create an EF migration:

- `20260509_VaultLifecyclePhase4`

Schema changes:

1. Add `SecretVersions` table + indexes/constraints.
2. Add `DeletedAt`, `PurgeAfter` to `Secrets`.
3. Add `PreviousRowHash`, `RowHash` to `SecretAccessAudit`.

### 6.2 Data backfill

1. **Backfill SecretVersions**
   - For each existing secret: create `Version=1`, `IsCurrent=true`.

2. **Bootstrap audit chain**
   - Iterate existing `SecretAccessAudit` rows in chronological order (`TimestampUtc, Id`).
   - Set `PreviousRowHash` and compute/store `RowHash`.

**Open question:** ≡ƒƒí What to do if legacy audit rows have null/empty fields used by the hash.
- **Recommended default:** normalize nulls to empty strings during hashing; log migration warnings.

---

## 7) Test strategy

### 7.1 Unit tests

- **Rotation atomicity**: concurrent resolves + rotate ΓåÆ resolves after rotate return new version; no split-brain ΓÇ£two currentΓÇ¥ rows.
- **Soft-delete recovery**: delete ΓåÆ resolve fails (NotFound semantics) ΓåÆ recover ΓåÆ resolve succeeds.
- **Hash-chain tamper detection**: modify any stored audit row field ΓåÆ `audit verify` fails.

### 7.2 Integration tests

- **Azure mapping** (AKV adapter):
  - Delete Γåö `BeginDeleteSecret`
  - Recover Γåö `RecoverDeletedSecret`
  - Purge Γåö `PurgeDeletedSecret`
  - Version resolve Γåö AKV version identifier behavior

### 7.3 CLI smoke tests

- `vault-cli rotate <name>` produces new version and updates the latest value.
- `vault-cli audit-verify` passes on clean store and fails on simulated corruption fixture.

**Open question:** ≡ƒƒó Where CLI integration tests should run (local-only vs CI).
- **Recommended default:** run smoke tests in CI for SQLite; AKV tests remain opt-in/integration pipeline.

---

## 8) Ops runbooks (short)

### 8.1 Rotate a credential

1. `vault-cli rotate <name>`
2. Confirm versions with `vault-cli list-versions <name>` and automated store validation.
3. Watch logs for rotation + audit rows.
4. If dependent tool fails, rollback by setting previous version current (admin-only operation; may require manual in Phase 4).
5. Run `vault-cli audit-verify` after change window.

### 8.2 Recover a deleted secret

1. Confirm secret is soft-deleted (admin list deleted, Phase B UI).
2. `vault-cli recover <name>`
3. Validate with `vault-cli list-versions <name>` and automated store validation.
4. Confirm audit contains recover event/outcome.
5. If secret was already purged, recovery is impossible; re-create/rotate.

### 8.3 Verify the audit chain

1. `vault-cli audit-verify`
2. If failure, identify first broken row index in CLI output.
3. Treat as security incident: stop admin UI changes to vault.
4. Restore DB from last known-good backup.
5. Re-run verify; retain the corrupted DB copy for forensics.

---

## 9) Coordination with MarkΓÇÖs ACL Phase 2

MarkΓÇÖs Phase 2 ACL proposal introduces DENY/GRANT semantics and new outcomes (e.g., `AccessDenied`, `ApprovalDenied`). Phase 4 hash-chaining should cover **all** audit rows, including ACL outcomes.

### 9.1 Overlap

- ACL enforcement occurs in `VaultService.ResolveAsync` before store access.
- ACL denial must be recorded as an audit row (already proposed in Phase 2).
- Phase 4 extends the audit table with hash-chain fields that will make ACL denials tamper-evident.

### 9.2 Recommendation

Have MarkΓÇÖs `IVaultAclProvider` feed additional reason codes into auditing.

- `IVaultAclProvider` returns a decision with reason codes.
- `ISecretAccessAuditor` stores a normalized reason string (or metadata) used in the audit outcome.

**Suggested reason codes:**

- `acl_deny` (no matching grant)
- `acl_grant_added` (admin created grant)
- `acl_grant_removed` (admin removed grant)
- `acl_approval_required`
- `acl_approval_denied`

**Open question:** ≡ƒƒí Where to record ACL grant changes (audit table vs separate admin events table).
- **Recommended default:** record grant changes into `SecretAccessAudit` initially (as admin-originated outcomes), then split later if the table becomes semantically overloaded.

---

## Summary

Phase 4 adds lifecycle correctness (versioning + rotation), recoverability (soft-delete), and forensics (audit hash-chain) while keeping cross-backend behavior explicit. SQLite becomes the "full fidelity" backend; AKV maps cleanly; env/docker remain resolve-only.

---

## Ratification Notes (2026-05-08)

**Mark (Lead Architect) verified against main:**

- ✅ Phase 1 shipped successfully; Phase 4 extends non-breaking via schema additions
- ✅ IVault interface (`ResolveAsync`) remains unchanged; versioning lives in ISecretsStore backend only
- ✅ VaultCallerContext enum uses {Tool, Configuration, Cli, System}; Phase 4 narrative updated to reflect current names
- ✅ All open questions have recommended defaults locked in implementation contract (.squad/decisions/inbox/mark-vault-phase4-contract.md)

This document is now the authoritative Phase 4 spec. Implementation begins per Irving/Dylan/Drummond handoff contract.
