using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// LLM-based service for generating concise chat session names (5-8 words)
/// based on recent conversation context.
/// </summary>
public sealed class ChatNamingService
{
    private readonly IModelClient _modelClient;
    private readonly ILogger<ChatNamingService> _logger;

    public ChatNamingService(IModelClient modelClient, ILogger<ChatNamingService> logger)
    {
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private const string SystemPrompt = """
        You are a chat session naming assistant. The user will provide the recent conversation messages.
        Generate a concise session title (5-8 words max) that captures the essence of the conversation.
        
        Rules:
        - Return ONLY the title text, no quotes, no markdown, no explanation
        - Use title case
        - Keep it 5-8 words maximum
        - Make it descriptive and specific to the conversation topic
        - If the conversation is unclear, use a generic title like "Mixed Topic Discussion"
        """;

    /// <summary>
    /// Generate a name for a chat session based on recent messages.
    /// </summary>
    public async Task<string> GenerateNameAsync(
        IReadOnlyList<ChatMessageEntity> recentMessages,
        CancellationToken ct = default)
    {
        if (!recentMessages.Any())
            return "New Chat";

        // Take the last 5-10 messages, filtering to user/assistant content
        var contextMessages = recentMessages
            .TakeLast(10)
            .Where(m => m.Role is "user" or "assistant")
            .ToList();

        if (!contextMessages.Any())
            return "New Chat";

        // Build context string
        var contextLines = new List<string>();
        foreach (var msg in contextMessages)
        {
            var prefix = msg.Role == "user" ? "User:" : "Assistant:";
            var content = msg.Content.Length > 100 ? msg.Content[..100] + "..." : msg.Content;
            contextLines.Add($"{prefix} {content}");
        }

        var contextString = string.Join("\n", contextLines);

        try
        {
            var request = new ChatRequest
            {
                Messages =
                [
                    new ChatMessage { Role = ChatMessageRole.System, Content = SystemPrompt },
                    new ChatMessage { Role = ChatMessageRole.User, Content = $"Generate a title for this chat session:\n\n{contextString}" }
                ],
                Temperature = 0.3
            };

            var response = await _modelClient.CompleteAsync(request, ct);
            var generatedName = response.Content.Trim();

            // Sanitize the response
            generatedName = generatedName.Trim('"', '\'', '*', '#', '`');

            if (string.IsNullOrWhiteSpace(generatedName))
                return "New Chat";

            // Truncate if it exceeds 256 chars (reasonable limit)
            if (generatedName.Length > 256)
                generatedName = generatedName[..256];

            _logger.LogInformation("Generated chat name: {GeneratedName}", generatedName);
            return generatedName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate chat name using LLM, using fallback");
            return "New Chat";
        }
    }
}
