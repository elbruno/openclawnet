namespace OpenClawNet.Web.Models.Audit;

/// <summary>
/// DTO for job state change audit records.
/// </summary>
public sealed record AuditJobStateChangeDto
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public string? JobName { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public string? Reason { get; init; }
    public string? ChangedBy { get; init; }
    public DateTime ChangedAt { get; init; }
}

/// <summary>
/// Response wrapper for job state changes endpoint.
/// </summary>
public sealed record AuditJobStateChangesResponse
{
    public List<AuditJobStateChangeDto> Changes { get; init; } = [];
    public int Count { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public object? Filters { get; init; }
}

/// <summary>
/// DTO for tool approval audit records.
/// </summary>
public sealed record AuditToolApprovalLogDto
{
    public Guid Id { get; init; }
    public Guid RequestId { get; init; }
    public Guid SessionId { get; init; }
    public required string ToolName { get; init; }
    public string? AgentProfileName { get; init; }
    public bool Approved { get; init; }
    public bool RememberForSession { get; init; }
    public required string Source { get; init; }
    public DateTime DecidedAt { get; init; }
}

/// <summary>
/// Response wrapper for tool approval logs endpoint.
/// </summary>
public sealed record AuditToolApprovalLogsResponse
{
    public List<AuditToolApprovalLogDto> Logs { get; init; } = [];
    public int Count { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public object? Filters { get; init; }
}

/// <summary>
/// DTO for adapter delivery audit records.
/// </summary>
public sealed record AdapterDeliveryLogDto
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public string? JobName { get; init; }
    public required string ChannelType { get; init; }
    public required string Status { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public string? ErrorMessage { get; init; }
    public int? ResponseCode { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Response wrapper for adapter delivery logs endpoint.
/// </summary>
public sealed record AdapterDeliveryLogsResponse
{
    public List<AdapterDeliveryLogDto> Logs { get; init; } = [];
    public int Count { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public object? Filters { get; init; }
}
