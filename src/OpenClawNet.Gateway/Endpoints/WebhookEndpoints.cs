using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Event-driven webhook endpoints that trigger agent runs via HTTP POST.
/// Useful for GitHub push events, calendar reminders, monitoring alerts, etc.
/// </summary>
public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webhooks").WithTags("Webhooks");

        // POST /api/webhooks/{eventType}
        // Fires a new agent session with the webhook payload as context.
        group.MapPost("/{eventType}", async (
            string eventType,
            WebhookPayload payload,
            IAgentOrchestrator orchestrator,
            IDbContextFactory<OpenClawDbContext> dbFactory,
            ILogger<GatewayProgramMarker> logger) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            // Each webhook fires creates its own session for a clean audit trail.
            var session = new ChatSession
            {
                Title = $"Webhook: {eventType} @ {DateTime.UtcNow:u}",
                Provider = "webhook",
                Model = null
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();

            // Build a natural-language message so the agent understands the context.
            var contextJson = payload.Data is not null
                ? JsonSerializer.Serialize(payload.Data, new JsonSerializerOptions { WriteIndented = true })
                : null;

            var userMessage = string.IsNullOrEmpty(payload.Message)
                ? $"A '{eventType}' webhook event was received. Process it and take appropriate action.\n\nPayload:\n{contextJson}"
                : $"[{eventType} event] {payload.Message}" + (contextJson is not null ? $"\n\nPayload:\n{contextJson}" : "");

            var request = new AgentRequest
            {
                SessionId = session.Id,
                UserMessage = userMessage
            };

            try
            {
                var response = await orchestrator.ProcessAsync(request);

                return Results.Ok(new WebhookResponse
                {
                    SessionId = session.Id,
                    EventType = eventType,
                    AgentResponse = response.Content,
                    ToolCallCount = response.ToolCallCount
                });
            }
            catch (ModelProviderUnavailableException ex)
            {
                logger.LogError(ex, "Model provider '{Provider}' is unavailable for webhook '{EventType}'", ex.ProviderName, eventType);
                return Results.Json(
                    new { error = ex.Message, sessionId = session.Id, eventType },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "HTTP error communicating with model provider for webhook '{EventType}'", eventType);
                return Results.Json(
                    new { error = "Model provider is unavailable. Please check that the provider is running.", sessionId = session.Id, eventType },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("TriggerWebhook")
        .WithDescription("Trigger an agent run from an external event (push event, alert, cron, etc.)");

        // GET /api/webhooks — list recent webhook-triggered sessions
        group.MapGet("/", async (IDbContextFactory<OpenClawDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var sessions = await db.Sessions
                .Where(s => s.Provider == "webhook")
                .OrderByDescending(s => s.CreatedAt)
                .Take(20)
                .Select(s => new { s.Id, s.Title, s.CreatedAt })
                .ToListAsync();

            return Results.Ok(sessions);
        })
        .WithName("ListWebhookSessions");
    }
}

public sealed record WebhookPayload
{
    /// <summary>Optional human-readable description of the event.</summary>
    public string? Message { get; init; }

    /// <summary>Arbitrary event data passed as JSON to the agent.</summary>
    public JsonElement? Data { get; init; }
}

public sealed record WebhookResponse
{
    public Guid SessionId { get; init; }
    public required string EventType { get; init; }
    public required string AgentResponse { get; init; }
    public int ToolCallCount { get; init; }
}
