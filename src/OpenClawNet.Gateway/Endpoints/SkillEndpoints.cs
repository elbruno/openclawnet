using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using OpenClawNet.Skills;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// K-1b #5 — real <c>/api/skills/*</c> implementations replacing the K-1a
/// 503 stubs. Wire-compatible with Helly's K-3 <c>SkillsClient</c>
/// (<c>src/OpenClawNet.Web/Services/SkillsClient.cs</c>) and her DTO shapes
/// in <c>src/OpenClawNet.Web/Models/Skills/SkillDtos.cs</c>.
/// </summary>
/// <remarks>
/// Endpoints exposed:
/// <list type="bullet">
///   <item><c>GET /api/skills/snapshot</c> — cheap pulse for the K-3 banner (5s poll).</item>
///   <item><c>GET /api/skills</c> — full per-layer list with per-agent enable map.</item>
///   <item><c>GET /api/skills/{name}</c> — single-skill detail.</item>
///   <item><c>POST /api/skills</c> — create an Installed-layer skill (L-2: System is read-only).</item>
///   <item><c>POST /api/skills/reload</c> — force rebuild of skills snapshot.</item>
///   <item><c>POST /api/skills/{name}/enable</c> — enable skill for all agents.</item>
///   <item><c>POST /api/skills/{name}/disable</c> — disable skill for all agents.</item>
///   <item><c>PUT /api/skills/{name}/enabled-for/{agentName}</c> — toggle per-agent enable.</item>
///   <item><c>PATCH /api/skills/enabled</c> — same as PUT, accepts <c>{agent, skill, enabled}</c> (Helly UI client form).</item>
///   <item><c>DELETE /api/skills/{name}</c> — Installed layer only (L-2 enforced).</item>
///   <item><c>GET /api/skills/changes-since/{snapshotId}</c> — diff for the hot-reload banner (Q2).</item>
/// </list>
/// All path inputs route through <see cref="ISafePathResolver"/> via
/// <see cref="OpenClawNetPaths"/> helpers — no <c>Path.GetFullPath</c>
/// callsites here (Drummond AC1).
/// </remarks>
public static class SkillEndpoints
{
    // Reserved skill names (S-4: skills the system uses internally).
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "installed", "agents", "enabled", "snapshot", "changes-since",
    };

    // agentskills.io name regex (per ISkillsRegistry.cs ISkillRecord.Name doc).
    private static readonly Regex NameRegex = new(
        @"^[a-z0-9]([-a-z0-9]{0,62}[a-z0-9])?$",
        RegexOptions.Compiled);

    public static void MapSkillEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/skills").WithTags("Skills");

        group.MapGet("/snapshot", GetSnapshot).WithName("GetSkillsSnapshot");
        group.MapGet("/", ListSkills).WithName("ListSkills");
        group.MapGet("/changes-since/{snapshotId}", GetChangesSince).WithName("GetSkillsChangesSince");
        group.MapGet("/{name}", GetSkill).WithName("GetSkill");
        group.MapPost("/", CreateSkill).WithName("CreateSkill");
        group.MapPost("/reload", ReloadSkills).WithName("ReloadSkills");
        group.MapPost("/{name}/enable", EnableSkill).WithName("EnableSkill");
        group.MapPost("/{name}/disable", DisableSkill).WithName("DisableSkill");
        group.MapPut("/{name}/enabled-for/{agentName}", PutEnabledFor).WithName("PutSkillEnabledFor");
        group.MapPatch("/enabled", PatchEnabled).WithName("PatchSkillEnabled");
        group.MapDelete("/{name}", DeleteSkill).WithName("DeleteSkill");
    }

    // ====================================================================
    // GET /api/skills/snapshot
    // ====================================================================
    private static async Task<IResult> GetSnapshot(ISkillsRegistry registry, CancellationToken ct)
    {
        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        return Results.Ok(new SkillsSnapshotDtoOut(
            Id: snap.SnapshotId,
            BuiltUtc: snap.BuiltUtc,
            ChangeSummary: null));
    }

    // ====================================================================
    // GET /api/skills
    // ====================================================================
    private static async Task<IResult> ListSkills(ISkillsRegistry registry, CancellationToken ct)
    {
        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var enabledByAgent = LoadAllEnabledMaps(registry);

        var dtos = snap.Skills
            .Select(s => ToDto(s, enabledByAgent))
            .ToList();
        return Results.Ok(dtos);
    }

    // ====================================================================
    // GET /api/skills/{name}
    // ====================================================================
    private static async Task<IResult> GetSkill(string name, ISkillsRegistry registry, CancellationToken ct)
    {
        if (!IsValidSkillName(name))
            return Problem(StatusCodes.Status400BadRequest, "invalid_name", $"Skill name '{name}' fails the agentskills.io name regex.");

        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var record = snap.Skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (record is null)
            return Problem(StatusCodes.Status404NotFound, "not_found", $"Skill '{name}' not in current snapshot.");

        var enabledByAgent = LoadAllEnabledMaps(registry, name);
        return Results.Ok(ToDto(record, enabledByAgent));
    }

    // ====================================================================
    // POST /api/skills — create Installed-layer skill
    // ====================================================================
    private static async Task<IResult> CreateSkill(
        [FromBody] CreateSkillRequestIn req,
        OpenClawNetSkillsRegistry registry,
        ISafePathResolver safePathResolver,
        ILogger<OpenClawNetSkillsRegistry> logger,
        CancellationToken ct)
    {
        if (req is null) return Problem(StatusCodes.Status400BadRequest, "missing_body");
        if (!IsValidSkillName(req.Name))
            return Problem(StatusCodes.Status400BadRequest, "invalid_name", "Name must match ^[a-z0-9]([-a-z0-9]{0,62}[a-z0-9])?$");
        if (ReservedNames.Contains(req.Name))
            return Problem(StatusCodes.Status400BadRequest, "reserved_name", $"'{req.Name}' is reserved (S-4).");
        if (string.IsNullOrWhiteSpace(req.Body))
            return Problem(StatusCodes.Status400BadRequest, "empty_body", "Skill body must not be empty.");

        // L-2: only the Installed layer accepts in-app authoring. System is
        // read-only (bundled). Agent overlays are out-of-scope for create
        // (handled by drag-drop / import flows in K-3 v2).
        if (!string.Equals(req.Layer, "installed", StringComparison.OrdinalIgnoreCase))
            return Problem(StatusCodes.Status400BadRequest, "unsupported_layer",
                "Only 'installed' layer skills can be created via this endpoint (L-2).");

        var installedRoot = OpenClawNetPaths.ResolveSkillsInstalledRoot(logger);
        // SafePathResolver guards against name traversal even though we
        // already validated the regex — defense in depth (AC1).
        var skillFolder = safePathResolver.ResolveSafePath(installedRoot, req.Name);
        if (Directory.Exists(skillFolder))
            return Problem(StatusCodes.Status409Conflict, "already_exists", $"Installed skill '{req.Name}' already exists.");

        Directory.CreateDirectory(skillFolder);
        var skillMd = AssembleSkillMd(req);
        var skillMdPath = Path.Combine(skillFolder, "SKILL.md");
        await File.WriteAllTextAsync(skillMdPath, skillMd, ct).ConfigureAwait(false);

        // Trigger an immediate rebuild so the new skill appears in the next
        // snapshot without waiting for the watcher (which will also fire,
        // but the rebuild is idempotent — same SnapshotId on re-entry).
        registry.Rebuild();

        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var record = snap.Skills.FirstOrDefault(s => string.Equals(s.Name, req.Name, StringComparison.Ordinal));
        if (record is null)
            return Problem(StatusCodes.Status500InternalServerError, "post_create_missing",
                $"Skill '{req.Name}' written to disk but not in rebuilt snapshot.");

        return Results.Created(
            $"/api/skills/{Uri.EscapeDataString(req.Name)}",
            ToDto(record, LoadAllEnabledMaps(registry, req.Name)));
    }

    // ====================================================================
    // PUT /api/skills/{name}/enabled-for/{agentName}
    // ====================================================================
    private sealed record EnabledBody(bool Enabled);

    private static async Task<IResult> PutEnabledFor(
        string name,
        string agentName,
        [FromBody] EnabledBody body,
        OpenClawNetSkillsRegistry registry,
        HttpContext http,
        CancellationToken ct)
    {
        return await SetEnabledCore(name, agentName, body?.Enabled ?? false, registry, http, ct).ConfigureAwait(false);
    }

    // ====================================================================
    // PATCH /api/skills/enabled — Helly UI form: { agent, skill, enabled }
    // ====================================================================
    private sealed record PatchEnabledBody(string Agent, string Skill, bool Enabled);

    private static async Task<IResult> PatchEnabled(
        [FromBody] PatchEnabledBody body,
        OpenClawNetSkillsRegistry registry,
        HttpContext http,
        CancellationToken ct)
    {
        if (body is null) return Problem(StatusCodes.Status400BadRequest, "missing_body");
        return await SetEnabledCore(body.Skill, body.Agent, body.Enabled, registry, http, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> SetEnabledCore(
        string skillName, string agentName, bool enabled,
        OpenClawNetSkillsRegistry registry, HttpContext http, CancellationToken ct)
    {
        if (!IsValidSkillName(skillName))
            return Problem(StatusCodes.Status400BadRequest, "invalid_skill_name");
        if (string.IsNullOrWhiteSpace(agentName))
            return Problem(StatusCodes.Status400BadRequest, "invalid_agent_name");

        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        if (!snap.Skills.Any(s => string.Equals(s.Name, skillName, StringComparison.Ordinal)))
            return Problem(StatusCodes.Status404NotFound, "skill_not_found", $"Skill '{skillName}' not in current snapshot.");

        var requestedBy = http.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(requestedBy)) requestedBy = "anonymous";

        try
        {
            await registry.SetEnabledForAgentAsync(agentName, skillName, enabled, requestedBy, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", ex.Message);
        }

        return Results.NoContent();
    }

    // ====================================================================
    // POST /api/skills/reload — force rebuild of skills snapshot
    // ====================================================================
    private static async Task<IResult> ReloadSkills(
        OpenClawNetSkillsRegistry registry,
        CancellationToken ct)
    {
        registry.Rebuild();
        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            reloaded = true,
            count = snap.Skills.Count
        });
    }

    // ====================================================================
    // POST /api/skills/{name}/enable — enable skill for all agents
    // ====================================================================
    private static async Task<IResult> EnableSkill(
        string name,
        OpenClawNetSkillsRegistry registry,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsValidSkillName(name))
            return Problem(StatusCodes.Status400BadRequest, "invalid_name");

        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var record = snap.Skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (record is null)
            return Problem(StatusCodes.Status404NotFound, "not_found", $"Skill '{name}' not in current snapshot.");

        var requestedBy = http.User?.Identity?.Name ?? "anonymous";
        
        // Get all agent folders and enable for each
        var (rootPath, _) = OpenClawNetPaths.ResolveRoot();
        var agentsRoot = Path.Combine(rootPath, "skills", "agents");
        var agents = Directory.Exists(agentsRoot) 
            ? Directory.EnumerateDirectories(agentsRoot).Select(Path.GetFileName).ToList()
            : new List<string?>();

        // If no agents exist, enable for a default agent
        if (agents.Count == 0)
            agents.Add("default");

        foreach (var agentName in agents.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            try
            {
                await registry.SetEnabledForAgentAsync(agentName!, name, true, requestedBy, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Log but continue with other agents
            }
        }

        var enabledByAgent = LoadAllEnabledMaps(registry, name);
        return Results.Ok(ToDto(record, enabledByAgent));
    }

    // ====================================================================
    // POST /api/skills/{name}/disable — disable skill for all agents
    // ====================================================================
    private static async Task<IResult> DisableSkill(
        string name,
        OpenClawNetSkillsRegistry registry,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsValidSkillName(name))
            return Problem(StatusCodes.Status400BadRequest, "invalid_name");

        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var record = snap.Skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (record is null)
            return Problem(StatusCodes.Status404NotFound, "not_found", $"Skill '{name}' not in current snapshot.");

        var requestedBy = http.User?.Identity?.Name ?? "anonymous";
        
        // Get all agent folders and disable for each
        var (rootPath, _) = OpenClawNetPaths.ResolveRoot();
        var agentsRoot = Path.Combine(rootPath, "skills", "agents");
        var agents = Directory.Exists(agentsRoot) 
            ? Directory.EnumerateDirectories(agentsRoot).Select(Path.GetFileName).ToList()
            : new List<string?>();

        // If no agents exist, disable for a default agent
        if (agents.Count == 0)
            agents.Add("default");

        foreach (var agentName in agents.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            try
            {
                await registry.SetEnabledForAgentAsync(agentName!, name, false, requestedBy, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Log but continue with other agents
            }
        }

        var enabledByAgent = LoadAllEnabledMaps(registry, name);
        return Results.Ok(ToDto(record, enabledByAgent));
    }

    // ====================================================================
    // DELETE /api/skills/{name} — Installed layer only (L-2)
    // ====================================================================
    private static async Task<IResult> DeleteSkill(
        string name,
        OpenClawNetSkillsRegistry registry,
        ISafePathResolver safePathResolver,
        ILogger<OpenClawNetSkillsRegistry> logger,
        CancellationToken ct)
    {
        if (!IsValidSkillName(name))
            return Problem(StatusCodes.Status400BadRequest, "invalid_name");

        var snap = await registry.GetSnapshotAsync(ct).ConfigureAwait(false);
        var record = snap.Skills.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (record is null)
            return Problem(StatusCodes.Status404NotFound, "not_found", $"Skill '{name}' not in current snapshot.");

        if (record.Layer != SkillLayer.Installed)
            return Problem(StatusCodes.Status403Forbidden, "read_only_layer",
                $"Skill '{name}' is in layer '{record.Layer}' which is read-only (L-2).");

        var installedRoot = OpenClawNetPaths.ResolveSkillsInstalledRoot(logger);
        var skillFolder = safePathResolver.ResolveSafePath(installedRoot, name);
        if (Directory.Exists(skillFolder))
        {
            try
            {
                Directory.Delete(skillFolder, recursive: true);
            }
            catch (IOException ex)
            {
                return Problem(StatusCodes.Status500InternalServerError, "delete_failed", ex.Message);
            }
        }

        registry.Rebuild();
        return Results.NoContent();
    }

    // ====================================================================
    // GET /api/skills/changes-since/{snapshotId}
    // ====================================================================
    private static IResult GetChangesSince(string snapshotId, OpenClawNetSkillsRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
            return Problem(StatusCodes.Status400BadRequest, "missing_snapshot_id");

        var diff = registry.DiffSince(snapshotId);
        return Results.Ok(new SkillsChangesDtoOut(
            PreviousSnapshotId: diff.PreviousSnapshotId,
            CurrentSnapshotId: diff.CurrentSnapshotId,
            Added: diff.Added.ToArray(),
            Modified: diff.Modified.ToArray(),
            Removed: diff.Removed.ToArray()));
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static bool IsValidSkillName(string? name)
        => !string.IsNullOrWhiteSpace(name) && NameRegex.IsMatch(name);

    private static IResult Problem(int status, string reason, string? detail = null)
        => Results.Json(
            new SkillsProblemOut(reason, detail),
            statusCode: status);

    /// <summary>
    /// Walks <c>{StorageRoot}/skills/agents/*</c> and returns a map of
    /// <c>agentName -> (skillName -> enabled)</c>. If <paramref name="onlySkill"/>
    /// is set, returns only that skill's per-agent slice (lighter for the
    /// detail endpoint).
    /// </summary>
    private static Dictionary<string, Dictionary<string, bool>> LoadAllEnabledMaps(
        ISkillsRegistry registry, string? onlySkill = null)
    {
        var result = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        if (registry is not OpenClawNetSkillsRegistry impl)
            return result;

        var (rootPath, _) = OpenClawNetPaths.ResolveRoot();
        var agentsRoot = Path.Combine(rootPath, "skills", "agents");
        if (!Directory.Exists(agentsRoot)) return result;

        foreach (var dir in Directory.EnumerateDirectories(agentsRoot))
        {
            var agentName = Path.GetFileName(dir);
            var map = impl.GetEnabledMapForAgent(agentName);
            if (onlySkill is not null)
            {
                if (map.TryGetValue(onlySkill, out var on))
                    result[agentName] = new Dictionary<string, bool>(StringComparer.Ordinal) { [onlySkill] = on };
            }
            else
            {
                result[agentName] = new Dictionary<string, bool>(map, StringComparer.Ordinal);
            }
        }
        return result;
    }

    private static SkillDtoOut ToDto(
        ISkillRecord record,
        Dictionary<string, Dictionary<string, bool>> enabledByAgent)
    {
        var layerName = record.Layer.ToString().ToLowerInvariant();
        var bundleSha = (record as LayeredSkill)?.ContentHash;

        // Flatten per-agent map to { agentName -> enabled-for-this-skill }.
        var flat = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (agent, map) in enabledByAgent)
        {
            if (map.TryGetValue(record.Name, out var on))
                flat[agent] = on;
        }

        DateTimeOffset updated;
        try
        {
            updated = File.GetLastWriteTimeUtc(record.SourcePath);
        }
        catch
        {
            updated = DateTimeOffset.UtcNow;
        }

        return new SkillDtoOut(
            Name: record.Name,
            Description: record.Description,
            Version: record.Metadata.TryGetValue("version", out var v) ? v : null,
            Layer: layerName,
            AgentScope: record.Layer == SkillLayer.Agent
                ? Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(record.SourcePath)))
                : null,
            Source: record.Metadata.TryGetValue("source", out var src) ? src : (record.Layer == SkillLayer.System ? "built-in" : "manual"),
            SourceCommitSha: record.Metadata.TryGetValue("source_commit_sha", out var sha) ? sha : null,
            BundleSha256: bundleSha,
            UpdatedUtc: updated,
            EffectiveLayer: layerName,
            EnabledByAgent: flat);
    }

    /// <summary>
    /// Serializes a <see cref="CreateSkillRequestIn"/> back into agentskills.io
    /// frontmatter + body. The frontmatter is intentionally minimal — only
    /// fields the user supplied land in the file.
    /// </summary>
    private static string AssembleSkillMd(CreateSkillRequestIn req)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(req.Name).Append('\n');
        sb.Append("description: ").Append(req.Description?.Replace("\n", " ") ?? string.Empty).Append('\n');
        if (!string.IsNullOrWhiteSpace(req.Version))
            sb.Append("version: ").Append(req.Version).Append('\n');
        if (req.Tags is { Length: > 0 })
            sb.Append("tags: [").Append(string.Join(", ", req.Tags)).Append("]\n");
        sb.Append("source: manual\n");
        sb.Append("---\n");
        sb.Append(req.Body);
        return sb.ToString();
    }
}

// ====================================================================
// Wire DTOs (must match Helly's K-3 SkillsClient shapes — keep in sync
// with src/OpenClawNet.Web/Models/Skills/SkillDtos.cs).
// ====================================================================

internal sealed record SkillDtoOut(
    string Name,
    string? Description,
    string? Version,
    string Layer,
    string? AgentScope,
    string Source,
    string? SourceCommitSha,
    string? BundleSha256,
    DateTimeOffset UpdatedUtc,
    string EffectiveLayer,
    Dictionary<string, bool> EnabledByAgent);

internal sealed record SkillsSnapshotDtoOut(
    string Id,
    DateTimeOffset BuiltUtc,
    string? ChangeSummary);

internal sealed record SkillsChangesDtoOut(
    string PreviousSnapshotId,
    string CurrentSnapshotId,
    string[] Added,
    string[] Modified,
    string[] Removed);

internal sealed record CreateSkillRequestIn(
    string Name,
    string Description,
    string? Version,
    string Layer,
    string? AgentScope,
    string[]? Tags,
    string Body);

internal sealed record SkillsProblemOut(string Reason, string? Detail);
