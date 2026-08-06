---
name: secrets-vault-pattern
description: "Implement vault:// configuration resolution with audited IVault access and LLM-safe masking."
category: security
tags: [secrets, vault, audit, configuration, redaction]
examples:
  - "Add vault:// support for a new tool credential"
  - "Wrap secret reads with audit logging and generic error masking"
  - "Prevent resolved secret values from entering tool output or LLM context"
enabled: true
---

# Secrets Vault Pattern

Use this pattern when credentials need to move from appsettings/user-secrets into OpenClawNet's encrypted vault while preserving existing `IOptions<T>` consumers.

## Components

1. **Facade**: tools and configuration resolvers call `IVault.ResolveAsync(name, VaultCallerContext, ct)`, not `ISecretsStore.GetAsync` directly.
2. **Audit**: every resolve attempt writes `SecretAccessAudit` with secret name, caller type, caller id, optional session id, timestamp, and success/failure. Never store plaintext.
3. **Tamper-evidence**: audit rows are hash-chained with `PreviousRowHash` and `RowHash`; canonical input includes prior hash, UTC timestamp, caller type/id/session, secret name, and outcome, never the secret value.
4. **Configuration overlay**: after normal configuration loads and the database is migrated, enumerate configuration values; replace any `vault://Name` reference by resolving `Name` through `IVault`; add the result through an in-memory `IConfigurationManager` overlay so option binding remains unchanged.
5. **Cache**: cache configuration resolutions for five minutes, keyed by secret name. Invalidate on `ISecretsStore.SetAsync` and `DeleteAsync`.
6. **Error shield**: tool code catches `VaultException` via `IVaultErrorShield` and returns exactly `required configuration unavailable` to LLM-visible paths.
7. **Redaction**: `IVault` registers successfully resolved plaintext with `IVaultSecretRedactor`; tool result sanitizers, test endpoints, logs, and any LLM-bound payload must redact tracked values before emitting text.

## Admin UI Surface

When building the vault admin UI (`docs/architecture/secrets-vault-admin-ui.md`):

- Admin pages live at `src/OpenClawNet.Web/Components/Pages/Vault/` and call Gateway REST endpoints at `/api/vault/` via `HttpClient("gateway")`. They do **not** inject `ISecretsStore` directly.
- Admin endpoints use a config-based auth filter (`Vault:Admins[]`). They are never registered in MCP tool manifests or agent-callable surfaces.
- Reveal operations (`?reveal=true`) are rate-limited (5/min), audit-logged with `Action=Reveal`, and auto-hidden after 30 seconds.
- Admin writes delegate to `ISecretsStore.SetAsync` / `DeleteAsync`, which already call `IVaultCacheInvalidator.Invalidate()` — no extra cache-flush wiring needed.
- Admin audit rows use `CallerType.System` with `CallerId = "VaultAdminUI:{userId}:{action}"`.

## Guardrails

- Reuse the `OpenClawNet.Secrets.v1` DataProtection purpose for the `Secrets` table.
- Log only secret name, caller type/id, and success/failure.
- Audit hash-chain verification must order rows by `AccessedAt, Id`; treat SQLite `DateTimeKind.Unspecified` as UTC when recomputing hashes.
- Do not expose `SecretAccessAudit` through MCP tools, agent-callable endpoints, or chat commands.
- Migration/import paths write through `ISecretsStore` and add CLI audit rows with `CallerType='Cli'`.

## Phase 4 Lifecycle Extension

When implementing vault lifecycle work:

- Keep `ISecretsStore.ListAsync` and version-list endpoints metadata-only; do not introduce plaintext-returning HTTP APIs unless the admin-auth/rate-limit/reveal pattern already exists.
- Store current secret material in `SecretVersions`; `Secrets` remains the logical identity/tombstone row. Rotation creates a new version and flips the previous current version inside one transaction where the provider supports it.
- Soft delete means `DeletedAt` + `PurgeAfter`; normal resolve/list behavior treats deleted secrets as missing until `RecoverAsync` clears the tombstone. `PurgeAsync` is the only physical delete path.
- Hash-chain verification should order audit rows by `AccessedAt, Id`, recompute canonical SHA-256 rows, and fail closed on the first mismatch or previous-hash break.
- Map Azure Key Vault only through the existing adapter seam: `SetSecret` = new version/rotate, `StartDeleteSecret` = soft delete, `RecoverDeletedSecret` = recover, `PurgeDeletedSecret` = irreversible purge.

