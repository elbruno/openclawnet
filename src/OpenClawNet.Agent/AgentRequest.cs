using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Agent;

public sealed record AgentRequest
{
    public required Guid SessionId { get; init; }
    public required string UserMessage { get; init; }
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public string? AgentProfileName { get; init; }
    public string? AgentProfileInstructions { get; init; }
    public ResolvedProviderConfig? ResolvedProvider { get; init; }
    public RetrievalLevel RetrievalLevel { get; init; } = RetrievalLevel.Off;

    /// <summary>
    /// When true, the agent runtime must pause and request explicit user approval
    /// before executing any tool whose <c>ToolMetadata.RequiresApproval</c> is true
    /// (and which isn't on the exempt list — see <c>ToolApprovalExemptions</c>).
    /// Default false preserves back-compat for callers that don't set it; the
    /// gateway sets it from the resolved <c>AgentProfile.RequireToolApproval</c>.
    /// </summary>
    public bool RequireToolApproval { get; init; }

    /// <summary>
    /// Storage-form tool names this profile is allowed to expose to the LLM
    /// (e.g. <c>web.fetch</c>, <c>scheduler.schedule</c>). When <c>null</c> or empty,
    /// every available tool is exposed (back-compat for unconfigured profiles).
    /// Sourced from <c>AgentProfile.EnabledTools</c>.
    /// </summary>
    public IReadOnlyList<string>? EnabledTools { get; init; }
}
