using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Foundry;
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
    public void GetOrCreate_ReturnsFoundryClient_WhenProviderIsFoundry()
    {
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "foundry",
            ["Model:Endpoint"] = "https://foundry.example/api/projects/test-project",
            ["Model:ApiKey"] = "test-key",
            ["Model:Model"] = "Phi-4"
        });

        using var client = new RuntimeModelClient(settings, _httpClientFactory, _loggerFactory);

        client.ProviderName.Should().Be("foundry");
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

    [Fact]
    public async Task CompleteAsync_ForFoundry_UsesResolvedEndpointApiKeyAndModel()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var factory = CreateHttpClientFactory(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"Phi-4","choices":[{"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "foundry",
            ["Model:Endpoint"] = "https://foundry.example/api/projects/profile-project",
            ["Model:ApiKey"] = "profile-api-key",
            ["Model:Model"] = "Phi-4"
        });

        using var client = new RuntimeModelClient(settings, factory, _loggerFactory);
        await client.CompleteAsync(CreateChatRequest());

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsoluteUri.Should().Be(
            "https://foundry.example/api/projects/profile-project/chat/completions");
        capturedRequest.Headers.GetValues("api-key").Should().ContainSingle("profile-api-key");
        JsonDocument.Parse(capturedBody!).RootElement.GetProperty("model").GetString().Should().Be("Phi-4");
        capturedRequest.RequestUri.Port.Should().Be(443, "Foundry must never fall back to Ollama localhost");
    }

    [Fact]
    public async Task StreamAsync_ForFoundry_UsesResolvedEndpointApiKeyAndModel()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var factory = CreateHttpClientFactory(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\ndata: [DONE]\n\n",
                    Encoding.UTF8,
                    "text/event-stream")
            };
        });
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "foundry",
            ["Model:Endpoint"] = "https://foundry.example/api/projects/profile-project",
            ["Model:ApiKey"] = "profile-api-key",
            ["Model:Model"] = "Phi-4"
        });

        using var client = new RuntimeModelClient(settings, factory, _loggerFactory);
        var chunks = new List<ChatResponseChunk>();
        await foreach (var chunk in client.StreamAsync(CreateChatRequest()))
            chunks.Add(chunk);

        chunks.Should().ContainSingle().Which.Content.Should().Be("Hello");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsoluteUri.Should().Be(
            "https://foundry.example/api/projects/profile-project/chat/completions");
        capturedRequest.Headers.GetValues("api-key").Should().ContainSingle("profile-api-key");
        using var payload = JsonDocument.Parse(capturedBody!);
        payload.RootElement.GetProperty("model").GetString().Should().Be("Phi-4");
        payload.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        capturedRequest.RequestUri.Port.Should().Be(443, "Foundry must never fall back to Ollama localhost");
    }

    [Fact]
    public async Task CompleteAsync_ForFoundryWithoutEndpoint_DoesNotSendToOllamaDefault()
    {
        var requestCount = 0;
        var factory = CreateHttpClientFactory(request =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var settings = CreateSettings(new Dictionary<string, string?>
        {
            ["Model:Provider"] = "foundry",
            ["Model:ApiKey"] = "profile-api-key",
            ["Model:Model"] = "Phi-4"
        });

        using var client = new RuntimeModelClient(settings, factory, _loggerFactory);
        var act = () => client.CompleteAsync(CreateChatRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Foundry is not configured*");
        requestCount.Should().Be(0, "an unconfigured Foundry provider must not contact localhost:11434");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ChatRequest CreateChatRequest() => new()
    {
        Messages =
        [
            new ChatMessage { Role = ChatMessageRole.User, Content = "Hi" }
        ]
    };

    private static IHttpClientFactory CreateHttpClientFactory(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new TestHttpMessageHandler(handler)));
        return factory.Object;
    }

    private RuntimeModelSettings CreateSettings(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);
        return new RuntimeModelSettings(config, mockEnv.Object, NullLogger<RuntimeModelSettings>.Instance);
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
