namespace OpenClawNet.Integrations.Jev;

public interface IAgentProfileDecisionService
{
    Task<AgentProfileDecisionResult> DecideAsync(
        AgentProfileDecisionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AgentProfileDecisionRequest(
    string State,
    IReadOnlyList<AgentProfileDecisionCandidate> Candidates);

public sealed record AgentProfileDecisionCandidate(
    string Name,
    string Description);

public enum AgentProfileDecisionStatus
{
    Recommended,
    Disabled,
    Misconfigured,
    TimedOut,
    Failed,
    InvalidResponse
}

public sealed record AgentProfileDecisionResult(
    AgentProfileDecisionStatus Status,
    string? RecommendedProfile,
    double? Confidence,
    TimeSpan Duration,
    string? FailureCode = null)
{
    public bool HasRecommendation =>
        Status == AgentProfileDecisionStatus.Recommended &&
        !string.IsNullOrWhiteSpace(RecommendedProfile);
}
