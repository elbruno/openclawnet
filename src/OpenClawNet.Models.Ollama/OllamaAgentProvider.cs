using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Models.Ollama;

/// <summary>
/// MAF-compatible agent provider backed by a local Ollama instance.
/// Uses <see cref="OllamaApiClient"/> which natively implements <see cref="IChatClient"/>.
/// </summary>
public sealed class OllamaAgentProvider : IAgentProvider
{
    private readonly IOptions<OllamaOptions> _options;
    private readonly ILogger<OllamaAgentProvider> _logger;

    public OllamaAgentProvider(IOptions<OllamaOptions> options, ILogger<OllamaAgentProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ProviderName => "ollama";

    public IChatClient CreateChatClient(AgentProfile profile)
    {
        // Profile endpoint overrides DI options (supports per-definition endpoints)
        var endpoint = profile.Endpoint ?? _options.Value.Endpoint ?? "http://localhost:11434";
        // Profile model takes priority, then DI-configured default, then built-in fallback.
        // The fallback ensures test profiles (which carry no model) still produce a valid client.
        var model = profile.Model ?? _options.Value.Model ?? "gemma4:e2b";

        _logger.LogDebug("Creating Ollama IChatClient: endpoint={Endpoint}, model={Model}", endpoint, model);

        var innerClient = new OllamaApiClient(new Uri(endpoint), model);
        return new ChatClientBuilder(innerClient)
            .UseOpenTelemetry(sourceName: "OpenClawNet.Ollama")
            .Build();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var endpoint = _options.Value.Endpoint ?? "http://localhost:11434";
            using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
            var response = await http.GetAsync("/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
