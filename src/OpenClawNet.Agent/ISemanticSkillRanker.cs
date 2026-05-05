namespace OpenClawNet.Agent;

/// <summary>
/// Service for re-ranking skills using semantic similarity.
/// Uses MempalaceNet's HybridSearchService with a 100ms timeout for graceful fallback.
/// </summary>
public interface ISemanticSkillRanker
{
    /// <summary>
    /// Re-ranks skills using semantic similarity to the task description.
    /// If semantic search times out or fails, returns the original skills unchanged.
    /// </summary>
    /// <param name="taskDescription">Task description to match semantically</param>
    /// <param name="skills">Original keyword-ranked skills</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Re-ranked skills sorted by semantic relevance</returns>
    Task<IReadOnlyList<SkillSummary>> RerankAsync(
        string taskDescription,
        IReadOnlyList<SkillSummary> skills,
        CancellationToken cancellationToken = default);
}
