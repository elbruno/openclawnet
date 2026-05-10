# Architecture Proposal: Secrets Vault Admin UI

| Field        | Value                                         |
|--------------|-----------------------------------------------|
| **Status**   | Proposed                                      |
| **Date**     | 2026-05-07                                    |
| **Owner**    | Mark (Lead / Architect)                       |
| **Reviewers**| Drummond (Security), Helly (Frontend), Irving (Backend) |

---

## Decisions Needed from Bruno

Before implementation begins, the following require a binary yes/no:

1. **REST vs. direct?** Should the Blazor UI call Gateway REST endpoints (recommended) or inject `ISecretsStore` directly into the Web project? _(Recommendation: REST — see §2.2.)_
2. **Admin identity source?** Use a config array `Vault:Admins[]` for Phase A, or require SSO integration from day one? _(Recommendation: config array now, SSO later.)_
3. **Feature flag scope?** Is `Features:VaultAdminUi` sufficient as a single toggle, or do we need per-phase flags (`VaultAdminUi:List`, `VaultAdminUi:Reveal`)? _(Recommendation: single flag.)_
4. **Reveal flow?** Should "Reveal" require a re-authentication challenge (password/PIN), or is a confirmation modal sufficient for a single-user local deployment? _(Recommendation: modal + audit for now; re-auth in Phase C.)_
5. **Audit retention?** Should vault audit rows auto-purge after N days, or retain indefinitely? _(Recommendation: retain indefinitely; operator can truncate manually.)_

---

## 1. Goals & Non-Goals

### Goals

- Web UI for an admin to CRUD vault entries: **list**, **view metadata**, **create**, **update/rotate**, **delete**.
- **Reveal** a secret value on demand with confirmation, auto-hide, and audit trail.
- **Audit viewer** for ad-hoc inspection of vault access history (who accessed what, when).
- All operations produce audit log entries in the existing `SecretAccessAudit` table.

### Non-Goals

- Agents do **not** use this UI. They continue consuming `IVault` / `ISecretsStore` programmatically. This UI is invisible to the chat surface.
- No multi-tenant support. Single admin role.
- No role hierarchy beyond "admin" — there is no "viewer" or "rotator" sub-role.
- No external secret provider UI (Azure Key Vault browser) — Phase 3 backends are listed read-only via chips.

---

## 2. Architecture & Placement

### 2.1 Blazor Pages

New pages under `src/OpenClawNet.Web/Components/Pages/Vault/`:

| Page           | Route              | Purpose                              |
|----------------|--------------------|--------------------------------------|
| `Index.razor`  | `/vault`           | List all secrets (metadata only)     |
| `Edit.razor`   | `/vault/edit/{name?}` | Create or update a secret         |
| `Audit.razor`  | `/vault/audit`     | Query vault access audit log         |

All pages use `@rendermode InteractiveServer` (consistent with `Settings.razor`, `Skills.razor`, etc.) and MudBlazor `MudDataGrid` for tables (consistent with the MudBlazor migration already in progress).

### 2.2 Gateway REST Endpoints (Recommended)

**Decision: UI talks to Gateway via REST, not `ISecretsStore` directly.**

Rationale:
- The Web project already uses named `HttpClient("gateway")` for all data access (Settings, UserFolders, Skills). Direct DI of storage services would break this pattern and create a second data-access path.
- Gateway owns the `ISecretsStore` registration, DataProtection keying, and audit wiring. Duplicating that in Web would violate the single-responsibility boundary.
- REST endpoints enable future scenarios: CLI admin tools, external dashboards, mobile admin.
- The existing `SecretsEndpoints.cs` already provides `GET /api/secrets`, `PUT /api/secrets/{name}`, `DELETE /api/secrets/{name}`. We extend — not replace — this surface.

**New file:** `src/OpenClawNet.Gateway/Endpoints/VaultAdminEndpoints.cs`

The existing `SecretsEndpoints` remain for backward compatibility. `VaultAdminEndpoints` adds the admin-specific surface under `/api/vault/`:

