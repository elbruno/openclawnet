using System.Text.Json.Serialization;

namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Request payload for publishing insights to the dashboard.
/// </summary>
public sealed class DashboardPublishRequest
{
    /// <summary>
    /// Title of the dashboard card or report.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Source system identifier (e.g., "openclawnet").
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "openclawnet";

    /// <summary>
    /// Timestamp when the insights were generated (ISO 8601).
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Collection of repository insights to publish.
    /// </summary>
    [JsonPropertyName("insights")]
    public required IReadOnlyList<RepositoryInsight> Insights { get; init; }

    /// <summary>
    /// Display format hint for the dashboard (optional).
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }
}

/// <summary>
/// Individual repository insight data.
/// </summary>
public sealed class RepositoryInsight
{
    /// <summary>
    /// Repository identifier (e.g., "owner/repo").
    /// </summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    /// Number of open issues.
    /// </summary>
    [JsonPropertyName("openIssues")]
    public int? OpenIssues { get; init; }

    /// <summary>
    /// Number of open pull requests.
    /// </summary>
    [JsonPropertyName("openPRs")]
    public int? OpenPRs { get; init; }

    /// <summary>
    /// Star count.
    /// </summary>
    [JsonPropertyName("stars")]
    public int? Stars { get; init; }

    /// <summary>
    /// Last push timestamp (ISO 8601).
    /// </summary>
    [JsonPropertyName("lastPush")]
    public string? LastPush { get; init; }

    /// <summary>
    /// Summary text or additional context.
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }
}
