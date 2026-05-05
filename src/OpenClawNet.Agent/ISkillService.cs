namespace OpenClawNet.Agent;

/// <summary>
/// Service for loading and querying the skills inventory.
/// </summary>
public interface ISkillService
{
    /// <summary>
    /// Finds relevant skills based on task keywords.
    /// </summary>
    /// <param name="taskDescription">Task description or keywords to match against</param>
    /// <param name="topN">Maximum number of skills to return (default 3)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of relevant skills, sorted by relevance (highest first)</returns>
    Task<IReadOnlyList<SkillSummary>> FindRelevantSkillsAsync(
        string taskDescription, 
        int topN = 3, 
        CancellationToken cancellationToken = default);
}
