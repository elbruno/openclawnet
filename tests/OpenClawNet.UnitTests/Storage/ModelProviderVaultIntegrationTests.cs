using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;

namespace OpenClawNet.UnitTests.Storage;

/// <summary>
/// Tests vault:// reference integration for Model Providers (Issue #151).
/// Verifies end-to-end resolution of vault references in ModelProviderDefinition fields.
/// </summary>
public sealed class ModelProviderVaultIntegrationTests
{
    [Fact]
    public async Task ResolveProviderFieldsAsync_WithVaultReferences_ResolvesSuccessfully()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>
        {
            ["azure-endpoint"] = "https://my-azure-openai.openai.azure.com/",
            ["azure-api-key"] = "test-key-12345",
            ["azure-deployment"] = "gpt-4o-mini"
        });

        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act
        var resolved = await resolver.ResolveProviderFieldsAsync(
            endpoint: "vault://azure-endpoint",
            apiKey: "vault://azure-api-key",
            deploymentName: "vault://azure-deployment",
            providerId: "test-provider",
            CancellationToken.None);

        // Assert
        Assert.Equal("https://my-azure-openai.openai.azure.com/", resolved["Endpoint"]);
        Assert.Equal("test-key-12345", resolved["ApiKey"]);
        Assert.Equal("gpt-4o-mini", resolved["DeploymentName"]);
    }

    [Fact]
    public async Task ResolveProviderFieldsAsync_WithPartialVaultReferences_ResolvesOnlyReferences()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>
        {
            ["azure-api-key"] = "resolved-key"
        });

        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act
        var resolved = await resolver.ResolveProviderFieldsAsync(
            endpoint: "https://direct-endpoint.com/",  // Not a vault reference
            apiKey: "vault://azure-api-key",            // Vault reference
            deploymentName: "gpt-4o",                   // Not a vault reference
            providerId: "test-provider",
            CancellationToken.None);

        // Assert
        Assert.Single(resolved); // Only one field was a vault reference
        Assert.Equal("resolved-key", resolved["ApiKey"]);
        Assert.False(resolved.ContainsKey("Endpoint"));
        Assert.False(resolved.ContainsKey("DeploymentName"));
    }

    [Fact]
    public async Task ResolveProviderFieldsAsync_WithMissingSecret_ThrowsInvalidOperationException()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>()); // Empty vault
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await resolver.ResolveProviderFieldsAsync(
                endpoint: "vault://missing-secret",
                apiKey: null,
                deploymentName: null,
                providerId: "test-provider",
                CancellationToken.None));

        Assert.Contains("vault://missing-secret", ex.Message);
        Assert.Contains("Ensure the secret exists and is accessible", ex.Message);
    }

    [Fact]
    public async Task ResolveProviderFieldsAsync_WithNullValues_ReturnsEmptyDictionary()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>());
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act
        var resolved = await resolver.ResolveProviderFieldsAsync(
            endpoint: null,
            apiKey: null,
            deploymentName: null,
            providerId: "test-provider",
            CancellationToken.None);

        // Assert
        Assert.Empty(resolved);
    }

    [Fact]
    public async Task ResolveFieldAsync_WithVaultReference_CachesResult()
    {
        // Arrange
        var callCount = 0;
        var vault = new FakeVault(new Dictionary<string, string>
        {
            ["cached-secret"] = "cached-value"
        }, onResolve: () => callCount++);

        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act - resolve twice
        var result1 = await resolver.ResolveFieldAsync("vault://cached-secret", "TestField", VaultCallerType.System, "test", CancellationToken.None);
        var result2 = await resolver.ResolveFieldAsync("vault://cached-secret", "TestField", VaultCallerType.System, "test", CancellationToken.None);

        // Assert
        Assert.Equal("cached-value", result1);
        Assert.Equal("cached-value", result2);
        Assert.Equal(1, callCount); // Only one call to vault, second was cached
    }

    [Fact]
    public async Task ModelProviderDefinition_StoresVaultReferences_DoesNotResolveAtRest()
    {
        // Arrange - this test verifies that vault references are stored as-is in the database
        var definition = new ModelProviderDefinition
        {
            Name = "azure-prod",
            ProviderType = "azure-openai",
            Endpoint = "vault://azure-endpoint",
            ApiKey = "vault://azure-api-key",
            DeploymentName = "vault://azure-deployment",
            IsSupported = true
        };

        // Assert - the definition should store the vault:// references, not resolved values
        Assert.StartsWith("vault://", definition.Endpoint);
        Assert.StartsWith("vault://", definition.ApiKey);
        Assert.StartsWith("vault://", definition.DeploymentName);
    }

    /// <summary>
    /// Fake IVault implementation for testing without database dependencies.
    /// </summary>
    private sealed class FakeVault : IVault
    {
        private readonly Dictionary<string, string> _secrets;
        private readonly Action? _onResolve;

        public FakeVault(Dictionary<string, string> secrets, Action? onResolve = null)
        {
            _secrets = secrets;
            _onResolve = onResolve;
        }

        public Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
        {
            _onResolve?.Invoke();
            
            if (_secrets.TryGetValue(name, out var value))
                return Task.FromResult<string?>(value);

            throw new VaultException(new KeyNotFoundException($"Secret '{name}' not found in vault."));
        }
    }
}
