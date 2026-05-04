namespace OpenClawNet.Channels.Dtos;

/// <summary>
/// Aggregated result of multi-channel delivery operation.
/// </summary>
public sealed class DeliveryResult
{
    /// <summary>Job identifier.</summary>
    public required string JobId { get; set; }

    /// <summary>Total number of channels attempted.</summary>
    public int TotalAttempted { get; set; }

    /// <summary>Number of successful deliveries.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Number of failed deliveries.</summary>
    public int FailureCount { get; set; }

    /// <summary>List of failures with details.</summary>
    public List<DeliveryFailure> Failures { get; set; } = [];

    /// <summary>Total time taken for all deliveries.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Timestamp when delivery orchestration completed.</summary>
    public DateTime CompletedAt { get; set; }
}
