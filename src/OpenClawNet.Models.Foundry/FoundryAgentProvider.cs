using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Models.Foundry;

/// <summary>
/// MAF-compatible agent provider for Microsoft Foundry (cloud).
/// Bridges the existing <see cref="FoundryModelClient"/> HTTP implementation
/// into an <see cref="IChatClient"/> via <see cref="FoundryModelChatClientBridge"/>.
/// </summary>
public sealed class FoundryAgentProvider : IAgentProvider
{
    private readonly IOptions<FoundryOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<FoundryAgentProvider> _logger;

    public FoundryAgentProvider(
        IOptions<FoundryOptions> options,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<FoundryAgentProvider> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public string ProviderName => "foundry";

    public IChatClient CreateChatClient(AgentProfile profile)
    {
        var opts = _options.Value;
        var endpoint = profile.Endpoint ?? opts.Endpoint;
        var apiKey = profile.ApiKey ?? opts.ApiKey;

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Foundry is not configured. Set Endpoint and ApiKey.");

        _logger.LogDebug("Creating Foundry IChatClient: endpoint={Endpoint}, model={Model}", endpoint, opts.Model);

        var http = _httpClientFactory.CreateClient();
        var perCallOpts = Options.Create(new FoundryOptions
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = profile.Model ?? opts.Model,
            Temperature = profile.Temperature ?? opts.Temperature,
            MaxTokens = profile.MaxTokens ?? opts.MaxTokens,
        });

        var client = new FoundryModelClient(http, perCallOpts, _loggerFactory.CreateLogger<FoundryModelClient>());
        var innerClient = new FoundryModelChatClientBridge(client);
        return new ChatClientBuilder(innerClient)
            .UseOpenTelemetry(sourceName: "OpenClawNet.Foundry")
            .Build();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var opts = _options.Value;
        if (string.IsNullOrEmpty(opts.Endpoint) || string.IsNullOrEmpty(opts.ApiKey))
            return false;

        try
        {
            using var http = _httpClientFactory.CreateClient();
            // Trailing slash is required — relative "models" resolves against full path.
            http.BaseAddress = new Uri(opts.Endpoint.TrimEnd('/') + "/");
            http.DefaultRequestHeaders.Add("api-key", opts.ApiKey);
            var response = await http.GetAsync("models", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
