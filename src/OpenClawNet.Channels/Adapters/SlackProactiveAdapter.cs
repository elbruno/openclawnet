using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Channels.Configuration;

namespace OpenClawNet.Channels.Adapters;

/// <summary>
/// Slack proactive adapter that delivers job artifacts to Slack channels using the Slack API.
/// Uses Bot Token authentication for proactive message delivery.
/// Fire-and-forget pattern: logs errors but doesn't throw, allowing job to succeed regardless.
/// </summary>
public class SlackProactiveAdapter : IChannelDeliveryAdapter
{
    private readonly HttpClient _httpClient;
    private readonly SlackClientOptions _options;
    private readonly ILogger<SlackProactiveAdapter> _logger;

    public string Name => "SlackProactive";

    public SlackProactiveAdapter(
        HttpClient httpClient,
        IOptions<SlackClientOptions> options,
        ILogger<SlackProactiveAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Validate configuration on construction
        _options.Validate();
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
            // Parse channel ID from channel config JSON: { "channelId": "C0123456789" }
            var channelId = ExtractChannelId(content);
            if (string.IsNullOrWhiteSpace(channelId))
            {
                var errorMsg = "Slack channel ID not found in channel config";
                _logger.LogError(
                    "Failed to deliver to Slack for job {JobId}, artifact {ArtifactId}: {Error}",
                    jobId, artifactId, errorMsg);
                return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
            }

            // Build Slack message with blocks (Slack Block Kit format)
            var slackMessage = BuildSlackMessage(jobName, artifactType, artifactId, channelId);

            var json = JsonSerializer.Serialize(slackMessage);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            // Set timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.Timeout);

            // Add authorization header
            var request = new HttpRequestMessage(HttpMethod.Post, _options.EndpointUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.BotToken}");
            request.Content = httpContent;

            // POST to Slack API
            var response = await _httpClient.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            // Parse Slack API response to extract message timestamp (ts)
            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);
            string? externalId = null;
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                {
                    if (doc.RootElement.TryGetProperty("ts", out var ts))
                    {
                        externalId = ts.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore JSON parsing errors for response - not critical
            }

            _logger.LogInformation(
                "Slack proactive message delivered successfully for job {JobId} ({JobName}), artifact {ArtifactId} ({ArtifactType}) to channel {ChannelId}",
                jobId, jobName, artifactId, artifactType, channelId);

            return new DeliveryResult(Success: true, ExternalId: externalId);
        }
        catch (OperationCanceledException)
        {
            // Timeout
            var errorMsg = $"Slack proactive message delivery timed out ({_options.Timeout.TotalSeconds} second limit)";
            _logger.LogError(
                "Slack proactive message delivery timed out for job {JobId}, artifact {ArtifactId}",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (HttpRequestException ex)
        {
            // HTTP errors (network, timeout, status code)
            var errorMsg = $"HTTP error: {ex.Message}";
            _logger.LogError(
                ex,
                "HTTP error delivering Slack proactive message for job {JobId}, artifact {ArtifactId}. Will be retried via audit log.",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (JsonException ex)
        {
            // JSON parsing errors (invalid channel config)
            var errorMsg = $"Invalid channel config JSON: {ex.Message}";
            _logger.LogError(
                ex,
                "Failed to parse Slack channel config for job {JobId}, artifact {ArtifactId}",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: log but don't throw
            var errorMsg = $"Unexpected error: {ex.Message}";
            _logger.LogError(
                ex,
                "Failed to deliver Slack proactive message for job {JobId}, artifact {ArtifactId}. Will be retried via audit log.",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
    }

    private string? ExtractChannelId(string channelConfig)
    {
        if (string.IsNullOrWhiteSpace(channelConfig))
            return null;

        try
        {
            // Parse as JSON: { "channelId": "C0123456789" }
            using var doc = JsonDocument.Parse(channelConfig);
            if (doc.RootElement.TryGetProperty("channelId", out var channelIdElement))
            {
                return channelIdElement.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            // Not valid JSON - return null
            return null;
        }
    }

    private object BuildSlackMessage(string jobName, string artifactType, Guid artifactId, string channelId)
    {
        // Slack Block Kit format with header and section blocks
        return new
        {
            channel = channelId,
            text = $"Job '{jobName}' artifact: {artifactType}", // Fallback text for notifications
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = $"🔔 Job '{jobName}' Complete"
                    }
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new
                        {
                            type = "mrkdwn",
                            text = $"*Artifact Type:*\n{artifactType}"
                        },
                        new
                        {
                            type = "mrkdwn",
                            text = $"*Artifact ID:*\n{artifactId}"
                        }
                    }
                }
            }
        };
    }
}
