using System.Collections.Concurrent;
using OpenClawNet.Memory;

namespace OpenClawNet.UnitTests.MemoryTools;

/// <summary>
/// Lightweight in-memory <see cref="IAgentMemoryStore"/> for round-trip tool tests.
/// Mirrors the contract enough to prove agentId scoping and read-after-write without
/// dragging in the (parallel) MempalaceNet implementation from issue #98.
/// </summary>
internal sealed class InMemoryAgentMemoryStore : IAgentMemoryStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MemoryEntry>> _store = new();

    public Task<string> StoreAsync(string agentId, MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entry);

        var id = $"mem-{Guid.NewGuid():N}";
        var bucket = _store.GetOrAdd(agentId, _ => new ConcurrentDictionary<string, MemoryEntry>());
        bucket[id] = entry;
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<MemoryHit>> SearchAsync(string agentId, string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        if (!_store.TryGetValue(agentId, out var bucket))
            return Task.FromResult<IReadOnlyList<MemoryHit>>(Array.Empty<MemoryHit>());

        var hits = bucket
            .Select(kv => new MemoryHit(
                kv.Key,
                kv.Value.Content,
                Score: kv.Value.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.1,
                kv.Value.Metadata))
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryHit>>(hits);
    }

    public Task DeleteAsync(string agentId, string memoryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);

        if (_store.TryGetValue(agentId, out var bucket))
            bucket.TryRemove(memoryId, out _);
        return Task.CompletedTask;
    }
}
