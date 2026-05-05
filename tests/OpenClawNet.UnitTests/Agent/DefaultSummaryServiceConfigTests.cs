using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Memory;
using OpenClawNet.Models.Abstractions;
using Xunit;

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Issue #107 — DefaultSummaryService must source the summarizer model name from
/// <see cref="SummaryOptions"/> instead of the previous hard-coded <c>"llama3.2"</c>
/// literal. These tests pin the new wiring so a future regression cannot silently
/// re-introduce the literal.
/// </summary>
[Trait("Category", "Unit")]
public class DefaultSummaryServiceConfigTests
{
    [Fact]
    public void SummaryOptions_DefaultModel_IsLlama32()
    {
        // Repo rule: default MUST stay "llama3.2" exactly — no tagged variants.
        new SummaryOptions().Model.Should().Be("llama3.2");
    }

    [Fact]
    public async Task LocalFallback_UsesConfiguredModel_NotHardCodedLlama32()
    {
        var memoryService = new Mock<IMemoryService>();
        memoryService.Setup(m => m.GetSessionSummaryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((string?)null);
        memoryService.Setup(m => m.StoreSummaryAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        ChatRequest? capturedRequest = null;
        var modelClient = new Mock<IModelClient>();
        modelClient.Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
                   .Callback<ChatRequest, CancellationToken>((req, _) => capturedRequest = req)
                   .ReturnsAsync(new ChatResponse
                   {
                       Content = "summary text",
                       Role = ChatMessageRole.Assistant,
                       Model = "irrelevant"
                   });

        // HttpClientFactory whose HttpClient always fails -> forces local fallback.
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(() => new HttpClient(new FailingHandler()) { BaseAddress = new Uri("http://memory.invalid") });

        var configuredModel = "qwen2.5";
        var options = Options.Create(new SummaryOptions { Model = configuredModel });

        var sut = new DefaultSummaryService(
            memoryService.Object,
            modelClient.Object,
            httpFactory.Object,
            options,
            NullLogger<DefaultSummaryService>.Instance);

        var messages = Enumerable.Range(0, 25)
            .Select(i => new ChatMessage { Role = ChatMessageRole.User, Content = $"msg-{i}" })
            .ToList();

        var result = await sut.SummarizeIfNeededAsync(Guid.NewGuid(), messages);

        result.Should().Be("summary text");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Model.Should().Be(configuredModel,
            "DefaultSummaryService must forward SummaryOptions.Model to the IModelClient instead of the legacy hard-coded literal");
        capturedRequest.Model.Should().NotBe("llama3.2",
            "configured Summary:Model must override the default — regression guard for issue #107");
    }

    [Fact]
    public async Task LocalFallback_FallsBackToDefault_WhenConfiguredModelIsBlank()
    {
        var memoryService = new Mock<IMemoryService>();
        memoryService.Setup(m => m.GetSessionSummaryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((string?)null);
        memoryService.Setup(m => m.StoreSummaryAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        ChatRequest? capturedRequest = null;
        var modelClient = new Mock<IModelClient>();
        modelClient.Setup(c => c.CompleteAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
                   .Callback<ChatRequest, CancellationToken>((req, _) => capturedRequest = req)
                   .ReturnsAsync(new ChatResponse
                   {
                       Content = "summary",
                       Role = ChatMessageRole.Assistant,
                       Model = "irrelevant"
                   });

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(() => new HttpClient(new FailingHandler()) { BaseAddress = new Uri("http://memory.invalid") });

        // Operator misconfiguration: blank Model — service must defend with the canonical default.
        var options = Options.Create(new SummaryOptions { Model = "   " });

        var sut = new DefaultSummaryService(
            memoryService.Object, modelClient.Object, httpFactory.Object, options, NullLogger<DefaultSummaryService>.Instance);

        var messages = Enumerable.Range(0, 25)
            .Select(i => new ChatMessage { Role = ChatMessageRole.User, Content = $"msg-{i}" })
            .ToList();

        await sut.SummarizeIfNeededAsync(Guid.NewGuid(), messages);

        capturedRequest!.Model.Should().Be("llama3.2");
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("memory service unavailable (test)");
    }
}
