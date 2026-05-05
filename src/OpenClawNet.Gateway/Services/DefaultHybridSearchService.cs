using Microsoft.Extensions.Logging;
using OpenClawNet.Agent;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Default in-memory implementation of IHybridSearchService.
/// Combines keyword and semantic ranking for hybrid search results.
/// </summary>
public sealed class DefaultHybridSearchService : IHybridSearchService
{
    private readonly ILogger<DefaultHybridSearchService> _logger;

    public DefaultHybridSearchService(ILogger<DefaultHybridSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        string collection,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentOutOfRangeException.ThrowIfNegative(topK);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug(
            "Hybrid search: query='{Query}', collection='{Collection}', topK={TopK}",
            query, collection, topK);

        // For now, return empty results - will be enhanced with actual semantic search
        // when embedder integration is available
        var results = new List<HybridSearchResult>();

        return Task.FromResult<IReadOnlyList<HybridSearchResult>>(results);
    }
}
