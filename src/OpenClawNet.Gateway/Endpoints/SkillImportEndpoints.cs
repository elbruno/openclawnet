using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenClawNet.Skills;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// K-4 — Two-step external skill import endpoints.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>POST /api/skills/import/preview</c> — fetch + parse + validate;
///   returns metadata only (Q5 — no body content) plus an opaque short-lived
///   <c>previewToken</c>. No disk write.</item>
///   <item><c>POST /api/skills/import/confirm</c> — accepts the previewToken
///   and writes the skill to the <c>installed</c> layer + provenance file;
///   returns 201 with the new skill name.</item>
/// </list>
/// <para>Status code mapping (see <see cref="SkillImportReasons"/>):</para>
/// <list type="bullet">
///   <item><c>RepoNotAllowed</c> → 403</item>
///   <item><c>InvalidSha</c> / <c>InvalidPath</c> / <c>UnsupportedExtension</c>
///   / <c>BodyTooLarge</c> / <c>MalformedSkill</c> / <c>InvalidName</c> → 400</item>
///   <item><c>NotFound</c> (raw 404) → 404</item>
///   <item><c>FetchFailed</c> (other GitHub failures) → 502</item>
///   <item><c>SkillAlreadyExists</c> → 409</item>
///   <item><c>PreviewNotFound</c> → 404</item>
///   <item><c>PreviewExpired</c> → 410</item>
/// </list>
/// </remarks>
public static class SkillImportEndpoints
{
    public static void MapSkillImportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/skills/import").WithTags("Skills.Import");

