using OpenClawNet.Agent;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");
        
        group.MapPost("/", async (
            ChatMessageRequest request,
            IAgentOrchestrator orchestrator,
            IAgentProfileStore profileStore,
            ILogger<GatewayProgramMarker> logger,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest(new { error = "Message is required and cannot be empty." });
            }

            try
            {
                // Resolve agent profile: explicit name → default profile
                AgentProfile? profile = null;
                if (!string.IsNullOrEmpty(request.AgentProfileName))
                {
                    profile = await profileStore.GetAsync(request.AgentProfileName);
                    // Guard: chat must use Standard profiles only.
                    if (profile is not null && profile.Kind != ProfileKind.Standard)
                    {
                        profile = null;
                    }
                }
                profile ??= await profileStore.GetDefaultAsync();

                // Resolve provider: profile.Provider or request.Provider → definition → global fallback
                var resolver = httpContext.RequestServices.GetService<ProviderResolver>();
                var resolvedProvider = resolver is not null
                    ? await resolver.ResolveAsync(request.Provider ?? profile.Provider)
                    : null;

                // Sync RuntimeModelSettings so the runtime uses the resolved endpoint/apiKey
                // instead of the global default. Acceptable for this single-user demo app.
                if (resolvedProvider?.DefinitionName is not null)
                {
                    var runtimeSettings = httpContext.RequestServices.GetService<RuntimeModelSettings>();
                    if (runtimeSettings is not null)
                    {
                        runtimeSettings.Update(new ModelProviderConfig
                        {
                            Provider = resolvedProvider.ProviderType,
                            Model = string.IsNullOrWhiteSpace(resolvedProvider.Model) ? null : resolvedProvider.Model,
                            Endpoint = resolvedProvider.Endpoint,
                            ApiKey = resolvedProvider.ApiKey,
                            DeploymentName = string.IsNullOrWhiteSpace(resolvedProvider.DeploymentName) ? null : resolvedProvider.DeploymentName,
                            AuthMode = resolvedProvider.AuthMode,
                        });
                    }
                }

                var agentRequest = new AgentRequest
                {
                    SessionId = request.SessionId,
                    UserMessage = request.Message,
                    Model = request.Model ?? resolvedProvider?.Model,
                    Provider = resolvedProvider?.ProviderType ?? request.Provider ?? profile.Provider,
                    AgentProfileName = profile.Name,
                    AgentProfileInstructions = profile.Instructions,
                    ResolvedProvider = resolvedProvider
                };
                
                var response = await orchestrator.ProcessAsync(agentRequest);
                
                return Results.Ok(new ChatMessageResponse
                {
                    Content = response.Content,
                    ToolCallCount = response.ToolCallCount,
                    TotalTokens = response.TotalTokens
                });
            }
            catch (ModelProviderUnavailableException ex)
            {
                logger.LogError(ex, "Model provider '{Provider}' is unavailable for chat request", ex.ProviderName);
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "HTTP error communicating with model provider for chat request");
                return Results.Json(
                    new { error = "Model provider is unavailable. Please check that the provider is running." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("SendChatMessage")
        .WithDescription("Send a message and get a response");

        group.MapPost("/{id}/auto-rename", PostAutoRename)
            .WithName("PostChatAutoRename")
            .WithDescription("Generate a session name based on recent conversation context");
    }

    private static async Task<IResult> PostAutoRename(
        Guid id,
        IConversationStore conversationStore,
        ChatNamingService namingService,
        ILogger<GatewayProgramMarker> logger,
        CancellationToken ct)
    {
        try
        {
            // Get the session
            var session = await conversationStore.GetSessionAsync(id, ct);
            if (session is null)
                return Results.NotFound(new { error = "Chat session not found." });

            // Get recent messages
            var messages = await conversationStore.GetMessagesAsync(id, ct);
            if (!messages.Any())
                return Results.Ok(new { generatedName = "New Chat", updated = false });

            // Generate new name
            var generatedName = await namingService.GenerateNameAsync(messages, ct);

            await conversationStore.UpdateSessionTitleAsync(id, generatedName, ct);

            logger.LogInformation("Auto-renamed chat session {SessionId} to '{Title}'", id, generatedName);

            return Results.Ok(new { generatedName, updated = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error generating chat name for session {SessionId}", id);
            return Results.Json(
                new { error = "Failed to generate name", generatedName = "New Chat" },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public sealed record ChatMessageRequest
{
    public Guid SessionId { get; init; }
    public required string Message { get; init; }
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public string? AgentProfileName { get; init; }
}

public sealed record ChatMessageResponse
{
    public required string Content { get; init; }
    public int ToolCallCount { get; init; }
    public int TotalTokens { get; init; }
}
