using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenClawNet.Channels.Adapters;
using System.Text.Json;

namespace OpenClawNet.Adapters.Teams;

/// <summary>
/// Delivers job artifacts to Microsoft Teams via proactive messaging (Bot Framework).
/// Requires conversation reference stored during inbound bot interaction.
/// </summary>
public sealed class TeamsProactiveAdapter : IChannelDeliveryAdapter
{
    private readonly IBotFrameworkHttpAdapter _adapter;
    private readonly ILogger<TeamsProactiveAdapter> _logger;
    private readonly string _appId;

    public string Name => "teams";

    public TeamsProactiveAdapter(
        IBotFrameworkHttpAdapter adapter,
        IConfiguration configuration,
        ILogger<TeamsProactiveAdapter> logger)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Teams Bot App ID from configuration
        _appId = configuration["MicrosoftAppId"] 
            ?? throw new InvalidOperationException("MicrosoftAppId not configured for Teams adapter");
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
                "Teams delivery started: Job={JobId} ({JobName}), Artifact={ArtifactId}, Type={ArtifactType}",
                jobId, jobName, artifactId, artifactType);

            // Parse conversation reference from channelConfig
            // Expected format: { "conversationReference": "{...}", "teamId": "...", "userId": "..." }
            var conversationRef = ParseConversationReference(content);
            if (conversationRef == null)
            {
                var errorMsg = "Invalid or missing conversation reference in channel config";
                _logger.LogWarning("{ErrorMessage}: Job={JobId}, Artifact={ArtifactId}", 
                    errorMsg, jobId, artifactId);
                return new DeliveryResult(false, errorMsg);
            }

            // Create activity message with artifact content
            var activity = CreateArtifactMessage(jobId, jobName, artifactId, artifactType, content);

            // Send proactive message using Bot Framework
            await SendProactiveMessageAsync(conversationRef, activity, cancellationToken);

            _logger.LogInformation(
                "Teams delivery succeeded: Job={JobId}, Artifact={ArtifactId}",
                jobId, artifactId);

            return new DeliveryResult(true, ExternalId: $"teams-{artifactId}");
        }
        catch (JsonException ex)
        {
            var errorMsg = $"Failed to parse conversation reference JSON: {ex.Message}";
            _logger.LogError(ex, "Teams delivery JSON parse error: Job={JobId}, Artifact={ArtifactId}", 
                jobId, artifactId);
            return new DeliveryResult(false, errorMsg);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: never throw, always log and return failure
            var errorMsg = $"Teams delivery failed: {ex.Message}";
            _logger.LogError(ex, "Teams delivery error: Job={JobId}, Artifact={ArtifactId}", 
                jobId, artifactId);
            return new DeliveryResult(false, errorMsg);
        }
    }

    private ConversationReference? ParseConversationReference(string channelConfig)
    {
        try
        {
            // channelConfig is expected to be JSON containing a serialized conversation reference
            // Format: { "conversationReference": "{...}", ... }
            var configDoc = JsonDocument.Parse(channelConfig);
            
            if (!configDoc.RootElement.TryGetProperty("conversationReference", out var refElement))
            {
                _logger.LogWarning("Channel config missing 'conversationReference' property");
                return null;
            }

            var refJson = refElement.GetString();
            if (string.IsNullOrWhiteSpace(refJson))
            {
                _logger.LogWarning("Conversation reference JSON is empty");
                return null;
            }

            // Deserialize the conversation reference
            var conversationRef = JsonSerializer.Deserialize<ConversationReference>(refJson);
            return conversationRef;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse conversation reference from channel config");
            return null;
        }
    }

    private IActivity CreateArtifactMessage(
        Guid jobId,
        string jobName,
        Guid artifactId,
        string artifactType,
        string content)
    {
        // Create Teams Adaptive Card or simple message
        // For MVP: simple text message with basic formatting
        var card = new HeroCard
        {
            Title = $"🤖 New Artifact from Job: {jobName}",
            Subtitle = $"Type: {artifactType}",
            Text = FormatContentForTeams(content, artifactType),
            Buttons = new List<CardAction>
            {
                new CardAction(
                    ActionTypes.OpenUrl,
                    "View in Dashboard",
                    value: $"https://localhost:7000/jobs/{jobId}/artifacts/{artifactId}")
            }
        };

        var activity = MessageFactory.Attachment(card.ToAttachment());
        return activity;
    }

    private string FormatContentForTeams(string content, string artifactType)
    {
        // Truncate long content and format based on type
        const int maxLength = 500;
        
        if (content.Length <= maxLength)
        {
            return content;
        }

        var truncated = content.Substring(0, maxLength);
        return $"{truncated}...\n\n_[Content truncated. View full artifact in dashboard.]_";
    }

    private async Task SendProactiveMessageAsync(
        ConversationReference conversationRef,
        IActivity activity,
        CancellationToken cancellationToken)
    {
        // Use CloudAdapter.ContinueConversationAsync to send proactive message
        // Cast to BotAdapter to access ContinueConversationAsync
        if (_adapter is BotAdapter botAdapter)
        {
            await botAdapter.ContinueConversationAsync(
                _appId,
                conversationRef,
                async (turnContext, ct) =>
                {
                    await turnContext.SendActivityAsync(activity, ct);
                },
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException(
                "Adapter does not support proactive messaging (must be BotAdapter)");
        }
    }
}
