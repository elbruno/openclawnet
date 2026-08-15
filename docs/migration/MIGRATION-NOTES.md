# Migration Notes

**Last Updated:** 2026-08-06  
**Status:** Planning phase (Harness adoption not yet started)

---

## Current State

**Stable Runtime:** `DefaultAgentRuntime` + `ModelClientChatClientAdapter`  
**Framework Version:** Microsoft.Agents.AI 1.17.0  
**Test Fixture:** AspireHostFixture (Aspire.Hosting.Testing 13.4.6)

All features working as expected:
- ✅ Chat streaming via HTTP NDJSON
- ✅ Tool approval flow
- ✅ Skill injection (ChatClientAgent + IAIContextProvider)
- ✅ Context compaction & session persistence
- ✅ Multi-provider support (Ollama, Azure OpenAI, Foundry, GitHub Copilot, FoundryLocal)

---

## Why No Harness Migration Yet

1. **Stability over Features:** Current pattern is production-proven. Harness is available but adds complexity without immediate benefit.
2. **Refactoring Scope:** Would require changes to:
   - `IAgentRuntime` interface
   - `ExecuteAsync()` / `ExecuteStreamAsync()` implementations
   - Tool approval flow (model-driven vs. explicit approval chain)
   - Streaming event model (AgentStreamEvent vs. Harness events)
3. **Testing Burden:** Would require new integration tests for Harness patterns + existing tests for compatibility.
4. **Historical Records:** Keeping `DefaultAgentRuntime` stable preserves learn path for Reactor attendees and future contributors.

---

## Future Harness Adoption (Planned)

### Phase 1: Evaluation (Q3 2026)
- [ ] Document Harness patterns (LoopAgent, ToolApprovalAgent, etc.)
- [ ] Create spike branch for proof-of-concept
- [ ] Benchmark: Harness vs. DefaultAgentRuntime (latency, throughput, memory)
- [ ] Decision: Adopt or stay current

### Phase 2: Migration (if approved)
- [ ] Update `IAgentRuntime` to use LoopAgent
- [ ] Refactor approval flow (if needed)
- [ ] Refactor streaming (if needed)
- [ ] New integration tests for Harness patterns
- [ ] Dual-mode: DefaultAgentRuntime (legacy) + HarnessAgentRuntime (new)

### Phase 3: Rollout
- [ ] Release: v2.0.0-harness-preview
- [ ] Gather feedback (Discord, issues, Reactor attendees)
- [ ] Fix bugs, document breaking changes
- [ ] Release: v2.0.0 (Harness as default, DefaultAgentRuntime deprecated but supported)

### Phase 4: Cleanup (v3.0.0 or later)
- [ ] Remove DefaultAgentRuntime (breaking change)
- [ ] Update all examples and session materials
- [ ] Publish updated Reactor materials

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
