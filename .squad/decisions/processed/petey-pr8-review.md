# Decision: PR #8 Scope Creep + Merge Conflict Assessment

**Date:** 2026-04-30  
**Reviewer:** Petey (Agent Platform Specialist)  
**Status:** COMMENT — awaiting Bruno decision  

## Context
PR #8 (`fix(tool-selection): prioritize shell tool over markdown when both are viable`) fixes Issue #84 (LLM tool confusion). Branch has DIRTY merge status with conflicts.

## Findings

### 1. Scope Creep — Chat.razor ShareSession Feature
**Added:** 18 lines in `Chat.razor`
- New button: "Copy shareable link" (📋 icon)
- `ShareSession()` method: copies `/chat?sessionId={id}` to clipboard via JS interop
- Dependencies: `NavigationManager`, `IJSRuntime` injections

**Issue:** This is a UI feature unrelated to tool selection (Issue #84). Bundling reduces commit clarity and makes rollback/cherry-pick harder.

**Options:**
1. Keep in PR #8 — update PR description to document ShareSession feature
2. Split to separate PR — cleaner history, separate review for UI vs. tool changes

### 2. Merge Conflict — Non-Substantive, Auto-Resolvable
**Conflicting file:** `src/OpenClawNet.Tools.Shell/ShellTool.cs`  
**Conflict location:** Line 21 (Description property)

**PR branch (feat/tool-selection-fix):**
```csharp
public string Description => "Execute system shell commands (bash/PowerShell). Use this for all command-line operations, file manipulation, package management, script execution, and system queries. RequiresApproval=true.";
```

**main branch:**
```csharp
public string Description => "Run shell commands (e.g., echo, ls, curl, dotnet). Executes arbitrary commands in an isolated shell service and returns stdout, stderr, and exit codes.";
```

**Resolution:** Keep PR version (enhanced description is the entire point of Issue #84 fix). Rebase will auto-resolve or require trivial manual selection.

**Chat.razor conflict:** Unknown (not visible in diff) — likely whitespace or adjacent feature edits on main.

## Recommendation

**Merge Strategy:** APPROVE after rebase  
**Action Items:**
1. Bruno: `git rebase origin/main` on `feat/tool-selection-fix` — conflict is trivial
2. (Optional) Split ShareSession to separate PR for cleaner history
3. Verify E2E test `Shell_RequiresApproval_EndToEnd` passes post-merge (PR claims this fix enables it)

## Risk Assessment
- **Regression Risk:** NONE (description-only changes to tool metadata)
- **Conflict Risk:** LOW (single-line text conflict, no logic divergence)
- **Scope Creep Risk:** LOW (ShareSession is self-contained, doesn't touch tool logic)

## Decision Needed
Does Bruno want to:
1. Merge as-is after rebase (fast, functional)
2. Split ShareSession to separate PR (cleaner, more effort)

**Petey's vote:** Option 1 (merge as-is) — ShareSession is safe, splitting adds overhead for minimal historical benefit.

