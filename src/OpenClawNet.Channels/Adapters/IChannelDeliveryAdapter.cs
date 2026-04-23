namespace OpenClawNet.Channels.Adapters;

/// <summary>
/// Interface for external channel delivery adapters.
/// Phase 2 implementations: Teams, Slack, Telegram, Discord, Webhook, Email.
/// </summary>
public interface IChannelDeliveryAdapter
{
    /// <summary>
    /// Adapter name (e.g., "Teams", "Slack", "Webhook").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Deliver an artifact to the external channel.
    /// </summary>
    /// <param name="jobId">Job identifier</param>
    /// <param name="jobName">Human-readable job name</param>
    /// <param name="artifactId">Artifact identifier</param>
    /// <param name="artifactType">Artifact type (markdown, json, text, file, error)</param>
    /// <param name="content">Artifact content (inline or file path)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delivery result with success flag and optional error message</returns>
    Task<DeliveryResult> DeliverAsync(
        Guid jobId,
        string jobName,
        Guid artifactId,
        string artifactType,
        string content,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a channel delivery operation.
/// </summary>
/// <param name="Success">True if delivery succeeded</param>
/// <param name="ErrorMessage">Optional error message if delivery failed</param>
/// <param name="ExternalId">Optional external message/post ID from the adapter</param>
public record DeliveryResult(bool Success, string? ErrorMessage = null, string? ExternalId = null);
