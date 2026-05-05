using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;
using OpenClawNet.Models.Foundry;
using OpenClawNet.Models.FoundryLocal;
using OpenClawNet.Models.GitHubCopilot;
using OpenClawNet.Models.Ollama;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Validates that the DI container is wired correctly — the "last line of defense"
/// against silent misconfiguration that causes runtime failures.
/// </summary>
public sealed class ServiceRegistrationTests : IAsyncLifetime
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;

    public ServiceRegistrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ServiceRegistrationTests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Model:Provider"] = "ollama",
                ["Model:Endpoint"] = "http://localhost:11434",
                ["Model:Model"] = "test-model",
                ["Model:ApiKey"] = "test-key",
                ["Model:DeploymentName"] = "gpt-5-mini",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);
        services.AddSingleton(mockEnv.Object);

        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        services.AddSingleton(mockHttpFactory.Object);

        // Model provider infrastructure
        services.AddSingleton<RuntimeModelSettings>();
        services.AddSingleton<IModelClient, RuntimeModelClient>();
        services.Configure<ModelOptions>(config.GetSection("Model"));

        // Provider-specific options
        services.Configure<AzureOpenAIOptions>(o =>
        {
            o.Endpoint = config["Model:Endpoint"] ?? string.Empty;
            o.ApiKey = config["Model:ApiKey"];
            o.DeploymentName = config["Model:DeploymentName"] ?? "gpt-5-mini";
            o.AuthMode = "api-key";
        });
        services.Configure<OllamaOptions>(o =>
        {
            o.Endpoint = config["Model:Endpoint"] ?? "http://localhost:11434";
            o.Model = config["Model:Model"] ?? "gemma4:e2b";
        });
        services.Configure<FoundryLocalOptions>(o => o.Model = "phi-4");
        services.Configure<FoundryOptions>(o =>
        {
            o.Endpoint = "https://test.foundry.example.com";
            o.ApiKey = "test-foundry-key";
        });
        services.Configure<GitHubCopilotOptions>(o =>
        {
            o.GitHubToken = "test-token";
            o.Model = "gpt-5-mini";
        });

        // Register concrete providers (without RuntimeAgentProvider to avoid circular IEnumerable resolution)
        services.AddSingleton<OllamaAgentProvider>();
        services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<OllamaAgentProvider>());
        services.AddSingleton<AzureOpenAIAgentProvider>();
        services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<AzureOpenAIAgentProvider>());
        services.AddSingleton<FoundryAgentProvider>();
        services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<FoundryAgentProvider>());
        services.AddSingleton<FoundryLocalAgentProvider>();
        services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<FoundryLocalAgentProvider>());
        services.AddSingleton<GitHubCopilotAgentProvider>();
        services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<GitHubCopilotAgentProvider>());

        // RuntimeAgentProvider registered as concrete only (not as IAgentProvider)
        // to avoid circular dependency with IEnumerable<IAgentProvider> in constructor.
        services.AddSingleton<RuntimeAgentProvider>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AllAgentProviders_AreResolvable()
    {
        var providers = _serviceProvider.GetServices<IAgentProvider>().ToList();

        providers.Should().HaveCount(5, "5 concrete providers should be registered");

        var names = providers.Select(p => p.ProviderName).ToList();
        names.Should().Contain("ollama");
        names.Should().Contain("azure-openai");
        names.Should().Contain("foundry");
        names.Should().Contain("foundry-local");
        names.Should().Contain("github-copilot");
    }

    [Fact]
    public void RuntimeAgentProvider_CanRouteToOllama()
    {
        var router = _serviceProvider.GetRequiredService<RuntimeAgentProvider>();
        router.ProviderName.Should().Be("ollama");
    }

    [Fact]
    public void RuntimeModelSettings_IsSingleton()
    {
        var instance1 = _serviceProvider.GetRequiredService<RuntimeModelSettings>();
        var instance2 = _serviceProvider.GetRequiredService<RuntimeModelSettings>();
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void RuntimeModelClient_IsSingleton()
    {
        var instance1 = _serviceProvider.GetRequiredService<IModelClient>();
        var instance2 = _serviceProvider.GetRequiredService<IModelClient>();
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void ModelOptions_BoundFromConfiguration()
    {
        var options = _serviceProvider.GetRequiredService<IOptions<ModelOptions>>();
        options.Value.Should().NotBeNull();
        options.Value.Provider.Should().Be("ollama");
        options.Value.Model.Should().Be("test-model");
    }
}
