using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Query endpoints for tool call history across all sessions and job runs.
/// Provides debugging and observability into tool invocation patterns, success rates, and errors.
/// </summary>
public static class ToolCallHistoryEndpoints
{
    public static void MapToolCallHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tool-call-history").WithTags("Tools");

        group.MapGet("/", async (
            string? toolName,
            Guid? sessionId,
            bool? success,
            DateTime? since,
            DateTime? until,
            int? limit,
            IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var query = db.ToolCalls.AsQueryable();

            if (!string.IsNullOrWhiteSpace(toolName))
                query = query.Where(tc => tc.ToolName == toolName);

            if (sessionId.HasValue)
                query = query.Where(tc => tc.SessionId == sessionId.Value);

            if (success.HasValue)
                query = query.Where(tc => tc.Success == success.Value);

            if (since.HasValue)
                query = query.Where(tc => tc.ExecutedAt >= since.Value);

            if (until.HasValue)
                query = query.Where(tc => tc.ExecutedAt <= until.Value);

            var pageSize = Math.Min(limit ?? 100, 500);

            var toolCalls = await query
                .OrderByDescending(tc => tc.ExecutedAt)
                .Take(pageSize)
                .Select(tc => new ToolCallHistoryDto
                {
                    Id = tc.Id,
                    SessionId = tc.SessionId,
                    ToolName = tc.ToolName,
                    Arguments = tc.Arguments,
                    Result = tc.Result,
                    Success = tc.Success,
                    DurationMs = tc.DurationMs,
                    ExecutedAt = tc.ExecutedAt
                })
                .ToListAsync();

            var successCount = toolCalls.Count(tc => tc.Success);
            var failureCount = toolCalls.Count(tc => !tc.Success);

            return Results.Ok(new ToolCallHistoryResponse
            {
                ToolCalls = toolCalls,
                TotalCount = toolCalls.Count,
                SuccessCount = successCount,
                FailureCount = failureCount,
                Filters = new
                {
                    toolName,
                    sessionId,
                    success,
                    since,
                    until
                }
            });
        })
        .WithName("GetToolCallHistory")
        .WithDescription("Query tool call history with filters (toolName, sessionId, success, date range). Default limit 100, max 500.");
    }
}

public sealed record ToolCallHistoryDto
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public string? Result { get; init; }
    public bool Success { get; init; }
    public double DurationMs { get; init; }
    public DateTime ExecutedAt { get; init; }
}

public sealed record ToolCallHistoryResponse
{
    public List<ToolCallHistoryDto> ToolCalls { get; init; } = [];
    public int TotalCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public object? Filters { get; init; }
}
