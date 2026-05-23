using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;
using MEAIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using MEAIChatResponse = Microsoft.Extensions.AI.ChatResponse;
using OCChatMessage = OpenClawNet.Models.Abstractions.ChatMessage;
using OCChatResponse = OpenClawNet.Models.Abstractions.ChatResponse;
using ModelToolCall = OpenClawNet.Models.Abstractions.ToolCall;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Tests for <see cref="ModelClientChatClientAdapter"/> — the bridge between
/// <see cref="IModelClient"/> and <see cref="IChatClient"/>.
/// </summary>
public sealed class ModelClientChatClientAdapterTests
{
    // ── GetResponseAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetResponseAsync_ConvertsMessages_Correctly()
    {
        // Arrange: provide MEAI messages, verify they reach IModelClient as OC messages
        ChatRequest? capturedRequest = null;
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(m => m.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new OCChatResponse
            {
                Content = "Hello!",
                Role = ChatMessageRole.Assistant,
                Model = "test"
            });

        var adapter = new ModelClientChatClientAdapter(modelClient.Object);
        var messages = new List<MEAIChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Hi there")
        };

        // Act
        var response = await adapter.GetResponseAsync(messages);

        // Assert: messages were converted
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().HaveCount(2);
        capturedRequest.Messages[0].Role.Should().Be(ChatMessageRole.System);
        capturedRequest.Messages[0].Content.Should().Be("You are a helpful assistant.");
        capturedRequest.Messages[1].Role.Should().Be(ChatMessageRole.User);
        capturedRequest.Messages[1].Content.Should().Be("Hi there");

        // Assert: response was converted
        response.Should().NotBeNull();
        response.Messages.Should().HaveCountGreaterThan(0);
        response.Messages[0].Text.Should().Be("Hello!");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_YieldsUpdates()
    {
        // Arrange: model client streams two chunks
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(m => m.StreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamChunks(
                new ChatResponseChunk { Content = "Hello", FinishReason = null },
                new ChatResponseChunk { Content = " World", FinishReason = "stop" }
            ));

        var adapter = new ModelClientChatClientAdapter(modelClient.Object);
        var messages = new List<MEAIChatMessage> { new(ChatRole.User, "Hi") };

        // Act
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter.GetStreamingResponseAsync(messages))
            updates.Add(update);

