<<<<<<< HEAD
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;
=======
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
>>>>>>> f71dd8ad (chore(squad): sync infrastructure, skills, and session state)

namespace OpenClawNet.UnitTests.Storage;

/// <summary>
<<<<<<< HEAD
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
=======
/// Tests for vault:// secret reference integration in Model Provider definitions.
/// Validates issue #151 acceptance criteria: vault references resolve at runtime,
/// missing secrets fail safely, and no plaintext persists in storage.
/// </summary>
[Trait("Category", "Vault")]
[Trait("Issue", "151")]
public class ModelProviderVaultIntegrationTests
{
    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOpenClawStorage("Data Source=:memory:");
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        SchemaMigrator.MigrateAsync(db).GetAwaiter().GetResult();
        return sp;
    }

    [Fact]
    public async Task ModelProvider_WithVaultApiKey_ResolvesAtRuntime()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var resolver = scope.ServiceProvider.GetRequiredService<VaultConfigurationResolver>();
        var providerStore = scope.ServiceProvider.GetRequiredService<IModelProviderDefinitionStore>();

        await store.SetAsync("AzureOpenAI/ApiKey", "secret-key-value");

        var provider = new ModelProviderDefinition
        {
            Name = "test-azure",
            ProviderType = "azure-openai",
            ApiKey = "vault://AzureOpenAI/ApiKey",
            Model = "gpt-4o"
        };

        // Act: Persist provider with vault reference
        await providerStore.SaveAsync(provider);

        // Reload from store to verify persistence
        var reloaded = await providerStore.GetAsync("test-azure");
        Assert.NotNull(reloaded);

        // Resolve vault reference
        var resolvedKey = await resolver.ResolveSecretAsync("AzureOpenAI/ApiKey", vault);

        // Assert: Stored value is vault reference (not plaintext)
        Assert.Equal("vault://AzureOpenAI/ApiKey", reloaded.ApiKey);

        // Assert: Resolved value is plaintext
        Assert.Equal("secret-key-value", resolvedKey);

        // Assert: Audit row created
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var audit = Assert.Single(await db.SecretAccessAudit.Where(a => a.SecretName == "AzureOpenAI/ApiKey").ToListAsync());
        Assert.True(audit.Success);
        Assert.Equal("Configuration", audit.CallerType);
    }

    [Fact]
    public async Task ModelProvider_WithMissingVaultSecret_ThrowsVaultException()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var resolver = scope.ServiceProvider.GetRequiredService<VaultConfigurationResolver>();

        // Act & Assert: Attempt to resolve missing secret
        await Assert.ThrowsAsync<VaultException>(() => 
            resolver.ResolveSecretAsync("Missing/ApiKey", vault));

        // Assert: Audit row created with failure
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var audit = Assert.Single(await db.SecretAccessAudit.Where(a => a.SecretName == "Missing/ApiKey").ToListAsync());
        Assert.False(audit.Success);
    }

    [Fact]
    public async Task ModelProvider_WithMultipleVaultFields_AllResolveCorrectly()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var resolver = scope.ServiceProvider.GetRequiredService<VaultConfigurationResolver>();
        var providerStore = scope.ServiceProvider.GetRequiredService<IModelProviderDefinitionStore>();

        await store.SetAsync("Azure/Endpoint", "https://myopenai.openai.azure.com");
        await store.SetAsync("Azure/ApiKey", "key-123");
        await store.SetAsync("Azure/Deployment", "gpt-4o");

        var provider = new ModelProviderDefinition
        {
            Name = "multi-vault",
            ProviderType = "azure-openai",
            Endpoint = "vault://Azure/Endpoint",
            ApiKey = "vault://Azure/ApiKey",
            DeploymentName = "vault://Azure/Deployment"
        };

        // Act: Persist and resolve all three fields
        await providerStore.SaveAsync(provider);
        var reloaded = await providerStore.GetAsync("multi-vault");
        Assert.NotNull(reloaded);

        var resolvedEndpoint = await resolver.ResolveSecretAsync("Azure/Endpoint", vault);
        var resolvedApiKey = await resolver.ResolveSecretAsync("Azure/ApiKey", vault);
        var resolvedDeployment = await resolver.ResolveSecretAsync("Azure/Deployment", vault);

        // Assert: All fields stored as vault references
        Assert.Equal("vault://Azure/Endpoint", reloaded.Endpoint);
        Assert.Equal("vault://Azure/ApiKey", reloaded.ApiKey);
        Assert.Equal("vault://Azure/Deployment", reloaded.DeploymentName);

        // Assert: All fields resolve to plaintext
        Assert.Equal("https://myopenai.openai.azure.com", resolvedEndpoint);
        Assert.Equal("key-123", resolvedApiKey);
        Assert.Equal("gpt-4o", resolvedDeployment);

        // Assert: 3 audit rows created
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var auditCount = await db.SecretAccessAudit.CountAsync(a => 
            a.SecretName.StartsWith("Azure/") && a.Success);
        Assert.Equal(3, auditCount);
    }

    [Fact]
    public async Task ModelProvider_PlaintextApiKey_DoesNotLeakToVaultReference()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var providerStore = scope.ServiceProvider.GetRequiredService<IModelProviderDefinitionStore>();

        var provider = new ModelProviderDefinition
        {
            Name = "plaintext-provider",
            ProviderType = "azure-openai",
            ApiKey = "plaintext-key-123",
            Model = "gpt-4o"
        };

        // Act: Persist provider with plaintext key (no vault://)
        await providerStore.SaveAsync(provider);
        var reloaded = await providerStore.GetAsync("plaintext-provider");
        Assert.NotNull(reloaded);

        // Assert: Stored value unchanged (no vault processing)
        Assert.Equal("plaintext-key-123", reloaded.ApiKey);

        // Assert: No vault audit rows created (vault not accessed)
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var auditCount = await db.SecretAccessAudit.CountAsync();
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task ModelProvider_VaultReference_PersistedAsReference_NotResolvedPlaintext()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        var providerStore = scope.ServiceProvider.GetRequiredService<IModelProviderDefinitionStore>();

        await store.SetAsync("Provider/Secret", "resolved-plaintext");

        var provider = new ModelProviderDefinition
        {
            Name = "ref-check",
            ProviderType = "azure-openai",
            ApiKey = "vault://Provider/Secret"
        };

        // Act: Persist provider
        await providerStore.SaveAsync(provider);

        // Assert: Verify stored reference is literal (not resolved)
        var reloaded = await providerStore.GetAsync("ref-check");
        Assert.NotNull(reloaded);
        Assert.Equal("vault://Provider/Secret", reloaded.ApiKey); // Literal vault reference, not plaintext
>>>>>>> f71dd8ad (chore(squad): sync infrastructure, skills, and session state)
    }
}
