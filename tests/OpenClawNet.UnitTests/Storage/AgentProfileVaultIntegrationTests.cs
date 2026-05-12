using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.UnitTests.Storage;

/// <summary>
/// Tests vault:// reference integration for Agent Profiles (Issue #151).
/// Verifies end-to-end resolution of vault references in AgentProfile fields.
/// </summary>
public sealed class AgentProfileVaultIntegrationTests
{
    [Fact]
    public async Task ResolveProfileFieldsAsync_WithVaultReferences_ResolvesSuccessfully()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>
        {
            ["profile-endpoint"] = "https://profile-azure-openai.openai.azure.com/",
            ["profile-api-key"] = "profile-key-67890",
            ["profile-deployment"] = "gpt-5-mini"
        });

        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act
        var resolved = await resolver.ResolveProfileFieldsAsync(
            endpoint: "vault://profile-endpoint",
            apiKey: "vault://profile-api-key",
            deploymentName: "vault://profile-deployment",
            profileName: "test-profile",
            CancellationToken.None);

        // Assert
        Assert.Equal("https://profile-azure-openai.openai.azure.com/", resolved["Endpoint"]);
        Assert.Equal("profile-key-67890", resolved["ApiKey"]);
        Assert.Equal("gpt-5-mini", resolved["DeploymentName"]);
    }

    [Fact]
    public async Task ResolveProfileFieldsAsync_WithMixedReferencesAndValues_ResolvesCorrectly()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>
        {
            ["secure-key"] = "resolved-secure-key"
        });

        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act
        var resolved = await resolver.ResolveProfileFieldsAsync(
            endpoint: "https://direct-endpoint.com/",  // Direct value
            apiKey: "vault://secure-key",               // Vault reference
            deploymentName: null,                       // Null value
            profileName: "mixed-profile",
            CancellationToken.None);

        // Assert
        Assert.Single(resolved); // Only ApiKey was a vault reference
        Assert.Equal("resolved-secure-key", resolved["ApiKey"]);
    }

    [Fact]
    public async Task ResolveProfileFieldsAsync_WithDeletedSecret_ThrowsInvalidOperationException()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>()); // Empty vault (secret deleted)
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await resolver.ResolveProfileFieldsAsync(
                endpoint: "vault://deleted-secret",
                apiKey: null,
                deploymentName: null,
                profileName: "failing-profile",
                CancellationToken.None));

        Assert.Contains("vault://deleted-secret", ex.Message);
        Assert.Contains("Ensure the secret exists and is accessible", ex.Message);
    }

    [Fact]
    public async Task AgentProfile_StoresVaultReferences_DoesNotResolveAtRest()
    {
        // Arrange - this test verifies that vault references are stored as-is in the profile entity
        var profile = new AgentProfile
        {
            Name = "secure-profile",
            Provider = "azure-openai",
            Endpoint = "vault://secure-endpoint",
            ApiKey = "vault://secure-api-key",
            DeploymentName = "vault://secure-deployment",
            Instructions = "You are a helpful assistant."
        };

        // Assert - the profile should store the vault:// references, not resolved values
        Assert.StartsWith("vault://", profile.Endpoint);
        Assert.StartsWith("vault://", profile.ApiKey);
        Assert.StartsWith("vault://", profile.DeploymentName);
    }

    [Fact]
    public async Task ResolveProfileFieldsAsync_WithEmptyStrings_ReturnsEmptyDictionary()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>());
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act
        var resolved = await resolver.ResolveProfileFieldsAsync(
            endpoint: "",
            apiKey: "",
            deploymentName: "",
            profileName: "empty-profile",
            CancellationToken.None);

        // Assert
        Assert.Empty(resolved);
    }

    [Fact]
    public async Task ResolveProfileFieldsAsync_WithWhitespaceOnlyValues_ReturnsEmptyDictionary()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>());
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act
        var resolved = await resolver.ResolveProfileFieldsAsync(
            endpoint: "   ",
            apiKey: "\t",
            deploymentName: "\n",
            profileName: "whitespace-profile",
            CancellationToken.None);

        // Assert
        Assert.Empty(resolved);
    }

    [Fact]
    public async Task ResolveProfileFieldsAsync_WithCaseInsensitiveVaultPrefix_ResolvesSuccessfully()
    {
        // Arrange
        var vault = new FakeVault(new Dictionary<string, string>
        {
            ["case-test"] = "case-resolved-value"
        });

        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var resolver = new RuntimeVaultResolver(vault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        // Act - using different case variations
        var resolved1 = await resolver.ResolveProfileFieldsAsync(
            endpoint: "vault://case-test",
            apiKey: null,
            deploymentName: null,
            profileName: "case-profile-1",
            CancellationToken.None);

        var resolved2 = await resolver.ResolveProfileFieldsAsync(
            endpoint: "VAULT://case-test",
            apiKey: null,
            deploymentName: null,
            profileName: "case-profile-2",
            CancellationToken.None);

        var resolved3 = await resolver.ResolveProfileFieldsAsync(
            endpoint: "Vault://case-test",
            apiKey: null,
            deploymentName: null,
            profileName: "case-profile-3",
            CancellationToken.None);

        // Assert - all should resolve successfully (vault:// prefix is case-insensitive)
        Assert.Equal("case-resolved-value", resolved1["Endpoint"]);
        Assert.Equal("case-resolved-value", resolved2["Endpoint"]);
        Assert.Equal("case-resolved-value", resolved3["Endpoint"]);
    }

    /// <summary>
    /// Fake IVault implementation for testing without database dependencies.
    /// </summary>
    private sealed class FakeVault : IVault
    {
        private readonly Dictionary<string, string> _secrets;

        public FakeVault(Dictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
        {
            if (_secrets.TryGetValue(name, out var value))
                return Task.FromResult<string?>(value);

            throw new VaultException(new KeyNotFoundException($"Secret '{name}' not found in vault."));
        }
    }
}
