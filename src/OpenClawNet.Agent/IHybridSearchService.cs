namespace OpenClawNet.Agent;

/// <summary>
/// Interface for hybrid search operations combining keyword and semantic search.
/// Implements Reciprocal Rank Fusion (RRF) for merging multiple ranking signals.
/// </summary>
public interface IHybridSearchService
{
    /// <summary>
    /// Performs a hybrid search combining keyword and semantic similarity.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="collection">The collection name to search in (e.g., "skills")</param>
    /// <param name="topK">Maximum number of results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of hybrid search results sorted by relevance</returns>
    Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        string collection,
        int topK = 10,
        CancellationToken cancellationToken = default);
}
