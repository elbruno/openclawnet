using OpenClawNet.Memory;

namespace OpenClawNet.Gateway.Endpoints;

public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/memory").WithTags("Memory");
        
        group.MapGet("/{sessionId:guid}/summary", async (Guid sessionId, IMemoryService memoryService) =>
        {
            var summary = await memoryService.GetSessionSummaryAsync(sessionId);
            return summary is not null
                ? Results.Ok(new { sessionId, summary })
                : Results.Ok(new { sessionId, summary = (string?)null, message = "No summary available" });
        })
        .WithName("GetSessionSummary");
        
        group.MapGet("/{sessionId:guid}/summaries", async (Guid sessionId, IMemoryService memoryService) =>
        {
            var summaries = await memoryService.GetAllSummariesAsync(sessionId);
            return Results.Ok(summaries);
        })
        .WithName("GetAllSummaries");
        
        group.MapGet("/{sessionId:guid}/stats", async (Guid sessionId, IMemoryService memoryService) =>
        {
            var stats = await memoryService.GetStatsAsync(sessionId);
            return Results.Ok(stats);
        })
        .WithName("GetMemoryStats");

        var agentMemory = app.MapGroup("/api/agents/{agentId}/memory").WithTags("Memory");

        agentMemory.MapPost("/search", async (
            string agentId,
            AgentMemorySearchRequest request,
            IAgentMemoryStore store,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new { error = "query is required" });
            }

            var topK = request.TopK is > 0 ? Math.Min(request.TopK.Value, 25) : 5;
            var hits = await store.SearchAsync(agentId, request.Query, topK, ct);
            loggerFactory.CreateLogger("MemoryEndpoints").LogInformation(
                "Agent memory search: agent={AgentId}, topK={TopK}, hits={HitCount}",
                agentId, topK, hits.Count);

            return Results.Ok(new
            {
                agentId,
                query = request.Query,
                topK,
                count = hits.Count,
                hits
            });
        })
        .WithName("SearchAgentMemory");

        agentMemory.MapDelete("/{memoryId}", async (
            string agentId,
            string memoryId,
            IAgentMemoryStore store,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            await store.DeleteAsync(agentId, memoryId, ct);
            loggerFactory.CreateLogger("MemoryEndpoints").LogInformation(
                "Agent memory delete: agent={AgentId}, memoryId={MemoryId}",
                agentId, memoryId);

            return Results.Ok(new { agentId, memoryId, deleted = true });
        })
        .WithName("DeleteAgentMemory");
    }
}

public sealed record AgentMemorySearchRequest
{
    public string Query { get; init; } = string.Empty;
    public int? TopK { get; init; }
}
