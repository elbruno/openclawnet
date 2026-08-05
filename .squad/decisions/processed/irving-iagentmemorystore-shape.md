# Decision: IAgentMemoryStore Interface Shape and Location

**By:** Irving (Backend Dev 🔧)  
**Date:** 2026-05-01  
**Issue:** elbruno/openclawnet-plan#99  
**Status:** ✅ IMPLEMENTED (PR #12)

## Context

PR #72 decided to split agent-specific vector memory (`IAgentMemoryStore`) from session summaries (`IMemoryService`). This decision document captures the interface design and project placement rationale.

## Decisions

### 1. Project Location: `OpenClawNet.Memory`

**Chosen:** Keep abstraction in `OpenClawNet.Memory` (existing project)  
**Rejected:** Create new `OpenClawNet.Memory.Abstractions` project

**Rationale:**
- `OpenClawNet.Memory` already contains `IMemoryService` and `IEmbeddingsService` abstractions
- Avoids project proliferation (20+ projects already)
- Co-locates related memory types (`MemoryEntry`, `MemoryHit`, `SummaryRecord`)
- No cross-cutting dependency issues — Memory project is already referenced by consumers

### 2. Interface Shape

```csharp
public interface IAgentMemoryStore
{
    Task<string> StoreAsync(string agentId, MemoryEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryHit>> SearchAsync(string agentId, string query, int topK = 5, CancellationToken ct = default);
    Task DeleteAsync(string agentId, string memoryId, CancellationToken ct = default);
}
```

**Key Design Choices:**

1. **Per-Agent Isolation via `agentId` Parameter**
   - Enforces boundary at interface level
   - Caller doesn't need to track per-agent instances
   - Simpler DI registration (single scoped service, not per-agent singletons)

2. **String-Based Memory IDs**
   - Flexible for different backends (MempalaceNet uses GUIDs, Qdrant uses snowflakes, etc.)
   - Avoids forcing Guid type when some stores use composite keys

3. **TopK Parameter with Default**
   - Default `topK = 5` covers typical chat scenarios
   - Customizable for batch/background processing (e.g., topK = 20 for analytics)

4. **Metadata as `IReadOnlyDictionary<string, string>`**
   - Extensibility point for tags, timestamps, source attribution
   - No schema changes required for new metadata fields
   - Read-only to prevent accidental mutation after storage

### 3. Record Types Design

```csharp
public sealed record MemoryEntry(string Content, IReadOnlyDictionary<string, string>? Metadata = null)
{
    public DateTime? Timestamp { get; init; }
}

public sealed record MemoryHit(string Id, string Content, double Score, IReadOnlyDictionary<string, string>? Metadata = null);
```

**Rationale:**
- **Sealed records:** Immutability + value equality semantics
- **Positional parameters:** Brevity for mandatory fields (Content, Id, Score)
- **Optional metadata:** Not all memories need tags/context
- **Optional timestamp:** Caller can set, backend can override (allows client-provided or server-assigned timestamps)

### 4. DI Lifetime: Scoped

**Chosen:** `services.AddScoped<IAgentMemoryStore, StubAgentMemoryStore>()`  
**Rejected:** Singleton, Transient

**Rationale:**
- Matches `IMemoryService` lifetime (consistency)
- Scoped = one instance per HTTP request (typical usage pattern)
- MempalaceNet backend (#98) may hold per-request state (transaction context, connection pooling)

### 5. Stub Implementation Strategy

**Pattern:** Minimal stub with `[Obsolete]` warning

```csharp
[Obsolete("Stub implementation for issue #99 - will be replaced by MempalaceNet-backed implementation in issue #98")]
public sealed class StubAgentMemoryStore : IAgentMemoryStore
{
    // Returns empty results, validates parameters, no-op operations
}
```

**Rationale:**
- Allows DI container to compile without MempalaceNet dependency
- `[Obsolete]` provides compile-time visibility of temporary nature
- Guard clauses establish contract early (fail-fast on invalid parameters)
- Empty results are semantically correct (no memories stored = no results)

## Alternatives Considered

### Alt 1: Guid-Based Memory IDs
**Rejected:** Some vector stores (Qdrant, Weaviate) use string-based IDs (snowflakes, ULIDs). Forcing Guid loses flexibility.

### Alt 2: Separate `IAgentMemoryReader` / `IAgentMemoryWriter`
**Rejected:** CQRS split adds complexity without clear benefit. Most tools (RememberTool, RecallTool) need both read + write.

### Alt 3: `Task<MemoryId> StoreAsync(...)` with custom `MemoryId` type
**Rejected:** Over-engineering. String is sufficient, type safety can be added later if needed (opaque type wrapper).

### Alt 4: New `OpenClawNet.Memory.Abstractions` Project
**Rejected:** Not needed — `OpenClawNet.Memory` already serves as abstractions layer (no implementations besides stub).

## Implementation Evidence

- **PR:** https://github.com/elbruno/openclawnet/pull/12
- **Files:** `IAgentMemoryStore.cs`, `StubAgentMemoryStore.cs`, `MemoryServiceCollectionExtensions.cs`
- **Tests:** 11/11 passed (DI registration, stub behavior, parameter validation)

## Next Steps

1. **#98 (Mark):** Replace `StubAgentMemoryStore` with MempalaceNet-backed implementation
2. **#100 (Tools):** Wire RememberTool/RecallTool to `IAgentMemoryStore`

---

**Status:** ✅ Accepted and Implemented  
**Reviewers:** Bruno (via PR #12 review)
