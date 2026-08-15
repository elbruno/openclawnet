# Migration Notes

**Last Updated:** 2026-08-15  
**Status:** Phase 2 complete — non-streaming `ExecuteAsync` migrated to `LoopAgent`

---

## Current State (After PR #213)

**Stable Runtime:** `DefaultAgentRuntime` + `ModelClientChatClientAdapter`  
**Framework Version:** Microsoft.Agents.AI 1.17.0 (required — Phase 2 uses `LoopAgent`)  
**Test Fixture:** AspireHostFixture (Aspire.Hosting.Testing 13.4.6)

### Phase 2 changes (PR #213)

- **Non-streaming `ExecuteAsync`**: replaced manual `while (iterations < 25)` loop with
  `ExecuteWithLoopAgentAsync()` backed by `LoopAgent` + `DelegateLoopEvaluator`.
  `MaxIterations = 25` is set explicitly (API-U-1 finding: `LoopAgent.DefaultMaxIterations = 10`).
  Token usage is accumulated across all iterations in the evaluator closure.
- **Streaming `ExecuteStreamAsync`**: manual loop **retained** — the HTTP-pause approval gate
  must `yield` NDJSON events mid-iteration, which `LoopEvaluator` cannot do.  Phase 3 will
  bridge this via an event-channel pattern.
- **`AgentSkillsProviderOptions`**: `DisableCaching` removed in MAF 1.17; fresh provider per
  request now satisfies K-D-1 instead.
- **`Microsoft.Agents.Core`**: remains at 1.5.181 — MAF 1.17 does not declare a dependency
  on Agents.Core; no Agents.Core APIs are used in Phase 2 source.

All features continue to work as expected:
- ✅ Chat streaming via HTTP NDJSON (manual loop, Phase 3 blocker documented)
- ✅ Tool approval flow (preserved in streaming path)
- ✅ Skill injection (ChatClientAgent + IAIContextProvider)
- ✅ Context compaction & session persistence
- ✅ Multi-provider support (Ollama, Azure OpenAI, Foundry, GitHub Copilot, FoundryLocal)

---

## Phase 3 Blocker (Streaming + Approval Bridge)

`LoopAgent.RunStreamingAsync` cannot be used for the streaming path because:
- The HTTP-pause approval gate requires **yielding `ToolApprovalRequest` events mid-iteration**
- A `LoopEvaluator` returns `ValueTask<LoopEvaluation>` — it cannot `yield return` to the outer `IAsyncEnumerable`

**Phase 3 design (planned):** event-channel bridge (`Channel<AgentStreamEvent>`) that lets the
evaluator post events to the outer NDJSON stream without `yield break` violations.

**D1 decision still required:**
- Option A: Preserve HTTP-pause via `FunctionInvocationDelegatingAgent` bridge (recommended)
- Option B: Adopt MAF multi-turn `ToolApprovalAgent` (requires client redesign)

---

## Future Roadmap

### Phase 3: Streaming LoopAgent Bridge (Planned)
- [ ] Design event-channel bridge for streaming path
- [ ] D1 decision (Option A vs B above)
- [ ] Migrate `ExecuteStreamAsync` to use `LoopAgent.RunStreamingAsync`
- [ ] Update integration tests for streaming approval flow

---

## AspireHostFixture Stability

**Current:** Aspire.Hosting.Testing 13.4.6  
**Status:** ✅ Stable and well-integrated

No migration planned. `AspireHostFixture` is the standard test fixture for .NET Aspire applications and will be maintained long-term.

**Usage:** All integration tests in `tests/OpenClawNet.IntegrationTests/` use it. No changes expected.

---

## Breaking Changes (None Expected)

The public API boundary (`IAgentOrchestrator`) will remain stable. Internal refactoring (Harness adoption, if it happens) will be isolated behind this boundary.

**Guarantee:** Code depending on `IAgentOrchestrator` will continue to work across major versions.

---

## Documentation Updates Required (When Migration Starts)

1. **Release Notes:** "v2.0.0 — Harness adoption (opt-in preview)"
2. **Architecture Docs:** New diagram for LoopAgent flow
3. **Session Materials:** No changes (uses stable public API)
4. **Migration Guide:** For internal developers and contributors
5. **Decision Log:** Record rationale and trade-offs

---

## Historical Records

- **Commit 674dbbd:** Last commit with DefaultAgentRuntime stable state (2026-08-06)
- **Current Branch:** docs/release-guidance-20260806 (documentation baseline)
- **Squad Decision:** `.squad/decisions/inbox/petey-harness-migration.md` (when created)

---

## Questions & Support

- **How does this affect my usage?** It doesn't. Public API is stable.
- **Can I use Harness today?** It's available in Microsoft.Agents.AI 1.17.0, but you'd need to implement your own integration (not supported in OpenClaw yet).
- **Will I need to update my code?** Only if you're using internal classes from OpenClawNet (not recommended). Public API (`IAgentOrchestrator`) is guaranteed stable.

---

## Next Review

- [ ] Quarterly: Evaluate Microsoft.Agents.AI releases for breaking changes
- [ ] Quarterly: Check Harness patterns in sample code and community
- [ ] Monthly: Monitor Harness adoption in other Aspire projects
