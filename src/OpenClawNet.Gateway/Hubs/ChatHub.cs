using Microsoft.AspNetCore.SignalR;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Gateway.Hubs;

/// <summary>
/// SignalR chat hub — kept for backward compatibility.
/// New clients should use POST /api/chat/stream (HTTP NDJSON streaming) instead.
/// </summary>
[Obsolete("Use POST /api/chat/stream (HTTP NDJSON streaming) instead. This hub will be removed in a future release.")]
public sealed class ChatHub : Hub
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IAgentOrchestrator orchestrator, ILogger<ChatHub> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async IAsyncEnumerable<ChatHubMessage> StreamChat(Guid sessionId, string message, string? model = null)
    {
        _logger.LogInformation("StreamChat invoked: SessionId={SessionId}, Model={Model}, MessageLength={Length}", sessionId, model ?? "default", message.Length);

        var request = new AgentRequest
        {
            SessionId = sessionId,
            UserMessage = message,
            Model = model
        };

        var eventCount = 0;
        string? errorMessage = null;

        var enumerator = _orchestrator.StreamAsync(request).GetAsyncEnumerator();
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (ModelProviderUnavailableException ex)
                {
                    _logger.LogError(ex, "Model provider '{Provider}' is unavailable during streaming chat", ex.ProviderName);
                    errorMessage = $"Model provider is unavailable: {ex.Message}";
                    break;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "HTTP error during streaming chat");
                    errorMessage = "Model provider is unavailable. Please check that the provider is running.";
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error during streaming chat: SessionId={SessionId}", sessionId);
                    errorMessage = $"Provider error: {ex.Message}";
                    break;
                }

                if (!hasNext)
                    break;

                var evt = enumerator.Current;
                eventCount++;

                if (evt.Type == AgentStreamEventType.Error)
                    _logger.LogError("Stream error event: SessionId={SessionId}, Content={Content}", sessionId, evt.Content);
                else
                    _logger.LogDebug("Stream event #{Count}: Type={Type}", eventCount, evt.Type);

                yield return evt.Type switch
                {
                    AgentStreamEventType.ContentDelta      => new ChatHubMessage("content",          evt.Content ?? ""),
                    AgentStreamEventType.ToolApprovalRequest => new ChatHubMessage("tool_approval",  evt.ToolName ?? ""),
                    AgentStreamEventType.ToolApprovalResolved => new ChatHubMessage("tool_approval_resolved",  evt.ToolName ?? ""),
                    AgentStreamEventType.ToolCallStart     => new ChatHubMessage("tool_start",       evt.ToolName ?? ""),
                    AgentStreamEventType.ToolCallComplete  => new ChatHubMessage("tool_complete",    evt.ToolResult?.Output ?? ""),
                    AgentStreamEventType.Complete          => new ChatHubMessage("complete",         evt.Content ?? ""),
                    AgentStreamEventType.Error             => new ChatHubMessage("error",            evt.Content ?? "An error occurred"),
                    _                                      => new ChatHubMessage("unknown",          "")
                };
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (errorMessage is not null)
        {
            yield return new ChatHubMessage("error", errorMessage);
        }
        else
        {
            _logger.LogInformation("StreamChat completed: SessionId={SessionId}, Events={EventCount}", sessionId, eventCount);
        }
    }
}

public sealed record ChatHubMessage(string Type, string Content);
