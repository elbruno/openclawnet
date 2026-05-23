using OpenClawNet.Models.Abstractions;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Agent;

/// <summary>
/// Represents the runtime context of a single agent interaction.
/// Used internally to coordinate between prompt composition, model calls, tool execution, and memory.
/// This abstraction allows for future integration with orchestration frameworks like Microsoft Agent Framework
/// while keeping the current implementation as the authoritative runtime.
/// </summary>
public sealed class AgentContext
{
    /// <summary>
    /// Unique identifier for this agent interaction/turn.
    /// </summary>
    public Guid InteractionId { get; } = Guid.NewGuid();

    /// <summary>
    /// The session this agent is operating within.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// The current user message being processed.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// The model name/identifier to use for this interaction.
    /// Empty/null means use the model provider's configured default.
    /// </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>
    /// The model provider (ollama, azure-openai, foundry).
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Custom instructions from the resolved agent profile, injected into the system prompt.
    /// </summary>
    public string? AgentProfileInstructions { get; init; }

    /// <summary>
    /// Resolved provider configuration for this request.
    /// </summary>
    public ResolvedProviderConfig? ResolvedProvider { get; init; }

    /// <summary>
    /// When true, the runtime must pause and emit a <c>ToolApprovalRequest</c> event
    /// before executing any tool whose metadata requires approval. Driven by
    /// <c>AgentProfile.RequireToolApproval</c>.
    /// </summary>
    public bool RequireToolApproval { get; init; }

    /// <summary>
    /// Storage-form tool names allowed by the active agent profile. <c>null</c>/empty
    /// means no filtering is applied (back-compat). See <see cref="AgentRequest.EnabledTools"/>.
    /// </summary>
    public IReadOnlyList<string>? EnabledTools { get; init; }

    /// <summary>
    /// Resolved agent profile name, used for diagnostic log lines describing tool filtering.
    /// Populated by <see cref="AgentOrchestrator"/> from <see cref="AgentRequest.AgentProfileName"/>.
    /// </summary>
    public string? AgentProfileName { get; init; }

    /// <summary>
    /// Retrieval level resolved from the active profile snapshot.
    /// </summary>
    public RetrievalLevel RetrievalLevel { get; init; } = RetrievalLevel.Off;

    /// <summary>
    /// Conversation history up to this point.
    /// </summary>
    public IReadOnlyList<ChatMessage> History { get; init; } = [];

    /// <summary>
    /// Any session summary from prior conversations.
    /// </summary>
    public string? SessionSummary { get; init; }

    /// <summary>
    /// The composed system + conversation messages ready for model processing.
    /// </summary>
    public IReadOnlyList<ChatMessage> ComposedMessages { get; set; } = [];

    /// <summary>
    /// Tool definitions available for this interaction.
    /// </summary>
    public IReadOnlyList<ToolDefinition> AvailableTools { get; set; } = [];

    /// <summary>
    /// Tool calls executed during this interaction.
    /// </summary>
    public IReadOnlyList<ToolCall> ExecutedToolCalls { get; set; } = [];

    /// <summary>
    /// Results from tool execution.
    /// </summary>
    public IReadOnlyList<ToolResult> ToolResults { get; set; } = [];

    /// <summary>
    /// The final response generated.
    /// </summary>
    public string? FinalResponse { get; set; }

    /// <summary>
    /// Total tokens used in this interaction (if reported by model).
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Whether this interaction has completed.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Timestamp when this interaction started.
    /// </summary>
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this interaction completed (null if still in progress).
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Represents a single tool call made by the model.
/// </summary>
public sealed record ToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Arguments { get; init; }
}
