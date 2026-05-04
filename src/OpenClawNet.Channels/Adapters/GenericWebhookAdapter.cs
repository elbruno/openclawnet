using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenClawNet.Channels.Adapters;

/// <summary>
/// Generic webhook adapter that POSTs job artifacts to a user-configured webhook URL.
/// Fire-and-forget pattern: logs errors but doesn't throw, allowing job to succeed regardless.
/// Implements retry logic: 3 attempts with exponential backoff (1s, 2s, 4s).
/// </summary>
public class GenericWebhookAdapter : IChannelDeliveryAdapter
{
    private const int MaxRetries = 3;
    private const int InitialDelayMs = 1000;
    private const int TimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly ILogger<GenericWebhookAdapter> _logger;

    public string Name => "GenericWebhook";

    public GenericWebhookAdapter(HttpClient httpClient, ILogger<GenericWebhookAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Set default timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
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
            // Parse webhook URL from channelConfig
            var webhookUrl = ExtractWebhookUrl(content);
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                var errorMsg1 = "Webhook URL not found in channel config";
                _logger.LogError(
                    "Failed to deliver webhook for job {JobId}, artifact {ArtifactId}: {Error}",
                    jobId, artifactId, errorMsg1);
                return new DeliveryResult(Success: false, ErrorMessage: errorMsg1);
            }

            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
            {
                var errorMsg2 = $"Invalid webhook URL: {webhookUrl}";
                _logger.LogError(
                    "Failed to deliver webhook for job {JobId}, artifact {ArtifactId}: {Error}",
                    jobId, artifactId, errorMsg2);
                return new DeliveryResult(Success: false, ErrorMessage: errorMsg2);
            }

            // Build payload with artifact metadata
            var payload = new
            {
                JobId = jobId,
                JobName = jobName,
                ArtifactId = artifactId,
                ArtifactType = artifactType,
                Content = content,
                DeliveredAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(payload);

            // Retry logic: 3 attempts with exponential backoff
            Exception? lastException = null;
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                    _logger.LogDebug(
                        "Webhook delivery attempt {Attempt}/{MaxRetries} to {WebhookUrl} for job {JobId}",
                        attempt, MaxRetries, webhookUrl, jobId);

                    var response = await _httpClient.PostAsync(webhookUrl, httpContent, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    _logger.LogInformation(
                        "Webhook delivered successfully to {WebhookUrl} for job {JobId}, artifact {ArtifactId} (attempt {Attempt}/{MaxRetries})",
                        webhookUrl, jobId, artifactId, attempt, MaxRetries);

                    return new DeliveryResult(Success: true);
                }
                catch (HttpRequestException ex) when (attempt < MaxRetries)
                {
                    lastException = ex;
                    var delayMs = InitialDelayMs * (int)Math.Pow(2, attempt - 1);

                    _logger.LogWarning(
                        ex,
                        "Webhook delivery attempt {Attempt}/{MaxRetries} failed for job {JobId}. Retrying in {DelayMs}ms...",
                        attempt, MaxRetries, jobId, delayMs);

                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < MaxRetries)
                    {
                        var delayMs = InitialDelayMs * (int)Math.Pow(2, attempt - 1);

                        _logger.LogWarning(
                            ex,
                            "Webhook delivery attempt {Attempt}/{MaxRetries} failed for job {JobId}. Retrying in {DelayMs}ms...",
                            attempt, MaxRetries, jobId, delayMs);

                        await Task.Delay(delayMs, cancellationToken);
                    }
                }
            }

            // All retries exhausted
            var errorMsg = $"All {MaxRetries} delivery attempts failed: {lastException?.Message ?? "Unknown error"}";
            _logger.LogError(
                lastException,
                "Webhook delivery failed after {MaxRetries} attempts for job {JobId}, artifact {ArtifactId}",
                MaxRetries, jobId, artifactId);

            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (OperationCanceledException)
        {
            var errorMsg = "Webhook delivery cancelled";
            _logger.LogWarning("Webhook delivery cancelled for job {JobId}, artifact {ArtifactId}", jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: errorMsg);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: log but don't throw
            _logger.LogError(
                ex,
                "Failed to deliver webhook for job {JobId}, artifact {ArtifactId}",
                jobId, artifactId);
            return new DeliveryResult(Success: false, ErrorMessage: ex.Message);
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
            return channelConfig.Trim();
        }
        catch (JsonException)
        {
            // Not valid JSON, treat as direct URL
            return channelConfig.Trim();
        }
    }
}
