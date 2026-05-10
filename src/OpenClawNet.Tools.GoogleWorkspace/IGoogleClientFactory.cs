using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;

namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Factory for creating Google Workspace service instances with OAuth credentials.
/// Enables hermetic testing by allowing dependency injection of mock services.
/// </summary>
public interface IGoogleClientFactory
{
    /// <summary>
    /// Creates a Gmail service instance for the specified user.
    /// </summary>
    /// <param name="userId">User identifier for token lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authenticated GmailService instance.</returns>
    Task<GmailService> CreateGmailServiceAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a Calendar service instance for the specified user.
    /// </summary>
    /// <param name="userId">User identifier for token lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authenticated CalendarService instance.</returns>
    Task<CalendarService> CreateCalendarServiceAsync(string userId, CancellationToken cancellationToken);
}
