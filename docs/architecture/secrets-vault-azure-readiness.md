# Architecture Proposal: Secrets Vault Azure Readiness (Phase 3)

| Field         | Value                                                     |
|---------------|-----------------------------------------------------------|
| **Status**    | Implemented                                               |
| **Date**      | 2026-05-07                                                |
| **Owner**     | Mark (Lead / Architect)                                   |
| **Reviewers** | Drummond (Security), Irving (Backend Impl), Bruno (Coord) |
| **Depends on**| `secrets-vault-evolution.md` (Phase 1 — shipped)          |
| **Companion** | `secrets-vault-threat-model.md` (Drummond — binding)      |

---

## 1. Context

Phase 1 shipped a working vault: `ISecretsStore` (SQLite + DataProtection), `IVault` facade with audit, `vault://` configuration resolver, redactor, and error shield. Phase 3 extends the vault to Docker and Azure without changing tool code.

### Target Environments

| # | Environment | Runtime | Characteristics |
|---|-------------|---------|-----------------|
| 1 | **Local dev** | `dotnet run` / Aspire | SQLite on disk, DataProtection key ring on disk, single instance |
| 2 | **Docker** | Container | Ephemeral filesystem, secrets via env vars or `/run/secrets/*`, key ring must survive restarts |
| 3 | **Azure** | Container Apps / App Service / AKS | Azure Key Vault source of truth, Managed Identity auth, multi-replica possible |

---

## 2. Goals (Phase 3 Scope)

1. **Zero tool/agent code changes** — `IVault.ResolveAsync` stays the only API surface.
2. **Environment-driven backend selection** — `appsettings.{env}.json` or env vars choose the backend.
3. **Docker unblocked** — env-var + Docker-secrets backends ship first.
4. **Azure Key Vault adapter** — read-path with DefaultAzureCredential.
5. **DataProtection portability** — key ring survives container restarts and works across replicas.

---

## 3. Non-Goals (Deferred)

| Phase | Scope |
|-------|-------|
| 2     | Per-tool ACL / approval-on-first-use UX |
| 4     | Rotation, expiry, hash-chain audit, write-back to Key Vault |

---

## 4. Design Decisions

### A. Adapter Pattern — Keep `ISecretsStore` as the Single Abstraction

**Decision:** ship multiple `ISecretsStore` implementations, no new backend interface.

| Implementation | Backend | Supports Write? | Package |
|----------------|---------|-----------------|---------|
| `SecretsStore` (existing) | SQLite + DataProtection | Yes | OpenClawNet.Storage |
| `EnvironmentSecretsStore` | Env vars + `/run/secrets/*` files | Read-only | OpenClawNet.Storage |
| `AzureKeyVaultSecretsStore` | Azure Key Vault SDK | Read-only (Phase 3) | OpenClawNet.Storage.Azure |
| `ChainedSecretsStore` | Delegates to ordered list of `ISecretsStore` | Delegates writes to first writable | OpenClawNet.Storage |

### B. Resolution Chain / Fallback Order

**Decision:** chain-of-responsibility, first non-null wins. Default order is configurable via `Vault:Backends`:

```
1. Azure Key Vault   (if configured)
2. Environment vars  (always available)
3. SQLite local      (always available)
```

Writes (`Set`/`Delete`) go to the first writable backend (SQLite).

### C. Configuration Shape

**Local dev (`appsettings.Development.json`)**

```json
{
  "Vault": { "Backends": [ "Sqlite" ] }
}
```

**Docker (`appsettings.Docker.json`)**

```json
{
  "Vault": {
    "Backends": [ "Environment", "Sqlite" ],
    "Environment": {
      "Prefix": "OPENCLAWNET_SECRET_",
      "DockerSecretsPath": "/run/secrets"
    }
  }
}
```

**Azure (`appsettings.Production.json`)**

```json
{
  "Vault": { "Backends": [ "AzureKeyVault", "Environment", "Sqlite" ] },
  "Storage": {
    "Azure": {
      "KeyVault": { "Uri": "https://openclawnet-prod.vault.azure.net/", "CacheTtlMinutes": 15 },
      "DataProtection": {
        "BlobUri": "https://account.blob.core.windows.net/",
        "Container": "dataprotection",
        "BlobName": "keys.xml",
        "KeyVaultKeyUri": "https://openclawnet-prod.vault.azure.net/keys/dataprotection-key"
      }
    }
  }
}
```

### D. Authentication (Azure Key Vault)

**Decision:** `DefaultAzureCredential` only. Managed Identity in Azure, `az login` in dev.

### E. DataProtection Key Ring Portability

**Azure:** `PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault`, reusing the existing DataProtection purpose string (`OpenClawNet.Secrets.v1`).

### F. Audit Destination

**Decision:** keep local SQLite audit per instance. Ship structured events to App Insights via `TrackEvent("VaultSecretAccess")`. Never include secret values.

---

## 5. Implementation Notes

1. `EnvironmentSecretsStore` honors `OPENCLAWNET_SECRET_<UPPER_SNAKE>` env vars first, then `/run/secrets/<lowercased-name>` files.
2. `ChainedSecretsStore` dedupes list entries by name with first-wins semantics.
3. Azure Key Vault name mapping replaces `.` and `_` with `-` and rejects invalid characters.
4. App Insights audit sink decorates `ISecretAccessAuditor` and only emits metadata fields.
5. Azure DataProtection wiring uses Blob + Key Vault key wrapping and keeps `OpenClawNet.Secrets.v1` unchanged.
