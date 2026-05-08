using System.Net;
using System.Text.Json;
using Google;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GoogleWorkspace;
using Xunit;

namespace OpenClawNet.UnitTests.Tools;

/// <summary>
/// Unit tests for GmailSummarizeTool (S5-7).
/// Validates metadata, input validation, OAuth error handling, HTTP behavior, and logging.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Layer", "Unit")]
public sealed class GmailSummarizeToolUnitTests
{
    private static ToolInput Args(string json) => new()
    {
        ToolName = "gmail_summarize",
        RawArguments = json
    };

    [Fact]
    public void Metadata_Has_Correct_Name_And_Description()
    {
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);

        var metadata = tool.Metadata;

        Assert.Equal("gmail_summarize", metadata.Name);
        Assert.Contains("Gmail", metadata.Description);
        Assert.Contains("unread", metadata.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Metadata_RequiresApproval_Is_False()
    {
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);

        var metadata = tool.Metadata;

        Assert.False(metadata.RequiresApproval, "Read-only Gmail access should not require approval");
    }

    [Fact]
    public void Metadata_Has_Integration_Category()
    {
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);

        var metadata = tool.Metadata;

        Assert.Equal("integration", metadata.Category);
    }

    [Fact]
    public void Metadata_Parameter_Schema_Has_Required_UserId()
    {
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);

        var root = tool.Metadata.ParameterSchema.RootElement;
        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.Contains("userId", required);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_UserId_Returns_Error()
    {
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);

        var result = await tool.ExecuteAsync(Args("""{ "maxResults": 10 }"""));

        Assert.False(result.Success);
        Assert.Contains("userId", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{ "userId": "testuser", "maxResults": 51 }""", "50")]
    [InlineData("""{ "userId": "testuser", "maxResults": 0 }""", "1")]
    [InlineData("""{ "userId": "testuser", "maxResults": -5 }""", "1")]
    public async Task ExecuteAsync_Invalid_MaxResults_Clamped_To_Valid_Range(string json, string expectedMaxResults)
    {
        var handler = new StubGoogleHandler(request =>
        {
            Assert.Equal("/gmail/v1/users/me/messages", request.RequestUri!.AbsolutePath);
            Assert.Contains($"maxResults={expectedMaxResults}", request.RequestUri.Query);
            return JsonResponse("""{ "messages": [], "resultSizeEstimate": 0 }""");
        });
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Args(json));

        Assert.True(result.Success, result.Error);
        Assert.Contains("No unread messages", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Query_Without_IsUnread_Returns_Error()
    {
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);

        var result = await tool.ExecuteAsync(Args("""{ "userId": "testuser", "query": "from:alice@example.com" }"""));

        Assert.False(result.Success);
        Assert.Contains("is:unread", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must include", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Successful_Fetch_Returns_Summary()
    {
        var handler = new StubGoogleHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/gmail/v1/users/me/messages" => JsonResponse("""
            {
              "messages": [
                { "id": "msg1", "threadId": "thread1" },
                { "id": "msg2", "threadId": "thread2" }
              ],
              "resultSizeEstimate": 2
            }
            """),
            "/gmail/v1/users/me/messages/msg1" => JsonResponse(MessageJson("msg1", "alice@example.com", "Test Email 1", "Mon, 1 May 2026 10:00:00 +0000")),
            "/gmail/v1/users/me/messages/msg2" => JsonResponse(MessageJson("msg2", "bob@example.com", "Test Email 2", "Mon, 1 May 2026 11:00:00 +0000")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Args("""{ "userId": "testuser", "maxResults": 10 }"""));

        Assert.True(result.Success, result.Error);
        Assert.Contains("alice@example.com", result.Output);
        Assert.Contains("Test Email 1", result.Output);
        Assert.Contains("Mon, 1 May 2026 10:00:00 +0000", result.Output);
        Assert.Contains("bob@example.com", result.Output);
        Assert.Contains("Test Email 2", result.Output);
        Assert.Contains("2 unread message", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_OAuthRequiredException_Returns_User_Friendly_Error()
    {
        var mockFactory = new Mock<IGoogleClientFactory>();
        mockFactory
            .Setup(f => f.CreateGmailServiceAsync("testuser", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OAuthRequiredException("testuser", "User has not authorized Google."));

        var tool = new GmailSummarizeTool(mockFactory.Object, NullLogger<GmailSummarizeTool>.Instance);

        var result = await tool.ExecuteAsync(Args("""{ "userId": "testuser" }"""));

        Assert.False(result.Success);
        Assert.Contains("authorization required", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User has not authorized Google", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_GoogleApiException_401_Returns_Reauthorize_Message()
    {
        var handler = new StubGoogleHandler(_ => JsonResponse("""
        {
          "error": {
            "code": 401,
            "message": "Invalid Credentials",
            "status": "UNAUTHENTICATED"
          }
        }
        """, HttpStatusCode.Unauthorized));
        var tool = CreateTool(handler, token: new GoogleTokenSet(
            AccessToken: "test_access_token",
            RefreshToken: "",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly"));

        var result = await tool.ExecuteAsync(Args("""{ "userId": "testuser" }"""));

        Assert.False(result.Success);
        Assert.Contains("authorization failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("re-authorize", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Logs_No_Message_Body_Content()
    {
        var handler = new StubGoogleHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/gmail/v1/users/me/messages" => JsonResponse("""{ "messages": [{ "id": "msg1" }], "resultSizeEstimate": 1 }"""),
            "/gmail/v1/users/me/messages/msg1" => JsonResponse("""
            {
              "id": "msg1",
              "payload": {
                "headers": [
                  { "name": "From", "value": "alice@example.com" },
                  { "name": "Subject", "value": "Sensitive Subject" },
                  { "name": "Date", "value": "Mon, 1 May 2026 10:00:00 +0000" }
                ],
                "body": { "data": "U2Vuc2l0aXZlIGJvZHkgY29udGVudA==" }
              }
            }
            """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Args("""{ "userId": "testuser" }"""));

        Assert.True(result.Success, result.Error);
        Assert.DoesNotContain("Sensitive body content", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Expired_Token_Refreshes_Through_Injected_Handler()
    {
        var tokenStore = new InMemoryGoogleOAuthTokenStore();
        await tokenStore.SaveTokenAsync("testuser", new GoogleTokenSet(
            AccessToken: "expired_access_token",
            RefreshToken: "test_refresh_token",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly"), CancellationToken.None);

        var handler = new StubGoogleHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                return JsonResponse("""
                {
                  "access_token": "refreshed_access_token",
                  "expires_in": 3600,
                  "scope": "https://www.googleapis.com/auth/gmail.readonly"
                }
                """);
            }

            Assert.Equal("Bearer refreshed_access_token", request.Headers.Authorization?.ToString());
            return JsonResponse("""{ "messages": [], "resultSizeEstimate": 0 }""");
        });
        var tool = CreateTool(handler, tokenStore: tokenStore);

        var result = await tool.ExecuteAsync(Args("""{ "userId": "testuser" }"""));

        Assert.True(result.Success, result.Error);
        Assert.Contains("No unread messages", result.Output);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsoluteUri == "https://oauth2.googleapis.com/token");

        var refreshedToken = await tokenStore.GetTokenAsync("testuser", CancellationToken.None);
        Assert.Equal("refreshed_access_token", refreshedToken?.AccessToken);
    }

    private static GmailSummarizeTool CreateTool(
        HttpMessageHandler handler,
        GoogleTokenSet? token = null,
        InMemoryGoogleOAuthTokenStore? tokenStore = null)
    {
        tokenStore ??= new InMemoryGoogleOAuthTokenStore();
        token ??= new GoogleTokenSet(
            AccessToken: "test_access_token",
            RefreshToken: "test_refresh_token",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/gmail.readonly");

        if (tokenStore.GetTokenAsync("testuser", CancellationToken.None).GetAwaiter().GetResult() is null)
        {
            tokenStore.SaveTokenAsync("testuser", token, CancellationToken.None).GetAwaiter().GetResult();
        }

        var factory = new GoogleClientFactory(
            tokenStore,
            Options.Create(new GoogleWorkspaceOptions
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret"
            }),
            NullLogger<GoogleClientFactory>.Instance,
            new StubHttpClientFactory(handler),
            handler);

        return new GmailSummarizeTool(factory, NullLogger<GmailSummarizeTool>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private static string MessageJson(string id, string from, string subject, string date) => $$"""
    {
      "id": "{{id}}",
      "payload": {
        "headers": [
          { "name": "From", "value": "{{from}}" },
          { "name": "Subject", "value": "{{subject}}" },
          { "name": "Date", "value": "{{date}}" }
        ]
      }
    }
    """;

    private sealed class StubGoogleHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
