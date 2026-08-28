using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Agent;

/// <summary>
/// Composes the full prompt including system instructions, skills, memory summaries, and conversation history.
/// </summary>
public interface IPromptComposer
{
    Task<IReadOnlyList<ChatMessage>> ComposeAsync(PromptContext context, CancellationToken cancellationToken = default);
}

public sealed record PromptContext
{
    public required Guid SessionId { get; init; }
    public required string UserMessage { get; init; }
    public IReadOnlyList<ChatMessage> History { get; init; } = [];
    public IReadOnlyList<string> ActiveSkills { get; init; } = [];
    public IReadOnlyList<OpenClawNet.Memory.MemoryHit> RetrievedMemories { get; init; } = [];
    public string? SessionSummary { get; init; }

    /// <summary>
    /// Custom instructions from the active Agent Profile, appended to the system prompt
    /// after workspace configuration and before session-specific context.
    /// Never logged.
    /// </summary>
    public string? ProfileInstructions { get; init; }
}
