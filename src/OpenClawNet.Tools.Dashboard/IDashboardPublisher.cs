namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Abstraction for publishing insights to an external dashboard.
/// Enables hermetic testing with mocked HTTP endpoints.
/// </summary>
public interface IDashboardPublisher
{
    /// <summary>
    /// Publishes insights to the dashboard API.
    /// </summary>
    /// <param name="request">The dashboard publish request containing title and insights.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result containing the dashboard ID and view URL.</returns>
    Task<DashboardPublishResult> PublishAsync(DashboardPublishRequest request, CancellationToken cancellationToken = default);
}
