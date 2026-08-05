### 2026-05-01: PR #72 — Vector Store Recommendation

**By:** Mark (Lead Architect)  
**Status:** PENDING_BRUNO_DECISION  
**PR:** #72 (`research/memory-service`)

**Question:** mempalace.net vs Qdrant vs pgvector for agent memory vector store

**Recommendation:** **MempalaceNet (v0.6.0)**

**Rationale Summary:**
1. Bruno authored the library — aligned with OpenClawNet architecture patterns
2. Zero operational overhead (SQLite in-process, no Docker/Postgres)
3. Native per-agent isolation via Wings/Rooms/Drawers hierarchy
4. M.E.AI `IEmbeddingGenerator<>` already integrated
5. Uses `ElBruno.LocalEmbeddings` with ONNX (`all-MiniLM-L6-v2`) — exact embedding model we want

**Scoring Matrix:**
| Criterion | MempalaceNet | Qdrant | pgvector |
|-----------|-------------|--------|----------|
| .NET Integration | ✅ | ✅ | ⚠️ |
| Aspire Integration | ⚠️ | ✅ | ✅ |
| Per-Agent Isolation | ✅ | ⚠️ | ⚠️ |
| Operational Cost | ✅ | ⚠️ | ⚠️ |
| Embedding Control | ✅ | ⚠️ | ⚠️ |

**Risks:**
- No `AddMempalaceNet()` Aspire extension yet (can contribute one)
- Library is v0.6.0 (low risk — Bruno maintains, 152 tests)

**Q3 Tool Transport:** **In-process via DI** (not HTTP to separate service). Simpler, faster, avoids network latency.

**Next:** Bruno confirms direction → Mark drafts implementation spec.