### Concurrency Safety Pattern

When implementing mutations that must enforce single-current or sequential-version constraints:

**Problem:** Per-instance locks (`SemaphoreSlim` fields) only protect within a single service instance. Under Gateway load with multiple `ISecretsStore` instances, concurrent rotations can create split-current or version number collisions.

**Solution:** Use process-wide per-resource locks via `static ConcurrentDictionary<string, SemaphoreSlim>`:

```csharp
private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerSecretLocks = new();

public async Task RotateAsync(string name, ...)
{
    var secretLock = PerSecretLocks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
    await secretLock.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        // ... load current versions, increment, flip IsCurrent, save ...
    }
    finally
    {
        secretLock.Release();
    }
}
```

**Why this works:**
- Static dictionary is shared across all instances within the same process
- Per-resource granularity (keyed by secret name) avoids global bottleneck
- Existing database filtered unique index `(SecretName) WHERE IsCurrent = 1` provides defense-in-depth

**When to use:**
- Single-process scenarios (ASP.NET Core Gateway with DI-resolved services)
- Mutations requiring atomic read-modify-write on shared state (version counters, current flags)

**When NOT sufficient:**
- Multi-process deployments (use Redis distributed locks or database-level row locks)
- Cross-machine coordination (requires external lock coordinator)

**Validation:** E2E test with 10+ concurrent mutations should produce sequential results with exactly one "current" entity.


## E2E Testing Pattern

When adding E2E tests for vault operations (see `tests/OpenClawNet.E2ETests/SecretsVaultPhase4E2ETests.cs`):

### Test Structure
- Use existing `GatewayE2EFactory` (in-memory DB, isolated storage root)
- Tag tests: `[Trait("Category", "Vault")]` + `[Trait("Layer", "E2E")]`
- Name secret keys without slashes (e.g., `E2EToken`, not `E2E/Token`) — ASP.NET Core minimal API `{name}` route parameter doesn't decode `/` by default

### Validation Pattern
- **Mutations:** HTTP calls through `HttpClient` (`PUT`, `POST`, `DELETE`)
- **Assertions:** Direct DI access via `ISecretsStore` or `IDbContextFactory<OpenClawDbContext>`
- **Rationale:** Gateway never exposes plaintext GET by design; validate resolved values through service layer

### Coverage Areas
1. **Full lifecycle:** Create → rotate → list versions → resolve latest/explicit versions
2. **Soft-delete semantics:** Delete makes resolution fail → recover restores → purge removes permanently (verify DB state)
3. **Audit hash-chain:** Verify chain succeeds → tamper detection works (flip audit row field, re-verify)
4. **Cache invalidation:** Rotate/delete invalidates cache (concurrent reads before/after)
5. **Edge cases:** Rotate non-existent secret (fallback to SetAsync), rotate soft-deleted (400 BadRequest)
6. **Concurrency:** Validate single current version constraint (note: sequential version numbering under concurrent load requires DB-level unique constraint)

### Gateway Endpoint Exception Handling
Wrap `ISecretsStore` calls in try-catch, translate domain exceptions:
```csharp
try {
    await store.RotateAsync(name, newValue, ct);
    return Results.NoContent();
} catch (InvalidOperationException ex) {
    return Results.BadRequest(new { error = ex.Message });
}
```

### Known Limitations
- In-memory DB may exhibit different concurrency behavior than SQLite on disk
- Audit recording depends on `VaultService` invocation path; direct `ISecretsStore` calls may not generate audit rows
- Tests should gracefully handle missing audit rows (log warning, don't fail)
