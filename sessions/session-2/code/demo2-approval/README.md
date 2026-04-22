# Demo 2 — The approval gate

Two runs of the **same** dangerous tool, with two different `IToolApprovalPolicy` implementations. Shows that hardening a tool framework against bad inputs is a **DI swap**, not a code rewrite.

## What it shows

1. The default `AlwaysApprovePolicy` — convenient for demos, **dangerous in production**.
2. A custom `DenyDangerousArgsPolicy` — checks the arguments and refuses anything touching `System32`, `/etc`, `.ssh`, etc.
3. Both runs use the **same `ToolExecutor`, same `Registry`, same tool**. Only the policy changes.

## Run

```pwsh
cd sessions\session-2\code\demo2-approval
dotnet run
```

Expected output:

```
━━━ Run #1 — AlwaysApprovePolicy (the default) ━━━
  safe   → ✅ ALLOWED: would-delete: C:\temp\junk.txt
  danger → ✅ ALLOWED: would-delete: C:\Windows\System32\drivers\etc\hosts

━━━ Run #2 — DenyDangerousArgsPolicy (production-style) ━━━
  safe   → ✅ ALLOWED: would-delete: C:\temp\junk.txt
  danger → ❌ BLOCKED: Tool 'delete_file' requires approval
```

## Try it

- Add a path of your own to `SensitivePathFragments` and re-run.
- Replace `IsApprovedAsync` with a prompt-the-user flow (`Console.ReadLine`) — that's how a real "hold and confirm" UX would work.
- Implement an RBAC version: only `IsApprovedAsync` if the calling user is in role `"FileAdmin"`.

> The real `DefaultAgentRuntime` runs every tool through this same gate. Anywhere you swap the policy, **every** tool inherits the new rule.
