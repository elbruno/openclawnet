using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Tests that POST /api/chat/ accepts and resolves AgentProfileName,
/// and that ProviderResolver integration works through the endpoint.
/// </summary>
public sealed class ChatEndpointProfileTests
{
    [Fact]
    public async Task Post_WithAgentProfileName_ResolvesProfile()
    {
        AgentRequest? capturedRequest = null;
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new AgentResponse { Content = "Arr!", ToolCallCount = 0, TotalTokens = 10 });

        var profileStore = new Mock<IAgentProfileStore>();
        profileStore.Setup(s => s.GetAsync("pirate", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProfile
            {
                Name = "pirate",
                DisplayName = "Pirate Agent",
                Provider = "ollama-pirate",
                Instructions = "You are a pirate."
            });

        var definitionStore = new Mock<IModelProviderDefinitionStore>();
        definitionStore.Setup(s => s.GetAsync("ollama-pirate", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelProviderDefinition
            {
                Name = "ollama-pirate",
                ProviderType = "ollama",
                Model = "pirate-7b",
                Endpoint = "http://localhost:11434",
                IsSupported = true
            });

        await using var app = await CreateTestAppAsync(orchestrator.Object, profileStore.Object, definitionStore.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hello",
            agentProfileName = "pirate"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.AgentProfileName.Should().Be("pirate");
        capturedRequest.AgentProfileInstructions.Should().Be("You are a pirate.");
        capturedRequest.ResolvedProvider.Should().NotBeNull();
        capturedRequest.ResolvedProvider!.ProviderType.Should().Be("ollama");
        capturedRequest.ResolvedProvider.Model.Should().Be("pirate-7b");
    }

    [Fact]
    public async Task Post_WithoutAgentProfileName_UsesDefault()
    {
        AgentRequest? capturedRequest = null;
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.ProcessAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new AgentResponse { Content = "Hi!", ToolCallCount = 0, TotalTokens = 5 });

        var profileStore = new Mock<IAgentProfileStore>();
        profileStore.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProfile
            {
                Name = "default",
                DisplayName = "Default Agent",
                IsDefault = true,
                Provider = "ollama",
                Instructions = "You are a helpful assistant."
            });

        var definitionStore = new Mock<IModelProviderDefinitionStore>();
        // Wave 5 PR-D: ProviderResolver now fails fast on unresolved non-empty
        // providerRef. Register an ollama definition so resolution succeeds —
        // this matches what the gateway does in production via the
        // ModelProviderDefinitionSeeder.
        definitionStore.Setup(s => s.GetAsync("ollama", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelProviderDefinition
            {
                Name = "ollama",
                ProviderType = "ollama",
                Endpoint = "http://localhost:11434",
                Model = "fallback-model",
                IsSupported = true
            });
        definitionStore.Setup(s => s.ListByTypeAsync("ollama", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ModelProviderDefinition>());

        await using var app = await CreateTestAppAsync(orchestrator.Object, profileStore.Object, definitionStore.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hello"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.AgentProfileName.Should().Be("default");
        capturedRequest.AgentProfileInstructions.Should().Be("You are a helpful assistant.");
        profileStore.Verify(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<WebApplication> CreateTestAppAsync(
        IAgentOrchestrator orchestrator,
        IAgentProfileStore profileStore,
        IModelProviderDefinitionStore definitionStore)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton(profileStore);
        builder.Services.AddSingleton(definitionStore);

        // Add logging and HTTP context accessor
        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();

        // Register ChatNamingService (needed by PostAutoRename endpoint)
        var mockModelClient = new Mock<IModelClient>();
        builder.Services.AddSingleton(mockModelClient.Object);
        builder.Services.AddSingleton<ChatNamingService>();

        // Register ProviderResolver with a real RuntimeModelSettings backed by in-memory config
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Model:Provider"] = "ollama",
                ["Model:Model"] = "fallback-model",
                ["Model:Endpoint"] = "http://fallback:11434"
            })
            .Build();
        var env = Mock.Of<IHostEnvironment>(e => e.ContentRootPath == Path.GetTempPath());
        var runtimeLogger = Mock.Of<ILogger<RuntimeModelSettings>>();
        var runtimeSettings = new RuntimeModelSettings(config, env, runtimeLogger);
        builder.Services.AddSingleton(runtimeSettings);

        var resolverLogger = Mock.Of<ILogger<ProviderResolver>>();
        builder.Services.AddSingleton(sp => new ProviderResolver(
            sp.GetRequiredService<IModelProviderDefinitionStore>(),
            sp.GetRequiredService<RuntimeModelSettings>(),
            resolverLogger));

        var app = builder.Build();
        app.MapChatEndpoints();
        await app.StartAsync();
        return app;
    }
}
