---
name: appinsights-vault-audit-decorator
description: "Decorate vault audit writes with Application Insights TrackEvent telemetry."
category: security
tags: [audit, app-insights, telemetry, secrets]
examples:
  - "Ship vault access metadata to App Insights"
  - "Add a telemetry-only audit decorator"
enabled: true
---

# App Insights Vault Audit Decorator

Use this pattern when vault access audits must emit telemetry events without exposing secret values.

## Components

1. **Decorator**: Implement `ISecretAccessAuditor` and wrap the existing auditor.
2. **TelemetryClient**: Emit `TrackEvent("VaultSecretAccess", properties)` on each audit row.
3. **Metadata-only fields**: `SecretName`, `CallerType`, `CallerId`, `SessionId`, `Success`, `Timestamp`.

## Guardrails

- NEVER include secret values (even hashed) in telemetry properties.
- Keep SQLite audit rows as the source of truth; App Insights is supplemental telemetry only.
