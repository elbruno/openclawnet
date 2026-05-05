namespace OpenClawNet.Skills;

/// <summary>
/// K-4 — Bound to the <c>SkillsImport</c> configuration section. Controls
/// the external-repo allowlist for the <c>/api/skills/import/*</c> flow.
/// </summary>
/// <remarks>
/// Default: only <c>github/awesome-copilot</c> is allowed. Operators can
/// extend via <c>appsettings.json</c>:
/// <code>
/// "SkillsImport": {
///   "AllowedRepos": [ "github/awesome-copilot", "myorg/internal-skills" ],
///   "PreviewTtlSeconds": 300
/// }
/// </code>
/// Repo identifiers are case-insensitive <c>owner/repo</c> pairs.
/// </remarks>
public sealed class SkillsImportOptions
{
    public const string SectionName = "SkillsImport";

    /// <summary>
    /// Allowlisted source repositories in <c>owner/repo</c> form. Matched
    /// case-insensitively. Anything not on this list is rejected with
    /// <c>RepoNotAllowed</c>.
    /// </summary>
    public string[] AllowedRepos { get; set; } = ["github/awesome-copilot"];

    /// <summary>
    /// Preview-token lifetime in seconds. Default 300 (5 min) per K-4 spec.
    /// </summary>
    public int PreviewTtlSeconds { get; set; } = 300;
}
