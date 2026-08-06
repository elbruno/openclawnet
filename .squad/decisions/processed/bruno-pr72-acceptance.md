# Decision Drop: Bruno accepts Mark's PR #72 vector-store + tool transport recommendations

- **Date:** 2026-05-01
- **Owner:** Bruno (final decision)
- **Origin:** PR #72 — `research(memory): architectural proposal for next-gen agent memory service`
- **Supersedes:** Mark's earlier Qdrant recommendation in the proposal doc
- **Status:** ✅ RESOLVED — PR #72 merged as historical record

## Decisions (FINAL)

1. **Vector store:** **`ElBruno.MempalaceNet`** (Bruno's own library).
   - Replaces the original Qdrant-via-Aspire recommendation.
   - Rationale: zero ops overhead, native per-agent isolation via Wings/Rooms hierarchy, built-in ONNX embeddings.
   - Implementation: **#98**
2. **RememberTool / RecallTool transport:** **In-process DI** against `IAgentMemoryStore`.
   - Not HTTP to `memory-service`.
   - Rationale: lower latency, simpler test surface, no extra hop for the most-called path.
   - Implementation: **#100**
3. **`IAgentMemoryStore` split:** Confirmed (already captured in `copilot-pr72-split-imemoryservice.md`, now in processed/).
   - Implementation: **#99**

## Follow-up

- ✅ Mark updated `docs/architecture/memory-service-proposal.md` to reflect MempalaceNet + DI transport.
- ✅ PR #72 rebased and merged as the historical record.
- Implementation issues: #98, #99, #100
- §22 side findings: tracked in **#101**

## References

- PR #72 — https://github.com/elbruno/openclawnet-plan/pull/72
- Mark's recommendation comment — https://github.com/elbruno/openclawnet-plan/pull/72#issuecomment-4357602751
