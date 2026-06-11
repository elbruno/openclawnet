namespace OpenClawNet.Skills;

/// <summary>
/// Manages the physical assignment of file-based skills to agents.
/// <para>
/// When a skill is assigned to an agent its <c>SKILL.md</c> is copied from the
/// global source layer (system or installed) into
/// <c>{StorageRoot}/skills/agents/{agentName}/{skillName}/SKILL.md</c>.
/// When unassigned the directory is deleted so new agent instances never load it.
/// </para>
/// </summary>
public interface IAgentSkillAssignmentService
{
    /// <summary>
    /// Copies the <c>SKILL.md</c> for <paramref name="skillName"/> from the
    /// global snapshot (system or installed layer) into the agent's skill
    /// directory. Idempotent: re-assigns silently if already present.
    /// </summary>
    /// <returns><c>true</c> if the file was written; <c>false</c> if the skill
    /// was not found in the global snapshot.</returns>
    Task<bool> AssignAsync(string skillName, string agentName, CancellationToken ct = default);

    /// <summary>
    /// Removes the agent-local copy of <paramref name="skillName"/>. The global
    /// (system/installed) copy is never touched. No-op if not assigned.
    /// </summary>
    Task UnassignAsync(string skillName, string agentName, CancellationToken ct = default);

    /// <summary>
    /// Returns the names of skills currently assigned to <paramref name="agentName"/>
    /// (i.e. skill directories present under <c>skills/agents/{agentName}/</c>).
    /// </summary>
    Task<IReadOnlyList<string>> GetAssignedAsync(string agentName, CancellationToken ct = default);

    /// <summary>
    /// Synchronises the agent's skill directory so it contains exactly the
    /// skills in <paramref name="skillNames"/>: assigns new ones, unassigns
    /// removed ones, leaves unchanged ones untouched.
    /// </summary>
    /// <returns>A summary of what changed.</returns>
    Task<SkillSyncResult> SyncAssignmentsAsync(
        string agentName,
        IEnumerable<string> skillNames,
        CancellationToken ct = default);
}

/// <summary>Result of a <see cref="IAgentSkillAssignmentService.SyncAssignmentsAsync"/> call.</summary>
public sealed record SkillSyncResult(
    IReadOnlyList<string> Assigned,
    IReadOnlyList<string> Unassigned,
    IReadOnlyList<string> NotFound);
