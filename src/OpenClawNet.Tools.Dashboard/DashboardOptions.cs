namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Configuration options for the Dashboard publisher tool.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// Configuration section name for Dashboard options.
    /// </summary>
    public const string SectionName = "Dashboard";

    /// <summary>
    /// Base URL of the external dashboard API (e.g., "https://dashboard.example.com").
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// API key for authenticating with the dashboard service.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// HTTP timeout in seconds for dashboard API requests. Default is 30 seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
