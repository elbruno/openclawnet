using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;
using OpenClawNet.Models.Foundry;
using OpenClawNet.Models.FoundryLocal;
using OpenClawNet.Models.Ollama;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// A delegating <see cref="IModelClient"/> that creates (and caches) the appropriate
/// provider-specific client based on the currently-active <see cref="RuntimeModelSettings"/>.
///
/// The underlying client is recreated automatically whenever the settings change —
/// no gateway restart is needed after updating the provider via the Settings UI.
///
/// When a request fails the primary provider, this client tries each configured fallback
/// provider in order, logging each attempt. If all providers fail, the last exception is re-thrown.
/// </summary>
internal sealed class RuntimeModelClient : IModelClient, IDisposable
{
    private readonly RuntimeModelSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RuntimeModelClient> _logger;

    private IModelClient? _client;
    private string _clientKey = string.Empty;
    private HttpClient? _ownedHttp;
    private readonly Lock _lock = new();

    public RuntimeModelClient(
        RuntimeModelSettings settings,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RuntimeModelClient>();
    }

    public string ProviderName => GetOrCreate().ProviderName;

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = _settings.Current;
        var primary = GetOrCreate();

        try
        {
            return await primary.CompleteAsync(request, cancellationToken);
        }
        catch (Exception primaryEx)
        {
            return await TryFallbacksAsync(
                cfg,
                client => client.CompleteAsync(request, cancellationToken),
                primaryEx,
                cancellationToken);
        }
    }

    public IAsyncEnumerable<ChatResponseChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => StreamWithFallbackAsync(request, cancellationToken);

    /// <summary>
    /// Streams from the primary provider and retries each fallback if the initial connection fails.
    /// Once the stream has started yielding chunks, fallback is not attempted for mid-stream errors.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseChunk> StreamWithFallbackAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cfg = _settings.Current;
        var clients = BuildClientChain(cfg);

        for (var i = 0; i < clients.Count; i++)
        {
            var isLast = i == clients.Count - 1;
            var enumerator = clients[i].StreamAsync(request, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            bool hasNext;

            // Test whether the stream can start — can't yield inside try/catch, but try/finally is fine
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (Exception ex)
            {
                await enumerator.DisposeAsync();

                if (isLast)
                    throw WrapIfProviderUnavailable(cfg.Provider, ex);

                _logger.LogWarning(
                    ex,
                    "Primary provider {Provider} stream failed, trying fallback {Fallback}",
                    cfg.Provider,
                    cfg.Fallbacks.ElementAtOrDefault(i) ?? "(none)");

                continue;
            }

            // Stream started successfully — yield all remaining chunks.
            // try/finally is allowed with yield return; try/catch is not.
            try
            {
                while (hasNext)
                {
                    yield return enumerator.Current;
                    hasNext = await enumerator.MoveNextAsync();
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            yield break;
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => GetOrCreate().IsAvailableAsync(cancellationToken);

    public void Dispose()
    {
        lock (_lock)
        {
            _ownedHttp?.Dispose();
            _ownedHttp = null;
            _client = null;
        }
    }

    // ──────────────────────────────────────────────────────────

    private async Task<T> TryFallbacksAsync<T>(
        ModelProviderConfig cfg,
        Func<IModelClient, Task<T>> call,
        Exception primaryException,
        CancellationToken cancellationToken)
    {
        if (cfg.Fallbacks.Count == 0)
            throw WrapIfProviderUnavailable(cfg.Provider, primaryException);

        Exception lastException = primaryException;

        foreach (var fallbackName in cfg.Fallbacks)
        {
            _logger.LogWarning(
                "Primary provider {Provider} failed, trying fallback {Fallback}",
                cfg.Provider, fallbackName);

            var fallbackCfg = CloneWithProvider(cfg, fallbackName);
            var fallbackClient = CreateClient(fallbackCfg);

            try
            {
                return await call(fallbackClient);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback provider {Fallback} also failed", fallbackName);
                lastException = ex;
            }
        }

        throw WrapIfProviderUnavailable(cfg.Provider, lastException);
    }

    /// <summary>
    /// Wraps connection-related exceptions in <see cref="ModelProviderUnavailableException"/>
    /// so endpoints can distinguish "provider down" from other failures.
    /// </summary>
    private static Exception WrapIfProviderUnavailable(string provider, Exception ex)
    {
        if (ex is ModelProviderUnavailableException)
            return ex;

        if (ex is HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            return new ModelProviderUnavailableException(
                provider,
                $"Model provider '{provider}' is unavailable. Ensure the provider is running and accessible.",
                ex);
        }

        return ex;
    }

    private IModelClient GetOrCreate()
    {
        var cfg = _settings.Current;
        var key = cfg.CacheKey;

        lock (_lock)
        {
            if (_client is not null && _clientKey == key)
                return _client;

            // Settings changed — dispose the old HTTP client (if we own one)
            _ownedHttp?.Dispose();
            _ownedHttp = null;

            _client = CreateClient(cfg, isPrimary: true);
            _clientKey = key;
            return _client;
        }
    }

    /// <summary>
    /// Clones a config but overrides the provider name, clearing provider-specific fields.
    /// Fallback entries intentionally don't inherit the Fallbacks list to prevent recursive chaining.
    /// </summary>
    private static ModelProviderConfig CloneWithProvider(ModelProviderConfig source, string providerName) =>
        new()
        {
            Provider       = providerName,
            Model          = source.Model,
            Endpoint       = source.Endpoint,
            ApiKey         = source.ApiKey,
            DeploymentName = source.DeploymentName,
            AuthMode       = source.AuthMode,
            // Fallbacks intentionally omitted — no recursive fallback chaining
        };

    /// <summary>Builds the ordered list of clients to try: primary first, then each fallback.</summary>
    private List<IModelClient> BuildClientChain(ModelProviderConfig cfg)
    {
        var chain = new List<IModelClient> { GetOrCreate() };
        foreach (var fallbackName in cfg.Fallbacks)
            chain.Add(CreateClient(CloneWithProvider(cfg, fallbackName), isPrimary: false));
        return chain;
    }

    private IModelClient CreateClient(ModelProviderConfig cfg, bool isPrimary = false) =>
        cfg.Provider.ToLowerInvariant() switch
        {
            "azure-openai"   => CreateAzureOpenAI(cfg),
            "foundry-local"  => CreateFoundryLocal(cfg),
            "github-copilot" => throw new ModelProviderUnavailableException("github-copilot",
                "GitHub Copilot must be used via the Agent Provider path. Configure a different default provider."),
            "foundry"   => CreateFoundry(cfg, isPrimary),
            "lm-studio" => CreateOllama(cfg, isPrimary), // LM Studio uses OpenAI-compatible API
            "ollama"    => CreateOllama(cfg, isPrimary),
            _           => CreateOllama(cfg, isPrimary)
        };

    private IModelClient CreateOllama(ModelProviderConfig cfg, bool isPrimary)
    {
        var endpoint = cfg.Endpoint ?? "http://localhost:11434";
        var http = new HttpClient { BaseAddress = new Uri(endpoint) };

        // Track ownership of the primary client's HTTP instance so it can be disposed on settings change.
        // Fallback-created clients are short-lived; their HTTP instances are intentionally leaked
        // (no pool exhaustion risk for occasional fallback requests).
        // NOTE: No lock here — the caller (GetOrCreate) already holds _lock when isPrimary is true.
        // System.Threading.Lock is non-reentrant; re-acquiring would deadlock.
        if (isPrimary)
            _ownedHttp = http;

        var opts = Options.Create(new OllamaOptions
        {
            Endpoint = endpoint,
            Model    = string.IsNullOrWhiteSpace(cfg.Model) ? "gemma4:e2b" : cfg.Model,
        });

        return new OllamaModelClient(
            http,
            opts,
            _loggerFactory.CreateLogger<OllamaModelClient>());
    }

    private IModelClient CreateAzureOpenAI(ModelProviderConfig cfg)
    {
        var opts = Options.Create(new AzureOpenAIOptions
        {
            Endpoint       = cfg.Endpoint ?? string.Empty,
            ApiKey         = cfg.ApiKey,
            DeploymentName = string.IsNullOrWhiteSpace(cfg.DeploymentName)
                ? (string.IsNullOrWhiteSpace(cfg.Model) ? "gpt-5-mini" : cfg.Model)
                : cfg.DeploymentName,
            AuthMode       = cfg.AuthMode ?? "api-key",
        });

        return new AzureOpenAIModelClient(
            opts,
            _loggerFactory.CreateLogger<AzureOpenAIModelClient>());
    }

    private IModelClient CreateFoundry(ModelProviderConfig cfg, bool isPrimary)
    {
        var http = _httpClientFactory.CreateClient();

        if (isPrimary)
            _ownedHttp = http;

        var opts = Options.Create(new FoundryOptions
        {
            Endpoint = cfg.Endpoint ?? string.Empty,
            ApiKey = cfg.ApiKey ?? string.Empty,
            Model = cfg.Model ?? string.Empty,
        });

        return new FoundryModelClient(
            http,
            opts,
            _loggerFactory.CreateLogger<FoundryModelClient>());
    }

    private IModelClient CreateFoundryLocal(ModelProviderConfig cfg)
    {
        var opts = Options.Create(new FoundryLocalOptions
        {
            Model = cfg.Model ?? "phi-4",
        });

        return new FoundryLocalModelClient(
            opts,
            _loggerFactory.CreateLogger<FoundryLocalModelClient>());
    }
}
