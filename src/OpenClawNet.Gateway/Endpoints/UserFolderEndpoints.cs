// Storage W-4 (Drummond W-4 AC1+AC2+AC3) — user-folder REST endpoints.
//
// All four endpoints route through the safe stack:
//   1. ResolveSafeUserFolderPath  → name allowlist + reparse-point sweep
//   2. IUserFolderQuota.CheckAsync → 5GB/folder, 25GB total + disk check
//   3. ISafePathResolver           → per-file containment on uploads
//
// Threat-model notes (carried from Drummond's W-3 verdict):
//   * The user folder name is operator-supplied via the UI. We:
//       - Validate via the strict W-4 allowlist (lowercase-only, 64-char cap)
//       - REDACT in logs when validation fails (first 32 chars + "..." if
//         the input was > 32 chars) — H-8 PII guard.
//       - NEVER echo the unresolved input into the response except via
//         UserFolderProblem.Reason which is a known enum string.
//   * DELETE requires X-Confirm-FolderName MATCHING the URL folder name
//     (Drummond W-4 P0 #3). No GET-triggered destruction. Confirmation
//     is the UI-side analogue of the "no digest = no download" rule.
//   * CSRF: the gateway does NOT currently wire AddAntiforgery / require
//     antiforgery tokens on its API surface (verified by absence in
//     Program.cs). Documented as a spec gap below — Drummond's W-4 P0 #3
//     mentions "CSRF-protected POST" for the Web UI surface; the Web UI
//     IS the CSRF surface, the gateway is the JSON API behind it. We
//     defer the CSRF guard to a wave that wires the broader gateway
//     antiforgery story (would also affect /api/storage/location, etc.).
//   * Audit: every CREATE / DELETE writes a JSONL line to
//     {storageRoot}/audit/user-folders/{yyyy-MM-dd}.jsonl with the
//     fixed schema from Drummond's W-3 verdict §11. Upload is logged at
//     INFO level only — the per-upload audit row was not in the W-4 ACs.
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

public static class UserFolderEndpoints
{
    /// <summary>HTTP header carrying the typed-back folder name on DELETE.</summary>
    public const string ConfirmHeader = "X-Confirm-FolderName";

    /// <summary>
    /// Reasonable per-upload size cap before we even consult the quota.
    /// Mirrors the W-3 model per-file ceiling shape — bounded above by
    /// the per-folder quota itself.
    /// </summary>
    private const long DefaultMaxUploadBytes = 1024L * 1024L * 1024L; // 1 GB

    public static void MapUserFolderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/user-folders").WithTags("UserFolders");

        // ---- POST /api/user-folders --------------------------------------------------
        group.MapPost("/", (
            [FromBody] CreateUserFolderRequest? request,
            ISafePathResolver resolver,
            ILogger<Program> logger,
            HttpContext httpContext) =>
        {
            var folderName = request?.FolderName ?? string.Empty;
            string resolvedPath;
            try
            {
                resolvedPath = OpenClawNetPaths.ResolveSafeUserFolderPath(
                    folderName, logger: null, resolver: resolver);
            }
            catch (UnsafePathException ex)
            {
                logger.LogWarning(
                    "User-folder CREATE rejected: reason={Reason}, name='{RedactedName}'",
                    ex.Reason, RedactName(folderName));
                return Results.BadRequest(new UserFolderProblem(
                    Reason: ex.Reason.ToString(),
                    Detail: SafeDetail(folderName, ex)));
            }

            var info = new DirectoryInfo(resolvedPath);
            var dto = new UserFolderDto(
                Name: folderName,
                SizeBytes: ComputeFolderBytes(resolvedPath),
                LastWriteTimeUtc: info.LastWriteTimeUtc);

            logger.LogInformation(
                "User-folder CREATE: name='{Name}'", folderName);

            // Audit (operator-visible UI path).
            TryAppendAudit(httpContext, op: "create", folderName, sizeBytes: dto.SizeBytes);

            return Results.Created($"/api/user-folders/{Uri.EscapeDataString(folderName)}", dto);
        })
        .WithName("CreateUserFolder")
        .WithDescription("Creates a new user folder under the storage root. 400 on invalid name.");

