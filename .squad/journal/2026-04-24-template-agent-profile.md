# Template Agent Profile Default Selection Fix

**Date**: 2026-04-24 09:15:01  
**Contributor**: Helly  
**Commit**: aa04e0c

## Summary
Fixed bug where jobs created from demo templates weren't using the configured default agent profile.

## Problem
Both `POST /api/jobs` and `POST /api/jobs/from-template/{name}/activate` were writing `request.AgentProfileName` directly to the database. When templates or the "(Default)" UI selection sent `null`, jobs landed with `AgentProfileName=null`. JobExecutor then fell back to literal string `"default"`, ignoring the configured default profile's instructions, tools, and approval policy.

## Solution
- Added `ResolveAgentProfileNameAsync` helper in `JobEndpoints.cs` that snapshots the current default profile's name onto the job whenever the caller omits one
- Both endpoints now inject `IAgentProfileStore`
- Snapshot strategy (not lazy resolve) ensures the UI shows the selected choice and remains stable if default changes later
- Added 3 regression tests in `JobsEndpointsTests`

## Decision
See `.squad/decisions/inbox/helly-template-default-agent.md`
