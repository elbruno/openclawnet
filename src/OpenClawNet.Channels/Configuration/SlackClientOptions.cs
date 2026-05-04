namespace OpenClawNet.Channels.Configuration;

/// <summary>
/// Configuration options for Slack proactive message delivery.
/// </summary>
public class SlackClientOptions
{
    /// <summary>
    /// Slack API endpoint URL for proactive messages (e.g., https://slack.com/api/chat.postMessage).
    /// </summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// Slack Bot Token (xoxb-...) for authentication.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// HTTP request timeout for Slack API calls.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Validates that required configuration values are present.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when EndpointUrl or BotToken are null or empty.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EndpointUrl))
        {
            throw new InvalidOperationException("Slack EndpointUrl must be configured.");
        }

        if (string.IsNullOrWhiteSpace(BotToken))
        {
            throw new InvalidOperationException("Slack BotToken must be configured.");
        }
    }
}
