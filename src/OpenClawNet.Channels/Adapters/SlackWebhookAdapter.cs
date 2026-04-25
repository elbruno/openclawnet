using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenClawNet.Channels.Adapters;

/// <summary>
/// Adapter for delivering artifacts to Slack via Incoming Webhooks.
/// Uses Slack Block Kit for rich message formatting.
/// </summary>
public class SlackWebhookAdapter : IChannelDeliveryAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SlackWebhookAdapter> _logger;

    public string Name => "Slack";

    public SlackWebhookAdapter(
        HttpClient httpClient,
        ILogger<SlackWebhookAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DeliveryResult> DeliverAsync(
        Guid jobId,
        string jobName,
        Guid artifactId,
        string artifactType,
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "SlackWebhookAdapter: Delivering artifact {ArtifactId} from job {JobId} ({JobName})",
                artifactId, jobId, jobName);

            // Parse webhook URL from channel config (expects JSON: { "webhookUrl": "..." })
            var webhookUrl = ExtractWebhookUrl(content);
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                var errorMsg = "Slack webhook URL is missing or invalid in channel configuration";
                _logger.LogError("SlackWebhookAdapter: {Error}", errorMsg);
                return new DeliveryResult(false, errorMsg);
            }

            // Validate webhook URL format
            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) ||
                !uri.Host.Contains("slack.com"))
            {
                var errorMsg = $"Invalid Slack webhook URL format: {webhookUrl}";
                _logger.LogError("SlackWebhookAdapter: {Error}", errorMsg);
                return new DeliveryResult(false, errorMsg);
            }

            // Build Slack message payload with Block Kit
            var payload = BuildSlackMessage(jobName, artifactId.ToString(), artifactType, content);
            var jsonPayload = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // POST to Slack webhook
            var response = await _httpClient.PostAsync(webhookUrl, httpContent, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "SlackWebhookAdapter: Successfully delivered artifact {ArtifactId} to Slack",
                    artifactId);
                return new DeliveryResult(true);
            }

            // Handle non-success status codes
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = $"Slack webhook returned {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}";
            _logger.LogError("SlackWebhookAdapter: {Error}", error);
            return new DeliveryResult(false, error);
        }
        catch (HttpRequestException ex)
        {
            var error = $"Network error while delivering to Slack: {ex.Message}";
            _logger.LogError(ex, "SlackWebhookAdapter: {Error}", error);
            return new DeliveryResult(false, error);
        }
        catch (Exception ex)
        {
            var error = $"Unexpected error while delivering to Slack: {ex.Message}";
            _logger.LogError(ex, "SlackWebhookAdapter: {Error}", error);
            return new DeliveryResult(false, error);
        }
    }

    private string? ExtractWebhookUrl(string channelConfig)
    {
        try
        {
            // Try to parse as JSON first
            var jsonDoc = JsonDocument.Parse(channelConfig);
            if (jsonDoc.RootElement.TryGetProperty("webhookUrl", out var webhookProp))
            {
                return webhookProp.GetString();
            }

            // Valid JSON but no webhookUrl property - return null
            return null;
        }
        catch (JsonException)
        {
            // Not valid JSON, treat as plain URL (MVP compatibility)
            return channelConfig;
        }
    }

    private object BuildSlackMessage(string jobName, string artifactId, string artifactType, string content)
    {
        // Slack Block Kit message structure
        // Reference: https://api.slack.com/block-kit
        return new
        {
            text = $"New artifact from OpenClawNet job: {jobName}",
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = "🤖 OpenClawNet Job Artifact",
                        emoji = true
                    }
                },
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = $"*Job:* {jobName}\n*Artifact ID:* `{artifactId}`\n*Type:* `{artifactType}`"
                    }
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new
                        {
                            type = "mrkdwn",
                            text = "Delivered via OpenClawNet Multi-Channel Delivery"
                        }
                    }
                }
            }
        };
    }
}
