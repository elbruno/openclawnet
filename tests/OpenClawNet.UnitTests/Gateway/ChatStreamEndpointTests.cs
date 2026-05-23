using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Tests the POST /api/chat/stream NDJSON endpoint contract.
/// Uses a minimal test server with a mock IAgentOrchestrator — no database,
/// no real model provider, no SignalR. Verifies the HTTP layer Dallas added
/// to replace the SignalR chat path.
/// </summary>
public sealed class ChatStreamEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Post_ValidMessage_ReturnsNdjsonContentEvents()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.ContentDelta, Content = "Hello" },
                new AgentStreamEvent { Type = AgentStreamEventType.ContentDelta, Content = " World" },
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "Hello World" }
            ));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hi"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await ReadNdjsonEventsAsync(response);
        events.Should().HaveCount(3);
        events[0].Type.Should().Be("content");
        events[0].Content.Should().Be("Hello");
        events[1].Type.Should().Be("content");
        events[1].Content.Should().Be(" World");
        events[2].Type.Should().Be("complete");
    }

    [Fact]
    public async Task Post_ValidMessage_SetsNdjsonContentType()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "done" }
            ));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hi"
        });

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/x-ndjson");
    }

    [Fact]
    public async Task Post_EmptyMessage_Returns400BadRequest()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        orchestrator.Verify(
            o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the orchestrator should never be called for blank messages");
    }

    [Fact]
    public async Task Post_WhitespaceMessage_Returns400BadRequest()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "   "
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_ModelProviderUnavailable_YieldsErrorEvent()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowAfterYield<AgentStreamEvent>(
                new ModelProviderUnavailableException("ollama", "Connection refused")));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hi"
        });

        // Stream started successfully — error is delivered in-band as NDJSON
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await ReadNdjsonEventsAsync(response);
        events.Should().ContainSingle(e => e.Type == "error");
        events.First(e => e.Type == "error").Content.Should().Contain("unavailable");
    }

    [Fact]
    public async Task Post_HttpRequestException_YieldsErrorEvent()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowAfterYield<AgentStreamEvent>(
                new HttpRequestException("Connection refused")));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hi"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await ReadNdjsonEventsAsync(response);
        events.Should().ContainSingle(e => e.Type == "error");
        events.First(e => e.Type == "error").Content.Should().Contain("provider is unavailable");
    }

    [Fact]
    public async Task Post_UnexpectedException_YieldsErrorEventWithMessage()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowAfterYield<AgentStreamEvent>(
                new InvalidOperationException("Something broke")));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hi"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await ReadNdjsonEventsAsync(response);
        events.Should().ContainSingle(e => e.Type == "error");
        events.First(e => e.Type == "error").Content.Should().Contain("Something broke");
    }

    [Fact]
    public async Task Post_AllEventTypes_MapCorrectly()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.ContentDelta, Content = "text" },
                new AgentStreamEvent { Type = AgentStreamEventType.ToolApprovalRequest, ToolName = "shell" },
                new AgentStreamEvent { Type = AgentStreamEventType.ToolCallStart, ToolName = "file_system" },
                new AgentStreamEvent { Type = AgentStreamEventType.ToolCallComplete, ToolName = "file_system" },
                new AgentStreamEvent { Type = AgentStreamEventType.Error, Content = "oops" },
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "final" }
            ));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "test"
        });

        var events = await ReadNdjsonEventsAsync(response);
        events.Should().HaveCount(6);
        events[0].Type.Should().Be("content");
        events[1].Type.Should().Be("tool_approval");
        events[2].Type.Should().Be("tool_start");
        events[3].Type.Should().Be("tool_complete");
        events[4].Type.Should().Be("error");
        events[5].Type.Should().Be("complete");
    }

    [Fact]
    public async Task Post_UsesCamelCaseJsonNaming()
    {
        var sessionId = Guid.NewGuid();
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.ToolCallStart, ToolName = "list_files" }
            ));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId,
            message = "Hi"
        });

        var body = await response.Content.ReadAsStringAsync();
        var firstLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).First();

        // Verify camelCase: "toolName" not "ToolName", "sessionId" not "SessionId"
        firstLine.Should().Contain("\"toolName\"");
        firstLine.Should().Contain("\"sessionId\"");
        firstLine.Should().NotContain("\"ToolName\"");
        firstLine.Should().NotContain("\"SessionId\"");
    }

    [Fact]
    public async Task Post_SessionIdPassedThrough_AppearsInEvents()
    {
        var sessionId = Guid.NewGuid();
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.ContentDelta, Content = "ok" }
            ));

        await using var app = await CreateTestAppAsync(orchestrator.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId,
            message = "Hi"
        });

        var events = await ReadNdjsonEventsAsync(response);
        events.Should().ContainSingle();
        events[0].SessionId.Should().Be(sessionId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<WebApplication> CreateTestAppAsync(IAgentOrchestrator orchestrator)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(orchestrator);

        // ChatStreamEndpoints now requires IAgentProfileStore for profile resolution
        var profileStore = new Mock<IAgentProfileStore>();
        profileStore.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProfile
            {
                Name = "default",
                DisplayName = "Default Agent",
                IsDefault = true,
                Instructions = "You are a helpful assistant."
            });
        builder.Services.AddSingleton(profileStore.Object);

        var app = builder.Build();
        app.MapChatStreamEndpoints();
        await app.StartAsync();
        return app;
    }

    private static async Task<List<ChatStreamEvent>> ReadNdjsonEventsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines
            .Select(line => JsonSerializer.Deserialize<ChatStreamEvent>(line, JsonOpts)!)
            .ToList();
    }

    private static async IAsyncEnumerable<T> YieldEvents<T>(params T[] events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }

    private static async IAsyncEnumerable<T> ThrowAfterYield<T>(
        Exception ex,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    // ── Profile resolution tests ─────────────────────────────────────────────

    [Fact]
    public async Task Post_WithExplicitProfileName_UsesSpecifiedProfile()
    {
        AgentRequest? capturedRequest = null;
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRequest, CancellationToken>((req, _) => capturedRequest = req)
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "Arr!" }
            ));

        var profileStore = new Mock<IAgentProfileStore>();
        profileStore.Setup(s => s.GetAsync("pirate", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProfile
            {
                Name = "pirate",
                DisplayName = "Pirate Agent",
                Provider = "ollama",
                Instructions = "You are a pirate."
            });

        await using var app = await CreateTestAppWithProfileStoreAsync(orchestrator.Object, profileStore.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hello",
            agentProfileName = "pirate"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Provider.Should().Be("ollama");
        capturedRequest.AgentProfileInstructions.Should().Be("You are a pirate.");
    }

    [Fact]
    public async Task Post_WithNonExistentProfileName_FallsBackToDefault()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "ok" }
            ));

        var profileStore = new Mock<IAgentProfileStore>();
        profileStore.Setup(s => s.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentProfile?)null);
        profileStore.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProfile
            {
                Name = "default",
                DisplayName = "Default Agent",
                IsDefault = true,
                Instructions = "You are a default assistant."
            });

        await using var app = await CreateTestAppWithProfileStoreAsync(orchestrator.Object, profileStore.Object);
        using var client = app.GetTestClient();

        await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hello",
            agentProfileName = "missing"
        });

        profileStore.Verify(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_WithoutProfileName_UsesDefaultProfile()
    {
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "ok" }
            ));

        var profileStore = new Mock<IAgentProfileStore>();
        profileStore.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProfile
            {
                Name = "default",
                DisplayName = "Default Agent",
                IsDefault = true,
                Instructions = "You are a default assistant."
            });

        await using var app = await CreateTestAppWithProfileStoreAsync(orchestrator.Object, profileStore.Object);
        using var client = app.GetTestClient();

        await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Hello"
        });

        profileStore.Verify(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<WebApplication> CreateTestAppWithProfileStoreAsync(
        IAgentOrchestrator orchestrator,
        IAgentProfileStore profileStore)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton(profileStore);

        var app = builder.Build();
        app.MapChatStreamEndpoints();
        await app.StartAsync();
        return app;
    }

    // ── IAgentProvider path (github-copilot) tests ───────────────────────────
    // These tests exercise the StreamViaAgentProviderAsync branch that is taken
    // when the profile's Provider is "github-copilot". See Issue #85 for the
    // fix that wraps this path in a 90-second session timeout.
    // The 90-second timeout itself is exercised by the E2E demo:
    //   tests/OpenClawNet.PlaywrightTests/Demos/BrowseAndScheduleE2EDemoTests.cs

    [Fact]
    public async Task AgentProviderPath_NormalCompletion_SendsContentAndCompleteEvents()
    {
        var chatClient = new Mock<Microsoft.Extensions.AI.IChatClient>();
        chatClient
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IList<Microsoft.Extensions.AI.ChatMessage>>(),
                It.IsAny<Microsoft.Extensions.AI.ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(YieldChatUpdates("Hello", " World"));
        var agentProvider = new Mock<IAgentProvider>();
        agentProvider.Setup(p => p.ProviderName).Returns("github-copilot");
        agentProvider.Setup(p => p.CreateChatClient(It.IsAny<AgentProfile>())).Returns(chatClient.Object);

        await using var app = await CreateTestAppWithAgentProviderAsync(agentProvider.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Summarise this page",
            agentProfileName = "ghcp-agent"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await ReadNdjsonEventsAsync(response);
        events.Should().HaveCount(3); // content, content, complete
        events[0].Type.Should().Be("content");
        events[0].Content.Should().Be("Hello");
        events[1].Type.Should().Be("content");
        events[1].Content.Should().Be(" World");
        events[2].Type.Should().Be("complete");
    }

    [Fact]
    public async Task AgentProviderPath_ExceptionDuringStream_SendsErrorEvent()
    {
        var chatClient = new Mock<Microsoft.Extensions.AI.IChatClient>();
        chatClient
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IList<Microsoft.Extensions.AI.ChatMessage>>(),
                It.IsAny<Microsoft.Extensions.AI.ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingChatUpdates(new InvalidOperationException("Copilot SDK exploded")));

        var agentProvider = new Mock<IAgentProvider>();
        agentProvider.Setup(p => p.ProviderName).Returns("github-copilot");
        agentProvider.Setup(p => p.CreateChatClient(It.IsAny<AgentProfile>())).Returns(chatClient.Object);

        await using var app = await CreateTestAppWithAgentProviderAsync(agentProvider.Object);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Schedule a meeting",
            agentProfileName = "ghcp-agent"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await ReadNdjsonEventsAsync(response);
        events.Should().ContainSingle(e => e.Type == "error");
        events.First(e => e.Type == "error").Content.Should().Contain("Copilot SDK exploded");
    }

    [Fact]
    public async Task AgentProviderPath_NoRegisteredProvider_SendsErrorEvent()
    {
        // No IAgentProvider registered for "github-copilot" → error event, no complete.
        await using var app = await CreateTestAppWithAgentProviderAsync(agentProvider: null);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId = Guid.NewGuid(),
            message = "Schedule a meeting",
            agentProfileName = "ghcp-agent"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await ReadNdjsonEventsAsync(response);
        events.Should().ContainSingle(e => e.Type == "error");
        events.First(e => e.Type == "error").Content.Should().Contain("github-copilot");
    }

    private static async Task<WebApplication> CreateTestAppWithAgentProviderAsync(
        IAgentProvider? agentProvider)
    {
        var orchestrator = new Mock<IAgentOrchestrator>();

        var ghcpProfile = new AgentProfile
        {
            Name = "ghcp-agent",
            DisplayName = "GitHub Copilot Agent",
            Provider = "github-copilot",
            Instructions = "You are a helpful assistant."
        };

        var profileStore = new Mock<IAgentProfileStore>();
        profileStore
            .Setup(s => s.GetAsync("ghcp-agent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ghcpProfile);
        profileStore
            .Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ghcpProfile);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(orchestrator.Object);
        builder.Services.AddSingleton(profileStore.Object);
        if (agentProvider is not null)
        {
            builder.Services.AddSingleton(agentProvider);
        }

        var app = builder.Build();
        app.MapChatStreamEndpoints();
        await app.StartAsync();
        return app;
    }

    private static async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> YieldChatUpdates(
        params string[] texts)
    {
        foreach (var text in texts)
        {
            await Task.Yield();
            yield return new Microsoft.Extensions.AI.ChatResponseUpdate
            {
                Role = Microsoft.Extensions.AI.ChatRole.Assistant,
                Contents = [new Microsoft.Extensions.AI.TextContent(text)]
            };
        }
    }

    private static async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> ThrowingChatUpdates(
        Exception ex,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
