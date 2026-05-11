using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GoogleWorkspace;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace OpenClawNet.IntegrationTests.Tools;

/// <summary>
/// Integration tests for CalendarCreateEventTool with WireMock (S5-7).
/// Tests full HTTP pipeline to mocked Calendar API endpoints.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class CalendarCreateEventToolWireMockTests : IAsyncLifetime
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
    public async Task CalendarCreateEventTool_Successful_Creation_Returns_HtmlLink()
    {
        _wireMockServer.Should().NotBeNull();

        _wireMockServer!.Given(
            Request.Create()

                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "id": "event123",
                        "summary": "Team Meeting",
                        "htmlLink": "https://calendar.google.com/event?eid=event123",
                        "start": {
                            "dateTime": "2026-05-07T10:00:00Z",
                            "timeZone": "UTC"
                        },
                        "end": {
                            "dateTime": "2026-05-07T11:00:00Z",
                            "timeZone": "UTC"
                        },
                        "status": "confirmed",
                        "created": "2026-05-06T12:00:00Z",
                        "updated": "2026-05-06T12:00:00Z"
                    }
                    """));

        var tool = await CreateToolAsync();

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = "calendar_create_event",
            RawArguments = """
            {
              "userId": "testuser",
              "summary": "Team Meeting",
              "startUtc": "2026-05-07T10:00:00Z",
              "endUtc": "2026-05-07T11:00:00Z"
            }
            """
        });

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain("Calendar event created successfully");
        result.Output.Should().Contain("Team Meeting");
        result.Output.Should().Contain("https://calendar.google.com/event?eid=event123");

        _wireMockServer.LogEntries.Count(e => e.RequestMessage.Method == "POST")
            .Should().Be(1);
    }

    [Fact]
    public async Task CalendarCreateEventTool_403_Forbidden_Returns_Error_ToolResult()
    {
        _wireMockServer.Should().NotBeNull();

        _wireMockServer!.Given(
            Request.Create()

                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(403)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "error": {
                            "code": 403,
                            "message": "Insufficient Permission",
                            "status": "PERMISSION_DENIED"
                        }
                    }
                    """));

        var tool = await CreateToolAsync();

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = "calendar_create_event",
            RawArguments = """
            {
              "userId": "testuser",
              "summary": "Team Meeting",
              "startUtc": "2026-05-07T10:00:00Z"
            }
            """
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("forbidden");
    }

    [Fact]
    public async Task CalendarCreateEventTool_Validates_Request_Body_Structure()
    {
        _wireMockServer.Should().NotBeNull();

        _wireMockServer!.Given(
            Request.Create()

                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "id": "event123",
                        "summary": "Meeting with Attendees",
                        "htmlLink": "https://calendar.google.com/event?eid=event123",
                        "start": { "dateTime": "2026-05-07T10:00:00Z", "timeZone": "UTC" },
                        "end": { "dateTime": "2026-05-07T11:00:00Z", "timeZone": "UTC" }
                    }
                    """));

        var tool = await CreateToolAsync();

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = "calendar_create_event",
            RawArguments = """
            {
              "userId": "testuser",
              "summary": "Meeting with Attendees",
              "startUtc": "2026-05-07T10:00:00Z",
              "endUtc": "2026-05-07T11:00:00Z",
              "attendees": ["alice@example.com", "bob@example.com"],
              "description": "Discuss launch plan",
              "location": "Conference Room A",
              "timeZone": "UTC"
            }
            """
        });

        result.Success.Should().BeTrue(result.Error);

        var request = _wireMockServer.LogEntries
            .Single(e => e.RequestMessage.Method == "POST");

        request.RequestMessage.Body.Should().NotBeNullOrWhiteSpace();
        using var bodyJson = JsonDocument.Parse(request.RequestMessage.Body!);
        var root = bodyJson.RootElement;

        root.GetProperty("summary").GetString().Should().Be("Meeting with Attendees");
        root.GetProperty("description").GetString().Should().Be("Discuss launch plan");
        root.GetProperty("location").GetString().Should().Be("Conference Room A");
        var startInstant = DateTimeOffset.Parse(root.GetProperty("start").GetProperty("dateTime").GetString()!);
        var endInstant = DateTimeOffset.Parse(root.GetProperty("end").GetProperty("dateTime").GetString()!);
        startInstant.UtcDateTime.Should().Be(DateTime.Parse("2026-05-07T10:00:00Z").ToUniversalTime());
        root.GetProperty("start").GetProperty("timeZone").GetString().Should().Be("UTC");
        endInstant.UtcDateTime.Should().Be(DateTime.Parse("2026-05-07T11:00:00Z").ToUniversalTime());
        root.GetProperty("end").GetProperty("timeZone").GetString().Should().Be("UTC");

        var attendees = root.GetProperty("attendees").EnumerateArray().ToArray();
        attendees.Should().HaveCount(2);
        attendees.Select(a => a.GetProperty("email").GetString()).Should().BeEquivalentTo("alice@example.com", "bob@example.com");
    }

    private async Task<CalendarCreateEventTool> CreateToolAsync()
    {
        var tokenStore = new InMemoryGoogleOAuthTokenStore();
        await tokenStore.SaveTokenAsync("testuser", new GoogleTokenSet(
            AccessToken: "test_access_token",
            RefreshToken: "test_refresh_token",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
            Scopes: "https://www.googleapis.com/auth/calendar.events"), CancellationToken.None);

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

        return new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
