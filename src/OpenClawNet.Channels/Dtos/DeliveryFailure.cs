namespace OpenClawNet.Channels.Dtos;

/// <summary>
/// Details of a single channel delivery failure.
/// </summary>
public sealed class DeliveryFailure
{
    /// <summary>Channel type that failed (e.g., "GenericWebhook", "Teams").</summary>
    public required string ChannelType { get; set; }

    /// <summary>Error message describing why the delivery failed.</summary>
    public required string ErrorMessage { get; set; }
}
