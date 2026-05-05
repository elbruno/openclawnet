using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using OpenClawNet.Mcp.Abstractions;
using OpenClawNet.Mcp.Core;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Services.Mcp;

/// <summary>
/// Read-only DTO surfaced by the API: the merged-view server (DB row OR bundled definition)
/// plus runtime status (running/error/tool-count) sampled from the in-process + stdio hosts.
/// </summary>
public sealed record McpServerListItem(
    Guid Id,
    string Name,
    string Transport,
    string? Command,
    IReadOnlyList<string> Args,
    string? Url,
    bool HasEnv,
    bool HasHeaders,
    bool Enabled,
    bool IsBuiltIn,
    bool IsRunning,
    int ToolCount,
    string? LastError);

/// <summary>
/// Body shape for create/update — matches the standard <c>mcp.json</c> server entry
/// (env / headers as plain dictionaries the service encrypts before persisting).
/// </summary>
public sealed class McpServerWriteRequest
{
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio";
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed record McpToolDescriptor(string Name, string? Description);

public sealed record McpTestResult(bool Ok, IReadOnlyList<McpToolDescriptor> Tools, string? Error);

/// <summary>
/// Service-layer entry point for the MCP settings UI. Owns the merged view of bundled +
/// persisted servers, CRUD with secret encryption, and the "test connection" dance.
/// </summary>
/// <remarks>
/// Persistence model: every server we know about — including the bundled built-ins —
/// is represented as a <see cref="McpServerListItem"/>. Built-ins are never written to the
/// DB by this service (PR-E owns that seed). For built-ins the only user-editable field
/// is <see cref="McpServerListItem.Enabled"/>; everything else is read-only and enforced
/// at the API layer.
/// </remarks>
public sealed class McpServerCatalogService
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;
    private readonly ISecretStore _secrets;
    private readonly IMcpToolProvider _toolProvider;
    private readonly InProcessMcpHost _inProcessHost;
    private readonly StdioMcpHost _stdioHost;
    private readonly BundledMcpServerRegistry _bundled;
    private readonly ILogger<McpServerCatalogService> _logger;

    public McpServerCatalogService(
        IDbContextFactory<OpenClawDbContext> dbFactory,
        ISecretStore secrets,
        IMcpToolProvider toolProvider,
        InProcessMcpHost inProcessHost,
        StdioMcpHost stdioHost,
        BundledMcpServerRegistry bundled,
        ILogger<McpServerCatalogService> logger)
    {
        _dbFactory = dbFactory;
        _secrets = secrets;
        _toolProvider = toolProvider;
        _inProcessHost = inProcessHost;
        _stdioHost = stdioHost;
        _bundled = bundled;
        _logger = logger;
    }

    // ── List + get ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<McpServerListItem>> ListAsync(CancellationToken ct)
    {
        // PR-E: DB is the source of truth — the 4 built-ins are seeded by SchemaMigrator
        // before this service ever runs. Bundled definitions only contribute runtime
        // status (IsRunning/ToolCount) which Map() pulls from the in-process host.
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await db.McpServerDefinitions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var merged = new List<McpServerListItem>(rows.Count);
        foreach (var row in rows)
            merged.Add(await MapAsync(EntityToDefinition(row), isBuiltInOverride: row.IsBuiltIn, ct).ConfigureAwait(false));

        return merged.OrderByDescending(s => s.IsBuiltIn).ThenBy(s => s.Name).ToList();
    }

