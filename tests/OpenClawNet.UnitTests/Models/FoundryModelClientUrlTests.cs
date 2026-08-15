using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Foundry;

namespace OpenClawNet.UnitTests.Models;

/// <summary>
/// Regression tests for Issue #223: FoundryModelClient with project-based or
/// path-based endpoints (e.g. /api/projects/proj-default or /openai/v1) must send
/// HTTP requests to the correct full URL, not strip the path from BaseAddress.
/// </summary>
public sealed class FoundryModelClientUrlTests
{
    // ── URL construction: BaseAddress + relative path ─────────────────────────

    [Theory]
    [InlineData(
        "https://foundry.services.ai.azure.com/api/projects/proj-default",
        "https://foundry.services.ai.azure.com/api/projects/proj-default/chat/completions")]
    [InlineData(
        "https://foundry.services.ai.azure.com/api/projects/proj-default/",
        "https://foundry.services.ai.azure.com/api/projects/proj-default/chat/completions")]
    [InlineData(
        "https://resource.openai.azure.com/openai/v1",
        "https://resource.openai.azure.com/openai/v1/chat/completions")]
    [InlineData(
        "https://resource.openai.azure.com/openai/v1/",
        "https://resource.openai.azure.com/openai/v1/chat/completions")]
    [InlineData(
        "http://localhost:11434",
        "http://localhost:11434/chat/completions")]
    public async Task CompleteAsync_PostsToCorrectChatCompletionsUrl(string endpoint, string expectedUrl)
    {
        // Issue #223: HttpClient relative paths with leading slash discard the BaseAddress
        // path component. "chat/completions" (no leading slash) + trailing-slash BaseAddress
        // must resolve to the full endpoint URL.
        Uri? capturedUri = null;

        var handler = new CapturingHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FakeChatCompletionJson(), Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var opts = Options.Create(new FoundryOptions
        {
            Endpoint = endpoint,
            ApiKey = "test-key",
            Model = "phi-4-reasoning",
        });

        var client = new FoundryModelClient(http, opts, NullLogger<FoundryModelClient>.Instance);

        var request = new OpenClawNet.Models.Abstractions.ChatRequest
        {
            Messages =
            [
                new() { Role = OpenClawNet.Models.Abstractions.ChatMessageRole.User, Content = "Hi" }
            ]
        };

        await client.CompleteAsync(request);

        capturedUri.Should().NotBeNull();
        capturedUri!.AbsoluteUri.Should().Be(expectedUrl,
            "endpoint path must be preserved when resolving chat/completions route");
    }

    [Theory]
    [InlineData(
        "https://foundry.services.ai.azure.com/api/projects/proj-default",
        "https://foundry.services.ai.azure.com/api/projects/proj-default/models")]
    [InlineData(
        "https://resource.openai.azure.com/openai/v1",
        "https://resource.openai.azure.com/openai/v1/models")]
    public async Task IsAvailableAsync_GetsCorrectModelsUrl(string endpoint, string expectedUrl)
    {
        Uri? capturedUri = null;

        var handler = new CapturingHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var opts = Options.Create(new FoundryOptions
        {
            Endpoint = endpoint,
            ApiKey = "test-key",
            Model = "phi-4-reasoning",
        });

        var client = new FoundryModelClient(http, opts, NullLogger<FoundryModelClient>.Instance);
        await client.IsAvailableAsync();

        capturedUri.Should().NotBeNull();
        capturedUri!.AbsoluteUri.Should().Be(expectedUrl,
            "endpoint path must be preserved when resolving models route");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FakeChatCompletionJson() =>
        JsonSerializer.Serialize(new
        {
            model = "phi-4-reasoning",
            choices = new[]
            {
                new
                {
                    message = new { role = "assistant", content = "Hello" },
                    finish_reason = "stop"
                }
            },
            usage = new { prompt_tokens = 5, completion_tokens = 1, total_tokens = 6 }
        });

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
