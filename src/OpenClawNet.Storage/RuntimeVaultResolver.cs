using Microsoft.Extensions.Logging;

namespace OpenClawNet.Storage;

/// <summary>
/// Resolves vault:// references in runtime configuration fields (ModelProviderDefinition, AgentProfile)
/// at the point of use. Complements VaultConfigurationResolver (which handles IConfiguration),
/// this resolver handles stored entity fields.
/// </summary>
public sealed class RuntimeVaultResolver
{
    private readonly IVault _vault;
    private readonly VaultConfigurationResolver _configResolver;
    private readonly ILogger<RuntimeVaultResolver> _logger;

    public RuntimeVaultResolver(
        IVault vault,
        VaultConfigurationResolver configResolver,
        ILogger<RuntimeVaultResolver> logger)
    {
        _vault = vault;
        _configResolver = configResolver;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a single field value if it's a vault reference.
    /// Returns the resolved secret value, or the original value if not a reference.
    /// </summary>
    public async Task<string?> ResolveFieldAsync(
        string? fieldValue,
        string fieldName,
        VaultCallerType callerType,
        string callerId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return fieldValue;

        if (!VaultConfigurationResolver.TryParseVaultReference(fieldValue, out var secretName))
            return fieldValue;

        try
        {
            _logger.LogDebug("Resolving vault reference for {FieldName} (caller={CallerId})",
                fieldName, callerId);

            // Use the configuration resolver's cache and version tracking
            var resolvedValue = await _configResolver.ResolveSecretAsync(secretName, _vault, ct).ConfigureAwait(false);

            if (resolvedValue is null)
            {
                _logger.LogWarning("Vault secret resolved to null for field {FieldName} (caller={CallerId})",
                    fieldName, callerId);
            }

            return resolvedValue;
        }
        catch (VaultException ex)
        {
            _logger.LogError(ex, "Failed to resolve vault reference for field {FieldName} (caller={CallerId})",
                fieldName, callerId);
            throw new InvalidOperationException(
                $"Failed to resolve vault reference for {fieldName}. The required secret was not found or could not be accessed.",
                ex);
        }
    }

    /// <summary>
    /// Resolves multiple fields from a model provider definition or agent profile.
    /// Returns a dictionary of resolved field values (only includes fields that were vault references).
    /// </summary>
    public async Task<Dictionary<string, string?>> ResolveProviderFieldsAsync(
        string? endpoint,
        string? apiKey,
        string? deploymentName,
        string providerId,
        CancellationToken ct = default)
    {
        var resolved = new Dictionary<string, string?>();

        if (VaultConfigurationResolver.TryParseVaultReference(endpoint, out _))
            resolved["Endpoint"] = await ResolveFieldAsync(endpoint, "Endpoint", VaultCallerType.System, $"ModelProvider:{providerId}", ct);

        if (VaultConfigurationResolver.TryParseVaultReference(apiKey, out _))
            resolved["ApiKey"] = await ResolveFieldAsync(apiKey, "ApiKey", VaultCallerType.System, $"ModelProvider:{providerId}", ct);

        if (VaultConfigurationResolver.TryParseVaultReference(deploymentName, out _))
            resolved["DeploymentName"] = await ResolveFieldAsync(deploymentName, "DeploymentName", VaultCallerType.System, $"ModelProvider:{providerId}", ct);

        return resolved;
    }

    /// <summary>
    /// Resolves fields for an agent profile.
    /// </summary>
    public async Task<Dictionary<string, string?>> ResolveProfileFieldsAsync(
        string? endpoint,
        string? apiKey,
        string? deploymentName,
        string profileName,
        CancellationToken ct = default)
    {
        var resolved = new Dictionary<string, string?>();

        if (VaultConfigurationResolver.TryParseVaultReference(endpoint, out _))
            resolved["Endpoint"] = await ResolveFieldAsync(endpoint, "Endpoint", VaultCallerType.System, $"AgentProfile:{profileName}", ct);

        if (VaultConfigurationResolver.TryParseVaultReference(apiKey, out _))
            resolved["ApiKey"] = await ResolveFieldAsync(apiKey, "ApiKey", VaultCallerType.System, $"AgentProfile:{profileName}", ct);

        if (VaultConfigurationResolver.TryParseVaultReference(deploymentName, out _))
            resolved["DeploymentName"] = await ResolveFieldAsync(deploymentName, "DeploymentName", VaultCallerType.System, $"AgentProfile:{profileName}", ct);

        return resolved;
    }
}
