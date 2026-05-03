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
    public void ToOpenClawMessage_PropagatesFunctionResultContent_StringResult()
    {
        // Regression for elbruno/openclawnet-plan#115:
        // FunctionResultContent.Result must flow into OCChatMessage.Content so that
        // tool results are not silently dropped on the round-trip into IModelClient.
        var meaiMsg = new MEAIChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call_42", "the weather in Seattle is 12C and rainy")
        ]);

        var ocMsg = ModelClientChatClientAdapter.ToOpenClawMessage(meaiMsg);

        ocMsg.Role.Should().Be(ChatMessageRole.Tool);
        ocMsg.ToolCallId.Should().Be("call_42");
        ocMsg.Content.Should().Be("the weather in Seattle is 12C and rainy");
    }

    [Fact]
    public void ToOpenClawMessage_PropagatesFunctionResultContent_ObjectResult()
    {
        // Non-string Result values must be JSON-serialized into Content rather than dropped.
        var payload = new Dictionary<string, object?> { ["city"] = "Seattle", ["tempC"] = 12 };
        var meaiMsg = new MEAIChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call_43", payload)
        ]);

        var ocMsg = ModelClientChatClientAdapter.ToOpenClawMessage(meaiMsg);

        ocMsg.ToolCallId.Should().Be("call_43");
        ocMsg.Content.Should().Contain("Seattle").And.Contain("tempC");
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
