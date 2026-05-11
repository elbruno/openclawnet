using System.Text.Json.Serialization;

namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Result returned from the dashboard API after publishing insights.
/// </summary>
public sealed class DashboardPublishResult
{
    /// <summary>
    /// Unique identifier assigned by the dashboard service.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Public URL to view the published dashboard insights.
    /// </summary>
    [JsonPropertyName("viewUrl")]
    public required string ViewUrl { get; init; }
}
