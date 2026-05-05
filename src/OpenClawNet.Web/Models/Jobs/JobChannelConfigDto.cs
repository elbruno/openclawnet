namespace OpenClawNet.Web.Models.Jobs;

/// <summary>
/// Channel configuration for job delivery.
/// </summary>
public sealed record JobChannelConfigDto
{
    public int Id { get; init; }
    public Guid JobId { get; init; }
    public required string ChannelType { get; init; }
    public bool IsEnabled { get; init; }
    public required string ChannelConfig { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to save channel configurations for a job.
/// </summary>
public sealed record SaveJobChannelConfigRequest
{
    public required List<ChannelConfigItem> Channels { get; init; }
}

/// <summary>
/// Individual channel configuration item.
/// </summary>
public sealed record ChannelConfigItem
{
    public required string ChannelType { get; init; }
    public bool IsEnabled { get; init; }
    public string? WebhookUrl { get; init; }
}


