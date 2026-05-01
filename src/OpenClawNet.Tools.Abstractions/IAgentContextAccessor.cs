namespace OpenClawNet.Tools.Abstractions;

/// <summary>
/// Ambient accessor that exposes the currently-executing agent's identity to tools.
/// Populated by the agent orchestrator at the start of a request and consumed by tools
/// that need per-agent isolation (e.g. <c>RememberTool</c>/<c>RecallTool</c> against
/// <c>IAgentMemoryStore</c>).
/// </summary>
/// <remarks>
/// The agent identity must NOT be sourced from LLM-supplied tool arguments, because
/// that would let one agent impersonate another and bypass per-agent memory isolation.
/// Tools requiring agent scoping should resolve it through this accessor.
/// </remarks>
public interface IAgentContextAccessor
{
    /// <summary>
    /// Snapshot of the active agent's execution context, or <c>null</c> when no agent
    /// scope has been established (e.g. tool executed from a background job or test).
    /// </summary>
    AgentExecutionContext? Current { get; }

    /// <summary>
    /// Sets the active agent context for the calling logical flow. Returns a disposable
    /// that restores the previous value when disposed (use with <c>using</c>).
    /// </summary>
    IDisposable Push(AgentExecutionContext context);
}

/// <summary>
/// Minimal subset of agent execution context that tools may need. Intentionally narrow
/// to keep <c>OpenClawNet.Tools.Abstractions</c> free of higher-level dependencies.
/// </summary>
/// <param name="AgentId">
/// Stable identifier for the active agent — used as the isolation key by
/// <c>IAgentMemoryStore</c>. Maps to <c>AgentRequest.AgentProfileName</c>.
/// </param>
public sealed record AgentExecutionContext(string AgentId);