```
GET    /api/vault/secrets                        → List (names + metadata, NEVER values)
GET    /api/vault/secrets/{name}                 → Single secret metadata
GET    /api/vault/secrets/{name}?reveal=true     → Reveal value (audit-logged, admin-only)
POST   /api/vault/secrets                        → Create new secret
PUT    /api/vault/secrets/{name}                 → Update / rotate value
DELETE /api/vault/secrets/{name}                 → Delete secret
GET    /api/vault/audit?secret=...&from=...&to=... → Query audit log
```

**Why a separate endpoint group?** The existing `/api/secrets` is used by internal callers (settings page, CLI). The `/api/vault/` surface adds admin-only authorization, reveal semantics, and audit-query capabilities that should not pollute the internal API.

### 2.3 Web Client

New typed client in `src/OpenClawNet.Web/Services/VaultAdminClient.cs`, registered as:

```csharp
builder.Services.AddScoped<VaultAdminClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new VaultAdminClient(factory.CreateClient("gateway"));
});
```

Follows the `SkillsClient` / `UserFolderClient` pattern exactly.

---

## 3. Security Gates

Drummond's 9 acceptance gates from `secrets-vault-threat-model.md` remain binding. This section maps each gate to the admin UI surface.

### Gate 2 — LLM Redaction

Vault values must **never** be sent to any LLM.

- The admin UI lives in Blazor Server pages with `@rendermode InteractiveServer`. It is rendered server-side over a SignalR circuit — there is no REST→LLM pipeline.
- The `/api/vault/secrets/{name}?reveal=true` endpoint returns plaintext **only** to the admin HTTP caller. No chat endpoint, MCP tool, or agent-callable path references this endpoint.
- `IVaultSecretRedactor.TrackResolvedValue()` is called when a reveal occurs, ensuring that if the value somehow enters a log or tool result, it is masked.

### Gate 3 + Gate 5 — Audit Isolation

