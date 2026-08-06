# Skill: Aspire Lifecycle Management — Safe Shutdown & Orphaned Process Recovery

@extracted: 2026-05-01, ricken, from Round 2 incident (Drummond + 9 orphaned AppHost processes)  
@validated-by: ricken (medium — one observed incident + repo memory precedent)

**Domain:** Aspire lifecycle operations (AppHost processes, ServiceDefaults DLL locking)

**When to use:** Every time you start an Aspire application (`aspire start`) or work with the AppHost in the OpenClawNet ecosystem.

---

## THE RULE: NEVER Ctrl+C an Aspire AppHost Terminal

⚠️ **Always use `aspire stop` (CLI) to shut down Aspire.** ⚠️

Killing the AppHost process with Ctrl+C orphans child processes that continue holding file locks on `OpenClawNet.ServiceDefaults.dll` and related binaries. These orphaned processes block subsequent builds with `MSB3027`/`MSB3021` errors.

---

## Why This Matters: The Lock Chain

### What Happens When You Ctrl+C AppHost

1. **AppHost process terminated abruptly** (signal not caught)
2. **Child processes (dotnet, aspire-hosting-runtime) orphaned** — parent dies but children remain
3. **`OpenClawNet.ServiceDefaults.dll` stays locked** in memory by zombie processes
4. **Next `dotnet build` fails immediately** with:
   ```
   error MSB3027: Could not copy "<path>\OpenClawNet.ServiceDefaults.dll" 
   to "bin\obj\...". Could not find a part of the path.
   ```
   or
   ```
   error MSB3021: Unable to copy file "<file>" to "<destination>". 
   The file is locked by another process.
   ```

### The Correct Flow: `aspire stop`

1. **`aspire stop` sends graceful shutdown signal** to AppHost
2. **AppHost catches signal, closes all resource handles** (DLLs, network sockets, temporary files)
3. **All child processes exit cleanly** (no zombies)
4. **DLLs released from memory** — next build proceeds normally

---

## Symptom Recognition: Is This Aspire Lock Contention?

