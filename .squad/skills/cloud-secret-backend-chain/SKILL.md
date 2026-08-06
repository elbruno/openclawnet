---
name: cloud-secret-backend-chain
description: "Chain-of-responsibility pattern for multi-source secret resolution across local, container, and cloud backends."
category: architecture
tags: [secrets, azure, docker, chain-of-responsibility, adapter]
examples:
  - "Add a new secret backend (HashiCorp Vault, AWS Secrets Manager)"
  - "Configure fallback order for multi-environment secret resolution"
  - "Wire environment-variable secrets for Docker deployments"
enabled: true
---

# Cloud Secret Backend Chain Pattern

Use this pattern when secrets must be resolved from multiple backends (local DB, env vars, cloud KV) with environment-driven selection and graceful fallback.

## Components

1. **Single backend interface**: Reuse the existing storage CRUD interface (e.g., `ISecretsStore`). Each backend implements the same 4-method contract.
2. **ChainedStore**: Accepts an ordered list of backend implementations. `GetAsync` returns the first non-null value. `SetAsync`/`DeleteAsync` delegate to the first writable backend.
3. **Configuration-driven chain**: `Vault:Backends` selects the ordered list (for example `["AzureKeyVault","Environment","Sqlite"]`).
4. **Environment adapter**: `EnvironmentSecretsStore` reads `OPENCLAWNET_SECRET_<UPPER_SNAKE>` first, then `/run/secrets/<lowercased-name>`.
5. **Cloud adapter**: `AzureKeyVaultSecretsStore` maps dots/underscores to dashes and rejects invalid characters.

## Guardrails

- Read-only backends throw `NotSupportedException` on `Set`/`Delete`.
- The facade layer (`IVault` / `VaultService`) handles audit, redaction, and error shielding — backends stay storage-only.
- Bootstrap infrastructure secrets (Key Vault URI, MI client ID) live in env vars or appsettings, NOT in the chain.
- Azure backends live in a separate project (`OpenClawNet.Storage.Azure`) to isolate SDK dependencies from local/Docker builds.
