using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.FileSystem;

public sealed class FileSystemTool : ITool
{
    private readonly ILogger<FileSystemTool> _logger;
    private readonly ISafePathResolver _safePathResolver;
    private readonly string _workspaceRoot;

    // Block access to sensitive paths
    private static readonly string[] BlockedPaths = [".env", ".git", "appsettings.Production"];

    /// <summary>
    /// W-2 ctor (Drummond P0 #2 / H-2 closure) — accepts an
    /// <see cref="ISafePathResolver"/>. Workspace root selection:
    ///   1. <c>Agent:WorkspacePath</c> (validated through the resolver),
    ///   2. else <c>OpenClawNetPaths.ResolveAgentRoot(Agent:Name)</c>,
    ///   3. else <c>OpenClawNetPaths.ResolveUserRoot("workspace")</c>
    ///      (logs WARN — operators should know the agent name was missing).
    /// </summary>
    public FileSystemTool(
        ILogger<FileSystemTool> logger,
        IConfiguration configuration,
        ISafePathResolver safePathResolver)
    {
        _logger = logger;
        _safePathResolver = safePathResolver;

        var configuredPath = configuration["Agent:WorkspacePath"];
        var agentName = configuration["Agent:Name"];

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            // Honor explicit operator override. Trust it as-is — this is
            // operator-supplied (not LLM-supplied) so the resolver's
            // segment-name policy doesn't apply at the root.
            _workspaceRoot = Path.TrimEndingDirectorySeparator(configuredPath);
        }
        else if (!string.IsNullOrWhiteSpace(agentName))
        {
            _workspaceRoot = OpenClawNetPaths.ResolveAgentRoot(agentName, _logger);
        }
        else
        {
            _logger.LogWarning(
                "FileSystemTool initialized without agent scope — using shared workspace " +
                "({Root}/workspace). Set Agent:Name or Agent:WorkspacePath to scope per-agent.",
                "{StorageRoot}");
            _workspaceRoot = OpenClawNetPaths.ResolveUserRoot("workspace", _logger);
        }