- The `/api/vault/audit` endpoint is admin-only (see AuthZ below). It is **not** registered in any MCP tool manifest or agent-callable surface.
- Reflection test: the existing test that confirms no agent-callable endpoint exposes audit data must be updated to also exclude `/api/vault/audit` (it's admin-gated, not agent-gated).
- Audit rows produced by admin actions use `CallerType = VaultCallerType.System` with `CallerId = "VaultAdminUI:{userId}"` to distinguish admin operations from tool/agent access.

### AuthZ — Admin Determination

**Phase A approach:** Configuration-based admin list.

```json
// appsettings.json
{
  "Vault": {
    "Admins": ["local-admin"]
  }
}
```

For single-user local deployment (the current OpenClawNet model), a single entry suffices. The admin check is a middleware/filter on the `/api/vault/` endpoint group:

```csharp
group.AddEndpointFilter(async (ctx, next) =>
{
    var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
    var admins = config.GetSection("Vault:Admins").Get<string[]>() ?? [];
    var userId = ctx.HttpContext.User?.Identity?.Name ?? "local-admin";
    if (!admins.Contains(userId, StringComparer.OrdinalIgnoreCase))
        return Results.Forbid();
    return await next(ctx);
});
```

**Future (Phase C):** Replace with ASP.NET Core `[Authorize(Policy = "VaultAdmin")]` backed by an SSO/role provider.

### Reveal-Value Flow

```
┌─────────┐     click "Show"     ┌──────────────────┐
│ List    │ ──────────────────► │ Confirmation     │
│ (Index) │                     │ Modal            │
└─────────┘                     │ "Reveal secret   │
                                │  {name}?"        │
                                │ [Cancel] [Reveal]│
                                └────────┬─────────┘
                                         │ Confirm
                                         ▼
                        ┌────────────────────────────┐
                        │ GET /api/vault/secrets/{name}│
                        │     ?reveal=true             │
                        │ → Audit row: Action=Reveal   │
                        └────────────┬─────────────────┘
                                     │
                                     ▼
                        ┌────────────────────────┐
                        │ Value shown in masked   │
                        │ <input type="password"> │
                        │ Auto-hide after 30s     │
                        │ Copy icon → also logs   │
                        │ Action=Reveal:Copy      │
                        └────────────────────────┘
```

- Clicking "Show" opens a Bootstrap modal (consistent with existing delete confirmations).
- Confirming calls `GET ?reveal=true`. The endpoint writes an audit row with `Action=Reveal`.
- Value is displayed in a `<input type="password">` with a toggle icon. A `Timer` auto-clears the field after 30 seconds.
- "Copy to clipboard" calls `navigator.clipboard.writeText()` via JS interop and logs a second audit row tagged `Reveal:Copy`.

### CSRF / Antiforgery

- Blazor Server SSR antiforgery is already enabled (`app.UseAntiforgery()` in `Program.cs:75`).
- POST/PUT/DELETE on the Gateway side are protected by the same `HttpClient` pipeline. Since Gateway is a backend API (no cookies), CSRF is mitigated by the architecture (bearer-token or service-to-service trust). No additional antiforgery tokens needed on the API side.

### Rate Limiting

- `GET /api/vault/secrets` (list): 30 req/min per caller.
- `GET /api/vault/secrets/{name}?reveal=true`: **5 req/min** per caller (aggressive — reveals are rare).
- All other CRUD: 30 req/min per caller.

Implemented via ASP.NET Core `RateLimiterOptions` with a fixed-window policy, keyed by caller identity.

---

## 4. Backend Awareness (Phase 3 Chained Store)

### 4.1 BackendName in Metadata

Extend `SecretSummary` with an optional `BackendName` property:

```csharp
public sealed record SecretSummary(
    string Name,
    string? Description,
    DateTime UpdatedAt,
    string BackendName = "sqlite");  // "sqlite", "env", "azure-keyvault"
```

The UI renders this as a chip/badge per row:

```
┌──────────────────┬───────────┬────────────┬──────────────┬──────────┐
│ Name             │ Backend   │ Created    │ Last Rotated │ Actions  │
├──────────────────┼───────────┼────────────┼──────────────┼──────────┤
│ Google:ClientId  │ 🟢 sqlite │ 2026-05-01 │ 2026-05-06   │ ✏️ 🗑️ 👁️ │
│ OPENAI_API_KEY   │ 🔵 env    │ —          │ —            │ 👁️      │
│ DB:ConnString    │ 🟣 akv    │ 2026-04-20 │ 2026-05-01   │ 👁️      │
└──────────────────┴───────────┴────────────┴──────────────┴──────────┘
```

### 4.2 Read-Only Backends

Backends that are read-only (e.g., `env` variables, Azure Key Vault when write is not configured) disable Edit and Delete buttons. Tooltip: _"This secret is managed by {backend} and cannot be modified from this UI."_

The `SecretSummary` gains a `bool IsWritable` property. The chained store implementation sets this based on the backend that owns the secret.

### 4.3 Cache Invalidation

After `Set` or `Delete` via the admin UI, the endpoint calls `IVaultCacheInvalidator.Invalidate(name)` — which is already wired through `SecretsStore` (see `SecretsStore.cs:80,94`). The `VaultConfigurationResolver` cache is flushed for that key.

No additional work needed — the existing `IVaultCacheInvalidator` pipeline handles this. The admin endpoint delegates to `ISecretsStore.SetAsync` / `DeleteAsync`, which already call `Invalidate()`.

---

## 5. UX Wireframes

### 5.1 List Page (`/vault`)

```
┌─────────────────────────────────────────────────────────────────────┐
│ 🔐 Vault Secrets                                    [+ New Secret] │
├─────────────────────────────────────────────────────────────────────┤
│ 🔍 Filter: [_______________]                                       │
├──────────────────┬──────────┬────────────┬──────────────┬──────────┤
│ Name             │ Backend  │ Updated    │ Description  │ Actions  │
├──────────────────┼──────────┼────────────┼──────────────┼──────────┤
│ Google:ClientId  │ sqlite   │ 2026-05-06 │ OAuth client │ ✏️ 🗑️ 👁️ │
│ Google:Secret    │ sqlite   │ 2026-05-06 │ OAuth secret │ ✏️ 🗑️ 👁️ │
│ OPENAI_API_KEY   │ env      │ —          │ —            │ 👁️      │
│ GitHub:PAT       │ sqlite   │ 2026-05-01 │ GitHub token │ ✏️ 🗑️ 👁️ │
└──────────────────┴──────────┴────────────┴──────────────┴──────────┘
│ Showing 4 secrets                              [View Audit Log →]  │
└─────────────────────────────────────────────────────────────────────┘
```

- Client-side text filter on Name/Description (< 200 secrets expected).
- `✏️` Edit → navigates to `/vault/edit/{name}`.
- `🗑️` Delete → opens confirmation modal.
- `👁️` Reveal → opens reveal modal (see §3).
- Read-only backends: ✏️ and 🗑️ are disabled with tooltip.

### 5.2 Edit Page (`/vault/edit/{name?}`)

```
┌─────────────────────────────────────────────────────────────────────┐
│ 🔐 Create Secret  /  Edit Secret: Google:ClientId                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Name:        [Google:ClientId    ]  (locked on edit)               │
│                                                                     │
│  Value:       [••••••••••••••] 👁️   (password input, toggle show)   │
│                                                                     │
│  Description: [OAuth client ID for Google Workspace___]             │
│                                                                     │
│  Backend:     [sqlite ▼]  (only on create; disabled on edit)        │
│               (dropdown shows only writable backends)               │
│                                                                     │
│  [Cancel]  [Save]                                                   │
└─────────────────────────────────────────────────────────────────────┘
```

- On **create**: Name is editable, Backend selector shows writable backends (defaults to first).
- On **edit**: Name is read-only, Backend is read-only (shows current), Value is blank (user must re-enter to rotate).
- Save calls `POST /api/vault/secrets` (create) or `PUT /api/vault/secrets/{name}` (update).

### 5.3 Audit Page (`/vault/audit`)

```
┌─────────────────────────────────────────────────────────────────────┐
│ 🔐 Vault Audit Log                                                 │
├─────────────────────────────────────────────────────────────────────┤
│ Filters:                                                            │
│  Secret: [___________]  Caller: [___________]                       │
│  From:   [2026-05-01 ]  To:     [2026-05-07 ]   [Apply]            │
├──────────────────┬──────────┬────────────┬─────────┬───────────────┤
│ Secret Name      │ Caller   │ Action     │ Success │ Timestamp     │
├──────────────────┼──────────┼────────────┼─────────┼───────────────┤
│ Google:ClientId  │ AdminUI  │ Reveal     │ ✅      │ 05-07 14:32   │
│ Google:ClientId  │ Tool:GWS │ Resolve    │ ✅      │ 05-07 14:30   │
│ GitHub:PAT       │ AdminUI  │ Delete     │ ✅      │ 05-07 13:15   │
│ OPENAI_API_KEY   │ Tool:Chat│ Resolve    │ ❌      │ 05-07 12:00   │
└──────────────────┴──────────┴────────────┴─────────┴───────────────┘
│ Page 1 of 3                                    [← Prev] [Next →]   │
└─────────────────────────────────────────────────────────────────────┘
```

- Server-side filtering and pagination (audit tables can grow large).
- Pagination follows the `AuditEndpoints.cs` pattern: `limit`, `offset`, date range.

### 5.4 Confirmation Modals

**Delete:**
```
┌───────────────────────────────────┐
│ ⚠️ Delete Secret                  │
│                                   │
│ Permanently delete "GitHub:PAT"?  │
│ This cannot be undone.            │
│                                   │
│         [Cancel]  [Delete]        │
└───────────────────────────────────┘
```

**Reveal:**
```
┌───────────────────────────────────┐
│ 👁️ Reveal Secret Value            │
│                                   │
│ Show the plaintext value of       │
│ "Google:ClientId"?                │
│                                   │
│ This action is logged.            │
│ Value auto-hides after 30s.       │
│                                   │
│         [Cancel]  [Reveal]        │
└───────────────────────────────────┘
```

---

## 6. Telemetry

Every admin CRUD operation and reveal logs to **two sinks**:

### 6.1 Local SQLite Audit

Reuse the existing `SecretAccessAudit` table via `ISecretAccessAuditor.RecordAsync()`. Extend the `CallerType` enum to include a new value or use `CallerType.System` with a structured `CallerId`:

```
CallerId = "VaultAdminUI:{userId}:{action}"
```

Where `action` ∈ { `List`, `Create`, `Update`, `Delete`, `Reveal`, `Reveal:Copy` }.

### 6.2 Application Insights (Phase 3 Audit Sink)

When App Insights is configured, emit a `TrackEvent`:

```csharp
telemetryClient.TrackEvent("VaultAdmin", new Dictionary<string, string>
{
    ["UserId"]    = userId,
    ["Action"]    = "Reveal",
    ["SecretName"]= name,
    ["Backend"]   = "sqlite",
    ["Success"]   = "true"
});
```

This integrates with the Phase 3 audit sink design. Until App Insights is wired, the local SQLite audit is the single source of truth.

### 6.3 Structured Logging

All operations emit structured log lines at `Information` level, following the safe logging guidance from the threat model (§7): secret **name** only, never **value**.

---

## 7. Test Strategy Preview

Test implementation is Hockney's responsibility; this section enumerates scope.

### 7.1 Unit Tests

- **VaultAdminEndpoints contract tests:** Verify each endpoint returns correct status codes, enforces admin auth, and never leaks values in list responses.
- **VaultAdminClient tests:** Verify HTTP request shaping (paths, query params, serialization).
- **Admin auth filter tests:** Verify config-based admin check (allow, deny, empty list = deny-all).

### 7.2 Integration Tests

- **In-memory SQLite store:** Full CRUD cycle through the endpoint pipeline using `WebApplicationFactory`.
- **Audit trail verification:** After create/reveal/delete, query audit table and assert rows with correct `CallerType` and `CallerId`.
- **Cache invalidation:** Set via admin endpoint → verify `VaultConfigurationResolver` cache is flushed.
- **Rate limiting:** Exceed reveal rate limit → assert 429.

### 7.3 E2E Tests (Playwright)

Six scenarios:

1. **List secrets:** Navigate to `/vault`, verify table renders with metadata (no values).
2. **Create secret:** Click "+ New Secret", fill form, save, verify appears in list.
3. **Edit/rotate secret:** Click edit on existing, change value, save, verify `UpdatedAt` changes.
4. **Delete secret:** Click delete, confirm modal, verify removed from list.
5. **Reveal secret:** Click reveal, confirm modal, verify value appears, verify auto-hide after 30s.
6. **Audit viewer:** Navigate to `/vault/audit`, apply filter, verify rows display.

---

## 8. Phased Rollout

### UI-Phase-A: List + Create + Delete

**Scope:** Read paths + minimal write. Behind `Features:VaultAdminUi` feature flag (default: `false`).

Deliverables:
- `VaultAdminEndpoints.cs` — List, Create, Delete endpoints with admin auth filter.
- `Index.razor` — List page with metadata table, delete confirmation modal.
- `Edit.razor` — Create-only mode (no edit/rotate yet).
- `VaultAdminClient.cs` — Typed HTTP client.
- Feature flag check in navigation menu (hide "Vault" link when disabled).
- Unit + integration tests for Phase A endpoints.

### UI-Phase-B: Reveal + Rotate + Audit Viewer

**Scope:** Sensitive operations.

Deliverables:
- Reveal endpoint (`?reveal=true`) with rate limiting.
- Reveal modal + auto-hide + copy-to-clipboard in `Index.razor`.
- Edit mode in `Edit.razor` (rotate value, name locked).
- `Audit.razor` — Audit viewer with filters and pagination.
- Audit endpoint (`GET /api/vault/audit`).
- E2E tests for reveal and audit scenarios.

### UI-Phase-C: Backend Chips + Advanced Features

**Scope:** Phase 3 chained-store awareness.

Deliverables:
- `BackendName` and `IsWritable` in `SecretSummary`.
- Backend chip rendering in list table.
- Read-only backend tooltip + disabled buttons.
- Cache invalidation hooks (verify existing pipeline).
- Advanced audit filters (by backend, by action type).
- Re-authentication challenge on reveal (replaces modal-only flow).

---

## 9. Risks & Open Questions

### Risks

| # | Risk | Severity | Mitigation |
|---|------|----------|------------|
| R1 | Admin UI accidentally exposed to agents via misconfigured routing | High | Feature flag + admin auth filter + no MCP registration. Gate 5 reflection test updated. |
| R2 | Reveal endpoint used as oracle attack (enumerate secret names) | Medium | Rate limiting (5/min) + audit trail. Names are already known to admins. |
| R3 | SignalR circuit holds revealed value in server memory | Low | Auto-clear after 30s in component state. Circuit disposal clears Blazor component tree. |
| R4 | Config-based admin list is weak for multi-user deployments | Medium | Acceptable for Phase A (single-user local). Phase C adds SSO. |
| R5 | Audit table grows unbounded | Low | Operator-managed retention. Document `DELETE FROM SecretAccessAudit WHERE AccessedAt < ...` as maintenance guidance. |

### Open Questions for Bruno

1. Should the vault admin pages be accessible from the main nav sidebar, or only via direct URL (hidden admin)?
2. For Phase B reveal: is 30-second auto-hide acceptable, or should it be configurable?
3. Should `DELETE` of a secret also archive the encrypted value to a `SecretArchive` table for recovery, or is hard-delete acceptable?
4. Is there a preferred naming convention for the feature flag (`Features:VaultAdminUi` vs. `FeatureFlags:Vault:AdminUi`)?

---

## Appendix A: Endpoint Specification

### `GET /api/vault/secrets`

**Response:** `200 OK`
```json
{
  "secrets": [
    {
      "name": "Google:ClientId",
      "description": "OAuth client ID",
      "updatedAt": "2026-05-06T14:00:00Z",
      "backendName": "sqlite",
      "isWritable": true
    }
  ],
  "count": 1
}
```

### `GET /api/vault/secrets/{name}?reveal=true`

**Response:** `200 OK`
```json
{
  "name": "Google:ClientId",
  "value": "actual-plaintext-value",
  "description": "OAuth client ID",
  "updatedAt": "2026-05-06T14:00:00Z",
  "backendName": "sqlite"
}
```

**Side effects:** Audit row with `Action=Reveal`.

### `POST /api/vault/secrets`

**Request:**
```json
{
  "name": "NewSecret",
  "value": "secret-value",
  "description": "Optional description"
}
```

**Response:** `201 Created`

### `PUT /api/vault/secrets/{name}`

**Request:**
```json
{
  "value": "rotated-value",
  "description": "Updated description"
}
```

**Response:** `204 No Content`

### `DELETE /api/vault/secrets/{name}`

**Response:** `204 No Content` or `404 Not Found`

### `GET /api/vault/audit?secret={name}&caller={id}&from={iso}&to={iso}&limit=100&offset=0`

**Response:** `200 OK` (follows `AuditEndpoints` pagination pattern)
```json
{
  "entries": [
    {
      "id": "...",
      "secretName": "Google:ClientId",
      "callerType": "System",
      "callerId": "VaultAdminUI:local-admin:Reveal",
      "accessedAt": "2026-05-07T14:32:00Z",
      "success": true
    }
  ],
  "count": 1,
  "offset": 0,
  "limit": 100
}
```
