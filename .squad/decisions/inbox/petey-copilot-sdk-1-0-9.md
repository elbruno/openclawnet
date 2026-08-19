### 2026-08-19: GitHub.Copilot.SDK 1.0.9 migration contract
**By:** Petey
**What:** For OpenClawNet Copilot provider code, migrate to `GitHub.Copilot.SDK` 1.0.9 with `GitHub.Copilot` namespace, use `RuntimeConnection.ForStdio(path)` instead of removed `CopilotClientOptions.CliPath`, and bind session events via `On<SessionEvent>(...)`.
**Why:** The 1.x API moved symbols and tightened session/event APIs. Explicitly setting `EnableManagedSettings=false` with `PermissionHandler.ApproveAll` preserves non-interactive auto-approval behavior, and keeping `InfiniteSessions.Enabled=false` preserves prior request-scoped context behavior. Security outcome: provider project no longer carries MessagePack/Nerdbank transitive dependencies and vulnerable-package scan is clean.
