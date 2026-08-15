using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Models.Foundry;

/// <summary>
/// Microsoft Foundry provider — uses the OpenAI-compatible chat completions API
/// exposed by Foundry-hosted model endpoints.
/// </summary>
public sealed class FoundryModelClient : IModelClient
{
    private readonly HttpClient _httpClient;
    private readonly FoundryOptions _options;
    private readonly ILogger<FoundryModelClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FoundryModelClient(HttpClient httpClient, IOptions<FoundryOptions> options, ILogger<FoundryModelClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrEmpty(_options.Endpoint))
        {
            // Trailing slash is required so that relative paths such as "chat/completions"
            // are resolved relative to the full endpoint path. Without it, HttpClient strips
            // any path segment from BaseAddress when resolving absolute-path relatives.
            _httpClient.BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/");
        }
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }
    }

    public string ProviderName => "foundry";

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var payload = BuildPayload(request, stream: false);
        _logger.LogDebug("Sending chat to Foundry: model={Model}", request.Model ?? _options.Model);

        var response = await _httpClient.PostAsJsonAsync("chat/completions", payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<FoundryChatResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty response from Foundry");

        var choice = result.Choices?.FirstOrDefault();

        return new ChatResponse
        {
            Content = choice?.Message?.Content ?? string.Empty,
            Role = ChatMessageRole.Assistant,
            Model = result.Model ?? _options.Model,
            Usage = result.Usage is not null ? new UsageInfo
            {
                PromptTokens = result.Usage.PromptTokens,
                CompletionTokens = result.Usage.CompletionTokens,
                TotalTokens = result.Usage.TotalTokens
            } : null,
            FinishReason = choice?.FinishReason
        };
    }

    public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var payload = BuildPayload(request, stream: true);
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
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
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            FoundryStreamChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<FoundryStreamChunk>(data, JsonOptions); }
            catch { continue; }

            if (chunk is null) continue;

            var delta = chunk.Choices?.FirstOrDefault()?.Delta;
            yield return new ChatResponseChunk
            {
                Content = delta?.Content,
                FinishReason = chunk.Choices?.FirstOrDefault()?.FinishReason
            };
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.Endpoint) || string.IsNullOrEmpty(_options.ApiKey))
            return false;

        try
        {
            var response = await _httpClient.GetAsync("models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrEmpty(_options.Endpoint) || string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("Foundry is not configured. Set Endpoint and ApiKey.");
    }

    private object BuildPayload(ChatRequest request, bool stream)
    {
        return new
        {
            model = request.Model ?? _options.Model,
            messages = request.Messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                content = m.Content
            }),
            temperature = request.Temperature ?? _options.Temperature,
            max_tokens = request.MaxTokens ?? _options.MaxTokens,
            stream
        };
    }
}

// Foundry API DTOs (OpenAI-compatible format)
internal sealed class FoundryChatResponse
{
    public string? Model { get; set; }
    public List<FoundryChoice>? Choices { get; set; }
    public FoundryUsage? Usage { get; set; }
}

internal sealed class FoundryChoice
{
    public FoundryMessage? Message { get; set; }
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class FoundryMessage
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

internal sealed class FoundryUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

internal sealed class FoundryStreamChunk
{
    public List<FoundryStreamChoice>? Choices { get; set; }
}

internal sealed class FoundryStreamChoice
{
    public FoundryStreamDelta? Delta { get; set; }
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class FoundryStreamDelta
{
    public string? Content { get; set; }
}
