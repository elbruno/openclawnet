using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AI.Foundry.Local;
using OpenClawNet.Models.Abstractions;
using BetalgoMessage = Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage;

namespace OpenClawNet.Models.FoundryLocal;

/// <summary>
/// IModelClient implementation using Microsoft Foundry Local SDK.
/// Runs models on-device with zero cloud dependency.
/// </summary>
public sealed class FoundryLocalModelClient : IModelClient, IAsyncDisposable
{
    private readonly FoundryLocalOptions _options;
    private readonly ILogger<FoundryLocalModelClient> _logger;
    private FoundryLocalManager? _manager;
    private OpenAIChatClient? _chatClient;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public FoundryLocalModelClient(IOptions<FoundryLocalOptions> options, ILogger<FoundryLocalModelClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "foundry-local";

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var config = new Configuration { AppName = _options.AppName };
#pragma warning disable CS8625 // SDK accepts null logger; signature is non-nullable
            await FoundryLocalManager.CreateAsync(config, logger: null!, ct);
#pragma warning restore CS8625
            _manager = FoundryLocalManager.Instance;

            var catalog = await _manager.GetCatalogAsync(ct);
            var model = await catalog.GetModelAsync(_options.Model, ct)
                ?? throw new InvalidOperationException($"Foundry Local model '{_options.Model}' not found in catalog.");
#pragma warning disable CS8625 // SDK accepts null progress; signature is non-nullable but tolerates null at runtime
            await model.DownloadAsync(null!, ct);
#pragma warning restore CS8625
            await model.LoadAsync(ct);

            _chatClient = await model.GetChatClientAsync(ct);

            _initialized = true;
            _logger.LogInformation("Foundry Local initialized with model {Model}", _options.Model);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static List<BetalgoMessage> MapMessages(IReadOnlyList<Abstractions.ChatMessage> messages)
    {
        return messages.Select(m => new BetalgoMessage
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
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var messages = MapMessages(request.Messages);
        var response = await _chatClient!.CompleteChatAsync(messages, cancellationToken);

        var choice = response.Choices?.FirstOrDefault();

        return new ChatResponse
        {
            Content = choice?.Message?.Content ?? string.Empty,
            Role = ChatMessageRole.Assistant,
            Model = request.Model ?? _options.Model,
            Usage = response.Usage is not null ? new UsageInfo
            {
                PromptTokens = response.Usage.PromptTokens,
                CompletionTokens = response.Usage.CompletionTokens ?? 0,
                TotalTokens = response.Usage.TotalTokens
            } : null,
            FinishReason = choice?.FinishReason ?? "stop"
        };
    }

    public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var messages = MapMessages(request.Messages);

        await foreach (var chunk in _chatClient!.CompleteChatStreamingAsync(messages, cancellationToken))
        {
            var choice = chunk.Choices?.FirstOrDefault();
            yield return new ChatResponseChunk
            {
                Content = choice?.Delta?.Content,
                FinishReason = choice?.FinishReason
            };
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_manager is not null)
        {
            await _manager.StopWebServiceAsync();
            _manager = null;
        }
        _chatClient = null;
        _initialized = false;
        _initLock.Dispose();
    }
}
