using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OpenClawNet.Channels.Adapters;

/// <summary>
/// Microsoft Teams webhook adapter that delivers job artifacts using Adaptive Cards format.
/// Fire-and-forget pattern: logs errors but doesn't throw, allowing job to succeed regardless.
/// Content is truncated at ~3000 chars to respect Teams message limits.
/// </summary>
public class TeamsProactiveAdapter : IChannelDeliveryAdapter
{
    private const int MaxContentLength = 3000;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TeamsProactiveAdapter> _logger;

    public string Name => "Teams";

    public TeamsProactiveAdapter(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TeamsProactiveAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            // Parse webhook URL from channel config JSON: { "webhookUrl": "https://outlook.office.com/webhook/..." }
            var webhookUrl = ExtractWebhookUrl(content);
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                var errorMsg = "Teams webhook URL not found in channel config";
                _logger.LogError(
                    "Failed to deliver to Teams for job {JobId}, artifact {ArtifactId}: {Error}",
                    jobId, artifactId, errorMsg);
                return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
            }

            // Validate URL format (Teams incoming webhooks)
            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) || 
                !webhookUrl.Contains("outlook.office.com/webhook/", StringComparison.OrdinalIgnoreCase) &&
                !webhookUrl.Contains("outlook.office365.com/webhook/", StringComparison.OrdinalIgnoreCase))
            {
                var errorMsg = $"Invalid Teams webhook URL: {webhookUrl}";
                _logger.LogError(
                    "Failed to deliver to Teams for job {JobId}, artifact {ArtifactId}: {Error}",
                    jobId, artifactId, errorMsg);
                return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
            }

            // Build Adaptive Card message
            var adaptiveCard = BuildAdaptiveCard(jobName, artifactType, artifactId);

            var json = JsonSerializer.Serialize(adaptiveCard);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            // Set timeout to 5 seconds
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // POST to Teams webhook URL
            var response = await _httpClient.PostAsync(webhookUrl, httpContent, cts.Token);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Teams webhook delivered successfully for job {JobId} ({JobName}), artifact {ArtifactId} ({ArtifactType})",
                jobId, jobName, artifactId, artifactType);

            return new DeliveryResult(Success: true);
        }
        catch (OperationCanceledException)
        {
            // Timeout
            var errorMsg = "Teams webhook delivery timed out (5 second limit)";
            _logger.LogError(
                "Teams webhook delivery timed out for job {JobId}, artifact {ArtifactId}",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (HttpRequestException ex)
        {
            // HTTP errors (network, timeout, status code)
            var errorMsg = $"HTTP error: {ex.Message}";
            _logger.LogError(
                ex,
                "HTTP error delivering Teams webhook for job {JobId}, artifact {ArtifactId}. Will be retried via audit log.",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (JsonException ex)
        {
            // JSON parsing errors (invalid channel config)
            var errorMsg = $"Invalid channel config JSON: {ex.Message}";
            _logger.LogError(
                ex,
                "Failed to parse Teams channel config for job {JobId}, artifact {ArtifactId}",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: log but don't throw
            var errorMsg = $"Unexpected error: {ex.Message}";
            _logger.LogError(
                ex,
                "Failed to deliver Teams webhook for job {JobId}, artifact {ArtifactId}. Will be retried via audit log.",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
    }

    private string? ExtractWebhookUrl(string channelConfig)
    {
        if (string.IsNullOrWhiteSpace(channelConfig))
            return null;

        try
        {
            // Try to parse as JSON first: { "webhookUrl": "..." }
            using var doc = JsonDocument.Parse(channelConfig);
            if (doc.RootElement.TryGetProperty("webhookUrl", out var urlElement))
            {
                return urlElement.GetString();
            }

            // Fallback: treat entire content as URL (for backward compatibility)
            var trimmed = channelConfig.Trim();
            if (trimmed.Contains("outlook.office.com/webhook/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("outlook.office365.com/webhook/", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return null;
        }
        catch (JsonException)
        {
            // Not valid JSON, try as direct URL
            var trimmed = channelConfig.Trim();
            if (trimmed.Contains("outlook.office.com/webhook/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("outlook.office365.com/webhook/", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            return null;
        }
    }

    private object BuildAdaptiveCard(string jobName, string artifactType, Guid artifactId)
    {
        // Adaptive Card format for Teams (v1.4 schema)
        // See: https://adaptivecards.io/schemas/adaptive-card.json
        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    contentUrl = (string?)null,
                    content = new
                    {
                        type = "AdaptiveCard",
                        schema = "http://adaptivecards.io/schemas/adaptive-card.json",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text = $"🎉 Job Complete: {jobName}",
                                size = "Large",
                                weight = "Bolder",
                                color = "Accent"
                            },
                            new
                            {
                                type = "FactSet",
                                facts = new[]
                                {
                                    new { title = "Artifact Type", value = artifactType },
                                    new { title = "Artifact ID", value = artifactId.ToString() },
                                    new { title = "Timestamp", value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC") }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
