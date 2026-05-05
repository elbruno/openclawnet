namespace OpenClawNet.Skills;

/// <summary>
/// K-4 — Public surface for the two-step external skill import flow.
/// Implementation lives in <see cref="SkillImportService"/>.
/// </summary>
public interface ISkillImportService
{
    /// <summary>
    /// Step 1 — fetch + parse + validate; returns a preview without writing
    /// anything. The returned <see cref="SkillImportPreview.PreviewToken"/>
    /// is short-lived (default 5 min, configurable via
    /// <see cref="SkillsImportOptions.PreviewTtlSeconds"/>).
    /// </summary>
    Task<SkillImportResult<SkillImportPreview>> PreviewAsync(SkillImportRequest request, CancellationToken ct = default);

    /// <summary>
    /// Step 2 — confirm a previously-issued preview. Writes to the
    /// <c>installed</c> layer + provenance metadata, registers via the
    /// registry rebuild, and returns the new skill name.
    /// </summary>
    Task<SkillImportResult<SkillImportConfirmed>> ConfirmAsync(string previewToken, CancellationToken ct = default);

    /// <summary>
    /// Import a markdown file directly from FormData upload.
    /// </summary>
    Task<SkillImportResult<SkillImportConfirmed>> ImportMarkdownFileAsync(string content, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Import a zip archive containing a skill folder structure (with SKILL.md).
    /// </summary>
    Task<SkillImportResult<SkillImportConfirmed>> ImportZipArchiveAsync(Stream zipStream, string fileName, CancellationToken ct = default);
}

/// <summary>K-4 — preview/confirm input.</summary>
public sealed record SkillImportRequest(string Repo, string Sha, string Path);

/// <summary>K-4 — preview output (Q5: body content NEVER returned, only metadata).</summary>
public sealed record SkillImportPreview(
    string PreviewToken,
    string Repo,
    string Sha,
    string SourcePath,
    string SkillName,
    string Description,
    int BodyBytes,
    string BodySha256,
    DateTimeOffset ExpiresUtc);

/// <summary>K-4 — confirm output.</summary>
public sealed record SkillImportConfirmed(
    string SkillName,
    string Repo,
    string Sha,
    string InstalledPath,
    string BodySha256);

/// <summary>
/// K-4 — typed outcome envelope. Endpoints translate <see cref="Reason"/>
/// to HTTP status codes (RepoNotAllowed=403, UnsupportedExtension=400,
/// InvalidName=400, BodyTooLarge=400, NotFound/Fetch failures=502 or 404,
/// SkillAlreadyExists=409, PreviewExpired=410, MalformedSkill=400, etc.)
/// </summary>
public sealed record SkillImportResult<T>(bool Success, T? Value, string? Reason, string? Detail)
{
    public static SkillImportResult<T> Ok(T value) => new(true, value, null, null);
    public static SkillImportResult<T> Fail(string reason, string? detail = null) => new(false, default, reason, detail);
}

/// <summary>K-4 — well-known reason strings used in <see cref="SkillImportResult{T}.Reason"/>.</summary>
public static class SkillImportReasons
{
    public const string RepoNotAllowed = "RepoNotAllowed";
    public const string InvalidSha = "InvalidSha";
    public const string InvalidPath = "InvalidPath";
    public const string UnsupportedExtension = "UnsupportedExtension";
    public const string FetchFailed = "FetchFailed";
    public const string NotFound = "NotFound";
    public const string BodyTooLarge = "BodyTooLarge";
    public const string MalformedSkill = "MalformedSkill";
    public const string InvalidName = "InvalidName";
    public const string SkillAlreadyExists = "SkillAlreadyExists";
    public const string PreviewNotFound = "PreviewNotFound";
    public const string PreviewExpired = "PreviewExpired";
}
