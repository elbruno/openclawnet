using FluentAssertions;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Tests that verify FakeAssertingToolCallingModelClient correctly validates
/// tool message handling in the second and subsequent calls.
///
/// This test class directly instantiates the fake client to test its validation logic
/// without needing a full integration test setup.
/// </summary>
public sealed class FakeAssertingToolCallingModelClientTests
{
    [Fact]
    public async Task CompleteAsync_FirstCall_ReturnsToolCallRequest()
    {
        var client = new FakeAssertingToolCallingModelClient();
        var request = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };

        var response = await client.CompleteAsync(request);

        response.Content.Should().BeEmpty("first call should have empty content");
        response.ToolCalls.Should().NotBeNull("first call should return tool calls");
        response.ToolCalls!.Count.Should().Be(1, "should return one tool call");
        response.ToolCalls[0].Name.Should().Be("list_files");
    }

    [Fact]
    public async Task CompleteAsync_SecondCall_WithValidToolMessage_ReturnsFinalAnswer()
    {
        var client = new FakeAssertingToolCallingModelClient();
        
        // First call
        var firstRequest = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };
        await client.CompleteAsync(firstRequest);

        // Second call with valid tool message
        var secondRequest = new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.User, Content = "List files" },
                new ChatMessage { Role = ChatMessageRole.Assistant, Content = "", ToolCalls = [new ToolCall { Id = "call_1", Name = "list_files", Arguments = "" }] },
                new ChatMessage { Role = ChatMessageRole.Tool, Content = "file1.txt\nfile2.txt", ToolCallId = "call_1" },
            ],
        };

        var response = await client.CompleteAsync(secondRequest);

        response.Content.Should().Be("Final answer after tool execution validation passed.", "second call should return final answer");
        response.ToolCalls.Should().BeNull("second call should not return tool calls");
    }

    [Fact]
    public async Task CompleteAsync_SecondCall_WithoutToolMessage_ThrowsException()
    {
        var client = new FakeAssertingToolCallingModelClient();
        
        // First call
        var firstRequest = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };
        await client.CompleteAsync(firstRequest);

        // Second call WITHOUT tool message (assertion should fail)
        var secondRequest = new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.User, Content = "List files" },
                new ChatMessage { Role = ChatMessageRole.Assistant, Content = "", ToolCalls = [new ToolCall { Id = "call_1", Name = "list_files", Arguments = "" }] },
                // Missing tool message here
            ],
        };

        Func<Task> act = () => client.CompleteAsync(secondRequest);
        
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Expected at least one Tool-role message*");
    }

    [Fact]
    public async Task CompleteAsync_SecondCall_WithEmptyToolContent_ThrowsException()
    {
        var client = new FakeAssertingToolCallingModelClient();
        
        // First call
        var firstRequest = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };
        await client.CompleteAsync(firstRequest);

        // Second call with empty tool message content
        var secondRequest = new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.User, Content = "List files" },
                new ChatMessage { Role = ChatMessageRole.Assistant, Content = "", ToolCalls = [new ToolCall { Id = "call_1", Name = "list_files", Arguments = "" }] },
                new ChatMessage { Role = ChatMessageRole.Tool, Content = "", ToolCallId = "call_1" },  // Empty content!
            ],
        };

        Func<Task> act = () => client.CompleteAsync(secondRequest);
        
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*with empty or whitespace-only content*");
    }

    [Fact]
    public async Task CompleteAsync_SecondCall_WithWhitespaceToolContent_ThrowsException()
    {
        var client = new FakeAssertingToolCallingModelClient();
        
        // First call
        var firstRequest = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };
        await client.CompleteAsync(firstRequest);

        // Second call with whitespace-only tool message content
        var secondRequest = new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.User, Content = "List files" },
                new ChatMessage { Role = ChatMessageRole.Assistant, Content = "", ToolCalls = [new ToolCall { Id = "call_1", Name = "list_files", Arguments = "" }] },
                new ChatMessage { Role = ChatMessageRole.Tool, Content = "   \t\n", ToolCallId = "call_1" },  // Whitespace only!
            ],
        };

        Func<Task> act = () => client.CompleteAsync(secondRequest);
        
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*with empty or whitespace-only content*");
    }

    [Fact]
    public async Task StreamAsync_FirstCall_ReturnsToolCallChunks()
    {
        var client = new FakeAssertingToolCallingModelClient();
        var request = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };

        var chunks = new List<ChatResponseChunk>();
        await foreach (var chunk in client.StreamAsync(request))
        {
            chunks.Add(chunk);
        }

        chunks.Should().NotBeEmpty("first call should yield chunks");
        chunks[0].ToolCalls.Should().NotBeNull("first chunk should contain tool calls");
    }

    [Fact]
    public async Task StreamAsync_SecondCall_WithValidToolMessage_ReturnsFinalAnswer()
    {
        var client = new FakeAssertingToolCallingModelClient();
        
        // First call
        var firstRequest = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };
        await foreach (var _ in client.StreamAsync(firstRequest))
        {
            // Consume stream
        }

        // Second call with valid tool message
        var secondRequest = new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.User, Content = "List files" },
                new ChatMessage { Role = ChatMessageRole.Assistant, Content = "", ToolCalls = [new ToolCall { Id = "call_1", Name = "list_files", Arguments = "" }] },
                new ChatMessage { Role = ChatMessageRole.Tool, Content = "file1.txt\nfile2.txt", ToolCallId = "call_1" },
            ],
        };

        var chunks = new List<ChatResponseChunk>();
        await foreach (var chunk in client.StreamAsync(secondRequest))
        {
            chunks.Add(chunk);
        }

        chunks.Should().NotBeEmpty("second call should yield chunks");
        var contentChunk = chunks.FirstOrDefault(c => c.Content != null);
        contentChunk?.Content.Should().Contain("Final answer after tool execution validation passed.");
    }

    [Fact]
    public async Task StreamAsync_SecondCall_WithoutToolMessage_ThrowsException()
    {
        var client = new FakeAssertingToolCallingModelClient();
        
        // First call
        var firstRequest = new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "List files" }],
        };
        await foreach (var _ in client.StreamAsync(firstRequest))
        {
            // Consume stream
        }

        // Second call WITHOUT tool message (assertion should fail)
        var secondRequest = new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.User, Content = "List files" },
                new ChatMessage { Role = ChatMessageRole.Assistant, Content = "", ToolCalls = [new ToolCall { Id = "call_1", Name = "list_files", Arguments = "" }] },
                // Missing tool message here
            ],
        };

        Func<Task> act = async () =>
        {
            await foreach (var _ in client.StreamAsync(secondRequest))
            {
                // Consume stream
            }
        };
        
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Expected at least one Tool-role message*");
    }
}
