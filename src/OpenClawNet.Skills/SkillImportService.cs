using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Storage;

namespace OpenClawNet.Skills;

/// <summary>
/// K-4 — External-skill import implementation. Two-step approval flow
/// (preview + confirm) backed by an in-memory short-lived preview cache.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline (preview):
/// allowlist → SHA shape → path shape (.md only — L-4) → fetch via
/// <c>github-raw</c> named HttpClient with <see cref="MaxBodyBytes"/> cap →
/// frontmatter parse → name regex (H-5) + reserved-name guard → conflict
/// check (<c>installed/{name}</c> must not exist) → mint preview token.
/// </para>
/// <para>
/// Pipeline (confirm): look up preview → re-check conflict → write
/// <c>SKILL.md</c> + <c>.import.json</c> provenance file under
/// <c>{StorageRoot}/skills/installed/{name}/</c> via
/// <see cref="ISafePathResolver"/> (defense in depth) → trigger registry
/// rebuild → emit <c>SkillImported</c> audit event.
/// </para>
/// <para>Q1: imports land DISABLED for all agents (no enabled.json
/// touched). Q5: SKILL.md body is never returned in DTOs and never logged.</para>
/// </remarks>
internal sealed class SkillImportService : ISkillImportService
{
    /// <summary>S-11 / Drummond AC-K2-4 — 256 KB cap on the SKILL.md body.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>K-4 — named HttpClient from <see cref="IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "github-raw";

    // agentskills.io name regex (matches SkillEndpoints).
    private static readonly Regex NameRegex = new(
        @"^[a-z0-9]([-a-z0-9]{0,62}[a-z0-9])?$",
        RegexOptions.Compiled);

    // owner/repo shape — keep consistent with the allowlist comparison.
    private static readonly Regex RepoRegex = new(
        @"^[A-Za-z0-9._-]{1,100}/[A-Za-z0-9._-]{1,100}$",
        RegexOptions.Compiled);

    // git sha shape: 7-40 hex chars (allow upper for tolerance, lower in storage).
    private static readonly Regex ShaRegex = new(
        @"^[A-Fa-f0-9]{7,40}$",
        RegexOptions.Compiled);

    // S-4 reserved names — aligned with SkillEndpoints.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "installed", "agents", "enabled", "snapshot", "changes-since",
        "memory", "doc-processor",
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenClawNetSkillsRegistry _registry;
    private readonly ISafePathResolver _safePathResolver;
    private readonly ISkillImportLogger _audit;
    private readonly IOptionsMonitor<SkillsImportOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SkillImportService> _logger;

    private readonly ConcurrentDictionary<string, PreviewEntry> _previews =
        new(StringComparer.Ordinal);

