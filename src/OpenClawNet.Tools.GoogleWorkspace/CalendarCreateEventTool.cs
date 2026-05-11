using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Google;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Tool for creating Google Calendar events.
/// Requires approval due to write operation (creates external resource).
/// </summary>
public sealed class CalendarCreateEventTool : ITool
{
    private static readonly ActivitySource ActivitySource = new("OpenClawNet.Tools.GoogleWorkspace");
    
    private readonly IGoogleClientFactory _clientFactory;
    private readonly ILogger<CalendarCreateEventTool> _logger;

    public CalendarCreateEventTool(
        IGoogleClientFactory clientFactory,
        ILogger<CalendarCreateEventTool> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public string Name => "calendar_create_event";

    public string Description =>
        "Create a Google Calendar event on the user's primary calendar. " +
        "Supports attendees, description, location, and custom time zones. " +
        "Requires user approval before execution.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "userId": { 
                    "type": "string", 
                    "description": "User identifier for OAuth token lookup (required)."
                },
                "summary": { 
                    "type": "string", 
                    "description": "Event title/summary (required)."
                },
                "startUtc": { 
                    "type": "string", 
                    "format": "date-time",
                    "description": "Event start time in ISO 8601 format (e.g., '2026-05-07T10:00:00Z'). Required."
                },
                "endUtc": { 
                    "type": "string", 
                    "format": "date-time",
                    "description": "Event end time in ISO 8601 format. If omitted, defaults to 1 hour after start."
                },
                "attendees": { 
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Email addresses of attendees (optional)."
                },
                "description": { 
                    "type": "string", 
                    "description": "Event description/notes (optional)."
                },
                "location": { 
                    "type": "string", 
                    "description": "Event location (optional)."
                },
                "timeZone": { 
                    "type": "string", 
                    "description": "IANA time zone name (e.g., 'America/Los_Angeles'). Default: UTC.",
                    "default": "UTC"
                }
            },
            "required": ["userId", "summary", "startUtc"]
        }
        """),
        RequiresApproval = true,  // Write operation requires user approval
        Category = "integration",
        Tags = ["calendar", "google", "workspace", "scheduling", "events"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("CalendarCreateEvent");
        var sw = Stopwatch.StartNew();

        try
        {
            // Parse and validate parameters
            var userId = input.GetStringArgument("userId");
            if (string.IsNullOrWhiteSpace(userId))
            {
                return ToolResult.Fail(Name, "'userId' parameter is required", sw.Elapsed);
            }

            var summary = input.GetStringArgument("summary");
            if (string.IsNullOrWhiteSpace(summary))
            {
                return ToolResult.Fail(Name, "'summary' parameter is required", sw.Elapsed);
            }

            var startUtcStr = input.GetStringArgument("startUtc");
            if (string.IsNullOrWhiteSpace(startUtcStr) || !DateTime.TryParse(startUtcStr, out var startUtc))
            {
                return ToolResult.Fail(Name, "'startUtc' must be a valid ISO 8601 date-time string", sw.Elapsed);
            }

            var endUtcStr = input.GetStringArgument("endUtc");
            DateTime endUtc;
            if (string.IsNullOrWhiteSpace(endUtcStr))
            {
                // Default to 1 hour after start
                endUtc = startUtc.AddHours(1);
            }
            else if (!DateTime.TryParse(endUtcStr, out endUtc))
            {
                return ToolResult.Fail(Name, "'endUtc' must be a valid ISO 8601 date-time string", sw.Elapsed);
            }

            var attendees = input.GetArgument<string[]>("attendees") ?? Array.Empty<string>();
            var description = input.GetStringArgument("description");
            var location = input.GetStringArgument("location");
            var timeZone = input.GetStringArgument("timeZone") ?? "UTC";

            activity?.SetTag("userId", userId);
            activity?.SetTag("summary", summary);
            activity?.SetTag("attendeeCount", attendees.Length);
            activity?.SetTag("timeZone", timeZone);

            // Create Calendar service
            var calendarService = await _clientFactory.CreateCalendarServiceAsync(userId, cancellationToken);

            // Build event object
            var calendarEvent = new Event
            {
                Summary = summary,
                Description = description,
                Location = location,
                Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = startUtc,
                    TimeZone = timeZone
                },
                End = new EventDateTime
                {
                    DateTimeDateTimeOffset = endUtc,
                    TimeZone = timeZone
                }
            };

            // Add attendees if provided
            if (attendees.Length > 0)
            {
                calendarEvent.Attendees = attendees
                    .Where(email => !string.IsNullOrWhiteSpace(email))
                    .Select(email => new EventAttendee { Email = email.Trim() })
                    .ToList();
            }

            // Create the event on primary calendar
            var insertRequest = calendarService.Events.Insert(calendarEvent, "primary");
            var createdEvent = await insertRequest.ExecuteAsync(cancellationToken);

            sw.Stop();

            // Log event creation (count only, not email addresses — per Drummond's checklist)
            _logger.LogInformation(
                "Created Calendar event {EventId} for user {UserId} with {AttendeeCount} attendees in {ElapsedMs}ms",
                createdEvent.Id,
                userId,
                attendees.Length,
                sw.ElapsedMilliseconds);

            activity?.SetTag("eventId", createdEvent.Id);

            // Build success response with event link
            var sb = new StringBuilder();
            sb.AppendLine($"✅ **Calendar event created successfully**");
            sb.AppendLine();
            sb.AppendLine($"**Title:** {createdEvent.Summary}");
            sb.AppendLine($"**Start:** {createdEvent.Start.DateTimeDateTimeOffset?.ToString("yyyy-MM-dd HH:mm")} {timeZone}");
            sb.AppendLine($"**End:** {createdEvent.End.DateTimeDateTimeOffset?.ToString("yyyy-MM-dd HH:mm")} {timeZone}");
            
            if (!string.IsNullOrWhiteSpace(createdEvent.Location))
            {
                sb.AppendLine($"**Location:** {createdEvent.Location}");
            }

            if (attendees.Length > 0)
            {
                sb.AppendLine($"**Attendees:** {attendees.Length} invited");
            }

            if (!string.IsNullOrWhiteSpace(createdEvent.HtmlLink))
            {
                sb.AppendLine();
                sb.AppendLine($"**View event:** {createdEvent.HtmlLink}");
            }

            return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
        }
        catch (OAuthRequiredException ex)
        {
            // User hasn't authorized or token refresh failed
            _logger.LogWarning(ex, "Calendar tool requires OAuth authorization for user {UserId}", ex.UserId);
            sw.Stop();
            return ToolResult.Fail(
                Name,
                $"Google Calendar authorization required for user '{ex.UserId}': {ex.Message}",
                sw.Elapsed);
        }
        catch (GoogleApiException ex)
        {
            // Sanitize Google API errors (no tokens, no auth headers)
            _logger.LogError(
                ex,
                "Calendar API error for user {UserId}: {StatusCode} {Message}",
                input.GetStringArgument("userId"),
                ex.HttpStatusCode,
                ex.Message);
            sw.Stop();

            var errorMsg = ex.HttpStatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Google Calendar authorization failed. Please re-authorize the application.",
                System.Net.HttpStatusCode.Forbidden => "Access to Calendar API forbidden. Check OAuth scopes.",
                System.Net.HttpStatusCode.BadRequest => $"Invalid calendar event data: {ex.Message}",
                _ => $"Calendar API error: {ex.Message}"
            };

            return ToolResult.Fail(Name, errorMsg, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Calendar tool for user {UserId}", input.GetStringArgument("userId"));
            sw.Stop();
            return ToolResult.Fail(Name, $"Unexpected error: {ex.Message}", sw.Elapsed);
        }
    }
}
