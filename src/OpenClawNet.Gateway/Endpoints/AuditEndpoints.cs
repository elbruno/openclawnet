using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Audit trail REST endpoints for observability and compliance.
/// Feature 2 Story 1 — exposes job state changes, tool approvals, and adapter deliveries.
/// </summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit").WithTags("Audit");

        // Job state changes
        group.MapGet("/job-state-changes", async (
            Guid? jobId,
            DateTime? since,
            DateTime? until,
            int? limit,
            int? offset,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var query = db.JobStateChanges.AsQueryable();

            if (jobId.HasValue)
                query = query.Where(sc => sc.JobId == jobId.Value);

            if (since.HasValue)
                query = query.Where(sc => sc.ChangedAt >= since.Value);

            if (until.HasValue)
                query = query.Where(sc => sc.ChangedAt <= until.Value);

            var pageSize = Math.Min(limit ?? 100, 500);
            var skip = offset ?? 0;

            var changes = await query
                .OrderByDescending(sc => sc.ChangedAt)
                .Skip(skip)
                .Take(pageSize)
                .Include(sc => sc.Job)
                .Select(sc => new AuditJobStateChangeDto
                {
                    Id = sc.Id,
                    JobId = sc.JobId,
                    JobName = sc.Job != null ? sc.Job.Name : null,
                    FromStatus = sc.FromStatus.ToString().ToLowerInvariant(),
                    ToStatus = sc.ToStatus.ToString().ToLowerInvariant(),
                    Reason = sc.Reason,
                    ChangedBy = sc.ChangedBy,
                    ChangedAt = sc.ChangedAt
                })
                .ToListAsync();

            return Results.Ok(new AuditJobStateChangesResponse
            {
                Changes = changes,
                Count = changes.Count,
                Offset = skip,
                Limit = pageSize,
                Filters = new
                {
                    jobId,
                    since,
                    until
                }
            });
        })
        .WithName("GetJobStateChanges")
        .WithDescription("List job state transitions. Paginated (default limit 100, max 500). Supports date-range and jobId filtering.");

        // Tool approvals
        group.MapGet("/tool-approvals", async (
            Guid? sessionId,
            string? toolName,
            DateTime? since,
            DateTime? until,
            int? limit,
            int? offset,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var query = db.ToolApprovalLogs.AsQueryable();

            if (sessionId.HasValue)
                query = query.Where(log => log.SessionId == sessionId.Value);

            if (!string.IsNullOrWhiteSpace(toolName))
                query = query.Where(log => log.ToolName == toolName);

            if (since.HasValue)
                query = query.Where(log => log.DecidedAt >= since.Value);

            if (until.HasValue)
                query = query.Where(log => log.DecidedAt <= until.Value);

            var pageSize = Math.Min(limit ?? 100, 500);
            var skip = offset ?? 0;

            var logs = await query
                .OrderByDescending(log => log.DecidedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(log => new AuditToolApprovalLogDto
                {
                    Id = log.Id,
                    RequestId = log.RequestId,
                    SessionId = log.SessionId,
                    ToolName = log.ToolName,
                    AgentProfileName = log.AgentProfileName,
                    Approved = log.Approved,
                    RememberForSession = log.RememberForSession,
                    Source = log.Source.ToString().ToLowerInvariant(),
                    DecidedAt = log.DecidedAt
                })
                .ToListAsync();

            return Results.Ok(new AuditToolApprovalLogsResponse
            {
                Logs = logs,
                Count = logs.Count,
                Offset = skip,
                Limit = pageSize,
                Filters = new
                {
                    sessionId,
                    toolName,
                    since,
                    until
                }
            });
        })
        .WithName("GetToolApprovals")
        .WithDescription("List tool approval decisions. Paginated (default limit 100, max 500). Supports date-range, sessionId, and toolName filtering.");

        // Adapter deliveries
        group.MapGet("/adapter-deliveries", async (
            Guid? jobId,
            string? status,
            DateTime? since,
            DateTime? until,
            int? limit,
            int? offset,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var query = db.AdapterDeliveryLogs.AsQueryable();

            if (jobId.HasValue)
                query = query.Where(log => log.JobId == jobId.Value);

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (Enum.TryParse<DeliveryStatus>(status, ignoreCase: true, out var statusEnum))
                {
                    query = query.Where(log => log.Status == statusEnum);
                }
            }

            if (since.HasValue)
                query = query.Where(log => log.CreatedAt >= since.Value);

            if (until.HasValue)
                query = query.Where(log => log.CreatedAt <= until.Value);

            var pageSize = Math.Min(limit ?? 100, 500);
            var skip = offset ?? 0;

            var logs = await query
                .OrderByDescending(log => log.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Include(log => log.Job)
                .Select(log => new AdapterDeliveryLogDto
                {
                    Id = log.Id,
                    JobId = log.JobId,
                    JobName = log.Job != null ? log.Job.Name : null,
                    ChannelType = log.ChannelType,
                    Status = log.Status.ToString().ToLowerInvariant(),
                    DeliveredAt = log.DeliveredAt,
                    ErrorMessage = log.ErrorMessage,
                    ResponseCode = log.ResponseCode,
                    CreatedAt = log.CreatedAt
                })
                .ToListAsync();

            return Results.Ok(new AdapterDeliveryLogsResponse
            {
                Logs = logs,
                Count = logs.Count,
                Offset = skip,
                Limit = pageSize,
                Filters = new
                {
                    jobId,
                    status,
                    since,
                    until
                }
            });
        })
        .WithName("GetAdapterDeliveries")
        .WithDescription("List adapter delivery attempts. Paginated (default limit 100, max 500). Supports date-range, jobId, and status filtering.");
    }
}

// DTOs
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

public sealed record AuditJobStateChangesResponse
{
    public List<AuditJobStateChangeDto> Changes { get; init; } = [];
    public int Count { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public object? Filters { get; init; }
}

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

public sealed record AuditToolApprovalLogsResponse
{
    public List<AuditToolApprovalLogDto> Logs { get; init; } = [];
    public int Count { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public object? Filters { get; init; }
}

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

public sealed record AdapterDeliveryLogsResponse
{
    public List<AdapterDeliveryLogDto> Logs { get; init; } = [];
    public int Count { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public object? Filters { get; init; }
}
