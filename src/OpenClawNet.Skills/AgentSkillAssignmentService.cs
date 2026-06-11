using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Skills;

/// <summary>
/// Default implementation of <see cref="IAgentSkillAssignmentService"/>.
/// Copies/deletes SKILL.md files in the agents layer of the skills storage hierarchy.
/// </summary>
public sealed class AgentSkillAssignmentService : IAgentSkillAssignmentService
{
    private readonly ISkillsRegistry _registry;
    private readonly ILogger<AgentSkillAssignmentService> _logger;

    public AgentSkillAssignmentService(
        ISkillsRegistry registry,
        ILogger<AgentSkillAssignmentService>? logger = null)
    {
        _registry = registry;
        _logger = logger ?? NullLogger<AgentSkillAssignmentService>.Instance;
    }

    /// <inheritdoc />
    public async Task<bool> AssignAsync(string skillName, string agentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        // Find the skill in the global snapshot (system or installed layer only —
        // we do NOT copy from another agent's layer to avoid cross-agent pollution).
        var snapshot = await _registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var source = snapshot.Skills.FirstOrDefault(s =>
            string.Equals(s.Name, skillName, StringComparison.Ordinal) &&
            s.Layer is SkillLayer.System or SkillLayer.Installed);

        if (source is null)
        {
            _logger.LogWarning(
                "AssignSkill: skill '{SkillName}' not found in system/installed layers for agent '{AgentName}'.",
                skillName, agentName);
            return false;
        }

        var destDir = OpenClawNetPaths.ResolveSkillsAgentRoot(agentName, _logger);
        destDir = Path.Combine(destDir, skillName);
        Directory.CreateDirectory(destDir);

        var destFile = Path.Combine(destDir, "SKILL.md");

        // Copy the full source file (frontmatter + body) verbatim so the
        // registry can re-parse it correctly on next scan.
        File.Copy(source.SourcePath, destFile, overwrite: true);

        _logger.LogInformation(
            "AssignSkill: copied '{SkillName}' (layer={Layer}) to agent '{AgentName}' at '{DestFile}'.",
            skillName, source.Layer, agentName, destFile);

        return true;
    }

    /// <inheritdoc />
    public Task UnassignAsync(string skillName, string agentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var agentRoot = OpenClawNetPaths.ResolveSkillsAgentRoot(agentName, _logger);
        var skillDir = Path.Combine(agentRoot, skillName);

        if (Directory.Exists(skillDir))
        {
            Directory.Delete(skillDir, recursive: true);
            _logger.LogInformation(
                "UnassignSkill: removed '{SkillName}' from agent '{AgentName}'.",
                skillName, agentName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAssignedAsync(string agentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var agentRoot = OpenClawNetPaths.ResolveSkillsAgentRoot(agentName, _logger);

        if (!Directory.Exists(agentRoot))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var assigned = Directory
            .EnumerateDirectories(agentRoot)
            .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(assigned);
    }

    /// <inheritdoc />
    public async Task<SkillSyncResult> SyncAssignmentsAsync(
        string agentName,
        IEnumerable<string> skillNames,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var desired = new HashSet<string>(
            skillNames ?? [],
            StringComparer.Ordinal);

        var current = await GetAssignedAsync(agentName, ct).ConfigureAwait(false);
        var currentSet = new HashSet<string>(current, StringComparer.Ordinal);

        var toAssign = desired.Except(currentSet).ToList();
        var toUnassign = currentSet.Except(desired).ToList();

        var assigned = new List<string>();
        var notFound = new List<string>();

        foreach (var skill in toAssign)
        {
            var ok = await AssignAsync(skill, agentName, ct).ConfigureAwait(false);
            (ok ? assigned : notFound).Add(skill);
        }

        foreach (var skill in toUnassign)
        {
            await UnassignAsync(skill, agentName, ct).ConfigureAwait(false);
        }

        return new SkillSyncResult(assigned, toUnassign, notFound);
    }

    // ====================================================================
    // No private helpers needed — all logic lives in the public methods.
    // ====================================================================
}
