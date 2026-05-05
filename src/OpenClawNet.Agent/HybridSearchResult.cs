namespace OpenClawNet.Agent;

/// <summary>
/// Represents a single result from a hybrid search operation.
/// </summary>
public class HybridSearchResult
{
    /// <summary>
    /// The unique identifier of the search result (e.g., skill name).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The relevance score of the result (0-1 range recommended).
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Optional metadata associated with the result.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}