        // Assert
        updates.Should().HaveCount(2);
        updates[0].Contents.OfType<TextContent>().Should().ContainSingle(t => t.Text == "Hello");
        updates[1].Contents.OfType<TextContent>().Should().ContainSingle(t => t.Text == " World");
        updates.Should().OnlyContain(u => u.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithToolCalls_YieldsFunctionContent()
    {
        // Arrange: model returns a chunk with tool calls
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(m => m.StreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(StreamChunks(new ChatResponseChunk
            {
                ToolCalls = [new ModelToolCall
                {
                    Id = "call_123",
                    Name = "get_weather",
                    Arguments = """{"city":"Seattle"}"""
                }],
                FinishReason = "tool_calls"
            }));

        var adapter = new ModelClientChatClientAdapter(modelClient.Object);
        var messages = new List<MEAIChatMessage> { new(ChatRole.User, "What's the weather?") };

        // Act
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter.GetStreamingResponseAsync(messages))
            updates.Add(update);

        // Assert
        updates.Should().HaveCount(1);
        var functionContent = updates[0].Contents.OfType<FunctionCallContent>().Should().ContainSingle().Subject;
        functionContent.Name.Should().Be("get_weather");
        functionContent.CallId.Should().Be("call_123");
        functionContent.Arguments.Should().ContainKey("city");
    }

    [Fact]
    public async Task GetResponseAsync_PropagatesExceptions()
    {
        // Arrange: model client throws
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(m => m.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var adapter = new ModelClientChatClientAdapter(modelClient.Object);
        var messages = new List<MEAIChatMessage> { new(ChatRole.User, "Hi") };

        // Act & Assert
        var act = () => adapter.GetResponseAsync(messages);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Connection refused*");
    }

    [Fact]
    public async Task GetResponseAsync_WithToolRoundTrip_ToolResultContentReachesSecondTurn()
    {
        // Arrange: Simulate a two-turn conversation:
        // Turn 1: User → Model calls a tool
        // Turn 2: Tool result → Model continues
        // This validates that FunctionResultContent.Result is preserved through the round-trip.

        // Realistic ~1KB markdown result (simulates tool output)
        var toolOutput = """
            # Search Results

            ## Result 1
            Title: Getting Started with OpenClawNet
            Link: https://example.com/docs/getting-started
            Summary: OpenClawNet is a comprehensive framework for building agent-driven applications with tool calling support. This guide walks through the basic setup, configuration, and usage patterns.

            ## Result 2
            Title: Advanced Tool Integration
            Link: https://example.com/docs/tools
            Summary: Learn how to integrate custom tools into your OpenClawNet agents. Includes examples for API calls, database queries, and real-time data fetching.

            ## Result 3
            Title: Architecture Overview
            Link: https://example.com/docs/architecture
            Summary: Explore the architecture of OpenClawNet, including the adapter patterns, message flow, and extensibility points.

            **Note:** This is a simulated search result with realistic markdown formatting for testing purposes.
            """;

        // Turn 1: User asks, model returns tool call
        ChatRequest? firstTurnRequest = null;
        var firstTurnResponse = new OCChatResponse
        {
            Content = string.Empty,
            Role = ChatMessageRole.Assistant,
            Model = "test",
            ToolCalls = [
                new ModelToolCall
                {
                    Id = "call_search_001",
                    Name = "search_docs",
                    Arguments = """{"query":"OpenClawNet getting started"}"""
                }
            ]
        };

        // Turn 2: Tool result comes back, model should see full result
        ChatRequest? secondTurnRequest = null;
        var secondTurnResponse = new OCChatResponse
        {
            Content = "Based on the search results, here's what I found...",
            Role = ChatMessageRole.Assistant,
            Model = "test"
        };

        var modelClient = new Mock<IModelClient>();
        var callCount = 0;

        modelClient
            .Setup(m => m.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatRequest, CancellationToken>((req, _) =>
            {
                if (callCount == 0)
                {
                    firstTurnRequest = req;
                }
                else
                {
                    secondTurnRequest = req;
                }
                callCount++;
            })
            .Returns((ChatRequest req, CancellationToken _) =>
            {
                var response = callCount == 1 ? firstTurnResponse : secondTurnResponse;
                return Task.FromResult(response);
            });

        var adapter = new ModelClientChatClientAdapter(modelClient.Object);

        // Act - Turn 1: User asks question
        var userMessage = new MEAIChatMessage(ChatRole.User, "Search for OpenClawNet documentation");
        var firstResponse = await adapter.GetResponseAsync([userMessage]);

        // Verify Turn 1 worked
        firstResponse.Messages.Should().HaveCountGreaterThan(0);
        firstResponse.Messages[0].Contents.OfType<FunctionCallContent>().Should().ContainSingle();

        // Act - Turn 2: Present tool result and get second response
        // Build the second turn with:
        // 1. Original user message
        // 2. Assistant's tool call (from first response)
        // 3. Tool result (what the tool returned)
        var assistantToolCallMessage = new MEAIChatMessage(ChatRole.Assistant, firstResponse.Messages[0].Contents);
        var toolResultMessage = new MEAIChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call_search_001", toolOutput)]
        );

        var secondTurnMessages = new List<MEAIChatMessage>
        {
            userMessage,
            assistantToolCallMessage,
            toolResultMessage
        };

        var secondResponse = await adapter.GetResponseAsync(secondTurnMessages);

        // Assert: Verify the second turn ChatRequest contains the full tool output
        secondTurnRequest.Should().NotBeNull("Second turn should have sent a request to the model");
        secondTurnRequest!.Messages.Should().HaveCountGreaterThanOrEqualTo(3, "Should have user, assistant (with tool call), and tool result");

        // Find the tool role message (should be the last message or nearby)
        var toolMessage = secondTurnRequest.Messages
            .Where(m => m.Role == ChatMessageRole.Tool)
            .ToList();
        toolMessage.Should().HaveCount(1, "Should have exactly one tool message");

        // Assert: Tool message content matches the full markdown result (no data loss)
        var toolContent = toolMessage[0].Content;
        toolContent.Should().Be(toolOutput, "Tool result content should match original markdown output exactly");
        toolContent.Should().Contain("Getting Started with OpenClawNet", "Tool result should contain expected content");
        toolContent.Length.Should().BeGreaterThan(500, "Tool result should be substantial (~1KB in this test)");

        // Assert: Response was successful
        secondResponse.Should().NotBeNull();
        secondResponse.Messages.Should().HaveCountGreaterThan(0);
        secondResponse.Messages[0].Text.Should().Contain("search results");
    }

    // ── Static conversion helpers (internal, accessible via InternalsVisibleTo) ──

    [Theory]
    [InlineData("system", ChatMessageRole.System)]
    [InlineData("user", ChatMessageRole.User)]
    [InlineData("assistant", ChatMessageRole.Assistant)]
    [InlineData("tool", ChatMessageRole.Tool)]
    public void ToOpenClawMessage_MapsRoles_Correctly(string meaiRole, ChatMessageRole expectedOcRole)
    {
        var role = new ChatRole(meaiRole);
        var meaiMsg = new MEAIChatMessage(role, "test content");

        var ocMsg = ModelClientChatClientAdapter.ToOpenClawMessage(meaiMsg);

        ocMsg.Role.Should().Be(expectedOcRole);
        ocMsg.Content.Should().Be("test content");
    }

    [Fact]
    public void ToOpenClawMessage_ExtractsFunctionCallContent()
    {
        var meaiMsg = new MEAIChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("call_1", "my_tool", new Dictionary<string, object?> { ["key"] = "value" })
        ]);

        var ocMsg = ModelClientChatClientAdapter.ToOpenClawMessage(meaiMsg);

        ocMsg.ToolCalls.Should().NotBeNull();
        ocMsg.ToolCalls.Should().ContainSingle(tc => tc.Name == "my_tool" && tc.Id == "call_1");
    }

