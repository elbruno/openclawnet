using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Google;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Tool for summarizing unread Gmail messages.
/// Read-only access to Gmail via gmail.readonly scope.
/// </summary>
public sealed class GmailSummarizeTool : ITool
{
    private static readonly ActivitySource ActivitySource = new("OpenClawNet.Tools.GoogleWorkspace");
    
    private readonly IGoogleClientFactory _clientFactory;
    private readonly ILogger<GmailSummarizeTool> _logger;

    public GmailSummarizeTool(
        IGoogleClientFactory clientFactory,
        ILogger<GmailSummarizeTool> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public string Name => "gmail_summarize";

    public string Description =>
        "Summarize unread Gmail messages. Returns sender, subject, and date for recent unread emails. " +
        "Read-only access (no modifications to mailbox).";

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
                "maxResults": { 
                    "type": "integer", 
                    "description": "Maximum number of messages to fetch (default 10, max 50).",
                    "default": 10,
                    "minimum": 1,
                    "maximum": 50
                },
                "query": { 
                    "type": "string", 
                    "description": "Gmail search query (default: 'is:unread'). Must include 'is:unread' for security.",
                    "default": "is:unread"
                }
            },
            "required": ["userId"]
        }
        """),
        RequiresApproval = false,
        Category = "integration",
        Tags = ["gmail", "email", "google", "workspace", "communication"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GmailSummarize");
        var sw = Stopwatch.StartNew();

        try
        {
            // Parse and validate parameters
            var userId = input.GetStringArgument("userId");
            if (string.IsNullOrWhiteSpace(userId))
            {
                return ToolResult.Fail(Name, "'userId' parameter is required", sw.Elapsed);
            }

            var maxResults = Math.Clamp(input.GetArgument<int?>("maxResults") ?? 10, 1, 50);
            var query = input.GetStringArgument("query") ?? "is:unread";

            // Security: enforce that query includes is:unread (allow narrowing, not broadening)
            if (!query.Contains("is:unread", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Gmail query security violation: query must include 'is:unread'. Query: {Query}",
                    query);
                return ToolResult.Fail(
                    Name,
                    "Query must include 'is:unread' to limit scope to unread messages only",
                    sw.Elapsed);
            }

            activity?.SetTag("userId", userId);
            activity?.SetTag("maxResults", maxResults);
            activity?.SetTag("query", query);

            // Create Gmail service
            var gmailService = await _clientFactory.CreateGmailServiceAsync(userId, cancellationToken);

            // List messages matching query
            var listRequest = gmailService.Users.Messages.List("me");
            listRequest.Q = query;
            listRequest.MaxResults = maxResults;

            var messageList = await listRequest.ExecuteAsync(cancellationToken);
            var messages = messageList.Messages ?? new List<Google.Apis.Gmail.v1.Data.Message>();

            if (messages.Count == 0)
            {
                _logger.LogInformation(
                    "No unread messages found for user {UserId} with query '{Query}'",
                    userId,
                    query);
                sw.Stop();
                return ToolResult.Ok(Name, "No unread messages found.", sw.Elapsed);
            }

            // Fetch message metadata (From, Subject, Date only — no body)
            var sb = new StringBuilder();
            sb.AppendLine($"**{messages.Count} unread message{(messages.Count == 1 ? "" : "s")}:**");
            sb.AppendLine();

            var fetchedCount = 0;
            foreach (var messageSummary in messages)
            {
                try
                {
                    var getRequest = gmailService.Users.Messages.Get("me", messageSummary.Id);
                    getRequest.Format = Google.Apis.Gmail.v1.UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    getRequest.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(new[] { "From", "Subject", "Date" });

                    var message = await getRequest.ExecuteAsync(cancellationToken);
                    fetchedCount++;

                    // Extract headers
                    var headers = message.Payload?.Headers ?? new List<Google.Apis.Gmail.v1.Data.MessagePartHeader>();
                    var from = headers.FirstOrDefault(h => h.Name?.Equals("From", StringComparison.OrdinalIgnoreCase) == true)?.Value ?? "(unknown)";
                    var subject = headers.FirstOrDefault(h => h.Name?.Equals("Subject", StringComparison.OrdinalIgnoreCase) == true)?.Value ?? "(no subject)";
                    var date = headers.FirstOrDefault(h => h.Name?.Equals("Date", StringComparison.OrdinalIgnoreCase) == true)?.Value ?? "(no date)";

                    // Log headers at Debug level only (per Drummond's checklist)
                    _logger.LogDebug(
                        "Gmail message {MessageId}: From={From}, Subject={Subject}",
                        messageSummary.Id,
                        from,
                        subject);

                    // Build bullet list (sender, subject, date)
                    sb.AppendLine($"- **{from}** | {subject} | {date}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to fetch Gmail message {MessageId} for user {UserId}",
                        messageSummary.Id,
                        userId);
                    // Continue processing other messages
                }
            }

            sw.Stop();

            _logger.LogInformation(
                "Fetched {FetchedCount}/{TotalCount} Gmail messages for user {UserId} in {ElapsedMs}ms",
                fetchedCount,
                messages.Count,
                userId,
                sw.ElapsedMilliseconds);

            activity?.SetTag("messageCount", fetchedCount);

            return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
        }
        catch (OAuthRequiredException ex)
        {
            // User hasn't authorized or token refresh failed
            _logger.LogWarning(ex, "Gmail tool requires OAuth authorization for user {UserId}", ex.UserId);
            sw.Stop();
            return ToolResult.Fail(
                Name,
                $"Gmail authorization required: {ex.Message}",
                sw.Elapsed);
        }
        catch (GoogleApiException ex)
        {
            // Sanitize Google API errors (no tokens, no auth headers)
            _logger.LogError(
                ex,
                "Gmail API error for user {UserId}: {StatusCode} {Message}",
                input.GetStringArgument("userId"),
                ex.HttpStatusCode,
                ex.Message);
            sw.Stop();

            var errorMsg = ex.HttpStatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Gmail authorization failed. Please re-authorize the application.",
                System.Net.HttpStatusCode.Forbidden => "Access to Gmail API forbidden. Check OAuth scopes.",
                System.Net.HttpStatusCode.NotFound => "Gmail resource not found.",
                _ => $"Gmail API error: {ex.Message}"
            };

            return ToolResult.Fail(Name, errorMsg, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Gmail tool for user {UserId}", input.GetStringArgument("userId"));
            sw.Stop();
            return ToolResult.Fail(Name, $"Unexpected error: {ex.Message}", sw.Elapsed);
        }
    }
}