| Symptom | Likely Culprit |
|---------|---|
| Build fails immediately with `MSB3027` or `MSB3021` on clean checkout | Orphaned AppHost holding lock |
| Error mentions `OpenClawNet.ServiceDefaults.dll` or `bin\obj\` directory | Aspire lock; check processes |
| Build hangs for 30+ seconds before failing | File lock timeout; orphaned process likely |
| Error gone after restarting machine, but recurs after Aspire session | Strong indicator of orphaned process |
| `tasklist` shows multiple `dotnet.exe` entries labeled "AppHost.dll" | Orphaned child processes present |

---

## Recovery Runbook: Unblock a Locked Build

### Step 1: Identify Orphaned Aspire Processes

**List candidate processes:**
```powershell
Get-Process | Where-Object { 
    ($_.ProcessName -eq 'dotnet' -or $_.Name -eq 'Aspire.Hosting') `
    -and (Get-Process -Id $_.Id | Select-Object -ExpandProperty CommandLine | Select-String 'AppHost|Aspire.Hosting')
}
```

**Or use the provided helper script (see bottom):**
```powershell
.\scripts\kill-orphaned-aspire.ps1  # Lists candidate PIDs without killing
.\scripts\kill-orphaned-aspire.ps1 -Force  # Actually kills them
```

### Step 2: Confirm You're Killing Only Aspire Processes

Look for these signatures in the process command line:
- `AppHost.dll` (primary Aspire host)
- `Aspire.Hosting` (Aspire runtime library)
- `OpenClawNet.AppHost` (our project's AppHost)

**DO NOT use blanket process kills like:**
- ❌ `taskkill /IM dotnet.exe /F` (kills ALL dotnet processes — breaks other projects)
- ❌ `Get-Process dotnet | Stop-Process -Force` (same issue)

**DO use explicit PID kills:**
- ✅ `Stop-Process -Id 12345` (one specific PID per invocation)
- ✅ `Stop-Process -Id 12345, 12346, 12347` (multiple explicit PIDs in one call)

### Step 3: Retry Build

```powershell
cd src\OpenClawNet.AppHost
dotnet build --verbosity quiet
```

If the build still fails, check for additional orphaned processes or DLL file-locking tools (`Process Explorer` / `Resource Monitor` on Windows).

---

## Prevention: Session Discipline

### Before Exiting a Session with Aspire Running

**Checklist:**
- [ ] Issue `aspire stop` and **wait for it to complete** (watch the terminal for the exit prompt)
- [ ] Verify `aspire stop` succeeded with status code 0:
  ```powershell
  aspire stop; $exitCode = $LASTEXITCODE; Write-Host "Exit code: $exitCode"
  ```
- [ ] If leaving the session **mid-session** with AppHost still running, document it:
  - Add a note to `.squad/agents/<agent-name>/handoff.md` or the issue comment:
    ```markdown
    ## Handoff Note
    AppHost still running (PID 12345). Next agent: please run `aspire stop` before exiting.
    ```

### If You Must Abandon a Session Gracefully

1. Ensure no unsaved work in AppHost output
2. **Always issue `aspire stop`** — do not rely on session cleanup
3. Wait 3–5 seconds for the process to exit
4. Verify with `Get-Process -Name dotnet -ErrorAction SilentlyContinue | Where-Object CommandLine -Like '*AppHost*'` — should return nothing

---

## Implementation: The (Optional) Helper Script

A PowerShell script at `scripts/kill-orphaned-aspire.ps1` is provided:

```powershell
# List candidate Aspire processes (no action)
.\scripts\kill-orphaned-aspire.ps1

# Kill identified processes
.\scripts\kill-orphaned-aspire.ps1 -Force
```

**What it does:**
1. Filters `dotnet` processes by command-line keywords (`AppHost.dll`, `Aspire.Hosting`)
2. Displays them in a table (PID, command-line snippet, memory)
3. With `-Force`, kills each by explicit PID (never name-based blanket kill)
4. Includes a prominent `# WARNING:` banner reminding users this is a last resort

**Limitations:**
- Does not auto-verify that killed processes are actually Aspire (edge case: user-built AppHost with different naming)
- Requires admin privileges to kill processes
- Should only be used after `aspire stop` has failed or been skipped

---

## Real-World Example: Round 2 Incident

**Incident:** Drummond (agent) killed 9 stale Aspire processes manually.

**Root cause:** Previous agent violated the "always use `aspire stop`" rule → orphaned AppHost processes.

**Impact:**
- `OpenClawNet.ServiceDefaults.dll` locked by zombie processes
- Subsequent builds failed with `MSB3027` errors
- 15–20 minutes debugging before Drummond identified and killed the processes manually

**Resolution:** This SKILL now makes the rule unmissable by embedding it in the agent workflow.

---

## References

- **Decision:** `.squad/decisions.md` § Aspire Lifecycle Discipline (2026-05-01)
- **Incident:** elbruno/openclawnet-plan#117 (Round 2: Orphaned Aspire processes)
- **Related:** `.squad/agents/ricken/` (this skill authored during DevRel phase)
- **MSBuild Errors:**
  - https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.tasks.copy.msbuild_copy_failed (MSB3027)
  - https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.tasks.copy (MSB3021)
- **Aspire Docs:** https://learn.microsoft.com/en-us/dotnet/aspire/

---

## Confidence & Maintenance

**Confidence:** Medium (one observed incident + strong repo memory precedent)

**Trigger for revision:** 
- Another orphaned process incident occurs
- Aspire CLI changes its shutdown behavior
- Helper script requires adjustment (edge cases discovered)

**Owner:** ricken (DevRel); escalate to squad:architecture if systemic issue emerges
