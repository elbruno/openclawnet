using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Gateway.Services;

namespace OpenClawNet.UnitTests.Gateway;

public class RuntimeModelSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public RuntimeModelSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Load tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Load_BackfillsApiKeyFromConfig_WhenFileHasNullApiKey()
    {
        // Arrange: persisted file has ApiKey=null (security strip), config has the secret
        var fileConfig = new ModelProviderConfig
        {
            Provider = "azure-openai",
            Endpoint = "https://my-resource.openai.azure.com/",
            DeploymentName = "gpt-4o",
            ApiKey = null
        };
        WritePersistFile(fileConfig);

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "azure-openai",
            ["Model:ApiKey"] = "secret-key-from-user-secrets"
        });

        // Act
        var settings = CreateSettings(config);

        // Assert: ApiKey should be backfilled from IConfiguration
        settings.Current.ApiKey.Should().Be("secret-key-from-user-secrets");
        settings.Current.Provider.Should().Be("azure-openai");
    }

    [Fact]
    public void Load_IConfigOverridesFileForProviderEndpointDeploymentAndModel()
    {
        // Arrange: file says "azure-openai", config says "ollama" — IConfiguration wins for all key fields
        var fileConfig = new ModelProviderConfig
        {
            Provider = "azure-openai",
            Endpoint = "https://my-resource.openai.azure.com/",
            Model = "gpt-4o"
        };
        WritePersistFile(fileConfig);

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "ollama",
            ["Model:Endpoint"] = "http://localhost:11434",
            ["Model:Model"] = "llama3"
        });

        // Act
        var settings = CreateSettings(config);

        // Assert: IConfiguration wins for Provider, Endpoint, AND Model
        settings.Current.Provider.Should().Be("ollama");
        settings.Current.Endpoint.Should().Be("http://localhost:11434");
        settings.Current.Model.Should().Be("llama3");
    }

    [Fact]
    public void Load_ClearsStaleModelWhenProviderOverriddenButModelNotInConfig()
    {
        // This is the exact scenario that caused the "Azure OpenAI + gemma4:e2b" bug:
        // file has Model="gemma4:e2b" (Ollama), IConfiguration overrides Provider to "azure-openai"
        // but does NOT set Model:Model. The stale Model must be cleared.
        var fileConfig = new ModelProviderConfig
        {
            Provider = "ollama",
            Model = "gemma4:e2b",
            Endpoint = "http://localhost:11434"
        };
        WritePersistFile(fileConfig);

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "azure-openai",
            ["Model:Endpoint"] = "https://my-resource.openai.azure.com/",
            ["Model:DeploymentName"] = "gpt-5-mini",
            ["Model:ApiKey"] = "test-key"
        });

        // Act
        var settings = CreateSettings(config);

        // Assert: stale Ollama model name must NOT leak into Azure OpenAI config
        settings.Current.Provider.Should().Be("azure-openai");
        settings.Current.Model.Should().BeNull("stale Model from a different provider must be cleared");
        settings.Current.DeploymentName.Should().Be("gpt-5-mini");
        settings.Current.Endpoint.Should().Be("https://my-resource.openai.azure.com/");
        settings.Current.ApiKey.Should().Be("test-key");
    }

    [Fact]
    public void Load_FallsBackToConfig_WhenNoFile()
    {
        // Arrange: no model-settings.json — everything comes from IConfiguration
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "ollama",
            ["Model:Model"] = "llama3",
            ["Model:Endpoint"] = "http://localhost:11434",
            ["Model:ApiKey"] = "my-api-key"
        });

        // Act
        var settings = CreateSettings(config);

        // Assert
        settings.Current.Provider.Should().Be("ollama");
        settings.Current.Model.Should().Be("llama3");
        settings.Current.Endpoint.Should().Be("http://localhost:11434");
        settings.Current.ApiKey.Should().Be("my-api-key");
    }

    [Fact]
    public void Load_HandlesMissingFile_Gracefully()
    {
        // Arrange: no file, empty config
        var config = BuildConfig(new Dictionary<string, string?>());

        // Act
        var settings = CreateSettings(config);

        // Assert: should return defaults without throwing
        settings.Current.Provider.Should().Be("ollama");
        settings.Current.ApiKey.Should().BeNull();
        settings.Current.Model.Should().BeNull();
    }

    // ── Update / Persist tests ────────────────────────────────────────────────

    [Fact]
    public void Update_PersistsConfig_WithApiKey()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "ollama"
        });
        var settings = CreateSettings(config);

        var updated = new ModelProviderConfig
        {
            Provider = "azure-openai",
            Endpoint = "https://my-resource.openai.azure.com/",
            ApiKey = "super-secret-key",
            DeploymentName = "gpt-4o"
        };

        // Act
        settings.Update(updated);

        // Assert: API key is now persisted for this educational demo app
        var persistPath = Path.Combine(_tempDir, "model-settings.json");
        File.Exists(persistPath).Should().BeTrue();

        var json = File.ReadAllText(persistPath);
        var persisted = JsonSerializer.Deserialize<ModelProviderConfig>(json);
        persisted.Should().NotBeNull();
        persisted!.ApiKey.Should().Be("super-secret-key", "demo app persists ApiKey so Settings UI keys survive restarts");
        persisted.Provider.Should().Be("azure-openai");
        persisted.DeploymentName.Should().Be("gpt-4o");
    }

    [Fact]
    public void Update_PreservesApiKey_InMemory()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "ollama"
        });
        var settings = CreateSettings(config);

        var updated = new ModelProviderConfig
        {
            Provider = "azure-openai",
            Endpoint = "https://my-resource.openai.azure.com/",
            ApiKey = "super-secret-key",
            DeploymentName = "gpt-4o"
        };

        // Act
        settings.Update(updated);

        // Assert: in-memory Current still has the ApiKey
        settings.Current.ApiKey.Should().Be("super-secret-key");
        settings.Current.Provider.Should().Be("azure-openai");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private RuntimeModelSettings CreateSettings(IConfiguration config)
    {
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);
        return new RuntimeModelSettings(config, mockEnv.Object, NullLogger<RuntimeModelSettings>.Instance);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private void WritePersistFile(ModelProviderConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_tempDir, "model-settings.json"), json);
    }
}