        _logger.LogInformation("FileSystem workspace root: {Root}", _workspaceRoot);
    }

    /// <summary>
    /// Back-compat 2-arg ctor — preserves the pre-W-2 ctor surface so any
    /// caller still constructing the tool without ISafePathResolver gets a
    /// working instance backed by the default <see cref="SafePathResolver"/>.
    /// New code should use the 3-arg ctor and inject the DI singleton.
    /// </summary>
    /// <remarks>
    /// W-3 sunset (Drummond W-2 deviation #2): this ctor is a fail-OPEN-eligible
    /// seam in the same way <see cref="SafePathResolver()"/> is. Migrate the
    /// remaining callers (<c>DocumentPipelineTests</c>, <c>BundledMcpWrapperTests</c>,
    /// <c>FileSystemToolTests</c>) to the 3-arg ctor + DI singleton; the ctor
    /// will be removed in W-4. Until removal, the runtime invariant is preserved
    /// because every check still runs through the default <see cref="SafePathResolver"/>.
    /// </remarks>
    [Obsolete("Use 3-arg ctor with ISafePathResolver from DI. Will be removed in W-4.", error: false)]
    public FileSystemTool(ILogger<FileSystemTool> logger, IConfiguration configuration)
        : this(logger, configuration, new SafePathResolver()) { }

    public string Name => "file_system";
    public string Description => $"Read, write, list files and find .NET projects in the workspace at: {_workspaceRoot}";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["read", "write", "list", "find_projects"],
                    "description": "Operation: read=get file content, list=list directory, find_projects=find all .sln/.csproj files, write=write file"
                },
                "path": {
                    "type": "string",
                    "description": "File or directory path. Use '.' for workspace root, or an absolute path. Can be relative or absolute."
                },
                "content": { "type": "string", "description": "Content to write (required for write action)" }
            },
            "required": ["action", "path"]
        }
        """),
        RequiresApproval = true,
        Category = "filesystem",
        Tags = ["file", "read", "write", "list", "projects", "dotnet"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var action = input.GetStringArgument("action");
            var path = input.GetStringArgument("path");

            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(path))
            {
                return ToolResult.Fail(Name, "Both 'action' and 'path' are required", sw.Elapsed);
            }

            // Blocklist check happens BEFORE resolver (operator/admin policy,
            // not a path-shape question — the resolver doesn't know which
            // names are "secret-shaped").
            foreach (var blocked in BlockedPaths)
            {
                if (path.Contains(blocked, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Blocked path access attempt: {Path}", Redact(path));
                    return ToolResult.Fail(Name, $"Path '{Redact(path)}' is blocked", sw.Elapsed);
                }
            }

            // W-2 (Drummond H-2 closure): EVERY path goes through the
            // resolver. No more inline path normalization / containment logic.
            string fullPath;
            try
            {
                fullPath = _safePathResolver.ResolveSafePath(_workspaceRoot, path);
            }
            catch (UnsafePathException ex)
            {
                // H-8 logging hygiene:
                //   - DEBUG carries the full untrusted input + scope (operators opt in),
                //   - INFO carries the redacted form (default surface),
                //   - WARN MUST NOT echo raw input (it's attacker-controlled).
                _logger.LogDebug(ex,
                    "FileSystemTool path rejected: scope='{ScopeRoot}', input='{RequestedPath}', reason={Reason}",
                    _workspaceRoot, path, ex.Reason);
                _logger.LogInformation(
                    "FileSystemTool path rejected (reason={Reason}, redacted='{Redacted}')",
                    ex.Reason, Redact(path));

                return ToolResult.Fail(
                    Name,
                    $"Path '{Redact(path)}' rejected by safe-path resolver: {ex.Reason}",
                    sw.Elapsed);
            }

            return action.ToLowerInvariant() switch
            {
                "read" => await ReadFileAsync(fullPath, sw, cancellationToken),
                "write" => await WriteFileAsync(fullPath, input.GetStringArgument("content") ?? "", sw, cancellationToken),
                "list" => ListDirectory(fullPath, sw),
                "find_projects" => FindProjects(fullPath, sw),
                _ => ToolResult.Fail(Name, $"Unknown action: {action}. Use 'read', 'write', 'list', or 'find_projects'.", sw.Elapsed)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileSystem tool error");
            return ToolResult.Fail(Name, ex.Message, sw.Elapsed);
        }
    }

    private async Task<ToolResult> ReadFileAsync(string fullPath, System.Diagnostics.Stopwatch sw, CancellationToken ct)
    {
        if (!File.Exists(fullPath))
        {
            return ToolResult.Fail(Name, $"File not found: {fullPath}", sw.Elapsed);
        }

        var info = new FileInfo(fullPath);
        if (info.Length > 1_000_000) // 1MB limit
        {
            return ToolResult.Fail(Name, $"File too large ({info.Length} bytes). Maximum is 1MB.", sw.Elapsed);
        }

        var content = await File.ReadAllTextAsync(fullPath, ct);
        _logger.LogInformation("Read file: {Path} ({Length} chars)", fullPath, content.Length);

        sw.Stop();
        return ToolResult.Ok(Name, content, sw.Elapsed);
    }

    private async Task<ToolResult> WriteFileAsync(string fullPath, string content, System.Diagnostics.Stopwatch sw, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content, ct);
        _logger.LogInformation("Wrote file: {Path} ({Length} chars)", fullPath, content.Length);

        sw.Stop();
        return ToolResult.Ok(Name, $"File written successfully: {fullPath} ({content.Length} chars)", sw.Elapsed);
    }

    private ToolResult ListDirectory(string fullPath, System.Diagnostics.Stopwatch sw)
    {
        if (!Directory.Exists(fullPath))
        {
            return ToolResult.Fail(Name, $"Directory not found: {fullPath}", sw.Elapsed);
        }

        var sb = new StringBuilder();

        var dirs = Directory.GetDirectories(fullPath);
        foreach (var dir in dirs.Take(50))
        {
            sb.AppendLine($"[DIR] {Path.GetFileName(dir)}/");
        }

        var files = Directory.GetFiles(fullPath);
        foreach (var file in files.Take(100))
        {
            var info = new FileInfo(file);
            sb.AppendLine($"      {Path.GetFileName(file)} ({info.Length:N0} bytes)");
        }

        if (dirs.Length > 50 || files.Length > 100)
        {
            sb.AppendLine($"... and more ({dirs.Length} total dirs, {files.Length} total files)");
        }

        sw.Stop();
        return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
    }

    private ToolResult FindProjects(string searchRoot, System.Diagnostics.Stopwatch sw)
    {
        if (!Directory.Exists(searchRoot))
        {
            return ToolResult.Fail(Name, $"Directory not found: {searchRoot}", sw.Elapsed);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# .NET Projects in {searchRoot}");
        sb.AppendLine();

        // Find solution files
        var slnFiles = Directory.GetFiles(searchRoot, "*.sln", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(searchRoot, "*.slnx", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();

        if (slnFiles.Length > 0)
        {
            sb.AppendLine("## Solution Files");
            foreach (var sln in slnFiles)
                sb.AppendLine($"  {Path.GetRelativePath(searchRoot, sln)}");
            sb.AppendLine();
        }

        // Find project files
        var projFiles = Directory.GetFiles(searchRoot, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(searchRoot, "*.fsproj", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .OrderBy(f => f)
            .ToArray();

        sb.AppendLine($"## Projects ({projFiles.Length} found)");
        foreach (var proj in projFiles)
        {
            var rel = Path.GetRelativePath(searchRoot, proj);
            var name = Path.GetFileNameWithoutExtension(proj);
            sb.AppendLine($"  - **{name}** → `{rel}`");
        }

        sw.Stop();
        return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until it finds a directory
    /// containing a .slnx or .sln file — that is the solution root.
    /// Falls back to <see cref="Directory.GetCurrentDirectory"/> if nothing is found.
    /// </summary>
    /// <remarks>
    /// W-2: kept for legacy contexts that may still want it (e.g. test
    /// fixtures). The W-2 ctor no longer calls this — workspace root now
    /// derives from <see cref="OpenClawNetPaths.ResolveAgentRoot"/> /
    /// <see cref="OpenClawNetPaths.ResolveUserRoot"/>.
    /// </remarks>
    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// W-2: redact attacker-controlled path input for user-facing surfaces.
    /// Paths longer than 32 chars are truncated to first 32 + "...". The
    /// raw input still goes to DEBUG logs for operators who opt in.
    /// </summary>
    private static string Redact(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return raw.Length <= 32 ? raw : raw[..32] + "...";
    }
}
