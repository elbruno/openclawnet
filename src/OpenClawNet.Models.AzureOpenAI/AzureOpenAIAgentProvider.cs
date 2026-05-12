using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Models.AzureOpenAI;

/// <summary>
/// MAF-compatible agent provider backed by Azure OpenAI Service.
/// Uses the <c>Azure.AI.OpenAI</c> SDK with <c>Microsoft.Extensions.AI.OpenAI</c>
/// to obtain an <see cref="IChatClient"/>.
/// Resolves vault:// references at runtime for secure credential management.
/// </summary>
public sealed class AzureOpenAIAgentProvider : IAgentProvider
{
    private readonly IOptions<AzureOpenAIOptions> _options;
    private readonly RuntimeVaultResolver _vaultResolver;
    private readonly ILogger<AzureOpenAIAgentProvider> _logger;

    public AzureOpenAIAgentProvider(
        IOptions<AzureOpenAIOptions> options,
        RuntimeVaultResolver vaultResolver,
        ILogger<AzureOpenAIAgentProvider> logger)
    {
        _options = options;
        _vaultResolver = vaultResolver;
        _logger = logger;
    }

    public string ProviderName => "azure-openai";

    public IChatClient CreateChatClient(AgentProfile profile)
    {
        var opts = _options.Value;
        
        // Resolve vault references in profile fields synchronously (CreateChatClient is sync)
        var vaultFields = _vaultResolver.ResolveProfileFieldsAsync(
            profile.Endpoint,
            profile.ApiKey,
            profile.DeploymentName,
            profile.Name,
            CancellationToken.None).GetAwaiter().GetResult();

        // Profile fields override DI options (supports per-definition endpoints)
        var endpoint = vaultFields.GetValueOrDefault("Endpoint") ?? profile.Endpoint ?? opts.Endpoint;
        var apiKey = vaultFields.GetValueOrDefault("ApiKey") ?? profile.ApiKey ?? opts.ApiKey;
        var authMode = profile.AuthMode ?? opts.AuthMode;
        var deployment = vaultFields.GetValueOrDefault("DeploymentName") ?? profile.DeploymentName ?? opts.DeploymentName ?? "gpt-5-mini";

        if (string.IsNullOrEmpty(endpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint not configured.");

        AzureOpenAIClient azureClient;
        if (authMode?.Equals("integrated", StringComparison.OrdinalIgnoreCase) == true)
            azureClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
        else if (!string.IsNullOrEmpty(apiKey))
            azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        else
            throw new InvalidOperationException("Azure OpenAI: no API key configured and not using integrated auth.");

        _logger.LogDebug("Creating Azure OpenAI IChatClient.");

        var innerClient = azureClient.GetChatClient(deployment).AsIChatClient();
        return new ChatClientBuilder(innerClient)
            .UseOpenTelemetry(sourceName: "OpenClawNet.AzureOpenAI")
            .Build();
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var opts = _options.Value;
        return Task.FromResult(!string.IsNullOrEmpty(opts.Endpoint) &&
            (!string.IsNullOrEmpty(opts.ApiKey) ||
             opts.AuthMode?.Equals("integrated", StringComparison.OrdinalIgnoreCase) == true));
    }
}
