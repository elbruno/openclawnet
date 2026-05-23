using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Resolves an agent profile's provider reference to a concrete provider configuration.
/// Supports both ModelProviderDefinition names (e.g. "ollama-default") and
/// provider type names (e.g. "ollama") for backward compatibility.
/// Resolves vault:// references at runtime for secure credential management.
/// </summary>
public sealed class ProviderResolver
{
    private readonly IModelProviderDefinitionStore _definitionStore;
    private readonly RuntimeModelSettings _runtimeSettings;
    private readonly RuntimeVaultResolver _vaultResolver;
    private readonly ILogger<ProviderResolver> _logger;

    public ProviderResolver(
        IModelProviderDefinitionStore definitionStore,
        RuntimeModelSettings runtimeSettings,
        RuntimeVaultResolver vaultResolver,
        ILogger<ProviderResolver> logger)
    {
        _definitionStore = definitionStore;
        _runtimeSettings = runtimeSettings;
        _vaultResolver = vaultResolver;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a provider reference to a concrete config.
    /// Resolution order:
    /// 1. If providerRef matches a ModelProviderDefinition name → use definition
    /// 2. If providerRef matches a provider type AND a definition of that type exists → use first enabled definition of that type
    /// 3. Fall back to RuntimeModelSettings global config
    /// Vault references (vault://) are resolved at runtime.
    /// </summary>
    public async Task<ResolvedProviderConfig> ResolveAsync(string? providerRef, CancellationToken ct = default)
    {
        // 1. Try exact definition name match
        if (!string.IsNullOrEmpty(providerRef))
        {
            var definition = await _definitionStore.GetAsync(providerRef, ct);
            if (definition is not null)
            {
                _logger.LogInformation("Resolved provider '{Ref}' → definition '{Name}' (type={Type})",
                    providerRef, definition.Name, definition.ProviderType);
                return await FromDefinitionAsync(definition, ct);
            }

            // 2. Try as a provider type name — find first enabled definition of that type
            var byType = await _definitionStore.ListByTypeAsync(providerRef, ct);
            var enabled = byType.FirstOrDefault(d => d.IsSupported) ?? byType.FirstOrDefault();
            if (enabled is not null)
            {
                _logger.LogInformation("Resolved provider type '{Ref}' → definition '{Name}'",
                    providerRef, enabled.Name);
                return await FromDefinitionAsync(enabled, ct);
            }

            // Wave 5 PR-D (Vasquez): a non-empty providerRef that fails to resolve is
            // a configuration error — the caller asked for a specific provider and we
            // can't honour it. Silently falling back to global settings has historically
            // masked profile typos (gateway routes traffic to the wrong model with no
            // visible error). Empty/null providerRef still falls back silently below.
            _logger.LogError("Provider reference '{Ref}' not found as definition or type — failing fast", providerRef);
            throw new ModelProviderUnavailableException(
                providerRef,
                $"Provider reference '{providerRef}' could not be resolved to any registered ModelProviderDefinition or provider type.");
        }

        // 3. Fall back to RuntimeModelSettings
        var cfg = _runtimeSettings.Current;
        _logger.LogDebug("Using global RuntimeModelSettings: provider={Provider}, model={Model}", cfg.Provider, cfg.Model);
        return new ResolvedProviderConfig
        {
            ProviderType = cfg.Provider,
            Endpoint = cfg.Endpoint,
            Model = cfg.Model,
            ApiKey = cfg.ApiKey,
            DeploymentName = cfg.DeploymentName,
            AuthMode = cfg.AuthMode,
            DefinitionName = null
        };
    }

    private async Task<ResolvedProviderConfig> FromDefinitionAsync(ModelProviderDefinition def, CancellationToken ct)
    {
        // Resolve vault references in provider fields
        var vaultFields = await _vaultResolver.ResolveProviderFieldsAsync(
            def.Endpoint,
            def.ApiKey,
            def.DeploymentName,
            def.Name,
            ct);

        return new ResolvedProviderConfig
        {
            ProviderType = def.ProviderType,
            Endpoint = vaultFields.GetValueOrDefault("Endpoint") ?? def.Endpoint,
            Model = def.Model,
            ApiKey = vaultFields.GetValueOrDefault("ApiKey") ?? def.ApiKey,
            DeploymentName = vaultFields.GetValueOrDefault("DeploymentName") ?? def.DeploymentName,
            AuthMode = def.AuthMode,
            DefinitionName = def.Name
        };
    }
}
