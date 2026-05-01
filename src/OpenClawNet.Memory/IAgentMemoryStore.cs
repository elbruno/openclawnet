namespace OpenClawNet.Memory;

/// <summary>
/// Abstraction for agent-specific vector memory storage and retrieval.
/// Provides per-agent isolation for semantic memory operations.
/// </summary>
/// <remarks>
/// This interface was split from <see cref="IMemoryService"/> to separate concerns:
/// - <see cref="IMemoryService"/>: session summaries and statistics (existing functionality)
/// - <see cref="IAgentMemoryStore"/>: agent-specific vector embeddings and semantic search (new)
/// 
/// Per PR #72 decision (2026-05-01):
/// - Per-agent isolation enforced at the interface boundary (agentId parameter)
/// - Backed by MempalaceNet in production (issue #98)
/// - Tools (RememberTool/RecallTool) access via in-process DI (issue #100)
/// </remarks>
public interface IAgentMemoryStore
{
    /// <summary>
    /// Stores a memory entry for the specified agent.
    /// </summary>
    /// <param name="agentId">Agent identifier for isolation.</param>
    /// <param name="entry">Memory entry to store (content + optional metadata).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unique memory identifier.</returns>
    Task<string> StoreAsync(string agentId, MemoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches agent-specific memories using semantic similarity.
    /// </summary>
    /// <param name="agentId">Agent identifier for isolation.</param>
    /// <param name="query">Natural language search query.</param>
    /// <param name="topK">Maximum number of results to return (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked list of memory hits with similarity scores.</returns>
    Task<IReadOnlyList<MemoryHit>> SearchAsync(string agentId, string query, int topK = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific memory entry for the specified agent.
    /// </summary>
    /// <param name="agentId">Agent identifier for isolation.</param>
    /// <param name="memoryId">Unique memory identifier (returned by <see cref="StoreAsync"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string agentId, string memoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a memory entry to be stored.
/// </summary>
/// <param name="Content">Memory content (will be embedded for semantic search).</param>
/// <param name="Metadata">Optional metadata (key-value pairs for filtering/display).</param>
public sealed record MemoryEntry(
    string Content,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>
    /// Timestamp when the memory was created (set by caller).
    /// </summary>
    public DateTime? Timestamp { get; init; }
}

/// <summary>
/// Represents a memory search result with similarity scoring.
/// </summary>
/// <param name="Id">Unique memory identifier.</param>
/// <param name="Content">Original memory content.</param>
/// <param name="Score">Similarity score (higher = more relevant, range depends on implementation).</param>
/// <param name="Metadata">Optional metadata from the original entry.</param>
public sealed record MemoryHit(
    string Id,
    string Content,
    double Score,
    IReadOnlyDictionary<string, string>? Metadata = null);
