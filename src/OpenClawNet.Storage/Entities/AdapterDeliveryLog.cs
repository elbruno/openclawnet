namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Audit log for channel delivery attempts.
/// Tracks success/failure of each adapter invocation for post-mortem analysis and manual retry.
/// </summary>
public sealed class AdapterDeliveryLog
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Foreign key to the job that triggered this delivery.</summary>
    public required Guid JobId { get; set; }

    /// <summary>Channel adapter type (e.g., "GenericWebhook", "Teams", "Slack").</summary>
    public required string ChannelType { get; set; }

    /// <summary>
    /// JSON snapshot of the channel configuration at delivery time.
    /// Allows admins to see exactly what was attempted, even if config changes later.
    /// </summary>
    public required string ChannelConfig { get; set; }

    /// <summary>Delivery status.</summary>
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    /// <summary>Timestamp when delivery completed (null if still pending or failed before attempt).</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Error message if delivery failed (null for success).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>HTTP response code for HTTP-based adapters (null for non-HTTP channels).</summary>
    public int? ResponseCode { get; set; }

    /// <summary>Timestamp when this log entry was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Navigation property to the parent job.</summary>
    public ScheduledJob? Job { get; set; }
}

/// <summary>Delivery status enum.</summary>
public enum DeliveryStatus
{
    /// <summary>Delivery not yet attempted.</summary>
    Pending,

    /// <summary>Delivery succeeded.</summary>
    Success,

    /// <summary>Delivery failed (adapter threw exception or returned error).</summary>
    Failed
}
