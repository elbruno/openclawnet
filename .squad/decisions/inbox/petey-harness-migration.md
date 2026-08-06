# Decision Proposal: MAF Harness Phase Migration
**Author:** Petey (Agent Platform Specialist)
**Date:** 2026-08-06
**Status:** PENDING — awaiting D1 / D2 owner decisions

---

## Background

A note in `docs/architecture/agent-runtime.md` stated the MAF "Harness" API was not yet published.
This is now incorrect: `Microsoft.Agents.AI` **v1.17.0** (already pinned in all project files) ships the full Harness surface.

This document inventories current `DefaultAgentRuntime` behavior, maps each behavior to the v1.17.0 API, and proposes a safe phased migration plan.

---

## Confirmed API Surface in Microsoft.Agents.AI 1.17.0

| API | Type | Notes |
|-----|------|-------|
| `AIAgentBuilder` | Pipeline builder | `.Use()`, `.UseLogging()`, `.UseOpenTelemetry()`, `.UseAIContextProviders()` |
| `LoopAgent` | Delegating loop wrapper | `MaxIterations`, `DelegateLoopEvaluator`, `LoopEvaluation.Stop/Continue/ContinueWithMessages` |
| `ToolApprovalAgent` | Multi-turn approval | `ToolApprovalRequestContent`, `ToolApprovalResponseContent`, `ToolApprovalAgentOptions` |
| `CompactionProvider` | AIContextProvider | `SummarizationCompactionStrategy`, `CompactionTriggers.MessagesExceed(n)` |
| `BackgroundAgentsProvider` | AIContextProvider | Background agent coordination |
| `TodoProvider` | AIContextProvider | Todo-list context injection |
| `AgentFileSkillsSource` | Skills provider | Progressive-disclosure skill loading (`load_skill` tool) |
| `FunctionInvocationDelegatingAgent` | Delegating agent | Intercept function calls |

**Key finding from probe tests (2026-08-06):**
- `LoopAgent.DefaultMaxIterations = 10` (not 25 — must set `MaxIterations = 25` explicitly)
- `ChatClientAgent` name flows through to `InvokingContext.Agent.Name` correctly
- `FunctionCallContent` IS surfaced in `LoopAgent` streaming output — current tool-collection code can work unchanged
- `CompactionProvider` API correct; trigger API is `CompactionTriggers.MessagesExceed(n)` (not `MessageCount`)
- `ToolApprovalRule` is process-scoped (no session key); `ToolApprovalAgentOptions` has public parameterless ctor so session isolation via per-request options is feasible

---

## Current Behavior Inventory (14 behaviors)

| # | Behavior | Location | Migration risk |
|---|----------|----------|----------------|
| B-1 | Max 25 iterations | `DefaultAgentRuntime` while loop | Low — set `LoopAgentOptions.MaxIterations = 25` |
| B-2 | Per-turn agent name | `BuildAgentForTurn()` + `ChatClientAgentOptions.Name` | Low — confirmed via API-U-2 probe |
| B-3 | Two-phase skills inject | `OpenClawNetSkillsProvider` always-inject | Low — provider works with `AIAgentBuilder.UseAIContextProviders()` |
| B-4 | HTTP-pause approval gate | `ToolApprovalCoordinator` TCS singleton | **HIGH** — architecturally incompatible with `ToolApprovalAgent` (see D1) |
| B-5 | Session-scoped remember | `ToolApprovalCoordinator._rememberedBySession` | Medium — needs per-session `ToolApprovalAgentOptions` wrapper |
| B-6 | NDJSON streaming | `ExecuteStreamAsync` yield loop | Low — `LoopAgent.RunStreamingAsync` streams updates |
| B-7 | Tool result injection | `ExecuteStreamAsync` tool-result injection | Low — confirmed via API-U-3 probe: `FunctionCallContent` surfaced |
| B-8 | Summary persistence | `DefaultSummaryService` | Medium — `CompactionProvider` is in-memory only (see D2) |
| B-9 | Skills turn-pin | `SkillsTurnPin.GetOrPin()` | Low — pin fires once; `LoopAgent` per-iteration provider calls are safe |
| B-10 | Audit trail | `IToolApprovalAuditor` | Low — wrap in `FunctionInvocationDelegatingAgent` middleware |
| B-11 | Tool exemptions | `ToolApprovalExemptions` | Low — filter on `FunctionCallContent.Name` in evaluator |
| B-12 | Cancellation | `CancellationToken` threading | Low — passes through unchanged |
| B-13 | MCP dedup | `McpDedupFilter` | Low — no MAF equivalent needed; keep as pre-loop step |
| B-14 | Max-iter guard output | Specific final message on iteration cap | Low — evaluator can detect `ctx.Iteration == MaxIterations` |

---

## API Uncertainties — Resolved by Probe Tests

| ID | Question | Finding |
|----|----------|---------|
| API-U-1 | Default `MaxIterations` | **10** — must set `MaxIterations = 25` explicitly |
| API-U-2 | Agent name in `InvokingContext` | **✅ Works** — `ChatClientAgentOptions.Name` flows to `InvokingContext.Agent.Name` |
| API-U-3 | `FunctionCallContent` in streaming | **✅ Surfaced** — current tool-collection code can work with `LoopAgent` unchanged |
| API-U-4 | `CompactionProvider` trigger API | **⚠️ Partial** — API is `MessagesExceed(n)`, not `MessageCount`; in-memory only (see D2) |
| API-U-5 | `ToolApprovalRule` session scope | **⚠️ Process-scoped** — per-session isolation requires per-request `ToolApprovalAgentOptions` |

