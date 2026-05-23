using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

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

        // POST /api/sessions/{sessionId}/promote-to-job
        // Promotes a chat session into a daily 9 AM scheduled job that runs for 5 days.
        // Prompt is derived from the most recent session summary, or the last user message if none exists.
        // Job is created in Draft state; the caller must POST /api/jobs/{id}/start to activate it.
        group.MapPost("/{sessionId:guid}/promote-to-job", async (
            Guid sessionId,
            PromoteChatToJobRequest? request,
            IDbContextFactory<OpenClawDbContext> dbFactory,
            IAgentProfileStore profileStore) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var session = await db.Sessions
                .Include(s => s.Messages)
                .Include(s => s.Summaries)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session is null) return Results.NotFound();

            // Prompt priority: latest summary → last user message → session title fallback
            var latestSummary = session.Summaries
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();

            string prompt;
            if (latestSummary is not null && !string.IsNullOrWhiteSpace(latestSummary.Summary))
            {
                prompt = latestSummary.Summary;
            }
            else
            {
                var lastUserMessage = session.Messages
                    .Where(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
                    .MaxBy(m => m.OrderIndex);
                prompt = !string.IsNullOrWhiteSpace(lastUserMessage?.Content)
                    ? lastUserMessage!.Content
                    : session.Title;
            }

            // Build a collision-free job name from the session title
            var rawName = !string.IsNullOrWhiteSpace(request?.Name)
                ? request!.Name.Trim()
                : session.Title.TrimEnd() + " (daily)";
            var jobName = await DemoEndpoints.GenerateUniqueJobNameAsync(db, rawName);

            // Prefer explicit agent profile → session's profile → default
            var agentProfileName = await JobEndpoints.ResolveAgentProfileNameAsync(
                request?.AgentProfileName ?? session.AgentProfileName, profileStore);

            var now = DateTime.UtcNow;
            const string cronExpression = "0 9 * * *"; // daily at 09:00 UTC
            var endAt = now.AddDays(5);

            // Compute next 9 AM UTC occurrence from now
            var today9 = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0, DateTimeKind.Utc);
            var nextRunAt = today9 > now ? today9 : today9.AddDays(1);

            var job = new ScheduledJob
            {
                Name = jobName,
                Prompt = prompt,
                CronExpression = cronExpression,
                IsRecurring = true,
                TriggerType = TriggerType.Cron,
                Status = JobStatus.Draft,
                StartAt = now,
                EndAt = endAt,
                NextRunAt = nextRunAt,
                NaturalLanguageSchedule = "Daily at 09:00 UTC for 5 days",
                AllowConcurrentRuns = false,
                AgentProfileName = agentProfileName,
                SourceTemplateName = "chat-promotion",
                TimeZone = request?.TimeZone
            };

            db.Jobs.Add(job);
            await db.SaveChangesAsync();

            return Results.Created($"/api/jobs/{job.Id}", new PromoteChatToJobResponse(
                job.Id,
                job.Name,
                sessionId,
                session.Title,
                prompt,
                cronExpression,
                nextRunAt,
                endAt,
                agentProfileName,
                $"Job '{job.Name}' created in Draft state. POST /api/jobs/{job.Id}/start to activate."
            ));
        })
        .WithName("PromoteChatToJob")
        .WithDescription("Promote a chat session into a daily 09:00 UTC scheduled job that runs for 5 days. " +
                         "Prompt is derived from the most recent session summary or last user message. " +
                         "Job starts in Draft state — activate via POST /api/jobs/{id}/start.");
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

/// <summary>Optional overrides for the promote-to-job endpoint.</summary>
public sealed record PromoteChatToJobRequest
{
    /// <summary>Override the generated job name. Dedup suffix is still applied on collision.</summary>
    public string? Name { get; init; }

    /// <summary>Override the agent profile. Falls back to the session's profile, then the system default.</summary>
    public string? AgentProfileName { get; init; }

    /// <summary>IANA timezone for the 09:00 trigger (e.g. "America/New_York"). Null = UTC.</summary>
    public string? TimeZone { get; init; }
}

/// <summary>Response from POST /api/sessions/{sessionId}/promote-to-job.</summary>
public sealed record PromoteChatToJobResponse(
    Guid JobId,
    string JobName,
    Guid SourceSessionId,
    string SourceSessionTitle,
    string Prompt,
    string CronExpression,
    DateTime? NextRunAt,
    DateTime EndsAt,
    string? AgentProfileName,
    string Message
);
