namespace OpenClawNet.Skills;

/// <summary>
/// K-1 contract: the layered, precedence-resolved view of skills available
/// to the OpenClawNet agent runtime. K-1b will provide the file-watching,
/// per-agent-resolving implementation; K-1a ships a no-op stub
/// (<see cref="StubSkillsRegistry"/>) that returns an empty snapshot so the
/// solution compiles and the runtime keeps working with zero skills active.
/// </summary>
/// <remarks>
/// Per K-D-1 (mark-k1-design-decisions inbox doc), the registry is the single
/// authority for what skills exist and which agent sees what — precedence
/// (<c>system</c> → <c>installed</c> → <c>agents/{name}</c>) is resolved here
/// BEFORE any skill set is handed to a Microsoft Agent Framework
/// <c>AgentSkillsProvider</c>. This keeps layer attribution available for K-2
/// logging (<c>Skills.SnapshotResolved</c> event) and per-agent enable/disable
/// filtering (<c>enabled.json</c>, K-3) inside our wrapper, where MAF cannot
/// see those metadata fields.
/// </remarks>
public interface ISkillsRegistry
{
    /// <summary>
    /// Returns the current global, precedence-resolved snapshot. Implementations
    /// are expected to cache the snapshot until a watcher fires (K-1b).
    /// </summary>
    Task<ISkillsSnapshot> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns whether <paramref name="skillName"/> is enabled for
    /// <paramref name="agentName"/>, applying the per-agent enable/disable
    /// overlay (K-3). Implementations should consult the snapshot's effective
    /// layer for the skill before checking the overlay.
    /// </summary>
    Task<bool> IsEnabledForAgentAsync(string skillName, string agentName, CancellationToken ct = default);
}

/// <summary>
/// Immutable, precedence-resolved view of every layered skill known to the
/// registry at the moment the snapshot was built. K-1b stamps a SHA-256
/// 16-hex-prefix into <see cref="SnapshotId"/> for K-2 log correlation
/// (ratified per <c>copilot-snapshotid-format-ratified.md</c>).
/// </summary>
public interface ISkillsSnapshot
{
    /// <summary>The resolved set, after applying layer precedence.</summary>
    IReadOnlyList<ISkillRecord> Skills { get; }

    /// <summary>UTC timestamp the snapshot was built.</summary>
    DateTimeOffset BuiltUtc { get; }

    /// <summary>Stable identifier for log correlation: SHA-256 16-hex prefix
    /// of the resolved (sorted name + content hash) set. Identical resolved
    /// snapshots produce identical ids — required for K-3 hot-reload divergence
    /// detection.</summary>
    string SnapshotId { get; }
}

/// <summary>
/// One layered skill resolved into the active snapshot. Carries layer attribution
/// so K-2 logs and the K-3 UI can show which folder a skill came from after
/// precedence resolution.
/// </summary>
public interface ISkillRecord
{
    /// <summary>agentskills.io <c>name</c> frontmatter (regex
    /// <c>^[a-z0-9]([-a-z0-9]{0,62}[a-z0-9])?$</c>).</summary>
    string Name { get; }

    /// <summary>agentskills.io <c>description</c> frontmatter (≤1024 chars).</summary>
    string Description { get; }

    /// <summary>Which layer this record was sourced from after precedence resolution.</summary>
    SkillLayer Layer { get; }

    /// <summary>Absolute path to the source <c>SKILL.md</c> file on disk.</summary>
    string SourcePath { get; }

    /// <summary>Free-form frontmatter <c>metadata</c> (K-1 carries
    /// <c>source: built-in</c> for system layer skills).</summary>
    IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Raw markdown body of the SKILL.md (excluding frontmatter).</summary>
    string Body { get; }
}

/// <summary>
/// Layer of origin after precedence resolution.
/// </summary>
/// <remarks>
/// Order is significant — later layers win on name collision per K-D-1 / proposal §5.
/// </remarks>
public enum SkillLayer
{
    /// <summary>Bundled with the gateway (<c>{StorageRoot}\skills\system</c>).</summary>
    System = 0,

    /// <summary>Marketplace or hand-installed (<c>{StorageRoot}\skills\installed</c>).</summary>
    Installed = 1,

    /// <summary>Per-agent overlay (<c>{StorageRoot}\skills\agents\{name}</c>).</summary>
    Agent = 2,
}
