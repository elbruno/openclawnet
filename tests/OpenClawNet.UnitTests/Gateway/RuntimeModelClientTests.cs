using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Ollama;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Tests for <see cref="RuntimeModelClient"/> — the delegating client that creates
/// and caches provider-specific <see cref="IModelClient"/> instances based on settings.
/// </summary>
public sealed class RuntimeModelClientTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    public RuntimeModelClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RuntimeModelClientTests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        _httpClientFactory = mockFactory.Object;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── GetOrCreate / Provider Routing ────────────────────────────────────────

    [Fact]
    public void GetOrCreate_ReturnsOllamaClient_WhenProviderIsOllama()
    {
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "ollama",
            ["Model:Endpoint"] = "http://localhost:11434"
        });

        using var client = new RuntimeModelClient(settings, _httpClientFactory, _loggerFactory);

        // ProviderName delegates to the inner client
        client.ProviderName.Should().Be("ollama");
    }

    [Fact]
    public void GetOrCreate_ReturnsAzureClient_WhenProviderIsAzureOpenAI()
    {
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "azure-openai",
            ["Model:Endpoint"] = "https://my-resource.openai.azure.com/",
            ["Model:ApiKey"] = "test-key",
            ["Model:DeploymentName"] = "gpt-5-mini"
        });

        using var client = new RuntimeModelClient(settings, _httpClientFactory, _loggerFactory);

        client.ProviderName.Should().Be("azure-openai");
    }

    [Fact]
    public void GetOrCreate_DefaultsToOllama_ForUnknownProvider()
    {
        // Unknown providers fall through the switch to the default Ollama branch
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "unknown-provider",
            ["Model:Endpoint"] = "http://localhost:11434"
        });

        using var client = new RuntimeModelClient(settings, _httpClientFactory, _loggerFactory);

        // Unknown provider defaults to Ollama in the CreateClient switch
        client.ProviderName.Should().Be("ollama");
    }

    [Fact]
    public void GetOrCreate_CachesClient()
    {
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "ollama",
            ["Model:Endpoint"] = "http://localhost:11434"
        });

        using var client = new RuntimeModelClient(settings, _httpClientFactory, _loggerFactory);

        // Access ProviderName twice — should use the cached client (same CacheKey)
        var name1 = client.ProviderName;
        var name2 = client.ProviderName;

        name1.Should().Be(name2);
        name1.Should().Be("ollama");
    }

    [Fact]
    public void GetOrCreate_RecreatesClient_WhenSettingsChange()
    {
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "ollama",
            ["Model:Endpoint"] = "http://localhost:11434"
        });

        using var client = new RuntimeModelClient(settings, _httpClientFactory, _loggerFactory);

        // Initial access — creates Ollama client
        client.ProviderName.Should().Be("ollama");

        // Change settings to Azure OpenAI
        settings.Update(new ModelProviderConfig
        {
            Provider = "azure-openai",
            Endpoint = "https://my-resource.openai.azure.com/",
            ApiKey = "test-key",
            DeploymentName = "gpt-5-mini"
        });

        // Next access should recreate with new provider
        client.ProviderName.Should().Be("azure-openai");
    }

    [Fact]
    public void CreateAzureOpenAI_ThrowsWhenApiKeyMissing()
    {
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "azure-openai",
            ["Model:Endpoint"] = "https://my-resource.openai.azure.com/"
            // No ApiKey!
        });

        using var client = new RuntimeModelClient(settings, _httpClientFactory, _loggerFactory);

        // Act: accessing ProviderName triggers GetOrCreate → CreateAzureOpenAI
        var act = () => client.ProviderName;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*API key*");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private RuntimeModelSettings CreateSettings(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);
        return new RuntimeModelSettings(config, mockEnv.Object, NullLogger<RuntimeModelSettings>.Instance);
    }
}
