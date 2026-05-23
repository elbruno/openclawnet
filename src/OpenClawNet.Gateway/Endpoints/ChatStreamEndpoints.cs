using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// HTTP streaming endpoint for chat — replaces SignalR for reliability.
/// Returns newline-delimited JSON (NDJSON) so errors surface as HTTP status codes
/// and each token arrives as a discrete JSON line the client can parse incrementally.
/// </summary>
public static class ChatStreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapChatStreamEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat/stream", async (
            ChatStreamRequest request,
            IAgentOrchestrator orchestrator,
            IAgentProfileStore profileStore,
            ILogger<GatewayProgramMarker> logger,
            HttpContext httpContext,
            [Microsoft.AspNetCore.Mvc.FromServices] OpenClawNet.Storage.Entities.AgentInvocationLogger? invocationLogger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(
                    new { error = "Message is required and cannot be empty." }, cancellationToken);
                return;
            }

            // Resolve agent profile: explicit name → default profile
            AgentProfile? profile = null;
            if (!string.IsNullOrEmpty(request.AgentProfileName))
            {
                profile = await profileStore.GetAsync(request.AgentProfileName, cancellationToken);
                // Guard: chat sessions must use Standard profiles. System / ToolTester
                // profiles are reserved for internal tasks and the Tool Test surface.
                if (profile is not null && profile.Kind != ProfileKind.Standard)
                {
                    profile = null;
                }
            }
            profile ??= await profileStore.GetDefaultAsync(cancellationToken);

            // Resolve provider definition → concrete config (definition name → type + endpoint + model)
            var resolver = httpContext.RequestServices.GetService<ProviderResolver>();
            var resolvedProvider = resolver is not null
                ? await resolver.ResolveAsync(profile.Provider, cancellationToken)
                : null;

            // Check if this provider type is supported by RuntimeModelClient (the IModelClient path).
            // Providers like github-copilot use SDK-based clients that can't be created by RuntimeModelClient.
            var providerType = resolvedProvider?.ProviderType ?? profile.Provider ?? "ollama";
            var useAgentProviderPath = providerType.Equals("github-copilot", StringComparison.OrdinalIgnoreCase);

            if (!useAgentProviderPath)
            {
                // Standard path: sync RuntimeModelSettings and use the orchestrator
                var runtimeSettings = httpContext.RequestServices.GetService<RuntimeModelSettings>();
                if (runtimeSettings is not null)
                {
                    // Priority: resolved provider definition → direct profile settings
                    if (resolvedProvider?.DefinitionName is not null)
                    {
                        runtimeSettings.Update(new ModelProviderConfig
                        {
                            Provider = resolvedProvider.ProviderType,
                            Model = NullIfEmpty(resolvedProvider.Model),
                            Endpoint = resolvedProvider.Endpoint,
                            ApiKey = resolvedProvider.ApiKey,
                            DeploymentName = NullIfEmpty(resolvedProvider.DeploymentName),
                            AuthMode = resolvedProvider.AuthMode,
                        });
                    }
                    else if (profile.Provider is not null)
                    {
                        // Fallback: profile has direct provider/model without a definition
                        runtimeSettings.Update(new ModelProviderConfig
                        {
                            Provider = profile.Provider,
                            Model = NullIfEmpty(profile.Model),
                            Endpoint = profile.Endpoint,
                            ApiKey = profile.ApiKey,
                            DeploymentName = NullIfEmpty(profile.DeploymentName),
                            AuthMode = profile.AuthMode,
                        });
                    }
                }
            }

            httpContext.Response.ContentType = "application/x-ndjson";
            httpContext.Response.Headers["Cache-Control"] = "no-cache";
            httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

            if (useAgentProviderPath)
            {
                // Direct IAgentProvider path for SDK-based providers (e.g., GitHub Copilot)
                await StreamViaAgentProviderAsync(
                    request, profile, resolvedProvider, providerType,
                    httpContext, logger, cancellationToken);
            }
            else
            {
                // Standard orchestrator path (RuntimeModelClient → OllamaModelClient etc.)
                var agentRequest = new AgentRequest
                {
                    SessionId = request.SessionId,
                    UserMessage = request.Message,
                    Model = request.Model ?? profile.Model ?? resolvedProvider?.Model,
                    Provider = resolvedProvider?.ProviderType ?? profile.Provider,
                    AgentProfileName = profile.Name,
                    AgentProfileInstructions = profile.Instructions,
                    ResolvedProvider = resolvedProvider,
                    RequireToolApproval = profile.RequireToolApproval,
                    EnabledTools = ParseEnabledTools(profile.EnabledTools)
                };

                await StreamViaOrchestratorAsync(
                    agentRequest, orchestrator, request.SessionId,
                    httpContext, logger, cancellationToken);

                // Concept-review §4c — record sibling-model invocation row for chat turns.
                if (invocationLogger is not null)
                {
                    _ = invocationLogger.RecordAsync(new OpenClawNet.Storage.Entities.AgentInvocationLog
                    {
                        Kind = OpenClawNet.Storage.Entities.AgentInvocationKind.Chat,
                        SourceId = request.SessionId,
                        AgentProfileName = profile.Name,
                        Provider = agentRequest.Provider,
                        Model = agentRequest.Model,
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                    }, CancellationToken.None);
                }
            }
        })
        .WithName("StreamChat")
        .WithTags("Chat")
        .WithDescription("Stream a chat response as newline-delimited JSON events");
    }

    /// <summary>Standard streaming via the orchestrator (RuntimeModelClient path).</summary>
    private static async Task StreamViaOrchestratorAsync(
        AgentRequest agentRequest,
        IAgentOrchestrator orchestrator,
        Guid sessionId,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in orchestrator.StreamAsync(agentRequest, cancellationToken))
            {
                var streamEvent = new ChatStreamEvent
                {
                    Type = MapEventType(evt.Type),
                    Content = evt.Content,
                    ToolName = evt.ToolName,
                    ToolDescription = evt.ToolDescription,
                    ToolArgsJson = evt.ToolArgsJson,
                    RequestId = evt.RequestId,
                    ApprovalExpiresAt = evt.ApprovalExpiresAt,
                    SessionId = sessionId,
                    Approved = evt.Approved,
                    DecisionSource = evt.DecisionSource,
                    DecidedAt = evt.DecidedAt
                };

                var line = JsonSerializer.Serialize(streamEvent, JsonOpts);
                await httpContext.Response.WriteAsync(line + "\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (ModelProviderUnavailableException ex)
        {
            logger.LogError(ex, "Model provider '{Provider}' unavailable during chat stream", ex.ProviderName);
            await WriteErrorEventAsync(httpContext, $"Model provider is unavailable: {ex.Message}", sessionId, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error during chat stream");
            await WriteErrorEventAsync(httpContext, "Model provider is unavailable. Please check that the provider is running.", sessionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error during chat stream");
            await WriteErrorEventAsync(httpContext, $"Agent error: {ex.Message}", sessionId, cancellationToken);
        }
    }

    /// <summary>
    /// Direct streaming via IAgentProvider for SDK-based providers (e.g., GitHub Copilot)
    /// that RuntimeModelClient can't handle.
    /// </summary>
    private static async Task StreamViaAgentProviderAsync(
        ChatStreamRequest request,
        AgentProfile profile,
        ResolvedProviderConfig? resolvedProvider,
        string providerType,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Find the IAgentProvider for this type
            var providers = httpContext.RequestServices.GetServices<IAgentProvider>();
            var provider = providers
                .Where(p => p.GetType().Name != "RuntimeAgentProvider")
                .FirstOrDefault(p => p.ProviderName.Equals(providerType, StringComparison.OrdinalIgnoreCase));

            if (provider is null)
            {
                await WriteErrorEventAsync(httpContext, $"No provider registered for '{providerType}'", request.SessionId, cancellationToken);
                return;
            }

            // Create a profile enriched with definition connection details
            var enrichedProfile = new AgentProfile
            {
                Name = profile.Name,
                Provider = providerType,
                Endpoint = resolvedProvider?.Endpoint,
                ApiKey = resolvedProvider?.ApiKey,
                DeploymentName = resolvedProvider?.DeploymentName,
                AuthMode = resolvedProvider?.AuthMode,
                Instructions = profile.Instructions,
            };

            var chatClient = provider.CreateChatClient(enrichedProfile);
            var systemMsg = profile.Instructions ?? "You are a helpful AI assistant.";
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(Microsoft.Extensions.AI.ChatRole.System, systemMsg),
                new(Microsoft.Extensions.AI.ChatRole.User, request.Message)
            };

            // PR-F: model is owned by the provider (and surfaced via resolvedProvider.Model
            // when an explicit ModelProviderDefinition was selected). Log the definition's
            // model when present so the operator still sees what's about to run.
            logger.LogInformation("Streaming via IAgentProvider path: provider={Provider}, model={Model}",
                providerType, resolvedProvider?.Model ?? "(provider default)");

            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    var streamEvent = new ChatStreamEvent
                    {
                        Type = "content",
                        Content = update.Text,
                        SessionId = request.SessionId
                    };
                    var line = JsonSerializer.Serialize(streamEvent, JsonOpts);
                    await httpContext.Response.WriteAsync(line + "\n", cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                }
            }

            // Send complete event
            var completeEvent = new ChatStreamEvent { Type = "complete", SessionId = request.SessionId };
            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(completeEvent, JsonOpts) + "\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error streaming via IAgentProvider for '{Provider}'", providerType);
            await WriteErrorEventAsync(httpContext, $"Agent error: {ex.Message}", request.SessionId, cancellationToken);
        }
    }

    private static async Task WriteErrorEventAsync(HttpContext httpContext, string message, Guid sessionId, CancellationToken ct)
    {
        var errorEvent = new ChatStreamEvent { Type = "error", Content = message, SessionId = sessionId };
        var line = JsonSerializer.Serialize(errorEvent, JsonOpts);
        await httpContext.Response.WriteAsync(line + "\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    private static string MapEventType(AgentStreamEventType type) => type switch
    {
        AgentStreamEventType.ContentDelta => "content",
        AgentStreamEventType.ToolApprovalRequest => "tool_approval",
        AgentStreamEventType.ToolApprovalResolved => "tool_approval_resolved",
        AgentStreamEventType.ToolCallStart => "tool_start",
        AgentStreamEventType.ToolCallComplete => "tool_complete",
        AgentStreamEventType.Complete => "complete",
        AgentStreamEventType.Error => "error",
        _ => "unknown"
    };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Splits the persisted CSV form of <c>AgentProfile.EnabledTools</c> into the
    /// list shape the runtime expects. Returns <c>null</c> for empty so the runtime
    /// short-circuits its filter (back-compat).
    /// </summary>
    private static IReadOnlyList<string>? ParseEnabledTools(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }
}

public sealed record ChatStreamRequest
{
    public Guid SessionId { get; init; }
    public required string Message { get; init; }
    public string? Model { get; init; }
    public string? AgentProfileName { get; init; }
}

public sealed record ChatStreamEvent
{
    public required string Type { get; init; }
    public string? Content { get; init; }
    public string? ToolName { get; init; }
    public string? ToolDescription { get; init; }
    public string? ToolArgsJson { get; init; }
    public Guid? RequestId { get; init; }
    /// <summary>UTC instant at which the approval prompt auto-denies. Null = no timeout.</summary>
    public DateTime? ApprovalExpiresAt { get; init; }
    public Guid SessionId { get; init; }
    // Phase B: tool_approval_resolved fields
    public bool? Approved { get; init; }
    public string? DecisionSource { get; init; }
    public DateTime? DecidedAt { get; init; }
}
