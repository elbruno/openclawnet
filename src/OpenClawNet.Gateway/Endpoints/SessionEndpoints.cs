using Microsoft.AspNetCore.Mvc;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sessions").WithTags("Sessions");
        
        group.MapGet("/", async (IConversationStore store) =>
        {
            var sessions = await store.ListSessionsAsync();
            return Results.Ok(sessions.Select(s => new SessionDto
            {
                Id = s.Id,
                Title = s.Title,
                Provider = s.Provider,
                Model = s.Model,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            }));
        })
        .WithName("ListSessions");
        
        group.MapPost("/", async (CreateSessionRequest? request, IConversationStore store) =>
        {
            var session = await store.CreateSessionAsync(request?.Title);
            return Results.Created($"/api/sessions/{session.Id}", new SessionDto
            {
                Id = session.Id,
                Title = session.Title,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt
            });
        })
        .WithName("CreateSession");
        
        group.MapGet("/{sessionId:guid}", async (Guid sessionId, IConversationStore store) =>
        {
            var session = await store.GetSessionAsync(sessionId);
            if (session is null) return Results.NotFound();
            
            return Results.Ok(new SessionDetailDto
            {
                Id = session.Id,
                Title = session.Title,
                Provider = session.Provider,
                Model = session.Model,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt,
                Messages = session.Messages.Select(m => new MessageDto
                {
                    Id = m.Id,
                    Role = m.Role,
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                    MessageType = m.MessageType,
                    ToolName = m.ToolName,
                    ToolArgsJson = m.ToolArgsJson,
                    ToolDecision = m.ToolDecision,
                    ToolDecidedBy = m.ToolDecidedBy,
                    ToolDecidedAt = m.ToolDecidedAt
                }).ToList()
            });
        })
        .WithName("GetSession");
        
        group.MapDelete("/{sessionId:guid}", async (Guid sessionId, IConversationStore store) =>
        {
            await store.DeleteSessionAsync(sessionId);
            return Results.NoContent();
        })
        .WithName("DeleteSession");

        group.MapDelete("/", async ([FromBody] BulkDeleteRequest request, IConversationStore store) =>
        {
            if (request.Ids is not { Count: > 0 })
                return Results.BadRequest("No session IDs provided.");
            var deleted = await store.DeleteSessionsBulkAsync(request.Ids);
            return Results.Ok(new { deleted });
        })
        .WithName("DeleteSessionsBulk")
        .Accepts<BulkDeleteRequest>("application/json");
        
        group.MapPatch("/{sessionId:guid}/title", async (Guid sessionId, UpdateTitleRequest request, IConversationStore store) =>
        {
            var session = await store.UpdateSessionTitleAsync(sessionId, request.Title);
            return Results.Ok(new SessionDto
            {
                Id = session.Id,
                Title = session.Title,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt
            });
        })
        .WithName("UpdateSessionTitle");
        
        group.MapGet("/{sessionId:guid}/messages", async (Guid sessionId, IConversationStore store) =>
        {
            var messages = await store.GetMessagesAsync(sessionId);
            return Results.Ok(messages.Select(m => new MessageDto
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                CreatedAt = m.CreatedAt,
                MessageType = m.MessageType,
                ToolName = m.ToolName,
                ToolArgsJson = m.ToolArgsJson,
                ToolDecision = m.ToolDecision,
                ToolDecidedBy = m.ToolDecidedBy,
                ToolDecidedAt = m.ToolDecidedAt
            }));
        })
        .WithName("GetSessionMessages");
    }
}

public sealed record CreateSessionRequest { public string? Title { get; init; } }
public sealed record UpdateTitleRequest { public required string Title { get; init; } }
public sealed record BulkDeleteRequest { public List<Guid> Ids { get; init; } = []; }

public sealed record SessionDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record SessionDetailDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<MessageDto> Messages { get; init; } = [];
}

public sealed record MessageDto
{
    public Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; }
    public string MessageType { get; init; } = "Chat";
    public string? ToolName { get; init; }
    public string? ToolArgsJson { get; init; }
    public string? ToolDecision { get; init; }
    public string? ToolDecidedBy { get; init; }
    public DateTime? ToolDecidedAt { get; init; }
}
