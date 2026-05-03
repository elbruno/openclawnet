using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenClawNet.Models.Abstractions;
using MEAIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using MEAIChatResponse = Microsoft.Extensions.AI.ChatResponse;
using OCChatMessage = OpenClawNet.Models.Abstractions.ChatMessage;
using OCChatResponse = OpenClawNet.Models.Abstractions.ChatResponse;
using ModelToolCall = OpenClawNet.Models.Abstractions.ToolCall;

namespace OpenClawNet.Agent;

/// <summary>
/// Bridges <see cref="IModelClient"/> to <see cref="IChatClient"/> so the Microsoft Agent Framework
/// can delegate to the solution-owned model abstraction.
/// </summary>
internal sealed class ModelClientChatClientAdapter : IChatClient
{
    private readonly IModelClient _modelClient;

    public ModelClientChatClientAdapter(IModelClient modelClient)
    {
        _modelClient = modelClient;
    }

    public async Task<MEAIChatResponse> GetResponseAsync(
        IEnumerable<MEAIChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var openClawMessages = messages.Select(ToOpenClawMessage).ToList();
        var tools = options?.Tools?.OfType<AIFunction>().Select(ToToolDefinition).ToList();

        var request = new ChatRequest
        {
            Messages = openClawMessages,
            Tools = tools is { Count: > 0 } ? tools : null
        };

        var response = await _modelClient.CompleteAsync(request, cancellationToken);
        return ToMEAIChatResponse(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<MEAIChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var openClawMessages = messages.Select(ToOpenClawMessage).ToList();
        var tools = options?.Tools?.OfType<AIFunction>().Select(ToToolDefinition).ToList();

        var request = new ChatRequest
        {
            Messages = openClawMessages,
            Tools = tools is { Count: > 0 } ? tools : null
        };

        await foreach (var chunk in _modelClient.StreamAsync(request, cancellationToken))
        {
            var contents = new List<AIContent>();
            if (!string.IsNullOrEmpty(chunk.Content))
                contents.Add(new TextContent(chunk.Content));
            if (chunk.ToolCalls is { Count: > 0 })
                foreach (var toolCall in chunk.ToolCalls)
                    contents.Add(new FunctionCallContent(toolCall.Id, toolCall.Name, ParseArguments(toolCall.Arguments)));

            if (contents.Count > 0)
                yield return new ChatResponseUpdate { Contents = contents, Role = ChatRole.Assistant };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    internal static OCChatMessage ToOpenClawMessage(MEAIChatMessage message)
    {
        var role = message.Role.Value switch
        {
            var v when v == ChatRole.System.Value => ChatMessageRole.System,
            var v when v == ChatRole.User.Value => ChatMessageRole.User,
            var v when v == ChatRole.Tool.Value => ChatMessageRole.Tool,
            _ => ChatMessageRole.Assistant
        };

        var text = message.Text ?? string.Empty;
        var functionResult = message.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        var toolCallId = functionResult?.CallId;
        if (string.IsNullOrEmpty(text) && functionResult?.Result is { } result)
        {
            text = result as string ?? JsonSerializer.Serialize(result);
        }
        var toolCalls = message.Contents
            .OfType<FunctionCallContent>()
            .Select(c => new ModelToolCall
            {
                Id = c.CallId ?? Guid.NewGuid().ToString("N"),
                Name = c.Name,
                Arguments = JsonSerializer.Serialize(c.Arguments)
            })
            .ToList();

        return new OCChatMessage
        {
            Role = role,
            Content = text,
            ToolCallId = toolCallId,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null
        };
    }

    internal static MEAIChatMessage ToMEAIMessage(OCChatMessage message)
    {
        var role = message.Role switch
        {
            ChatMessageRole.System => ChatRole.System,
            ChatMessageRole.User => ChatRole.User,
            ChatMessageRole.Tool => ChatRole.Tool,
            _ => ChatRole.Assistant
        };

        var contents = new List<AIContent>();

        if (message.Role == ChatMessageRole.Tool && message.ToolCallId is not null)
        {
            contents.Add(new FunctionResultContent(message.ToolCallId, message.Content));
        }
        else
        {
            if (!string.IsNullOrEmpty(message.Content))
                contents.Add(new TextContent(message.Content));

            if (message.ToolCalls is { Count: > 0 })
                foreach (var tc in message.ToolCalls)
                    contents.Add(new FunctionCallContent(tc.Id, tc.Name, ParseArguments(tc.Arguments)));
        }

        return new MEAIChatMessage(role, contents);
    }

    internal static MEAIChatResponse ToMEAIChatResponse(OCChatResponse response)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(response.Content))
            contents.Add(new TextContent(response.Content));
        if (response.ToolCalls is { Count: > 0 })
            foreach (var toolCall in response.ToolCalls)
                contents.Add(new FunctionCallContent(toolCall.Id, toolCall.Name, ParseArguments(toolCall.Arguments)));

        var meaiResponse = new MEAIChatResponse
        {
            Messages = [new MEAIChatMessage(ChatRole.Assistant, contents)]
        };

        if (response.Usage is { } usage)
        {
            meaiResponse.Usage = new UsageDetails
            {
                InputTokenCount = usage.PromptTokens,
                OutputTokenCount = usage.CompletionTokens,
                TotalTokenCount = usage.TotalTokens
            };
        }

        return meaiResponse;
    }

    private static ToolDefinition ToToolDefinition(AIFunction tool) =>
        new()
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = tool.JsonSchema.ValueKind != JsonValueKind.Undefined
                ? JsonDocument.Parse(tool.JsonSchema.GetRawText())
                : null
        };

    internal static IDictionary<string, object?> ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return new Dictionary<string, object?>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments) ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?> { ["raw"] = arguments };
        }
    }
}
