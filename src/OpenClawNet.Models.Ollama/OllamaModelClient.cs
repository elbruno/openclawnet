using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Models.Ollama;

public sealed class OllamaModelClient : IModelClient
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaModelClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaModelClient(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaModelClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        // BaseAddress is configured via AddHttpClient in OllamaServiceCollectionExtensions
        _logger = logger;
    }

    public string ProviderName => "ollama";

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var ollamaRequest = MapToOllamaRequest(request, stream: false);

        _logger.LogDebug("Sending chat request to Ollama: model={Model}", request.Model);

        var response = await _httpClient.PostAsJsonAsync("/api/chat", ollamaRequest, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty response from Ollama");

        return MapToChatResponse(ollamaResponse, request.Model ?? string.Empty);
    }

    public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ollamaRequest = MapToOllamaRequest(request, stream: true);

        _logger.LogDebug("Starting streaming chat with Ollama: model={Model}", request.Model);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(ollamaRequest, JsonOptions), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(line)) continue;

            OllamaChatResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk is null) continue;

            // Emit tool calls from any chunk — Ollama sends tool_calls in a
            // separate chunk before the final done=true chunk.
            List<ToolCall>? toolCalls = null;
            if (chunk.Message?.ToolCalls is { Count: > 0 } ollamaToolCalls)
            {
                toolCalls = ollamaToolCalls.Select((tc, i) => new ToolCall
                {
                    Id = tc.Id ?? $"call_{i}_{tc.Function?.Name ?? "unknown"}",
                    Name = tc.Function?.Name ?? string.Empty,
                    Arguments = tc.Function?.Arguments is not null
                        ? System.Text.Json.JsonSerializer.Serialize(tc.Function.Arguments)
                        : "{}"
                }).ToList();
            }

            yield return new ChatResponseChunk
            {
                Content = chunk.Message?.Content,
                ToolCalls = toolCalls?.AsReadOnly(),
                FinishReason = chunk.Done ? "stop" : null
            };

            if (chunk.Done) break;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private OllamaChatRequest MapToOllamaRequest(ChatRequest request, bool stream)
    {
        var messages = request.Messages.Select(m => new OllamaMessage
        {
            Role = m.Role switch
            {
                ChatMessageRole.System => "system",
                ChatMessageRole.User => "user",
                ChatMessageRole.Assistant => "assistant",
                ChatMessageRole.Tool => "tool",
                _ => "user"
            },
            Content = m.Content
        }).ToList();

        List<OllamaToolDefinition>? tools = null;
        if (request.Tools is { Count: > 0 })
        {
            tools = request.Tools.Select(t => new OllamaToolDefinition
            {
                Type = "function",
                Function = new OllamaFunctionDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Parameters?.RootElement
                }
            }).ToList();
        }

        return new OllamaChatRequest
        {
            Model = string.IsNullOrEmpty(request.Model) ? _options.Model : request.Model,
            Messages = messages,
            Tools = tools,
            Stream = stream,
            Options = new OllamaRequestOptions
            {
                Temperature = request.Temperature ?? _options.Temperature,
                NumPredict = request.MaxTokens ?? _options.MaxTokens
            }
        };
    }

    private static ChatResponse MapToChatResponse(OllamaChatResponse response, string model)
    {
        List<ToolCall>? toolCalls = null;
        if (response.Message?.ToolCalls is { Count: > 0 })
        {
            toolCalls = response.Message.ToolCalls
                .Select((tc, i) => new ToolCall
                {
                    Id = tc.Id ?? $"call_{i}_{tc.Function?.Name ?? "unknown"}",
                    Name = tc.Function?.Name ?? string.Empty,
                    Arguments = tc.Function?.Arguments is not null
                        ? System.Text.Json.JsonSerializer.Serialize(tc.Function.Arguments)
                        : "{}"
                })
                .ToList();
        }

        return new ChatResponse
        {
            Content = response.Message?.Content ?? string.Empty,
            Role = ChatMessageRole.Assistant,
            ToolCalls = toolCalls,
            Model = model,
            Usage = response.PromptEvalCount > 0 ? new UsageInfo
            {
                PromptTokens = response.PromptEvalCount,
                CompletionTokens = response.EvalCount,
                TotalTokens = response.PromptEvalCount + response.EvalCount
            } : null,
            FinishReason = response.Done ? "stop" : null
        };
    }
}

// Ollama API DTOs
internal sealed class OllamaChatRequest
{
    public string Model { get; set; } = string.Empty;
    public List<OllamaMessage> Messages { get; set; } = [];
    public List<OllamaToolDefinition>? Tools { get; set; }
    public bool Stream { get; set; }
    public OllamaRequestOptions? Options { get; set; }
    public string? Format { get; set; }
}

internal sealed class OllamaMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

internal sealed class OllamaToolDefinition
{
    public string Type { get; set; } = "function";
    public OllamaFunctionDefinition Function { get; set; } = new();
}

internal sealed class OllamaFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement? Parameters { get; set; }
}

internal sealed class OllamaToolCall
{
    public string? Id { get; set; }
    public OllamaFunctionCall? Function { get; set; }
}

internal sealed class OllamaFunctionCall
{
    public string? Name { get; set; }
    public JsonElement? Arguments { get; set; }
}

internal sealed class OllamaRequestOptions
{
    public double? Temperature { get; set; }
    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }
}

internal sealed class OllamaChatResponse
{
    public string Model { get; set; } = string.Empty;
    public OllamaMessage? Message { get; set; }
    public bool Done { get; set; }
    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; set; }
    [JsonPropertyName("eval_count")]
    public int EvalCount { get; set; }
}
