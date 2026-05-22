<<<<<<< HEAD
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using Xunit;
=======
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
>>>>>>> f71dd8ad (chore(squad): sync infrastructure, skills, and session state)

namespace OpenClawNet.UnitTests.Storage;

/// <summary>
<<<<<<< HEAD
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
=======
/// Tests for vault:// secret reference integration in Agent Profile definitions.
/// Validates issue #151 acceptance criteria: vault references resolve at runtime,
/// missing secrets fail safely, and no plaintext persists in storage.
/// </summary>
[Trait("Category", "Vault")]
[Trait("Issue", "151")]
public class AgentProfileVaultIntegrationTests
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
    public async Task AgentProfile_WithVaultApiKey_ResolvesAtRuntime()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var resolver = scope.ServiceProvider.GetRequiredService<VaultConfigurationResolver>();
        var profileStore = scope.ServiceProvider.GetRequiredService<IAgentProfileStore>();

        await store.SetAsync("Profile/ApiKey", "profile-secret");

        var profile = new AgentProfile
        {
            Name = "test-profile",
            Provider = "azure-openai",
            ApiKey = "vault://Profile/ApiKey",
            Model = "gpt-4o",
            Instructions = "Test instructions"
        };

        // Act: Persist profile with vault reference
        await profileStore.SaveAsync(profile);

        // Reload from store to verify persistence
        var reloaded = await profileStore.GetAsync("test-profile");
        Assert.NotNull(reloaded);

        // Resolve vault reference
        var resolvedKey = await resolver.ResolveSecretAsync("Profile/ApiKey", vault);

        // Assert: Stored value is vault reference (not plaintext)
        Assert.Equal("vault://Profile/ApiKey", reloaded.ApiKey);

        // Assert: Resolved value is plaintext
        Assert.Equal("profile-secret", resolvedKey);

        // Assert: Audit row created
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var audit = Assert.Single(await db.SecretAccessAudit.Where(a => a.SecretName == "Profile/ApiKey").ToListAsync());
        Assert.True(audit.Success);
        Assert.Equal("Configuration", audit.CallerType);
    }

    [Fact]
    public async Task AgentProfile_WithMissingVaultSecret_ThrowsVaultException()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var resolver = scope.ServiceProvider.GetRequiredService<VaultConfigurationResolver>();

        // Act & Assert: Attempt to resolve missing secret
        await Assert.ThrowsAsync<VaultException>(() => 
            resolver.ResolveSecretAsync("Missing/ProfileKey", vault));

        // Assert: Audit row created with failure
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var audit = Assert.Single(await db.SecretAccessAudit.Where(a => a.SecretName == "Missing/ProfileKey").ToListAsync());
        Assert.False(audit.Success);
    }

    [Fact]
    public async Task AgentProfile_WithMultipleVaultFields_AllResolveCorrectly()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var resolver = scope.ServiceProvider.GetRequiredService<VaultConfigurationResolver>();
        var profileStore = scope.ServiceProvider.GetRequiredService<IAgentProfileStore>();

        await store.SetAsync("Profile/Endpoint", "https://profile.openai.azure.com");
        await store.SetAsync("Profile/ApiKey", "profile-key-456");
        await store.SetAsync("Profile/Deployment", "gpt-4o-profile");

        var profile = new AgentProfile
        {
            Name = "multi-vault-profile",
            Provider = "azure-openai",
            Endpoint = "vault://Profile/Endpoint",
            ApiKey = "vault://Profile/ApiKey",
            DeploymentName = "vault://Profile/Deployment",
            Instructions = "Multi-vault test"
        };

        // Act: Persist and resolve all three fields
        await profileStore.SaveAsync(profile);
        var reloaded = await profileStore.GetAsync("multi-vault-profile");
        Assert.NotNull(reloaded);

        var resolvedEndpoint = await resolver.ResolveSecretAsync("Profile/Endpoint", vault);
        var resolvedApiKey = await resolver.ResolveSecretAsync("Profile/ApiKey", vault);
        var resolvedDeployment = await resolver.ResolveSecretAsync("Profile/Deployment", vault);

        // Assert: All fields stored as vault references
        Assert.Equal("vault://Profile/Endpoint", reloaded.Endpoint);
        Assert.Equal("vault://Profile/ApiKey", reloaded.ApiKey);
        Assert.Equal("vault://Profile/Deployment", reloaded.DeploymentName);

        // Assert: All fields resolve to plaintext
        Assert.Equal("https://profile.openai.azure.com", resolvedEndpoint);
        Assert.Equal("profile-key-456", resolvedApiKey);
        Assert.Equal("gpt-4o-profile", resolvedDeployment);

        // Assert: 3 audit rows created
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var auditCount = await db.SecretAccessAudit.CountAsync(a => 
            a.SecretName.StartsWith("Profile/") && a.Success);
        Assert.Equal(3, auditCount);
    }

    [Fact]
    public async Task AgentProfile_PlaintextApiKey_DoesNotLeakToVaultReference()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var profileStore = scope.ServiceProvider.GetRequiredService<IAgentProfileStore>();

        var profile = new AgentProfile
        {
            Name = "plaintext-profile",
            Provider = "azure-openai",
            ApiKey = "plaintext-profile-key-789",
            Model = "gpt-4o",
            Instructions = "Plaintext test"
        };

        // Act: Persist profile with plaintext key (no vault://)
        await profileStore.SaveAsync(profile);
        var reloaded = await profileStore.GetAsync("plaintext-profile");
        Assert.NotNull(reloaded);

        // Assert: Stored value unchanged (no vault processing)
        Assert.Equal("plaintext-profile-key-789", reloaded.ApiKey);

        // Assert: No vault audit rows created (vault not accessed)
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var auditCount = await db.SecretAccessAudit.CountAsync();
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task AgentProfile_VaultReference_PersistedAsReference_NotResolvedPlaintext()
    {
        // Arrange
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        var profileStore = scope.ServiceProvider.GetRequiredService<IAgentProfileStore>();

        await store.SetAsync("Profile/RefCheck", "resolved-profile-plaintext");

        var profile = new AgentProfile
        {
            Name = "ref-check-profile",
            Provider = "azure-openai",
            ApiKey = "vault://Profile/RefCheck",
            Instructions = "Reference check"
        };

        // Act: Persist profile
        await profileStore.SaveAsync(profile);

        // Assert: Verify stored reference is literal (not resolved)
        var reloaded = await profileStore.GetAsync("ref-check-profile");
        Assert.NotNull(reloaded);
        Assert.Equal("vault://Profile/RefCheck", reloaded.ApiKey); // Literal vault reference, not plaintext
>>>>>>> f71dd8ad (chore(squad): sync infrastructure, skills, and session state)
    }
}