        group.MapPost("/preview", PostPreview).WithName("PostSkillImportPreview");
        group.MapPost("/confirm", PostConfirm).WithName("PostSkillImportConfirm");
        group.MapPost("/", PostImportFile)
            .WithName("PostSkillImportFile")
            .Accepts<IFormFile>("multipart/form-data")
            .WithDescription("Import a skill from a local .md file or .zip folder archive via FormData");
    }

    private static async Task<IResult> PostPreview(
        [FromBody] SkillImportPreviewRequestIn? body,
        ISkillImportService importer,
        CancellationToken ct)
    {
        if (body is null)
            return Problem(StatusCodes.Status400BadRequest, SkillImportReasons.InvalidPath, "Request body required.");

        var result = await importer.PreviewAsync(
            new SkillImportRequest(body.Repo ?? "", body.Sha ?? "", body.Path ?? ""), ct).ConfigureAwait(false);

        if (result.Success)
        {
            return Results.Ok(new SkillImportPreviewOut(
                PreviewToken: result.Value!.PreviewToken,
                Repo: result.Value.Repo,
                Sha: result.Value.Sha,
                SourcePath: result.Value.SourcePath,
                SkillName: result.Value.SkillName,
                Description: result.Value.Description,
                BodyBytes: result.Value.BodyBytes,
                BodySha256: result.Value.BodySha256,
                ExpiresUtc: result.Value.ExpiresUtc));
        }

        return Problem(MapStatus(result.Reason!), result.Reason!, result.Detail);
    }

    private static async Task<IResult> PostConfirm(
        [FromBody] SkillImportConfirmRequestIn? body,
        ISkillImportService importer,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.PreviewToken))
            return Problem(StatusCodes.Status400BadRequest, SkillImportReasons.PreviewNotFound, "previewToken required.");

        var result = await importer.ConfirmAsync(body.PreviewToken, ct).ConfigureAwait(false);
        if (result.Success)
        {
            return Results.Created(
                $"/api/skills/{Uri.EscapeDataString(result.Value!.SkillName)}",
                new SkillImportConfirmOut(
                    SkillName: result.Value.SkillName,
                    Repo: result.Value.Repo,
                    Sha: result.Value.Sha,
                    BodySha256: result.Value.BodySha256));
        }

        return Problem(MapStatus(result.Reason!), result.Reason!, result.Detail);
    }

    private static async Task<IResult> PostImportFile(
        HttpContext httpContext,
        ISkillImportService importer,
        ILogger<GatewayProgramMarker> logger,
        CancellationToken ct)
    {
        if (!httpContext.Request.HasFormContentType)
            return Problem(StatusCodes.Status400BadRequest, "InvalidRequest", "Expected multipart/form-data.");

        IFormCollection form;
        try
        {
            form = await httpContext.Request.ReadFormAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BadHttpRequestException or InvalidDataException)
        {
            logger.LogWarning(ex, "Skill import file upload: malformed multipart.");
            return Problem(StatusCodes.Status400BadRequest, "InvalidRequest", "Malformed multipart payload.");
        }

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length <= 0)
            return Problem(StatusCodes.Status400BadRequest, "InvalidRequest", "No file in multipart payload.");

        var fileName = file.FileName ?? "unknown";
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (ext == ".md")
        {
            // Single .md file import
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync().ConfigureAwait(false);

            var result = await importer.ImportMarkdownFileAsync(content, fileName, ct).ConfigureAwait(false);
            if (result.Success)
            {
                return Results.Created(
                    $"/api/skills/{Uri.EscapeDataString(result.Value!.SkillName)}",
                    new SkillImportFileOut(
                        SkillName: result.Value.SkillName,
                        InstalledPath: result.Value.InstalledPath,
                        BodySha256: result.Value.BodySha256));
            }
            return Problem(MapStatus(result.Reason!), result.Reason!, result.Detail);
        }
        else if (ext == ".zip")
        {
            // Folder archive import
            using var stream = file.OpenReadStream();
            var result = await importer.ImportZipArchiveAsync(stream, fileName, ct).ConfigureAwait(false);
            if (result.Success)
            {
                return Results.Created(
                    $"/api/skills/{Uri.EscapeDataString(result.Value!.SkillName)}",
                    new SkillImportFileOut(
                        SkillName: result.Value.SkillName,
                        InstalledPath: result.Value.InstalledPath,
                        BodySha256: result.Value.BodySha256));
            }
            return Problem(MapStatus(result.Reason!), result.Reason!, result.Detail);
        }
        else
        {
            return Problem(StatusCodes.Status400BadRequest, SkillImportReasons.UnsupportedExtension,
                $"Only .md files and .zip archives are supported. Got '{ext}'.");
        }
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static int MapStatus(string reason) => reason switch
    {
        SkillImportReasons.RepoNotAllowed => StatusCodes.Status403Forbidden,
        SkillImportReasons.SkillAlreadyExists => StatusCodes.Status409Conflict,
        SkillImportReasons.NotFound => StatusCodes.Status404NotFound,
        SkillImportReasons.PreviewNotFound => StatusCodes.Status404NotFound,
        SkillImportReasons.PreviewExpired => StatusCodes.Status410Gone,
        SkillImportReasons.FetchFailed => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Problem(int status, string reason, string? detail)
        => Results.Json(new SkillImportProblemOut(reason, detail), statusCode: status);
}

// ====================================================================
// Wire DTOs (K-3 import wizard surface).
// ====================================================================

internal sealed record SkillImportPreviewRequestIn(string? Repo, string? Sha, string? Path);
internal sealed record SkillImportConfirmRequestIn(string? PreviewToken);

internal sealed record SkillImportPreviewOut(
    string PreviewToken,
    string Repo,
    string Sha,
    string SourcePath,
    string SkillName,
    string Description,
    int BodyBytes,
    string BodySha256,
    DateTimeOffset ExpiresUtc);

internal sealed record SkillImportConfirmOut(
    string SkillName,
    string Repo,
    string Sha,
    string BodySha256);

internal sealed record SkillImportFileOut(
    string SkillName,
    string InstalledPath,
    string BodySha256);

internal sealed record SkillImportProblemOut(string Reason, string? Detail);