    public SkillImportService(
        IHttpClientFactory httpFactory,
        OpenClawNetSkillsRegistry registry,
        ISafePathResolver safePathResolver,
        IOptionsMonitor<SkillsImportOptions> options,
        ISkillImportLogger? audit = null,
        TimeProvider? timeProvider = null,
        ILogger<SkillImportService>? logger = null)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _safePathResolver = safePathResolver ?? throw new ArgumentNullException(nameof(safePathResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? NullSkillImportLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SkillImportService>.Instance;
    }

    // ====================================================================
    // Preview
    // ====================================================================

    public async Task<SkillImportResult<SkillImportPreview>> PreviewAsync(
        SkillImportRequest request, CancellationToken ct = default)
    {
        if (request is null)
            return SkillImportResult<SkillImportPreview>.Fail(SkillImportReasons.InvalidPath, "Request body required.");

        var (validation, normalizedRepo, normalizedSha, normalizedPath) = ValidateRequest(request);
        if (validation is not null)
            return SkillImportResult<SkillImportPreview>.Fail(validation.Reason!, validation.Detail);

        // TODO(K-4-v2): script support. v1 is md-only per L-4.
        if (!normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.UnsupportedExtension,
                $"Only .md files are supported in v1 (L-4). Got '{normalizedPath}'.");
        }

        // ---- Fetch ----
        FetchOutcome fetched;
        try
        {
            fetched = await FetchAsync(normalizedRepo, normalizedSha, normalizedPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Skill import fetch failed for repo={Repo} sha={Sha} path={Path}.",
                normalizedRepo, normalizedSha, normalizedPath);
            return SkillImportResult<SkillImportPreview>.Fail(SkillImportReasons.FetchFailed, ex.Message);
        }

        if (fetched.Reason is not null)
            return SkillImportResult<SkillImportPreview>.Fail(fetched.Reason, fetched.Detail);

        var bodyBytes = fetched.Body!;
        var content = Encoding.UTF8.GetString(bodyBytes);

        // ---- Parse ----
        SkillFrontmatterParser.ParsedSkill parsed;
        try
        {
            // Use the basename (without .md) as the fallback name; the
            // frontmatter `name` key wins when present.
            var fallbackName = System.IO.Path.GetFileNameWithoutExtension(normalizedPath);
            parsed = SkillFrontmatterParser.Parse(content, fallbackName);
        }
        catch (FormatException ex)
        {
            return SkillImportResult<SkillImportPreview>.Fail(SkillImportReasons.MalformedSkill, ex.Message);
        }
        catch (Exception ex) when (ex.GetType().FullName?.StartsWith("YamlDotNet", StringComparison.Ordinal) == true)
        {
            // YamlDotNet exceptions (SemanticErrorException, SyntaxErrorException, etc.)
            return SkillImportResult<SkillImportPreview>.Fail(SkillImportReasons.MalformedSkill, $"Invalid YAML: {ex.Message}");
        }

        // ---- Name validation ----
        if (!IsValidSkillName(parsed.Name))
        {
            return SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.InvalidName,
                $"Resolved skill name '{parsed.Name}' fails the agentskills.io name allowlist (H-5).");
        }
        if (ReservedNames.Contains(parsed.Name))
        {
            return SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.InvalidName,
                $"Skill name '{parsed.Name}' is reserved (S-4).");
        }

        // ---- Conflict check (installed layer only) ----
        if (InstalledSkillExists(parsed.Name))
        {
            return SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.SkillAlreadyExists,
                $"An installed skill named '{parsed.Name}' already exists. Delete it first.");
        }

        var bodySha256 = ComputeSha256(bodyBytes);
        var token = MintToken();
        var ttlSeconds = Math.Max(30, _options.CurrentValue.PreviewTtlSeconds);
        var expires = _time.GetUtcNow().AddSeconds(ttlSeconds);

        var preview = new SkillImportPreview(
            PreviewToken: token,
            Repo: normalizedRepo,
            Sha: normalizedSha,
            SourcePath: normalizedPath,
            SkillName: parsed.Name,
            Description: parsed.Description,
            BodyBytes: bodyBytes.Length,
            BodySha256: bodySha256,
            ExpiresUtc: expires);

        _previews[token] = new PreviewEntry(preview, content, expires);
        PurgeExpired();

        _audit.ImportRequested(
            normalizedRepo, normalizedSha, normalizedPath,
            parsed.Name, bodySha256, bodyBytes.Length);

        _logger.LogInformation(
            "Skill import preview minted: skill={Name}, repo={Repo}, sha={Sha}, bytes={Bytes}, hash={Hash}.",
            parsed.Name, normalizedRepo, normalizedSha, bodyBytes.Length, bodySha256);

        return SkillImportResult<SkillImportPreview>.Ok(preview);
    }

    // ====================================================================
    // Confirm
    // ====================================================================

    public async Task<SkillImportResult<SkillImportConfirmed>> ConfirmAsync(
        string previewToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(previewToken))
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.PreviewNotFound, "previewToken is required.");
        }

        if (!_previews.TryRemove(previewToken, out var entry))
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.PreviewNotFound,
                "Preview token not found. Call /api/skills/import/preview first.");
        }

        if (_time.GetUtcNow() > entry.ExpiresUtc)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.PreviewExpired,
                $"Preview token expired at {entry.ExpiresUtc:O}. Re-run preview.");
        }

        var preview = entry.Preview;

        // Re-check that the allowlist still trusts this repo (operator may
        // have removed it between preview and confirm).
        if (!IsRepoAllowed(preview.Repo))
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.RepoNotAllowed,
                $"Repo '{preview.Repo}' is no longer in the allowlist.");
        }

        // Re-check the conflict — another importer (or manual drop) may
        // have created the same skill while the preview sat in the cache.
        if (InstalledSkillExists(preview.SkillName))
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.SkillAlreadyExists,
                $"An installed skill named '{preview.SkillName}' already exists.");
        }

        // 256 KB enforced again on write (Drummond AC-K2-4: "...check in
        // CreateSkill endpoint and in any import flow"). Defense in depth.
        var contentBytes = Encoding.UTF8.GetByteCount(entry.Content);
        if (contentBytes > MaxBodyBytes)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.BodyTooLarge,
                $"SKILL.md exceeds {MaxBodyBytes} bytes (was {contentBytes}).");
        }

        var installedRoot = OpenClawNetPaths.ResolveSkillsInstalledRoot(_logger);
        // SafePathResolver gives defense-in-depth even though name is
        // already validated against the H-5-aligned regex.
        string skillFolder;
        try
        {
            skillFolder = _safePathResolver.ResolveSafePath(installedRoot, preview.SkillName);
        }
        catch (Exception ex)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.InvalidName, ex.Message);
        }

        Directory.CreateDirectory(skillFolder);

        var skillMdPath = System.IO.Path.Combine(skillFolder, "SKILL.md");
        await File.WriteAllTextAsync(skillMdPath, entry.Content, ct).ConfigureAwait(false);

        // Provenance metadata adjacent to SKILL.md (not enabled.json — that
        // file is per-AGENT under skills/agents/{agent}/, not per-SKILL).
        // .import.json sits next to SKILL.md so the registry's standard
        // walk ignores it (it only reads SKILL.md per layer scan).
        var provenance = new
        {
            repo = preview.Repo,
            sha = preview.Sha,
            sourcePath = preview.SourcePath,
            skillName = preview.SkillName,
            bodySha256 = preview.BodySha256,
            bodyBytes = preview.BodyBytes,
            importedUtc = _time.GetUtcNow(),
            importer = "K-4 SkillImportService",
        };
        var importJsonPath = System.IO.Path.Combine(skillFolder, ".import.json");
        await File.WriteAllTextAsync(
            importJsonPath,
            JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);

        // Force an immediate rebuild so the new skill appears in the next
        // snapshot (the watcher will also fire; rebuild is idempotent).
        _registry.Rebuild();

        _audit.ImportApproved(previewToken, preview.Repo, preview.Sha, preview.SkillName);
        _audit.ImportCompleted(
            preview.Repo, preview.Sha, preview.SkillName,
            skillMdPath, preview.BodySha256, preview.BodyBytes);

        _logger.LogInformation(
            "Skill imported: name={Name}, repo={Repo}, sha={Sha}, bytes={Bytes}, hash={Hash}, path={Path}.",
            preview.SkillName, preview.Repo, preview.Sha, preview.BodyBytes, preview.BodySha256, skillMdPath);

        return SkillImportResult<SkillImportConfirmed>.Ok(new SkillImportConfirmed(
            SkillName: preview.SkillName,
            Repo: preview.Repo,
            Sha: preview.Sha,
            InstalledPath: skillMdPath,
            BodySha256: preview.BodySha256));
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private (SkillImportResult<SkillImportPreview>? Failure,
             string Repo, string Sha, string Path)
        ValidateRequest(SkillImportRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Repo) || !RepoRegex.IsMatch(req.Repo))
        {
            return (SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.RepoNotAllowed, "Repo must be 'owner/repo'."),
                "", "", "");
        }
        if (!IsRepoAllowed(req.Repo))
        {
            return (SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.RepoNotAllowed,
                $"Repo '{req.Repo}' is not in SkillsImport:AllowedRepos."),
                "", "", "");
        }
        if (string.IsNullOrWhiteSpace(req.Sha) || !ShaRegex.IsMatch(req.Sha))
        {
            return (SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.InvalidSha,
                "Pin to a 7-40 hex git SHA. Branch names and tags are not allowed."),
                "", "", "");
        }
        if (string.IsNullOrWhiteSpace(req.Path))
        {
            return (SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.InvalidPath, "Path is required."),
                "", "", "");
        }
        var path = req.Path.Replace('\\', '/').Trim().TrimStart('/');
        if (path.Contains("..", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal))
        {
            return (SkillImportResult<SkillImportPreview>.Fail(
                SkillImportReasons.InvalidPath, "Path must not contain '..' or '//'."),
                "", "", "");
        }
        // Normalize sha to lowercase for storage and for raw URL.
        return (null, req.Repo.Trim(), req.Sha.Trim().ToLowerInvariant(), path);
    }

    private bool IsRepoAllowed(string repo)
    {
        var allowed = _options.CurrentValue.AllowedRepos ?? Array.Empty<string>();
        foreach (var entry in allowed)
        {
            if (string.Equals(entry, repo, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsValidSkillName(string? name)
        => !string.IsNullOrWhiteSpace(name) && NameRegex.IsMatch(name);

    private bool InstalledSkillExists(string name)
    {
        var installedRoot = OpenClawNetPaths.ResolveSkillsInstalledRoot(_logger);
        var candidate = System.IO.Path.Combine(installedRoot, name);
        return Directory.Exists(candidate);
    }

    private async Task<FetchOutcome> FetchAsync(
        string repo, string sha, string path, CancellationToken ct)
    {
        // raw.githubusercontent.com URL form: /{owner}/{repo}/{sha}/{path}
        var requestUri = $"{repo}/{sha}/{path}";

        var client = _httpFactory.CreateClient(HttpClientName);
        // Defensive: if a custom factory forgot the BaseAddress, fall back.
        if (client.BaseAddress is null)
            client.BaseAddress = new Uri("https://raw.githubusercontent.com/");

        using var resp = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return new FetchOutcome(null, SkillImportReasons.NotFound, $"GitHub raw returned 404 for {requestUri}.");
        if (!resp.IsSuccessStatusCode)
            return new FetchOutcome(null, SkillImportReasons.FetchFailed, $"GitHub raw returned {(int)resp.StatusCode} for {requestUri}.");

        // Bound the read at MaxBodyBytes + 1 so we can detect over-cap files
        // before allocating arbitrary memory.
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        while (total <= MaxBodyBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        if (total > MaxBodyBytes)
        {
            return new FetchOutcome(null, SkillImportReasons.BodyTooLarge,
                $"SKILL.md exceeds {MaxBodyBytes} bytes (S-11).");
        }

        var bytes = new byte[total];
        Buffer.BlockCopy(buffer, 0, bytes, 0, total);
        return new FetchOutcome(bytes, null, null);
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string MintToken()
    {
        // 24 random bytes → 32-char base64url. Plenty of entropy and
        // URL-safe so it can round-trip through query strings if needed.
        Span<byte> b = stackalloc byte[24];
        RandomNumberGenerator.Fill(b);
        return Convert.ToBase64String(b)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private void PurgeExpired()
    {
        var now = _time.GetUtcNow();
        foreach (var kv in _previews)
        {
            if (now > kv.Value.ExpiresUtc)
                _previews.TryRemove(kv.Key, out _);
        }
    }

    private sealed record PreviewEntry(SkillImportPreview Preview, string Content, DateTimeOffset ExpiresUtc);
    private sealed record FetchOutcome(byte[]? Body, string? Reason, string? Detail);

    // ====================================================================
    // File Import (FormData)
    // ====================================================================

    public async Task<SkillImportResult<SkillImportConfirmed>> ImportMarkdownFileAsync(
        string content, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.MalformedSkill, "File content is empty.");

        try
        {
            var skillName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            return await WriteSkillToInstalledAsync(content, skillName, fileName, ct).ConfigureAwait(false);
        }
        catch (FormatException ex)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(SkillImportReasons.MalformedSkill, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error importing markdown file: {FileName}", fileName);
            return SkillImportResult<SkillImportConfirmed>.Fail(SkillImportReasons.FetchFailed, ex.Message);
        }
    }

    public async Task<SkillImportResult<SkillImportConfirmed>> ImportZipArchiveAsync(
        Stream zipStream, string fileName, CancellationToken ct = default)
    {
        try
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Extract zip
                using var zipArchive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
                zipArchive.ExtractToDirectory(tempDir);

                // Find SKILL.md
                var skillMdPath = System.IO.Directory.GetFiles(tempDir, "SKILL.md", System.IO.SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrEmpty(skillMdPath))
                {
                    return SkillImportResult<SkillImportConfirmed>.Fail(
                        SkillImportReasons.MalformedSkill,
                        "ZIP archive must contain a SKILL.md file at the root or in a subdirectory.");
                }

                // Read content
                var content = await File.ReadAllTextAsync(skillMdPath, ct).ConfigureAwait(false);
                var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                var skillName = baseName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? baseName[..^4] : baseName;

                return await WriteSkillToInstalledAsync(content, skillName, fileName, ct).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* best effort cleanup */ }
            }
        }
        catch (FormatException ex)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(SkillImportReasons.MalformedSkill, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.MalformedSkill, $"Invalid ZIP archive: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error importing ZIP archive: {FileName}", fileName);
            return SkillImportResult<SkillImportConfirmed>.Fail(SkillImportReasons.FetchFailed, ex.Message);
        }
    }

    private async Task<SkillImportResult<SkillImportConfirmed>> WriteSkillToInstalledAsync(
        string content, string skillName, string fileName, CancellationToken ct)
    {
        // Parse and validate
        SkillFrontmatterParser.ParsedSkill parsed;
        try
        {
            parsed = SkillFrontmatterParser.Parse(content, skillName);
        }
        catch (FormatException ex)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(SkillImportReasons.MalformedSkill, ex.Message);
        }

        // Validate skill name
        if (!IsValidSkillName(parsed.Name))
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.InvalidName,
                $"Resolved skill name '{parsed.Name}' fails the agentskills.io name allowlist.");
        }
        if (ReservedNames.Contains(parsed.Name))
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.InvalidName,
                $"Skill name '{parsed.Name}' is reserved.");
        }

        // Check for conflicts
        if (InstalledSkillExists(parsed.Name))
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.SkillAlreadyExists,
                $"An installed skill named '{parsed.Name}' already exists.");
        }

        // Enforce 256 KB body size limit
        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (contentBytes > MaxBodyBytes)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(
                SkillImportReasons.BodyTooLarge,
                $"SKILL.md exceeds {MaxBodyBytes} bytes.");
        }

        // Write to installed layer
        var installedRoot = OpenClawNetPaths.ResolveSkillsInstalledRoot(_logger);
        string skillFolder;
        try
        {
            skillFolder = _safePathResolver.ResolveSafePath(installedRoot, parsed.Name);
        }
        catch (Exception ex)
        {
            return SkillImportResult<SkillImportConfirmed>.Fail(SkillImportReasons.InvalidName, ex.Message);
        }

        Directory.CreateDirectory(skillFolder);

        var skillMdPath = System.IO.Path.Combine(skillFolder, "SKILL.md");
        await File.WriteAllTextAsync(skillMdPath, content, ct).ConfigureAwait(false);

        // Write provenance metadata
        var bodySha256 = ComputeSha256(Encoding.UTF8.GetBytes(content));
        var provenance = new
        {
            fileName = fileName,
            skillName = parsed.Name,
            bodySha256 = bodySha256,
            bodyBytes = contentBytes,
            importedUtc = _time.GetUtcNow(),
            importer = "FormData FileImport",
        };
        var importJsonPath = System.IO.Path.Combine(skillFolder, ".import.json");
        await File.WriteAllTextAsync(
            importJsonPath,
            JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);

        // Force registry rebuild
        _registry.Rebuild();

        _audit.ImportCompleted(
            "local-upload", "local", parsed.Name,
            skillMdPath, bodySha256, contentBytes);

        _logger.LogInformation(
            "Skill imported from file: name={Name}, fileName={FileName}, bytes={Bytes}, hash={Hash}, path={Path}.",
            parsed.Name, fileName, contentBytes, bodySha256, skillMdPath);

        return SkillImportResult<SkillImportConfirmed>.Ok(new SkillImportConfirmed(
            SkillName: parsed.Name,
            Repo: "local-upload",
            Sha: "local",
            InstalledPath: skillMdPath,
            BodySha256: bodySha256));
    }
}
