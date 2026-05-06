using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Tool for publishing repository insights to an external dashboard.
/// Requires user approval due to side-effectful external API calls.
/// </summary>
public sealed class DashboardPublisherTool : ITool
{
    private readonly IDashboardPublisher _publisher;
    private readonly ILogger<DashboardPublisherTool> _logger;

    public DashboardPublisherTool(
        IDashboardPublisher publisher,
        ILogger<DashboardPublisherTool> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public string Name => "dashboard_publish";

    public string Description =>
        "Publishes repository insights (open issues, PRs, stars, etc.) to an external dashboard. " +
        "Requires approval before publishing. Returns the dashboard view URL on success.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "title": {
                    "type": "string",
                    "description": "Dashboard card title (e.g., 'Multi-repo Insights 2026-05-06')"
                },
                "insights": {
                    "type": "array",
                    "description": "Array of repository insights to publish",
                    "items": {
                        "type": "object",
                        "properties": {
                            "repo": {
                                "type": "string",
                                "description": "Repository identifier (e.g., 'owner/repo')"
                            },
                            "openIssues": {
                                "type": "integer",
                                "description": "Number of open issues"
                            },
                            "openPRs": {
                                "type": "integer",
                                "description": "Number of open pull requests"
                            },
                            "stars": {
                                "type": "integer",
                                "description": "Star count"
                            },
                            "lastPush": {
                                "type": "string",
                                "format": "date-time",
                                "description": "Last push timestamp (ISO 8601)"
                            },
                            "summary": {
                                "type": "string",
                                "description": "Summary text or additional context"
                            }
                        },
                        "required": ["repo"]
                    }
                },
                "format": {
                    "type": "string",
                    "enum": ["card", "table", "chart"],
                    "default": "card",
                    "description": "Display format hint for the dashboard"
                }
            },
            "required": ["title", "insights"]
        }
        """),
        RequiresApproval = true,
        Category = "integration",
        Tags = ["dashboard", "insights", "publish", "external"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var title = input.GetStringArgument("title");
            if (string.IsNullOrWhiteSpace(title))
                return ToolResult.Fail(Name, "'title' is required", sw.Elapsed);

            var insightsArray = input.GetArgument<JsonElement>("insights");
            if (insightsArray.ValueKind != JsonValueKind.Array || insightsArray.GetArrayLength() == 0)
                return ToolResult.Fail(Name, "'insights' must be a non-empty array", sw.Elapsed);

            var insights = new List<RepositoryInsight>();
            foreach (var item in insightsArray.EnumerateArray())
            {
                var insight = JsonSerializer.Deserialize<RepositoryInsight>(item.GetRawText());
                if (insight is not null)
                    insights.Add(insight);
            }

            if (insights.Count == 0)
                return ToolResult.Fail(Name, "No valid insights found in input", sw.Elapsed);

            var format = input.GetStringArgument("format");

            var request = new DashboardPublishRequest
            {
                Title = title,
                Insights = insights,
                Format = format
            };

            _logger.LogInformation(
                "Executing dashboard_publish tool: {Title}, {RepoCount} insights",
                title,
                insights.Count);

            var result = await _publisher.PublishAsync(request, cancellationToken);

            var output = $"✅ Published to dashboard. View: {result.ViewUrl}";
            
            sw.Stop();
            
            _logger.LogInformation(
                "Dashboard publish succeeded: ID={DashboardId}, URL={ViewUrl}, Duration={DurationMs}ms",
                result.Id,
                result.ViewUrl,
                sw.ElapsedMilliseconds);

            return ToolResult.Ok(Name, output, sw.Elapsed);
        }
        catch (DashboardPublisherException ex)
        {
            sw.Stop();
            var errorMsg = $"Dashboard API error: {(int)ex.StatusCode} {ex.StatusCode}";
            
            _logger.LogWarning(
                "Dashboard publish failed with HTTP error: StatusCode={StatusCode}, Duration={DurationMs}ms",
                (int)ex.StatusCode,
                sw.ElapsedMilliseconds);
            
            return ToolResult.Fail(Name, errorMsg, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            
            _logger.LogError(ex, 
                "Dashboard publish failed with unexpected error. Duration={DurationMs}ms",
                sw.ElapsedMilliseconds);
            
            return ToolResult.Fail(Name, $"Unexpected error: {ex.Message}", sw.Elapsed);
        }
    }
}