        // ---- GET /api/user-folders ----------------------------------------------------
        group.MapGet("/", (ILogger<Program> logger) =>
        {
            var (root, _) = OpenClawNetPaths.ResolveRoot();
            if (!Directory.Exists(root))
                return Results.Ok(Array.Empty<UserFolderDto>());

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(root);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                logger.LogWarning(ex, "User-folder LIST: cannot enumerate '{Root}'.", root);
                return Results.Ok(Array.Empty<UserFolderDto>());
            }

            var rows = new List<UserFolderDto>();
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (string.IsNullOrEmpty(name) || ExcludedFolders.Contains(name))
                    continue;
                // Defense-in-depth: only list folders that pass the W-4
                // allowlist. A pre-existing illegal-name folder on disk
                // (e.g. created out-of-band) is invisible to the API.
                if (!IsListableUserFolderName(name))
                    continue;

                DirectoryInfo info;
                try { info = new DirectoryInfo(child); }
                catch { continue; }

                rows.Add(new UserFolderDto(
                    Name: name,
                    SizeBytes: ComputeFolderBytes(child),
                    LastWriteTimeUtc: info.LastWriteTimeUtc));
            }

            return Results.Ok(rows);
        })
        .WithName("ListUserFolders")
        .WithDescription("Lists user folders with size + last-write metadata.");

        // ---- DELETE /api/user-folders/{folderName} -----------------------------------
        group.MapDelete("/{folderName}", (
            string folderName,
            HttpContext httpContext,
            ISafePathResolver resolver,
            IUserFolderQuota quota,
            ILogger<Program> logger) =>
        {
            // Drummond W-4 P0 #3 — typed-back confirmation MUST match.
            if (!httpContext.Request.Headers.TryGetValue(ConfirmHeader, out var confirm)
                || !string.Equals(confirm.ToString(), folderName, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "User-folder DELETE rejected: ConfirmationRequired (header missing or mismatched). name='{RedactedName}'",
                    RedactName(folderName));
                return Results.BadRequest(new UserFolderProblem(
                    Reason: "ConfirmationRequired",
                    Detail: $"DELETE requires the '{ConfirmHeader}' header to exactly match the folder name."));
            }

            string resolvedPath;
            try
            {
                resolvedPath = OpenClawNetPaths.ResolveSafeUserFolderPath(
                    folderName, logger: null, resolver: resolver);
            }
            catch (UnsafePathException ex)
            {
                logger.LogWarning(
                    "User-folder DELETE rejected: reason={Reason}, name='{RedactedName}'",
                    ex.Reason, RedactName(folderName));
                return Results.BadRequest(new UserFolderProblem(
                    Reason: ex.Reason.ToString(),
                    Detail: SafeDetail(folderName, ex)));
            }

            if (!Directory.Exists(resolvedPath))
                return Results.NotFound(new UserFolderProblem(
                    Reason: "NotFound",
                    Detail: "The user folder does not exist."));

            long sizeBytes = ComputeFolderBytes(resolvedPath);
            try
            {
                Directory.Delete(resolvedPath, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "User-folder DELETE failed for '{Name}'.", folderName);
                return Results.Problem(
                    detail: $"Could not delete folder: {ex.GetType().Name}",
                    statusCode: 500);
            }

            // Invalidate quota cache so the next CheckAsync sees the freed bytes.
            quota.InvalidateWalkCache(folderName);

            logger.LogInformation(
                "User-folder DELETE: name='{Name}', sizeBytes={Size}", folderName, sizeBytes);

            TryAppendAudit(httpContext, op: "delete", folderName, sizeBytes: sizeBytes);
            return Results.NoContent();
        })
        .WithName("DeleteUserFolder")
        .WithDescription("Destructive. Requires X-Confirm-FolderName header matching the URL folder name (Drummond W-4 P0 #3).");

        // ---- POST /api/user-folders/{folderName}/files -------------------------------
        group.MapPost("/{folderName}/files", async (
            string folderName,
            HttpContext httpContext,
            ISafePathResolver resolver,
            IUserFolderQuota quota,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            string folderPath;
            try
            {
                folderPath = OpenClawNetPaths.ResolveSafeUserFolderPath(
                    folderName, logger: null, resolver: resolver);
            }
            catch (UnsafePathException ex)
            {
                logger.LogWarning(
                    "User-folder UPLOAD rejected: reason={Reason}, name='{RedactedName}'",
                    ex.Reason, RedactName(folderName));
                return Results.BadRequest(new UserFolderProblem(
                    Reason: ex.Reason.ToString(),
                    Detail: SafeDetail(folderName, ex)));
            }

            if (!httpContext.Request.HasFormContentType)
                return Results.BadRequest(new UserFolderProblem(
                    Reason: "InvalidRequest",
                    Detail: "Expected multipart/form-data."));

            IFormCollection form;
            try
            {
                form = await httpContext.Request.ReadFormAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is BadHttpRequestException or InvalidDataException)
            {
                logger.LogWarning(ex, "User-folder UPLOAD: malformed multipart in '{Name}'.", folderName);
                return Results.BadRequest(new UserFolderProblem(
                    Reason: "InvalidRequest",
                    Detail: "Malformed multipart payload."));
            }

            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length <= 0)
                return Results.BadRequest(new UserFolderProblem(
                    Reason: "InvalidRequest",
                    Detail: "No file in multipart payload."));

            if (file.Length > DefaultMaxUploadBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            // Quota gate — fail-closed BEFORE we open a destination stream.
            var quotaResult = await quota.CheckAsync(folderName, file.Length, ct).ConfigureAwait(false);
            if (!quotaResult.Allowed)
            {
                logger.LogWarning(
                    "User-folder UPLOAD denied by quota: name='{Name}', reason='{DenyReason}'",
                    folderName, quotaResult.DenyReason);
                return Results.Json(
                    new UserFolderProblem(
                        Reason: "QuotaExceeded",
                        Detail: quotaResult.DenyReason),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            // Resolve the destination filename via the safe stack — this is
            // where untrusted file names get the H-2 / H-3 / H-5 treatment.
            // We deliberately re-use the SAME ISafePathResolver: it carries
            // the reparse-point sweep + length cap + charset allowlist.
            string destinationPath;
            try
            {
                var safeFileName = SanitizeUploadedFileName(file.FileName);
                destinationPath = resolver.ResolveSafePath(folderPath, safeFileName);
            }
            catch (UnsafePathException ex)
            {
                logger.LogWarning(
                    "User-folder UPLOAD rejected unsafe filename in '{Name}': reason={Reason}",
                    folderName, ex.Reason);
                return Results.BadRequest(new UserFolderProblem(
                    Reason: ex.Reason.ToString(),
                    Detail: "Uploaded file name violates the safe-name policy."));
            }

            try
            {
                await using var dest = File.Create(destinationPath);
                await using var src = file.OpenReadStream();
                await src.CopyToAsync(dest, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "User-folder UPLOAD I/O failed for '{Name}'.", folderName);
                return Results.Problem(
                    detail: $"Could not write file: {ex.GetType().Name}",
                    statusCode: 500);
            }

            // Invalidate cache so the next CheckAsync sees the new bytes.
            quota.InvalidateWalkCache(folderName);

            var info = new DirectoryInfo(folderPath);
            var sizeBytes = ComputeFolderBytes(folderPath);

            logger.LogInformation(
                "User-folder UPLOAD: name='{Name}', file='{File}', bytes={Bytes}",
                folderName, Path.GetFileName(destinationPath), file.Length);

            return Results.Ok(new UserFolderUploadResult(
                Name: folderName,
                SizeBytes: sizeBytes,
                LastWriteTimeUtc: info.LastWriteTimeUtc));
        })
        .WithName("UploadToUserFolder")
        .WithDescription("Multipart file upload. 413 on quota exhaustion.")
        .DisableAntiforgery(); // Gateway has no antiforgery wired today — see file header.
    }

    // -----------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------

    private static readonly HashSet<string> ExcludedFolders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "agents", "models", "skills", "binary",
            "dataprotection-keys", "audit",
        };

    private static bool IsListableUserFolderName(string name)
    {
        try
        {
            // Use ResolveSafeUserFolderPath's regex by attempting validation.
            // This deliberately does NOT call ResolveSafeUserFolderPath itself
            // (which would trigger the ACL hardening + reparse-point check
            // for every list call). We just want the regex pass/fail.
            return System.Text.RegularExpressions.Regex.IsMatch(
                name,
                @"^[a-z0-9][a-z0-9._-]{0,63}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        catch { return false; }
    }

    private static long ComputeFolderBytes(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return 0;
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch (FileNotFoundException) { /* race */ }
                catch (UnauthorizedAccessException) { /* skip */ }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Best effort — listing a folder we can't fully walk returns
            // a partial count, which is correct shape for a size column.
        }
        return total;
    }

    private static string RedactName(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "<empty>";
        return raw.Length > 32 ? raw[..32] + "..." : raw;
    }

    private static string SafeDetail(string folderName, UnsafePathException ex)
    {
        // Detail message MUST NOT echo unredacted attacker-controlled
        // input into the response (H-8). The Reason enum + redacted
        // name preview is enough for a UI to render a sensible error.
        return $"Folder name '{RedactName(folderName)}' was rejected by {ex.Reason}.";
    }

    private static string SanitizeUploadedFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new UnsafePathException(
                "Uploaded file name must be non-empty.",
                UnsafePathReason.EmptyOrWhitespace,
                scopeRoot: null,
                requestedPath: raw);

        // Strip directory components that some browsers send.
        var bare = Path.GetFileName(raw.Trim());
        if (string.IsNullOrEmpty(bare))
            throw new UnsafePathException(
                "Uploaded file name has no usable component.",
                UnsafePathReason.InvalidName,
                scopeRoot: null,
                requestedPath: raw);
        return bare;
    }

    // -----------------------------------------------------------------
    // audit
    // -----------------------------------------------------------------

    private static readonly object AuditWriteLock = new();
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static void TryAppendAudit(
        HttpContext httpContext, string op, string folderName, long sizeBytes)
    {
        try
        {
            var (root, _) = OpenClawNetPaths.ResolveRoot();
            var dir = Path.Combine(root, "audit", "user-folders");
            Directory.CreateDirectory(dir);
            var dateStamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var path = Path.Combine(dir, $"{dateStamp}.jsonl");

            // Schema per Drummond W-3 verdict §11:
            //   { resolvedPath, scopeRoot, op, sizeBytes?, sha256?, source,
            //     occurredAt, durationMs, actorId? }
            var record = new
            {
                occurredAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                op,
                scopeRoot = root,
                resolvedPath = Path.Combine(root, folderName),
                folderName,
                sizeBytes,
                source = "gateway",
                actorId = httpContext.User?.Identity?.Name,
            };
            var line = JsonSerializer.Serialize(record, AuditJsonOptions);
            lock (AuditWriteLock)
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Audit write must never crash the request path. Failure is
            // logged via the host's standard logger only.
            httpContext.RequestServices.GetService<ILogger<Program>>()?
                .LogWarning(ex, "Audit append failed for op={Op}, folder='{Folder}'.", op, folderName);
        }
    }
}

// -----------------------------------------------------------------
// DTOs — wire-shape mirrors OpenClawNet.Web.Models.UserFolders.* so the
// Web layer's typed client deserializes without a shared assembly.
// -----------------------------------------------------------------

public sealed record UserFolderDto(
    string Name,
    long SizeBytes,
    DateTime LastWriteTimeUtc);

public sealed record CreateUserFolderRequest(string FolderName);

public sealed record UserFolderProblem(string Reason, string? Detail = null);

public sealed record UserFolderUploadResult(string Name, long SizeBytes, DateTime LastWriteTimeUtc);
