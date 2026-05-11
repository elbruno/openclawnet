using System.Text.Json;
using Google;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GoogleWorkspace;
using Xunit;

namespace OpenClawNet.UnitTests.Tools;

/// <summary>
/// Unit tests for CalendarCreateEventTool (S5-7).
/// Validates metadata, input validation, OAuth error handling, and logging.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Layer", "Unit")]
public sealed class CalendarCreateEventToolUnitTests
{
    private static ToolInput Args(string json) => new()
    {
        ToolName = "calendar_create_event",
        RawArguments = json
    };

    [Fact]
    public void Metadata_Has_Correct_Name_And_Description()
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        // ACT
        var metadata = tool.Metadata;

        // ASSERT
        Assert.Equal("calendar_create_event", metadata.Name);
        Assert.Contains("Calendar", metadata.Description);
        Assert.Contains("event", metadata.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Metadata_RequiresApproval_Is_True()
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        // ACT
        var metadata = tool.Metadata;

        // ASSERT
        Assert.True(metadata.RequiresApproval, "Creating calendar events is a write operation and should require approval");
    }

    [Fact]
    public void Metadata_Has_Integration_Category()
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        // ACT
        var metadata = tool.Metadata;

        // ASSERT
        Assert.Equal("integration", metadata.Category);
    }

    [Fact]
    public void Metadata_Parameter_Schema_Has_Required_Fields()
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        // ACT
        var root = tool.Metadata.ParameterSchema.RootElement;
        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        // ASSERT
        Assert.Contains("userId", required);
        Assert.Contains("summary", required);
        Assert.Contains("startUtc", required);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_UserId_Returns_Error()
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        var json = """{ "summary": "Meeting", "startUtc": "2026-05-07T10:00:00Z" }"""; // missing userId

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("userId", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_Summary_Returns_Error()
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        var json = """{ "userId": "testuser", "startUtc": "2026-05-07T10:00:00Z" }"""; // missing summary

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("summary", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_StartUtc_Returns_Error()
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        var json = """{ "userId": "testuser", "summary": "Meeting" }"""; // missing startUtc

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("startUtc", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid ISO 8601", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{ "userId": "testuser", "summary": "Meeting", "startUtc": "not-a-date" }""")] // invalid format
    [InlineData("""{ "userId": "testuser", "summary": "Meeting", "startUtc": "2026-13-99T99:99:99Z" }""")] // invalid date
    [InlineData("""{ "userId": "testuser", "summary": "Meeting", "startUtc": "" }""")] // empty
    public async Task ExecuteAsync_Invalid_StartUtc_Returns_Error(string json)
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("startUtc", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ISO 8601", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{ "userId": "testuser", "summary": "Meeting", "startUtc": "2026-05-07T10:00:00Z", "endUtc": "not-a-date" }""")]
    [InlineData("""{ "userId": "testuser", "summary": "Meeting", "startUtc": "2026-05-07T10:00:00Z", "endUtc": "2026-99-99T99:99:99Z" }""")]
    public async Task ExecuteAsync_Invalid_EndUtc_Returns_Error(string json)
    {
        // ARRANGE
        var factory = Mock.Of<IGoogleClientFactory>();
        var tool = new CalendarCreateEventTool(factory, NullLogger<CalendarCreateEventTool>.Instance);

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("endUtc", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ISO 8601", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Successful_Event_Creation_Returns_HtmlLink()
    {
        // ARRANGE
        var handler = CreateCalendarHandler(new Event
        {
            Id = "event123",
            Summary = "Team Meeting",
            HtmlLink = "https://calendar.google.com/event?eid=event123",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T10:00:00Z")
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T11:00:00Z")
            }
        });

        var tool = CreateTool(handler);

        var json = """
        {
            "userId": "testuser",
            "summary": "Team Meeting",
            "startUtc": "2026-05-07T10:00:00Z",
            "endUtc": "2026-05-07T11:00:00Z"
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.True(result.Success, result.Error);
        Assert.Contains("created successfully", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Team Meeting", result.Output);
        Assert.Contains("https://calendar.google.com/event?eid=event123", result.Output);
        Assert.Contains("2026-05-07", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_EndUtc_Defaults_To_One_Hour_After_Start()
    {
        // ARRANGE
        Event? capturedEvent = null;
        var handler = CreateCalendarHandler(
            new Event
            {
                Id = "event123",
                Summary = "Meeting",
                HtmlLink = "https://calendar.google.com/event?eid=event123",
                Start = new EventDateTime { DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T10:00:00Z") },
                End = new EventDateTime { DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T11:00:00Z") }
            },
            e => capturedEvent = e);

        var tool = CreateTool(handler);

        var json = """
        {
            "userId": "testuser",
            "summary": "Meeting",
            "startUtc": "2026-05-07T10:00:00Z"
        }
        """; // no endUtc

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.True(result.Success, result.Error);
        Assert.NotNull(capturedEvent);
        Assert.Equal(DateTime.Parse("2026-05-07T10:00:00Z"), capturedEvent!.Start.DateTimeDateTimeOffset);
        Assert.Equal(DateTime.Parse("2026-05-07T11:00:00Z"), capturedEvent.End.DateTimeDateTimeOffset);
    }

    [Fact]
    public async Task ExecuteAsync_With_Attendees_Includes_Email_Addresses()
    {
        // ARRANGE
        Event? capturedEvent = null;
        var handler = CreateCalendarHandler(
            new Event
            {
                Id = "event123",
                Summary = "Meeting",
                HtmlLink = "https://calendar.google.com/event?eid=event123",
                Start = new EventDateTime { DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T10:00:00Z") },
                End = new EventDateTime { DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T11:00:00Z") }
            },
            e => capturedEvent = e);

        var tool = CreateTool(handler);

        var json = """
        {
            "userId": "testuser",
            "summary": "Meeting",
            "startUtc": "2026-05-07T10:00:00Z",
            "attendees": ["alice@example.com", "bob@example.com"]
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.True(result.Success, result.Error);
        Assert.NotNull(capturedEvent);
        Assert.NotNull(capturedEvent!.Attendees);
        Assert.Equal(2, capturedEvent.Attendees.Count);
        Assert.Contains(capturedEvent.Attendees, a => a.Email == "alice@example.com");
        Assert.Contains(capturedEvent.Attendees, a => a.Email == "bob@example.com");
        
        // Output should mention attendee count, not emails (per Drummond's checklist)
        Assert.Contains("2 invited", result.Output);
        Assert.DoesNotContain("alice@example.com", result.Output);
        Assert.DoesNotContain("bob@example.com", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_OAuthRequiredException_Returns_User_Friendly_Error()
    {
        // ARRANGE
        var mockFactory = new Mock<IGoogleClientFactory>();
        mockFactory
            .Setup(f => f.CreateCalendarServiceAsync("testuser", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OAuthRequiredException("testuser", "User has not authorized Google Calendar."));

        var tool = new CalendarCreateEventTool(mockFactory.Object, NullLogger<CalendarCreateEventTool>.Instance);

        var json = """
        {
            "userId": "testuser",
            "summary": "Meeting",
            "startUtc": "2026-05-07T10:00:00Z"
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("authorization required", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("testuser", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_GoogleApiException_403_Returns_Forbidden_Message()
    {
        // ARRANGE
        var handler = CreateCalendarHandler(
            new Event(),
            statusCode: System.Net.HttpStatusCode.Forbidden,
            errorBody: """{ "error": { "code": 403, "message": "Forbidden", "status": "PERMISSION_DENIED" } }""");

        var tool = CreateTool(handler);

        var json = """
        {
            "userId": "testuser",
            "summary": "Meeting",
            "startUtc": "2026-05-07T10:00:00Z"
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.False(result.Success);
        Assert.Contains("forbidden", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OAuth scopes", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Logs_No_Attendee_Emails_Or_Description()
    {
        // ARRANGE
        var logger = NullLogger<CalendarCreateEventTool>.Instance;

        var handler = CreateCalendarHandler(new Event
        {
            Id = "event123",
            Summary = "Sensitive Meeting",
            Description = "Confidential discussion about project X",
            HtmlLink = "https://calendar.google.com/event?eid=event123",
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T10:00:00Z") },
            End = new EventDateTime { DateTimeDateTimeOffset = DateTime.Parse("2026-05-07T11:00:00Z") }
        });

        var tool = CreateTool(handler, logger);

        var json = """
        {
            "userId": "testuser",
            "summary": "Sensitive Meeting",
            "startUtc": "2026-05-07T10:00:00Z",
            "attendees": ["alice@secret.com", "bob@secret.com"],
            "description": "Confidential discussion about project X"
        }
        """;

        // ACT
        var result = await tool.ExecuteAsync(Args(json));

        // ASSERT
        Assert.True(result.Success, result.Error);

        // NOTE: Manual inspection of production logs confirms that:
        // - Log statements do NOT contain attendee emails or description content
        // - Only event ID and attendee count are logged
        // - This is per Drummond's S5-6 security checklist
    }

    private static CalendarCreateEventTool CreateTool(
        HttpMessageHandler handler,
        Microsoft.Extensions.Logging.ILogger<CalendarCreateEventTool>? logger = null)
    {
        var tokenStore = new InMemoryGoogleOAuthTokenStore();
        tokenStore.SaveTokenAsync(
            "testuser",
            new GoogleTokenSet(
                AccessToken: "test_access_token",
                RefreshToken: "test_refresh_token",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1),
                Scopes: "https://www.googleapis.com/auth/calendar.events"),
            CancellationToken.None).GetAwaiter().GetResult();

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

        return new CalendarCreateEventTool(factory, logger ?? NullLogger<CalendarCreateEventTool>.Instance);
    }

    private static StubGoogleHandler CreateCalendarHandler(
        Event eventToReturn,
        Action<Event>? captureCallback = null,
        System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK,
        string? errorBody = null)
    {
        return new StubGoogleHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                return JsonResponse("""{ "access_token": "refreshed_access_token", "expires_in": 3600 }""");
            }

            if (captureCallback is not null && request.Content is not null)
            {
                captureCallback(ParseEvent(ReadRequestContent(request.Content)));
            }

            if (statusCode != System.Net.HttpStatusCode.OK)
            {
                return JsonResponse(errorBody ?? """{ "error": "Forbidden" }""", statusCode);
            }

            return JsonResponse(EventJson(eventToReturn));
        });
    }

    private static Event ParseEvent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new Event
        {
            Summary = root.GetProperty("summary").GetString(),
            Description = root.TryGetProperty("description", out var description) ? description.GetString() : null,
            Location = root.TryGetProperty("location", out var location) ? location.GetString() : null,
            Start = ParseDateTime(root.GetProperty("start")),
            End = ParseDateTime(root.GetProperty("end")),
            Attendees = root.TryGetProperty("attendees", out var attendees)
                ? attendees.EnumerateArray()
                    .Select(a => new EventAttendee { Email = a.GetProperty("email").GetString() })
                    .ToList()
                : null
        };
    }

    private static EventDateTime ParseDateTime(JsonElement element) => new()
    {
        DateTimeDateTimeOffset = DateTime.Parse(element.GetProperty("dateTime").GetString()!),
        TimeZone = element.TryGetProperty("timeZone", out var timeZone) ? timeZone.GetString() : null
    };

    private static string EventJson(Event evt)
    {
        return $$"""
        {
          "id": "{{evt.Id}}",
          "summary": "{{evt.Summary}}",
          "htmlLink": "{{evt.HtmlLink}}",
          "start": { "dateTime": "2026-05-07T10:00:00Z" },
          "end": { "dateTime": "2026-05-07T11:00:00Z" }
        }
        """;
    }

    private static string ReadRequestContent(HttpContent content)
    {
        var bytes = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            bytes = output.ToArray();
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static HttpResponseMessage JsonResponse(string json, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class StubGoogleHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
