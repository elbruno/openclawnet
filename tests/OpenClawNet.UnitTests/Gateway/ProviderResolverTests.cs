using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Gateway;

public sealed class ProviderResolverTests
{
    private readonly Mock<IModelProviderDefinitionStore> _mockStore = new();
    private readonly Mock<ILogger<ProviderResolver>> _logger = new();

    private ProviderResolver CreateResolver(string provider = "ollama", string? model = "fallback-model", string? endpoint = "http://fallback:11434")
    {
        var runtimeSettings = CreateRuntimeSettings(provider, model, endpoint);
        var vaultResolver = CreateVaultResolver();
        return new ProviderResolver(_mockStore.Object, runtimeSettings, vaultResolver, _logger.Object);
    }
    
    private static RuntimeVaultResolver CreateVaultResolver()
    {
        var fakeVault = new FakeVault();
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        return new RuntimeVaultResolver(fakeVault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);
    }

    private static RuntimeModelSettings CreateRuntimeSettings(
        string provider = "ollama", string? model = "fallback-model", string? endpoint = "http://fallback:11434")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Model:Provider"] = provider,
                ["Model:Model"] = model,
                ["Model:Endpoint"] = endpoint
            })
            .Build();
        var env = Mock.Of<IHostEnvironment>(e => e.ContentRootPath == Path.GetTempPath());
        var logger = Mock.Of<ILogger<RuntimeModelSettings>>();
        return new RuntimeModelSettings(config, env, logger);
    }

    [Fact]
    public async Task ResolveAsync_WithDefinitionName_ReturnsDefinitionConfig()
    {
        var definition = new ModelProviderDefinition
        {
            Name = "ollama-gemma",
            ProviderType = "ollama",
            Model = "gemma3:4b",
            Endpoint = "http://localhost:11434",
            IsSupported = true
        };
        _mockStore.Setup(s => s.GetAsync("ollama-gemma", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync("ollama-gemma");

        result.ProviderType.Should().Be("ollama");
        result.Model.Should().Be("gemma3:4b");
        result.Endpoint.Should().Be("http://localhost:11434");
        result.DefinitionName.Should().Be("ollama-gemma");
    }

    [Fact]
    public async Task ResolveAsync_WithTypeName_ReturnsFirstEnabledDefinition()
    {
        _mockStore.Setup(s => s.GetAsync("ollama", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelProviderDefinition?)null);
        _mockStore.Setup(s => s.ListByTypeAsync("ollama", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ModelProviderDefinition>
            {
                new() { Name = "ollama-disabled", ProviderType = "ollama", Model = "disabled-model", IsSupported = false },
                new() { Name = "ollama-enabled", ProviderType = "ollama", Model = "enabled-model", IsSupported = true }
            });

        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync("ollama");

        result.DefinitionName.Should().Be("ollama-enabled");
        result.Model.Should().Be("enabled-model");
    }

    [Fact]
    public async Task ResolveAsync_WithTypeName_NoEnabledDef_ReturnsFirstDef()
    {
        _mockStore.Setup(s => s.GetAsync("ollama", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelProviderDefinition?)null);
        _mockStore.Setup(s => s.ListByTypeAsync("ollama", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ModelProviderDefinition>
            {
                new() { Name = "ollama-first", ProviderType = "ollama", Model = "first-model", IsSupported = false },
                new() { Name = "ollama-second", ProviderType = "ollama", Model = "second-model", IsSupported = false }
            });

        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync("ollama");

        result.DefinitionName.Should().Be("ollama-first");
        result.Model.Should().Be("first-model");
    }

    [Fact]
    public async Task ResolveAsync_WithUnknownRef_ThrowsModelProviderUnavailable()
    {
        // Wave 5 PR-D: when caller supplies a non-empty providerRef that resolves to
        // nothing, fail loudly rather than silently routing to global settings.
        _mockStore.Setup(s => s.GetAsync("unknown-provider", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelProviderDefinition?)null);
        _mockStore.Setup(s => s.ListByTypeAsync("unknown-provider", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ModelProviderDefinition>());

        var resolver = CreateResolver(provider: "ollama", model: "fallback-model", endpoint: "http://fallback:11434");

        var act = async () => await resolver.ResolveAsync("unknown-provider");
        var ex = await act.Should().ThrowAsync<OpenClawNet.Models.Abstractions.ModelProviderUnavailableException>();
        ex.Which.ProviderName.Should().Be("unknown-provider");
    }

    [Fact]
    public async Task ResolveAsync_WithNullRef_FallsBackToRuntimeSettings()
    {
        var resolver = CreateResolver(provider: "azure-openai", model: "gpt-4o");
        var result = await resolver.ResolveAsync(null);

        result.ProviderType.Should().Be("azure-openai");
        // Azure OpenAI uses DeploymentName, not Model — RuntimeModelSettings may null out Model for non-Ollama
        result.DefinitionName.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_DefinitionFields_MappedCorrectly()
    {
        var definition = new ModelProviderDefinition
        {
            Name = "azure-prod",
            ProviderType = "azure-openai",
            Model = "gpt-4o",
            Endpoint = "https://myresource.openai.azure.com",
            ApiKey = "test-key-123",
            DeploymentName = "gpt-4o-deployment",
            AuthMode = "api-key",
            IsSupported = true
        };
        _mockStore.Setup(s => s.GetAsync("azure-prod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync("azure-prod");

        result.ProviderType.Should().Be("azure-openai");
        result.Model.Should().Be("gpt-4o");
        result.Endpoint.Should().Be("https://myresource.openai.azure.com");
        result.ApiKey.Should().Be("test-key-123");
        result.DeploymentName.Should().Be("gpt-4o-deployment");
        result.AuthMode.Should().Be("api-key");
        result.DefinitionName.Should().Be("azure-prod");
    }
    
    private sealed class FakeVault : IVault
    {
        public Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
