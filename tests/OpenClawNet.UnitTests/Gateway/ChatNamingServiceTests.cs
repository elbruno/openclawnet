using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Gateway;

public sealed class ChatNamingServiceTests
{
    [Fact]
    public async Task GenerateNameAsync_MathConversation_ReplacesGenericModelTitle()
    {
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                Content = "New Chat",
                Role = ChatMessageRole.Assistant,
                Model = "test-model"
            });

        var service = new ChatNamingService(modelClient.Object, NullLogger<ChatNamingService>.Instance);
        var title = await service.GenerateNameAsync([
            new ChatMessageEntity { Role = "user", Content = "Can you solve math problems?" },
            new ChatMessageEntity { Role = "assistant", Content = "Yes, send me one." },
            new ChatMessageEntity { Role = "user", Content = "What is 2 + 8?" },
            new ChatMessageEntity { Role = "assistant", Content = "2 + 8 = 10." }
        ]);

        title.Should().Be("Math Problem Solving");
    }

    [Fact]
    public async Task GenerateNameAsync_NonMathConversation_ReplacesGenericModelTitle()
    {
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                Content = "New Chat",
                Role = ChatMessageRole.Assistant,
                Model = "test-model"
            });

        var service = new ChatNamingService(modelClient.Object, NullLogger<ChatNamingService>.Instance);
        var title = await service.GenerateNameAsync([
            new ChatMessageEntity { Role = "user", Content = "Let's plan a weekend trip to the coast." },
            new ChatMessageEntity { Role = "assistant", Content = "Sounds good." },
            new ChatMessageEntity { Role = "user", Content = "We need hotels and food ideas." }
        ]);

        title.Should().Be("Mixed Topic Discussion");
    }

    [Fact]
    public async Task GenerateNameAsync_TrimsQuotedModelTitles()
    {
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                Content = "  \"Python For Beginners\"  ",
                Role = ChatMessageRole.Assistant,
                Model = "test-model"
            });

        var service = new ChatNamingService(modelClient.Object, NullLogger<ChatNamingService>.Instance);
        var title = await service.GenerateNameAsync([
            new ChatMessageEntity { Role = "user", Content = "What should I learn first in Python?" },
            new ChatMessageEntity { Role = "assistant", Content = "Start with variables and loops." }
        ]);

        title.Should().Be("Python For Beginners");
    }

    [Fact]
    public async Task GenerateNameAsync_CollapsesWhitespaceBeforePersisting()
    {
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                Content = "  Math\n   Problem\tSolving  ",
                Role = ChatMessageRole.Assistant,
                Model = "test-model"
            });

        var service = new ChatNamingService(modelClient.Object, NullLogger<ChatNamingService>.Instance);
        var title = await service.GenerateNameAsync([
            new ChatMessageEntity { Role = "user", Content = "Can you solve math problems?" },
            new ChatMessageEntity { Role = "assistant", Content = "Yes, send me one." }
        ]);

        title.Should().Be("Math Problem Solving");
    }

    [Fact]
    public async Task GenerateNameAsync_CapsTitlesAtEightWords()
    {
        var modelClient = new Mock<IModelClient>();
        modelClient
            .Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse
            {
                Content = "Planning A Scenic Weekend Trip Along The Oregon Coast Together",
                Role = ChatMessageRole.Assistant,
                Model = "test-model"
            });

        var service = new ChatNamingService(modelClient.Object, NullLogger<ChatNamingService>.Instance);
        var title = await service.GenerateNameAsync([
            new ChatMessageEntity { Role = "user", Content = "Let's plan a scenic weekend trip." },
            new ChatMessageEntity { Role = "assistant", Content = "Sure, where do you want to go?" }
        ]);

        title.Should().Be("Planning A Scenic Weekend Trip Along The Oregon");
    }

    [Fact]
    public async Task GenerateNameAsync_WhenNoMessages_ReturnsNewChat()
    {
        var modelClient = new Mock<IModelClient>();
        var service = new ChatNamingService(modelClient.Object, NullLogger<ChatNamingService>.Instance);

        var title = await service.GenerateNameAsync([]);

        title.Should().Be("New Chat");
    }
}
