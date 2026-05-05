// These tests deliberately exercise the obsolete ChatHub for back-compat regression coverage.
#pragma warning disable CS0618
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Hubs;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Unit tests for <see cref="ChatHub"/> — verifies event mapping, error handling,
/// and edge cases in isolation from the real SignalR pipeline.
/// </summary>
public sealed class ChatHubTests
{
    private readonly ILogger<ChatHub> _logger = NullLogger<ChatHub>.Instance;

    [Fact]
    public async Task StreamChat_YieldsContentEvents()
    {
        // Arrange: orchestrator yields content deltas
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.ContentDelta, Content = "Hello" },
                new AgentStreamEvent { Type = AgentStreamEventType.ContentDelta, Content = " World" },
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "Hello World" }
            ));

        var hub = new ChatHub(orchestrator.Object, _logger);

        // Act
        var messages = new List<ChatHubMessage>();
        await foreach (var msg in hub.StreamChat(Guid.NewGuid(), "Hi"))
            messages.Add(msg);

        // Assert
        messages.Should().HaveCount(3);
        messages[0].Type.Should().Be("content");
        messages[0].Content.Should().Be("Hello");
        messages[1].Type.Should().Be("content");
        messages[1].Content.Should().Be(" World");
        messages[2].Type.Should().Be("complete");
    }

    [Fact]
    public async Task StreamChat_CatchesProviderException_YieldsError()
    {
        // Arrange: orchestrator throws ModelProviderUnavailableException
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowAfterYield<AgentStreamEvent>(
                new ModelProviderUnavailableException("ollama", "Ollama is down")));

        var hub = new ChatHub(orchestrator.Object, _logger);

        // Act
        var messages = new List<ChatHubMessage>();
        await foreach (var msg in hub.StreamChat(Guid.NewGuid(), "Hi"))
            messages.Add(msg);

        // Assert: error event should be yielded (not swallowed)
        messages.Should().ContainSingle(m => m.Type == "error");
        messages[0].Content.Should().Contain("Model provider is unavailable");
    }

    [Fact]
    public async Task StreamChat_CatchesGenericException_YieldsError()
    {
        // Arrange: orchestrator throws an unexpected exception
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowAfterYield<AgentStreamEvent>(
                new InvalidOperationException("Something went wrong")));

        var hub = new ChatHub(orchestrator.Object, _logger);

        // Act
        var messages = new List<ChatHubMessage>();
        await foreach (var msg in hub.StreamChat(Guid.NewGuid(), "Hi"))
            messages.Add(msg);

        // Assert
        messages.Should().ContainSingle(m => m.Type == "error");
        messages[0].Content.Should().Contain("Something went wrong");
    }

    [Fact]
    public async Task StreamChat_EmptyQuestion_StillProcesses()
    {
        // Arrange: even with empty string, the hub should forward to orchestrator
        // (validation is the orchestrator's job, not the hub's)
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.Error, Content = "Message cannot be empty" }
            ));

        var hub = new ChatHub(orchestrator.Object, _logger);

        // Act
        var messages = new List<ChatHubMessage>();
        await foreach (var msg in hub.StreamChat(Guid.NewGuid(), ""))
            messages.Add(msg);

        // Assert
        messages.Should().ContainSingle(m => m.Type == "error");
    }

    [Fact]
    public async Task StreamChat_MapsAllEventTypes()
    {
        // Arrange: test every AgentStreamEventType mapping
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamEvents(
                new AgentStreamEvent { Type = AgentStreamEventType.ContentDelta, Content = "text" },
                new AgentStreamEvent { Type = AgentStreamEventType.ToolApprovalRequest, ToolName = "shell" },
                new AgentStreamEvent { Type = AgentStreamEventType.ToolCallStart, ToolName = "file_system" },
                new AgentStreamEvent { Type = AgentStreamEventType.ToolCallComplete, ToolResult = new OpenClawNet.Tools.Abstractions.ToolResult { ToolName = "file_system", Output = "done", Success = true } },
                new AgentStreamEvent { Type = AgentStreamEventType.Error, Content = "oops" },
                new AgentStreamEvent { Type = AgentStreamEventType.Complete, Content = "final" }
            ));

        var hub = new ChatHub(orchestrator.Object, _logger);

        // Act
        var messages = new List<ChatHubMessage>();
        await foreach (var msg in hub.StreamChat(Guid.NewGuid(), "test"))
            messages.Add(msg);

        // Assert
        messages.Should().HaveCount(6);
        messages[0].Type.Should().Be("content");
        messages[1].Type.Should().Be("tool_approval");
        messages[1].Content.Should().Be("shell");
        messages[2].Type.Should().Be("tool_start");
        messages[2].Content.Should().Be("file_system");
        messages[3].Type.Should().Be("tool_complete");
        messages[3].Content.Should().Be("done");
        messages[4].Type.Should().Be("error");
        messages[5].Type.Should().Be("complete");
    }

    [Fact]
    public async Task StreamChat_HttpRequestException_YieldsProviderUnavailableError()
    {
        // Arrange: simulate a network error during streaming
        var orchestrator = new Mock<IAgentOrchestrator>();
        orchestrator
            .Setup(o => o.StreamAsync(It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowAfterYield<AgentStreamEvent>(
                new HttpRequestException("Connection refused")));

        var hub = new ChatHub(orchestrator.Object, _logger);

        // Act
        var messages = new List<ChatHubMessage>();
        await foreach (var msg in hub.StreamChat(Guid.NewGuid(), "Hi"))
            messages.Add(msg);

        // Assert
        messages.Should().ContainSingle(m => m.Type == "error");
        messages[0].Content.Should().Contain("provider is unavailable");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<T> StreamEvents<T>(params T[] events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }

    /// <summary>
    /// Creates an IAsyncEnumerable that throws on the first MoveNextAsync call.
    /// Used to test exception handling in the streaming loop.
    /// </summary>
    private static async IAsyncEnumerable<T> ThrowAfterYield<T>(
        Exception ex,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162 // Unreachable code detected — required to make this an async iterator
        yield break;
#pragma warning restore CS0162
    }
}