    public async Task<McpServerListItem?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.McpServerDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return row is null
            ? null
            : await MapAsync(EntityToDefinition(row), isBuiltInOverride: row.IsBuiltIn, ct).ConfigureAwait(false);
    }

    // ── Create ──────────────────────────────────────────────────────────────

    public async Task<(McpServerListItem? created, string? error)> CreateAsync(McpServerWriteRequest req, CancellationToken ct)
    {
        var validation = Validate(req, isCreate: true);
        if (validation is not null) return (null, validation);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await db.McpServerDefinitions.AnyAsync(x => x.Name == req.Name, ct).ConfigureAwait(false))
            return (null, $"An MCP server named '{req.Name}' already exists.");

        if (_bundled.Definitions.Any(d => string.Equals(d.Name, req.Name, StringComparison.OrdinalIgnoreCase)))
            return (null, $"The name '{req.Name}' is reserved for a built-in MCP server.");

        var entity = new McpServerDefinitionEntity
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Transport = ParseTransport(req.Transport).ToString(),
            Command = req.Command,
            ArgsJson = JsonSerializer.Serialize(req.Args ?? new List<string>()),
            EnvJson = EncryptDictOrNull(req.Env),
            Url = req.Url,
            HeadersJson = EncryptDictOrNull(req.Headers),
            Enabled = req.Enabled,
            IsBuiltIn = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.McpServerDefinitions.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var created = await MapAsync(EntityToDefinition(entity), isBuiltInOverride: false, ct).ConfigureAwait(false);
        return (created, null);
    }

    // ── Update ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Result of an update attempt. <see cref="Forbidden"/> is set when the caller tried
    /// to mutate an immutable field on a built-in row.
    /// </summary>
    public sealed record UpdateResult(McpServerListItem? Item, string? Error, bool NotFound = false, bool Forbidden = false);

    public async Task<UpdateResult> UpdateAsync(Guid id, McpServerWriteRequest req, CancellationToken ct)
    {
        // PR-E: built-in rows always exist in the DB after SchemaMigrator has run, so
        // there's no longer a "bundled but unpersisted" branch — IsBuiltIn drives the
        // immutable-fields rule.
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.McpServerDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);

        if (entity is null)
            return new UpdateResult(null, "Server not found.", NotFound: true);

        if (entity.IsBuiltIn)
        {
            var current = EntityToDefinition(entity);
            if (!BuiltInOnlyEnabledChanged(current, req))
                return new UpdateResult(null, "Built-in MCP servers can only be enabled or disabled.", Forbidden: true);

            entity.Enabled = req.Enabled;
            entity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var item = await MapAsync(EntityToDefinition(entity), isBuiltInOverride: true, ct).ConfigureAwait(false);
            return new UpdateResult(item, null);
        }

        // User-defined row → full update.
        var validation = Validate(req, isCreate: false);
        if (validation is not null) return new UpdateResult(null, validation);

        if (await db.McpServerDefinitions.AnyAsync(x => x.Name == req.Name && x.Id != id, ct).ConfigureAwait(false))
            return new UpdateResult(null, $"An MCP server named '{req.Name}' already exists.");

        entity!.Name = req.Name;
        entity.Transport = ParseTransport(req.Transport).ToString();
        entity.Command = req.Command;
        entity.ArgsJson = JsonSerializer.Serialize(req.Args ?? new List<string>());
        entity.EnvJson = EncryptDictOrNull(req.Env);
        entity.Url = req.Url;
        entity.HeadersJson = EncryptDictOrNull(req.Headers);
        entity.Enabled = req.Enabled;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var updated = await MapAsync(EntityToDefinition(entity), isBuiltInOverride: false, ct).ConfigureAwait(false);
        return new UpdateResult(updated, null);
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    public sealed record DeleteResult(bool Deleted, string? Error, bool NotFound = false, bool Forbidden = false);

    public async Task<DeleteResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (_bundled.Definitions.Any(d => d.Id == id))
            return new DeleteResult(false, "Built-in MCP servers cannot be deleted.", Forbidden: true);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.McpServerDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return new DeleteResult(false, "Server not found.", NotFound: true);
        if (entity.IsBuiltIn) return new DeleteResult(false, "Built-in MCP servers cannot be deleted.", Forbidden: true);

        db.McpServerDefinitions.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DeleteResult(true, null);
    }

    // ── Test (saved server) ─────────────────────────────────────────────────

    public async Task<McpTestResult> TestAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.McpServerDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return new McpTestResult(false, Array.Empty<McpToolDescriptor>(), "Server not found.");

        return await TestDefinitionAsync(EntityToDefinition(entity), ct).ConfigureAwait(false);
    }

    /// <summary>Test an unsaved configuration (used by the "Test" button on the create form).</summary>
    public async Task<McpTestResult> TestConfigAsync(McpServerWriteRequest req, CancellationToken ct)
    {
        var validation = Validate(req, isCreate: true);
        if (validation is not null) return new McpTestResult(false, Array.Empty<McpToolDescriptor>(), validation);

        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Transport = ParseTransport(req.Transport),
            Command = req.Command,
            Args = req.Args?.ToArray() ?? Array.Empty<string>(),
            EnvJson = EncryptDictOrNull(req.Env),
            Url = req.Url,
            HeadersJson = EncryptDictOrNull(req.Headers),
            Enabled = true,
        };
        return await TestDefinitionAsync(def, ct).ConfigureAwait(false);
    }

    private async Task<McpTestResult> TestDefinitionAsync(McpServerDefinition def, CancellationToken ct)
    {
        // Built-ins are already running through InProcessMcpHost — just enumerate.
        if (def.Transport == McpTransport.InProcess)
        {
            try
            {
                var tools = await _toolProvider.GetToolsForServerAsync(def.Id.ToString(), ct).ConfigureAwait(false);
                return new McpTestResult(true,
                    tools.Select(t => new McpToolDescriptor(t.Name, t.Description)).ToList(),
                    null);
            }
            catch (Exception ex)
            {
                return new McpTestResult(false, Array.Empty<McpToolDescriptor>(), ex.Message);
            }
        }

        // Stdio: spin up an ephemeral subprocess just to call ListTools.
        if (def.Transport == McpTransport.Stdio)
        {
            if (string.IsNullOrWhiteSpace(def.Command))
                return new McpTestResult(false, Array.Empty<McpToolDescriptor>(), "Command is required for stdio transport.");

            var env = DecryptEnv(def.EnvJson);
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = def.Name,
                Command = def.Command!,
                Arguments = def.Args ?? Array.Empty<string>(),
                EnvironmentVariables = env,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));

            McpClient? client = null;
            try
            {
                client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token).ConfigureAwait(false);
                var tools = await client.ListToolsAsync(cancellationToken: cts.Token).ConfigureAwait(false);
                return new McpTestResult(true,
                    tools.Select(t => new McpToolDescriptor(t.Name, t.Description ?? string.Empty)).ToList(),
                    null);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Test of MCP server '{ServerName}' failed.", def.Name);
                return new McpTestResult(false, Array.Empty<McpToolDescriptor>(), ex.Message);
            }
            finally
            {
                if (client is not null)
                    try { await client.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            }
        }

        // HTTP transport — out of scope for PR-C beyond accepting the config.
        return new McpTestResult(false, Array.Empty<McpToolDescriptor>(),
            "HTTP transport test is not yet implemented in this build.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<McpServerListItem> MapAsync(McpServerDefinition def, bool isBuiltInOverride, CancellationToken ct)
    {
        var isRunning =
            (def.Transport == McpTransport.InProcess && _inProcessHost.IsRunning(def.Id)) ||
            (def.Transport == McpTransport.Stdio && _stdioHost.IsRunning(def.Id));

        var toolCount = 0;
        if (isRunning && def.Enabled)
        {
            try
            {
                var tools = await _toolProvider.GetToolsForServerAsync(def.Id.ToString(), ct).ConfigureAwait(false);
                toolCount = tools.Count;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Tool listing failed for server '{Name}' during merged view.", def.Name);
            }
        }

        return new McpServerListItem(
            Id: def.Id,
            Name: def.Name,
            Transport: def.Transport.ToString(),
            Command: def.Command,
            Args: def.Args ?? Array.Empty<string>(),
            Url: def.Url,
            HasEnv: !string.IsNullOrEmpty(def.EnvJson),
            HasHeaders: !string.IsNullOrEmpty(def.HeadersJson),
            Enabled: def.Enabled,
            IsBuiltIn: isBuiltInOverride || def.IsBuiltIn,
            IsRunning: isRunning,
            ToolCount: toolCount,
            LastError: def.LastError);
    }

    private static McpServerDefinition EntityToDefinition(McpServerDefinitionEntity e)
    {
        var args = string.IsNullOrWhiteSpace(e.ArgsJson)
            ? Array.Empty<string>()
            : (JsonSerializer.Deserialize<string[]>(e.ArgsJson) ?? Array.Empty<string>());

        return new McpServerDefinition
        {
            Id = e.Id,
            Name = e.Name,
            Transport = Enum.TryParse<McpTransport>(e.Transport, ignoreCase: true, out var t) ? t : McpTransport.InProcess,
            Command = e.Command,
            Args = args,
            EnvJson = e.EnvJson,
            Url = e.Url,
            HeadersJson = e.HeadersJson,
            Enabled = e.Enabled,
            IsBuiltIn = e.IsBuiltIn,
            LastError = e.LastError,
            LastSeenUtc = e.LastSeenUtc,
        };
    }

    private static bool BuiltInOnlyEnabledChanged(McpServerDefinition current, McpServerWriteRequest req)
    {
        // Allow the API to omit the read-only fields entirely; if supplied, they must match.
        if (!string.IsNullOrEmpty(req.Name) && !string.Equals(req.Name, current.Name, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrEmpty(req.Transport) &&
            !string.Equals(req.Transport, current.Transport.ToString(), StringComparison.OrdinalIgnoreCase)) return false;
        if (req.Command is not null && !string.Equals(req.Command, current.Command, StringComparison.Ordinal)) return false;
        if (req.Url is not null && !string.Equals(req.Url, current.Url, StringComparison.Ordinal)) return false;
        if (req.Args is not null && !req.Args.SequenceEqual(current.Args ?? Array.Empty<string>(), StringComparer.Ordinal)) return false;
        if (req.Env is { Count: > 0 }) return false;
        if (req.Headers is { Count: > 0 }) return false;
        return true;
    }

    private static McpTransport ParseTransport(string s)
        => Enum.TryParse<McpTransport>(s, ignoreCase: true, out var t) ? t : McpTransport.Stdio;

    private string? EncryptDictOrNull(Dictionary<string, string>? dict)
    {
        if (dict is null || dict.Count == 0) return null;
        var json = JsonSerializer.Serialize(dict);
        return _secrets.Protect(json);
    }

    private Dictionary<string, string?>? DecryptEnv(string? envJson)
    {
        if (string.IsNullOrEmpty(envJson)) return null;
        var plain = _secrets.Unprotect(envJson);
        if (plain is null) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(plain); }
        catch { return null; }
    }

    private static string? Validate(McpServerWriteRequest req, bool isCreate)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "Name is required.";
        if (req.Name.Length > 100) return "Name must be 100 characters or fewer.";

        var transport = ParseTransport(req.Transport);
        switch (transport)
        {
            case McpTransport.Stdio:
                if (string.IsNullOrWhiteSpace(req.Command))
                    return "Command is required for stdio transport.";
                if (!string.IsNullOrWhiteSpace(req.Url))
                    return "URL must be empty for stdio transport.";
                break;
            case McpTransport.Http:
                if (string.IsNullOrWhiteSpace(req.Url))
                    return "URL is required for HTTP transport.";
                if (!Uri.TryCreate(req.Url, UriKind.Absolute, out _))
                    return "URL must be an absolute http(s) URI.";
                if (!string.IsNullOrWhiteSpace(req.Command))
                    return "Command must be empty for HTTP transport.";
                break;
            case McpTransport.InProcess:
                return "InProcess transport is reserved for built-in servers.";
        }

        return null;
    }
}
