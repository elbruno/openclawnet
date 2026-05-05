namespace OpenClawNet.Agent;

/// <summary>
/// Represents a skill's metadata for discovery and injection.
/// </summary>
public sealed record SkillSummary
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string[] Keywords { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required string ExtractedDate { get; init; }
    public required string[] ValidatedBy { get; init; }
    public int RelevanceScore { get; set; }
    
    // Phase 2B: Semantic re-ranking metadata
    public double? SemanticScore { get; set; }      // RRF fused score from semantic ranker (0.0-2.0 range)
    public bool IsSemanticRanked { get; set; }      // Flag: true if re-ranked semantically, false if keyword-only fallback
}

public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}
