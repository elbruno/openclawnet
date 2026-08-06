---
name: azure-dataprotection-wiring
description: "Persist ASP.NET Core DataProtection keys to Azure Blob Storage and wrap with Azure Key Vault."
category: security
tags: [azure, dataprotection, key-vault, blob-storage]
examples:
  - "Share DataProtection key ring across replicas"
  - "Configure Blob + Key Vault key wrapping for production"
enabled: true
---

# Azure DataProtection Wiring Pattern

Use this pattern when OpenClawNet needs a shared DataProtection key ring in Azure.

## Steps

1. Bind `Storage:Azure:DataProtection` configuration:
   - `BlobUri`
   - `Container`
   - `BlobName`
   - `KeyVaultKeyUri`
2. Call `services.AddDataProtection()`
3. Configure:
   - `PersistKeysToAzureBlobStorage(blobUri, DefaultAzureCredential)`
   - `ProtectKeysWithAzureKeyVault(keyUri, DefaultAzureCredential)`

## Guardrails

- Reuse existing DataProtection purpose string (`OpenClawNet.Secrets.v1`) in the secrets store.
- The Key Vault key used for wrapping is infrastructure config — never a vault-managed secret.
