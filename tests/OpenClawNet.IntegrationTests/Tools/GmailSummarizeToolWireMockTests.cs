using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GoogleWorkspace;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace OpenClawNet.IntegrationTests.Tools;

/// <summary>
/// Integration tests for GmailSummarizeTool with WireMock (S5-7).
/// Tests full HTTP pipeline to mocked Gmail API endpoints.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class GmailSummarizeToolWireMockTests : IAsyncLifetime
{
    private WireMockServer? _wireMockServer;

    public Task InitializeAsync()
    {
        _wireMockServer = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _wireMockServer?.Stop();
        _wireMockServer?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GmailSummarizeTool_Successful_Fetch_Returns_Message_Summary()
    {
        _wireMockServer.Should().NotBeNull();

        _wireMockServer!.Given(
            Request.Create()
                .WithPath("/gmail/v1/users/me/messages")
                .WithParam("q", "is:unread")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "messages": [
                            { "id": "msg1", "threadId": "thread1" },
                            { "id": "msg2", "threadId": "thread2" }
                        ],
                        "resultSizeEstimate": 2
                    }
                    """));

        _wireMockServer.Given(
            Request.Create()
                .WithPath("/gmail/v1/users/me/messages/msg1")
                .WithParam("format", "metadata")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "id": "msg1",
                        "threadId": "thread1",
                        "payload": {
                            "headers": [
                                { "name": "From", "value": "alice@example.com" },
                                { "name": "Subject", "value": "Test Email 1" },
                                { "name": "Date", "value": "Mon, 1 May 2026 10:00:00 +0000" }
                            ]
                        }
                    }
                    """));

        _wireMockServer.Given(
            Request.Create()
                .WithPath("/gmail/v1/users/me/messages/msg2")
                .WithParam("format", "metadata")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "id": "msg2",
                        "threadId": "thread2",
                        "payload": {
                            "headers": [
                                { "name": "From", "value": "bob@example.com" },
                                { "name": "Subject", "value": "Test Email 2" },
                                { "name": "Date", "value": "Mon, 1 May 2026 11:00:00 +0000" }
                            ]
                        }
                    }
                    """));

        var tool = await CreateToolAsync("https://www.googleapis.com/auth/gmail.readonly");

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = "gmail_summarize",
            RawArguments = """{ "userId": "testuser", "maxResults": 10 }"""
        });

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain("alice@example.com");
        result.Output.Should().Contain("Test Email 1");
        result.Output.Should().Contain("bob@example.com");
        result.Output.Should().Contain("Test Email 2");
        result.Output.Should().Contain("2 unread message");

        _wireMockServer.LogEntries.Count(e => e.RequestMessage.Path == "/gmail/v1/users/me/messages").Should().Be(1);
        _wireMockServer.LogEntries.Count(e => e.RequestMessage.Path?.StartsWith("/gmail/v1/users/me/messages/msg", StringComparison.Ordinal) == true).Should().Be(2);
    }

    [Fact]
    public async Task GmailSummarizeTool_401_Unauthorized_Returns_Error_ToolResult()
    {
        _wireMockServer.Should().NotBeNull();

        _wireMockServer!.Given(
            Request.Create()
                .WithPath("/gmail/v1/users/me/messages")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(401)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "error": {
                            "code": 401,
                            "message": "Invalid Credentials",
                            "status": "UNAUTHENTICATED"
                        }
                    }
                    """));

        var tool = await CreateToolAsync("https://www.googleapis.com/auth/gmail.readonly", refreshToken: "");

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = "gmail_summarize",
            RawArguments = """{ "userId": "testuser" }"""
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("authorization failed");
        result.Error.Should().Contain("re-authorize");
    }

    private async Task<GmailSummarizeTool> CreateToolAsync(string scopes, string refreshToken = "test_refresh_token")
    {
        var tokenStore = new InMemoryGoogleOAuthTokenStore();
        await tokenStore.SaveTokenAsync("testuser", new GoogleTokenSet(
            AccessToken: "test_access_token",
            RefreshToken: refreshToken,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: scopes), CancellationToken.None);

        var handler = new HttpClientHandler();
        var factory = new GoogleClientFactory(
            tokenStore,
            Options.Create(new GoogleWorkspaceOptions
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret"
            }),
            NullLogger<GoogleClientFactory>.Instance,
            new StubHttpClientFactory(handler),
            handler,
            new Uri(_wireMockServer!.Url! + "/"));

        return new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
