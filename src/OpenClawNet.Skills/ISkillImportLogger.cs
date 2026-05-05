namespace OpenClawNet.Skills;

/// <summary>
/// K-4 — Shim over Petey's K-2 audit logger. Wave 6 lands K-2 and K-4 in
/// parallel; this interface lets the import flow emit structured events
/// without taking a hard dependency on the K-2 implementation. When K-2
/// merges into <c>main</c>, the real <c>SkillsAuditLogger</c> will adapt
/// to this shape (or this shim will adapt to it).
/// </summary>
/// <remarks>
/// Three events are emitted across the two-step import:
/// <list type="bullet">
///   <item><c>SkillImportRequested</c> — a preview was created (no write).</item>
///   <item><c>SkillImportApproved</c> — the operator confirmed a preview.</item>
///   <item><c>SkillImported</c> — the file was successfully written + registered.</item>
/// </list>
/// Per Q5, no SKILL.md body content is included in any event payload —
/// only metadata (name, repo, sha, content-hash, byte-length).
/// </remarks>
public interface ISkillImportLogger
{
    void ImportRequested(string repo, string sha, string sourcePath, string skillName, string bodySha256, int bodyBytes);
    void ImportApproved(string previewToken, string repo, string sha, string skillName);
    void ImportCompleted(string repo, string sha, string skillName, string installedPath, string bodySha256, int bodyBytes);
}

/// <summary>
/// K-4 — No-op default. Replaced by the K-2 implementation when it lands.
/// </summary>
internal sealed class NullSkillImportLogger : ISkillImportLogger
{
    public static readonly NullSkillImportLogger Instance = new();

    public void ImportRequested(string repo, string sha, string sourcePath, string skillName, string bodySha256, int bodyBytes) { }
    public void ImportApproved(string previewToken, string repo, string sha, string skillName) { }
    public void ImportCompleted(string repo, string sha, string skillName, string installedPath, string bodySha256, int bodyBytes) { }
}
