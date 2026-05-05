using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Memory;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Agent;

/// <summary>
/// Delegates conversation summarization to the external memory service.
/// Falls back to local summarization if the memory service is unavailable.
/// </summary>
public sealed class DefaultSummaryService : ISummaryService
{
    private readonly IMemoryService _memoryService;
    private readonly IModelClient _modelClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SummaryOptions> _summaryOptions;
    private readonly ILogger<DefaultSummaryService> _logger;

    private const int SummarizationThreshold = 20;

    public DefaultSummaryService(
        IMemoryService memoryService,
        IModelClient modelClient,
        IHttpClientFactory httpClientFactory,
        IOptions<SummaryOptions> summaryOptions,
        ILogger<DefaultSummaryService> logger)
    {
        _memoryService = memoryService;
        _modelClient = modelClient;
        _httpClientFactory = httpClientFactory;
        _summaryOptions = summaryOptions ?? throw new ArgumentNullException(nameof(summaryOptions));
        _logger = logger;
    }

    public async Task<string?> SummarizeIfNeededAsync(Guid sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var existingSummary = await _memoryService.GetSessionSummaryAsync(sessionId, cancellationToken);
        if (messages.Count < SummarizationThreshold) return existingSummary;

        try
        {
            // Try memory service first
            var client = _httpClientFactory.CreateClient("memory-service");
            var messageDtos = messages.Select(m => new { role = m.Role.ToString().ToLowerInvariant(), content = m.Content }).ToList();
            var response = await client.PostAsJsonAsync("/api/memory/summarize",
                new { sessionId, messages = messageDtos }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SummarizeResult>(cancellationToken: cancellationToken);
                if (result?.Success == true && !string.IsNullOrEmpty(result.Summary))
                {
                    await _memoryService.StoreSummaryAsync(sessionId, result.Summary, messages.Count, cancellationToken);
                    return result.Summary;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory service unavailable, falling back to local summarization");
        }

        // Fallback: summarize locally
        return await SummarizeLocallyAsync(sessionId, messages, existingSummary, cancellationToken);
    }

    private async Task<string?> SummarizeLocallyAsync(Guid sessionId, IReadOnlyList<ChatMessage> messages, string? existingSummary, CancellationToken ct)
    {
        try
        {
            var conversationText = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
            // Issue #107: model name is now sourced from SummaryOptions (Summary:Model in config),
            // defaulting to "llama3.2" via SummaryOptions.Model. No tagged variants are used.
            var model = _summaryOptions.Value.Model;
            if (string.IsNullOrWhiteSpace(model))
            {
                model = new SummaryOptions().Model;
            }
            var summaryRequest = new ChatRequest
            {
                Model = model,
                Messages =
                [
                    new ChatMessage { Role = ChatMessageRole.System, Content = "You are a conversation summarizer. Summarize the following conversation concisely. Keep the summary under 500 words." },
                    new ChatMessage { Role = ChatMessageRole.User, Content = $"Please summarize this conversation:\n\n{conversationText}" }
                ]
            };
            var response = await _modelClient.CompleteAsync(summaryRequest, ct);
            await _memoryService.StoreSummaryAsync(sessionId, response.Content, messages.Count, ct);
            return response.Content;
        }
        catch
        {
            return existingSummary;
        }
    }

    private sealed record SummarizeResult(bool Success, string? Summary, string? Error, int MessageCount);
}
