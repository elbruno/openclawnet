using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Models;

public class AzureOpenAIAgentProviderTests
{
    [Fact]
    public void ProviderName_ReturnsAzureOpenAI()
    {
        var provider = CreateProvider();

        provider.ProviderName.Should().Be("azure-openai");
    }

    [Fact]
    public void CreateChatClient_Throws_WhenEndpointNotConfigured()
    {
        var provider = CreateProvider(new AzureOpenAIOptions { Endpoint = "" });
        var profile = new AgentProfile { Name = "test" };

        var act = () => provider.CreateChatClient(profile);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*endpoint not configured*");
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenNoEndpoint()
    {
        var provider = CreateProvider(new AzureOpenAIOptions { Endpoint = "" });

        var result = await provider.IsAvailableAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue_WhenEndpointAndKeyConfigured()
    {
        var provider = CreateProvider(new AzureOpenAIOptions
        {
            Endpoint = "https://my-resource.openai.azure.com/",
            ApiKey = "test-key"
        });

        var result = await provider.IsAvailableAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue_WhenEndpointAndIntegratedAuth()
    {
        var provider = CreateProvider(new AzureOpenAIOptions
        {
            Endpoint = "https://my-resource.openai.azure.com/",
            AuthMode = "integrated"
        });

        var result = await provider.IsAvailableAsync();

        result.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AzureOpenAIAgentProvider CreateProvider(AzureOpenAIOptions? options = null)
    {
        var fakeVault = new FakeVault();
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var vaultResolver = new RuntimeVaultResolver(fakeVault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);
        
        return new AzureOpenAIAgentProvider(
            Options.Create(options ?? new AzureOpenAIOptions()),
            vaultResolver,
            NullLogger<AzureOpenAIAgentProvider>.Instance);
    }
    
    private sealed class FakeVault : IVault
    {
        public Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
