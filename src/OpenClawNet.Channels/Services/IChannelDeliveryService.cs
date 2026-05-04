using OpenClawNet.Channels.Dtos;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Channels.Services;

/// <summary>
/// Multi-channel delivery orchestration service.
/// Coordinates adapter calls, captures results, and logs audit trail.
/// </summary>
public interface IChannelDeliveryService
{
    /// <summary>
    /// Deliver job artifacts to all enabled channels.
    /// Fire-and-forget pattern: never throws on adapter failure; logs errors and continues.
    /// </summary>
    /// <param name="job">Job that completed and needs delivery</param>
    /// <param name="artifactId">Artifact identifier to deliver</param>
    /// <param name="artifactType">Artifact type (markdown, json, text, etc.)</param>
    /// <param name="content">Artifact content to deliver</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated delivery result with counts and failures</returns>
    Task<DeliveryResult> DeliverAsync(
        ScheduledJob job,
        Guid artifactId,
        string artifactType,
        string content,
        CancellationToken cancellationToken = default);
}