---

## Phased Migration Plan

### Phase 1 (COMPLETE — this PR): Doc fix + API probe tests
- [x] Fix stale `v1.1.0` reference in `agent-runtime.md`
- [x] 7 probe tests confirming API-U-1 through API-U-5
- [x] `AIAgentBuilder` prototype passes

### Phase 2: `LoopAgent` wrapping (Low risk, behavior-preserving)
Replace the manual `while (iterations < 25)` loop in `DefaultAgentRuntime.ExecuteStreamAsync` with:
```csharp
var loop = new LoopAgent(
    innerAgent,
    new DelegateLoopEvaluator(EvaluateIterationAsync),
    new LoopAgentOptions { MaxIterations = 25 },
    _loggerFactory);
```
The evaluator contains the existing stop-condition logic. The HTTP-pause approval gate
stays in place as `FunctionInvocationDelegatingAgent` middleware (Option A, see D1).

**Gate:** D1 decision required before implementing the approval portion of Phase 2.

### Phase 3: `AIAgentBuilder` pipeline (Low risk)
Replace `BuildAgentForTurn()` (new `ChatClientAgent` per request) with an `AIAgentBuilder`
pipeline that calls `.UseAIContextProviders()` with `OpenClawNetSkillsProvider` and any
future `CompactionProvider`.

```csharp
var agent = new AIAgentBuilder(sp => new ChatClientAgent(chatClient, options))
    .UseLogging()
    .UseAIContextProviders()
    .Build(serviceProvider);
```

**Note:** The agent pipeline is statically built but the `LoopAgent` wrapping is per-request.
Agent name must be passed via `ChatClientAgentRunOptions` (to be validated).

### Phase 4: `ToolApprovalAgent` (High risk — requires D1)
**Option A (Recommended):** Preserve HTTP-pause via `FunctionInvocationDelegatingAgent` bridge.
A `FunctionInvocationDelegatingAgent` intercepts tool calls, calls
`IToolApprovalCoordinator.RequestApprovalAsync()`, and the existing HTTP POST flow resumes the
stream. The `ToolApprovalAgent` is NOT used; the existing coordinator is kept.

**Option B:** Replace HTTP-pause with MAF multi-turn model. Requires client redesign
(stream ends at approval request; client must re-invoke with `ToolApprovalResponseContent`).
This is a breaking change to the NDJSON protocol.

### Phase 5: `CompactionProvider` (Medium risk — requires D2)
**Option A:** Drop persistence — use `CompactionProvider` in-memory only.
Summary resets on app restart.

**Option B:** `PersistingCompactionStrategy` wrapper that serializes `AgentSession` state
at end of each turn and rehydrates on next turn using existing `DefaultSummaryService` storage.
Maintains restart-resilient summaries.

**Option C (Recommended):** Keep `DefaultSummaryService` as persistence layer; wire
`CompactionProvider` with a custom `CompactionStrategy` that delegates to it.

---

## Decisions Required

### D1: Approval Model (blocks Phase 4)
| Option | Description | Risk |
|--------|-------------|------|
| **A (recommended)** | Keep HTTP-pause via `FunctionInvocationDelegatingAgent` bridge | Low — no client changes |
| B | Adopt MAF multi-turn `ToolApprovalAgent` | High — requires client redesign |

**Owner:** Mark / Bruno

### D2: Compaction Persistence (blocks Phase 5)
| Option | Description | Risk |
|--------|-------------|------|
| A | Drop persistence (in-memory only) | Low — loss of restart-resilience |
| B | Dual systems (MAF + existing) | Medium — maintenance burden |
| **C (recommended)** | Custom `CompactionStrategy` delegating to `DefaultSummaryService` | Medium — new wrapper code |

**Owner:** Mark / Bruno

---

## File Impact

| File | Phase | Change |
|------|-------|--------|
| `DefaultAgentRuntime.cs` | 2 | Replace `while` loop with `LoopAgent` |
| `DefaultAgentRuntime.cs` | 3 | Replace `BuildAgentForTurn()` with `AIAgentBuilder` |
| `DefaultAgentRuntime.cs` | 4 (Option A) | Add `FunctionInvocationDelegatingAgent` middleware |
| `AgentServiceCollectionExtensions.cs` | 3 | Wire `AIAgentBuilder` in DI |
| `DefaultSummaryService.cs` | 5 | Wrap as `CompactionStrategy` (Option C) |
| `ToolApprovalCoordinator.cs` | 4 (Option A) | No change |
| `OpenClawNetSkillsProvider.cs` | 3 | No change (already `AIContextProvider`) |

No files are modified in Phase 1.

---

## Non-Goals (Phase 1)
- Do not replace the HTTP approval flow
- Do not alter production loop semantics
- Do not change public NDJSON protocol
- Do not upgrade `Microsoft.Agents.AI` version (already on 1.17.0)
