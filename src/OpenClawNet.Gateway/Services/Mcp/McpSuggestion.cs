namespace OpenClawNet.Gateway.Services.Mcp;

/// <summary>
/// One curated MCP server suggestion loaded from <c>docs/mcp-suggestions.yaml</c>.
/// Surfaced through the Settings UI so users can install vetted servers in one click.
/// </summary>
public sealed class McpSuggestion
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"stdio" or "http".</summary>
    public string Transport { get; set; } = "stdio";

    public string? Command { get; set; }
    public List<string> Args { get; set; } = new();
    public string? Url { get; set; }
    public string? Category { get; set; }
    public string? Homepage { get; set; }

    /// <summary>Names of env vars the user must populate to make this server work.</summary>
    public List<string> RequiresEnv { get; set; } = new();

    /// <summary>
    /// ISO-8601 date (YYYY-MM-DD) this entry was last verified against its authoritative registry.
    /// Populated in the YAML; used by offline provenance tests to confirm the field ships.
    /// </summary>
    public string? VerifiedOn { get; set; }

    /// <summary>
    /// Authoritative registry/source used for verification, e.g. "pypi", "npm", "ghcr.io", "github".
    /// </summary>
    public string? SourceRegistry { get; set; }
}
