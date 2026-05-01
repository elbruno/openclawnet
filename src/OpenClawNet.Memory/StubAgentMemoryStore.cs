namespace OpenClawNet.Memory;

/// <summary>
/// Stub implementation of <see cref="IAgentMemoryStore"/> for issue #99.
/// This is a temporary placeholder until the MempalaceNet-backed implementation is completed in issue #98.
/// </summary>
/// <remarks>
/// This stub allows the DI container to resolve IAgentMemoryStore without breaking compilation.
/// All operations return empty results or no-op behaviors.
/// 
/// ⚠️ DO NOT USE IN PRODUCTION - Replace with MempalaceNet implementation (issue #98).
/// </remarks>
[Obsolete("Stub implementation for issue #99 - will be replaced by MempalaceNet-backed implementation in issue #98")]
public sealed class StubAgentMemoryStore : IAgentMemoryStore
{
    /// <summary>
    /// Stub: Returns a dummy memory ID without persisting anything.
    /// </summary>
    public Task<string> StoreAsync(string agentId, MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entry);

        // Return a deterministic stub ID for testing
        return Task.FromResult($"stub-{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Stub: Always returns empty search results.
    /// </summary>
    public Task<IReadOnlyList<MemoryHit>> SearchAsync(string agentId, string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        // Return empty results - no memories stored in stub
        return Task.FromResult<IReadOnlyList<MemoryHit>>(Array.Empty<MemoryHit>());
    }

    /// <summary>
    /// Stub: No-op deletion (nothing is stored).
    /// </summary>
    public Task DeleteAsync(string agentId, string memoryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);

        // No-op - stub doesn't persist anything
        return Task.CompletedTask;
    }
}