    [Fact]
    public void PropagatesFunctionResultContent_StringResult()
    {
        // Arrange: tool role message with FunctionResultContent containing a string result
        var meaiMsg = new MEAIChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call_1", "tool output result")
        ]);

        // Act
        var ocMsg = ModelClientChatClientAdapter.ToOpenClawMessage(meaiMsg);

        // Assert: string result is propagated as content
        ocMsg.Role.Should().Be(ChatMessageRole.Tool);
        ocMsg.ToolCallId.Should().Be("call_1");
        ocMsg.Content.Should().Be("tool output result");
    }

    [Fact]
    public void PropagatesFunctionResultContent_ObjectResult()
    {
        // Arrange: tool role message with FunctionResultContent containing an object result
        var resultObject = new { data = "test", count = 42 };
        var meaiMsg = new MEAIChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call_2", resultObject)
        ]);

        // Act
        var ocMsg = ModelClientChatClientAdapter.ToOpenClawMessage(meaiMsg);

        // Assert: object result is ToString()'d and propagated as content
        ocMsg.Role.Should().Be(ChatMessageRole.Tool);
        ocMsg.ToolCallId.Should().Be("call_2");
        ocMsg.Content.Should().Contain("data");
        ocMsg.Content.Should().Contain("test");
    }

    [Fact]
    public void PropagatesFunctionResultContent_NullResult()
    {
        // Arrange: tool role message with FunctionResultContent where Result is null
        var meaiMsg = new MEAIChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call_3", null)
        ]);

        // Act
        var ocMsg = ModelClientChatClientAdapter.ToOpenClawMessage(meaiMsg);

        // Assert: null result falls back to empty string without exception
        ocMsg.Role.Should().Be(ChatMessageRole.Tool);
        ocMsg.ToolCallId.Should().Be("call_3");
        ocMsg.Content.Should().Be(string.Empty);
    }

    [Fact]
    public void ParseArguments_HandlesEmptyString()
    {
        var result = ModelClientChatClientAdapter.ParseArguments("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseArguments_HandlesInvalidJson()
    {
        var result = ModelClientChatClientAdapter.ParseArguments("not json");
        result.Should().ContainKey("raw");
    }

    [Fact]
    public void ParseArguments_HandlesValidJson()
    {
        var result = ModelClientChatClientAdapter.ParseArguments("""{"name":"test","count":42}""");
        result.Should().ContainKey("name");
        result.Should().ContainKey("count");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<ChatResponseChunk> StreamChunks(
        params ChatResponseChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}
