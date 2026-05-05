namespace OpenClawNet.Web.Models.Skills;

/// <summary>
/// K-3 skills wire contract. Matches the K-1b backend Petey is rebuilding.
/// "Layer" is the user-visible primitive (system/installed/agent) — every row,
/// drawer, and console event surfaces it (D-1 / spec §1).
/// </summary>
public sealed record SkillDto(
    string Name,
    string? Description,
    string? Version,
    string Layer,                                 // "system" | "installed" | "agent"
    string? AgentScope,                           // null unless Layer == "agent"
    string Source,                                // "built-in" | "manual" | "awesome-copilot" | …
    string? SourceCommitSha,                      // null for built-in / manual
    string? BundleSha256,
    DateTimeOffset UpdatedUtc,
    string EffectiveLayer,                        // resolved winner if collision
    Dictionary<string, bool> EnabledByAgent       // agentName -> enabled
);

/// <summary>Body used by <c>POST /api/skills</c> (L-5 in-app authoring).</summary>
public sealed record CreateSkillRequest(
    string Name,
    string Description,
    string? Version,
    string Layer,                                 // "installed" only for v1 (system is read-only L-2)
    string? AgentScope,                           // optional — required when Layer == "agent"
    string[]? Tags,
    string Body                                   // raw markdown body (frontmatter is server-assembled)
);

/// <summary>Snapshot pulse — polled every 5s by <c>SkillsSnapshotBanner</c> (D-3).</summary>
public sealed record SkillsSnapshotDto(
    string Id,
    DateTimeOffset BuiltUtc,
    string? ChangeSummary
);

/// <summary>Diff between two snapshots (returned by <c>GET /api/skills/changes-since/{id}</c>).</summary>
public sealed record SkillsChangesDto(
    string PreviousSnapshotId,
    string CurrentSnapshotId,
    string[] Added,
    string[] Modified,
    string[] Removed
);

/// <summary>4xx body shape for skills endpoints. Mirrors the W-4 problem pattern.</summary>
public sealed record SkillsProblem(string Reason, string? Detail = null);
