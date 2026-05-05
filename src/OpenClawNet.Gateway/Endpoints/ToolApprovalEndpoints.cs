using Microsoft.EntityFrameworkCore;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// HTTP endpoint that resolves a pending tool-approval request emitted on the
/// NDJSON chat stream as a <c>tool_approval</c> event.
///
/// Wave 4 PR-2 (Dallas). Contract documented in
/// <c>.squad/decisions/inbox/lambert-toolapproval-ui-pr1.md</c>:
///   POST /api/chat/tool-approval
///   { requestId: Guid, approved: bool, rememberForSession: bool }
///
/// Returns 200 when the decision was matched to a pending request, or 404 when
/// the request id is unknown (stale UI, server restart, request already resolved).
/// </summary>
public static class ToolApprovalEndpoints
{
    public static void MapToolApprovalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat/tool-approval", (
            ToolApprovalDecisionRequest body,
            IToolApprovalCoordinator coordinator,
            ILogger<ToolApprovalDecisionRequest> logger) =>
        {
            logger.LogDebug("POST /api/chat/tool-approval received: RequestId={RequestId}, Approved={Approved}",
                body.RequestId, body.Approved);
            
            if (body.RequestId == Guid.Empty)
            {
                logger.LogWarning("Tool approval rejected - empty requestId");
                return Results.BadRequest(new { error = "requestId is required." });
            }

            var resolved = coordinator.TryResolve(
                body.RequestId,
                new ApprovalDecision(body.Approved, body.RememberForSession));

            if (!resolved)
            {
                logger.LogWarning("Tool approval not found: {RequestId}", body.RequestId);
                return Results.NotFound(new { error = "Unknown or already-resolved approval request." });
            }

            logger.LogDebug("Tool approval resolved: {RequestId}", body.RequestId);
            return Results.Ok(new { resolved = true, requestId = body.RequestId });
        })
        .WithName("ResolveToolApproval")
        .WithTags("Chat")
        .WithDescription("Resolve a pending tool-approval request with the user's Approve/Deny decision.");

        app.MapGet("/api/tool-approvals", async (
            Guid? sessionId,
            string? toolName,
            bool? approved,
            DateTime? since,
            DateTime? until,
            int? limit,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var query = db.ToolApprovalLogs.AsQueryable();

            if (sessionId.HasValue)
                query = query.Where(log => log.SessionId == sessionId.Value);

            if (!string.IsNullOrWhiteSpace(toolName))
                query = query.Where(log => log.ToolName == toolName);

            if (approved.HasValue)
                query = query.Where(log => log.Approved == approved.Value);

            if (since.HasValue)
                query = query.Where(log => log.DecidedAt >= since.Value);

            if (until.HasValue)
                query = query.Where(log => log.DecidedAt <= until.Value);

            var pageSize = Math.Min(limit ?? 100, 500);

            var logs = await query
                .OrderByDescending(log => log.DecidedAt)
                .Take(pageSize)
                .Select(log => new ToolApprovalLogDto
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

            return Results.Ok(new ToolApprovalHistoryResponse
            {
                Logs = logs,
                TotalCount = logs.Count,
                ApprovedCount = logs.Count(l => l.Approved),
                DeniedCount = logs.Count(l => !l.Approved),
                Filters = new
                {
                    sessionId,
                    toolName,
                    approved,
                    since,
                    until
                }
            });
        })
        .WithName("GetToolApprovalHistory")
        .WithTags("Tools")
        .WithDescription("Query tool approval audit log with filters (sessionId, toolName, approved, date range). Default limit 100, max 500.");
    }
}

/// <summary>
/// Body of <c>POST /api/chat/tool-approval</c>. Lambert's <c>ToolApprovalCard</c>
/// posts this when the user clicks Approve or Deny.
/// </summary>
public sealed record ToolApprovalDecisionRequest
{
    public Guid RequestId { get; init; }
    public bool Approved { get; init; }
    public bool RememberForSession { get; init; }
}

public sealed record ToolApprovalLogDto
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

public sealed record ToolApprovalHistoryResponse
{
    public List<ToolApprovalLogDto> Logs { get; init; } = [];
    public int TotalCount { get; init; }
    public int ApprovedCount { get; init; }
    public int DeniedCount { get; init; }
    public object? Filters { get; init; }
}

