using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace OpenClawNet.Skills;

/// <summary>
/// K-1b — Concrete <see cref="ISkillRecord"/> implementation. Sealed +
/// immutable; constructed by <see cref="OpenClawNetSkillsRegistry"/> after
/// layer-precedence resolution.
/// </summary>
public sealed record LayeredSkill(
    string Name,
    string Description,
    SkillLayer Layer,
    string SourcePath,
    IReadOnlyDictionary<string, string> Metadata,
    string Body,
    /// <summary>SHA-256 of the SKILL.md body+metadata, used for snapshot id stability.</summary>
    string ContentHash) : ISkillRecord;

/// <summary>
/// K-1b — Immutable snapshot returned by <see cref="ISkillsRegistry.GetSnapshotAsync"/>.
/// </summary>
public sealed class SkillsSnapshot : ISkillsSnapshot
{
    public IReadOnlyList<ISkillRecord> Skills { get; }
    public DateTimeOffset BuiltUtc { get; }
    public string SnapshotId { get; }

    public SkillsSnapshot(IReadOnlyList<ISkillRecord> skills, DateTimeOffset builtUtc, string snapshotId)
    {
        Skills = skills;
        BuiltUtc = builtUtc;
        SnapshotId = snapshotId;
    }

    /// <summary>
    /// Computes a stable snapshot id from the resolved skill set: SHA-256
    /// over (sorted name + content hash) pairs, hex-encoded, first 16 chars.
    /// Identical resolved snapshots produce identical ids — required for
    /// K-3 hot-reload divergence detection.
    /// </summary>
    public static string ComputeId(IEnumerable<LayeredSkill> resolved)
    {
        var sb = new StringBuilder();
        foreach (var s in resolved.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            sb.Append(s.Name).Append('\0').Append(s.ContentHash).Append('\n');
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public static SkillsSnapshot Empty { get; } = new(
        Array.Empty<ISkillRecord>(),
        DateTimeOffset.UtcNow,
        "0000000000000000");
}

/// <summary>
/// K-1b — Diff between two snapshots, used by
/// <c>GET /api/skills/changes-since/{snapshotId}</c> for the K-3 hot-reload
/// banner.
/// </summary>
public sealed record SnapshotDiff(
    string PreviousSnapshotId,
    string CurrentSnapshotId,
    ImmutableArray<string> Added,
    ImmutableArray<string> Modified,
    ImmutableArray<string> Removed);
