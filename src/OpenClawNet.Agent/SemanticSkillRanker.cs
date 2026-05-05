using Microsoft.Extensions.Logging;

namespace OpenClawNet.Agent;

/// <summary>
/// Implements semantic re-ranking of skills using hybrid search with RRF (Reciprocal Rank Fusion).
/// Merges keyword and semantic ranking signals with a 100ms timeout for graceful fallback.
/// </summary>
public sealed class SemanticSkillRanker : ISemanticSkillRanker
{
    private readonly IHybridSearchService _hybridSearch;
    private readonly ILogger<SemanticSkillRanker> _logger;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMilliseconds(100);

    public SemanticSkillRanker(
        IHybridSearchService hybridSearch,
        ILogger<SemanticSkillRanker> logger)
    {
        _hybridSearch = hybridSearch;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SkillSummary>> RerankAsync(
        string taskDescription,
        IReadOnlyList<SkillSummary> skills,
        CancellationToken cancellationToken = default)
    {
        // If no skills to rerank, return empty
        if (skills.Count == 0)
        {
            return skills;
        }

        try
        {
            // Create a cancellation token with 100ms timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(SearchTimeout);

            // Use HybridSearchService to search semantically across skills
            var searchResults = await _hybridSearch.SearchAsync(
                taskDescription,
                collection: "skills",
                topK: skills.Count,
                cancellationToken: cts.Token);

            // If no semantic results, return original skills
            if (searchResults == null || searchResults.Count == 0)
            {
                _logger.LogDebug("No semantic results returned, using original skill order");
                return skills;
            }

            // Build a map of skill names to semantic scores
            var semanticScores = new Dictionary<string, double>();
            for (int i = 0; i < searchResults.Count; i++)
            {
                var result = searchResults[i];
                // RRF: 1 / (k + rank) where k=60 (standard RRF k value)
                double score = 1.0 / (60.0 + i);
                
                if (!semanticScores.ContainsKey(result.Id))
                {
                    semanticScores[result.Id] = score;
                }
            }

            // Re-rank skills using RRF fusion: combine keyword rank + semantic score
            var rerankResults = skills
                .Select((skill, keywordRank) =>
                {
                    // Keyword RRF: 1 / (k + rank)
                    double keywordScore = 1.0 / (60.0 + keywordRank);
                    
                    // Get semantic score if available
                    double semanticScore = semanticScores.TryGetValue(skill.Name, out var semScore)
                        ? semScore
                        : 0.0;

                    // RRF fusion: combine both scores (additive)
                    double fusedScore = keywordScore + semanticScore;

                    // Create new instance with semantic metadata populated
                    var ranked = new SkillSummary
                    {
                        Name = skill.Name,
                        Description = skill.Description,
                        Keywords = skill.Keywords,
                        Confidence = skill.Confidence,
                        ExtractedDate = skill.ExtractedDate,
                        ValidatedBy = skill.ValidatedBy,
                        RelevanceScore = (int)fusedScore,
                        SemanticScore = fusedScore,         // NEW: RRF fused score
                        IsSemanticRanked = true             // NEW: marked as semantically ranked
                    };

                    return (Skill: ranked, FusedScore: fusedScore);
                })
                .OrderByDescending(x => x.FusedScore)
                .Select(x => x.Skill)
                .ToList();

            _logger.LogInformation(
                "Semantic re-ranking completed: {Count} skills re-ranked using HybridSearchService. Semantic metadata propagated to all results",
                rerankResults.Count);

            return rerankResults;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Semantic search timed out after {TimeoutMs}ms, returning original skill order",
                SearchTimeout.TotalMilliseconds);
            return skills;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Semantic re-ranking failed, falling back to keyword-only ranking");
            return skills;
        }
    }
}
