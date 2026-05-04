namespace OpenClawNet.Mcp.Abstractions;

/// <summary>
/// Concept-review §4a (Security, scaffold) — a strategy for isolating the OS process
/// that hosts an MCP server. The default implementation does nothing; richer policies
/// (working-dir jail, env scrubbing, resource limits, container) plug in here without
/// touching the launch site.
/// </summary>
/// <remarks>
/// This is intentionally a minimal scaffold. Full sandboxing (cgroups, namespaces,
/// AppContainer, etc.) is deferred. Wire a policy via DI:
/// <c>services.AddSingleton&lt;IMcpProcessIsolationPolicy, WorkingDirIsolationPolicy&gt;()</c>.
/// </remarks>
public interface IMcpProcessIsolationPolicy
{
    /// <summary>
    /// Applies the policy to a launch plan immediately before the child process is started.
    /// Implementations may mutate the working directory, environment, or argument list.
    /// </summary>
    void Apply(McpProcessLaunchPlan plan);
}

/// <summary>
/// Mutable launch plan handed to <see cref="IMcpProcessIsolationPolicy.Apply"/>.
/// Held by the launcher and applied verbatim to <c>ProcessStartInfo</c>.
/// </summary>
public sealed class McpProcessLaunchPlan
{
    public required string ServerName { get; init; }
    public required string Executable { get; set; }
    public required IList<string> Arguments { get; set; }
    public required IDictionary<string, string?> Environment { get; set; }
    public string? WorkingDirectory { get; set; }
}

/// <summary>Default no-op policy — preserves legacy "trust everything" behavior.</summary>
public sealed class NoIsolationPolicy : IMcpProcessIsolationPolicy
{
    public void Apply(McpProcessLaunchPlan plan) { /* intentionally empty */ }
}

/// <summary>
/// Minimal isolation: each MCP server runs in a dedicated working directory under
/// the OS temp folder, and inherits a scrubbed environment containing only the
/// PATH variable. Concept-review §4a (Security, scaffold).
/// </summary>
/// <remarks>
/// This is a stepping stone toward proper sandboxing. It does NOT prevent the
/// child process from reading other parts of the file system or making network
/// calls — just removes ambient secrets and gives a clean CWD.
/// Activate by setting <c>Mcp:Isolation:Enabled=true</c> in configuration.
/// </remarks>
public sealed class WorkingDirIsolationPolicy : IMcpProcessIsolationPolicy
{
    public void Apply(McpProcessLaunchPlan plan)
    {
        var safeName = MakeSafeDirName(plan.ServerName);
        var dir = Path.Combine(Path.GetTempPath(), "openclawnet-mcp", safeName);
        Directory.CreateDirectory(dir);
        plan.WorkingDirectory = dir;

        // Scrub environment — keep only PATH so the executable can still resolve binaries.
        if (plan.Environment.TryGetValue("PATH", out var path))
        {
            plan.Environment.Clear();
            plan.Environment["PATH"] = path;
        }
        else
        {
            plan.Environment.Clear();
        }
    }

    private static string MakeSafeDirName(string serverName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(serverName.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
