using OpenClawNet.Integrations.Jev;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Services;

public sealed class AgentProfileDecisionRouter
{
    private const int MaxCandidates = 32;

    private readonly IAgentProfileDecisionService _decisionService;
    private readonly IAgentProfileStore _profileStore;
    private readonly JevRoutingOptions _options;
    private readonly ILogger<AgentProfileDecisionRouter> _logger;

    public AgentProfileDecisionRouter(
        IAgentProfileDecisionService decisionService,
        IAgentProfileStore profileStore,
        JevRoutingOptions options,
        ILogger<AgentProfileDecisionRouter> logger)
    {
        _decisionService = decisionService;
        _profileStore = profileStore;
        _options = options;
        _logger = logger;
    }

    public async Task<AgentProfile> ResolveAsync(
        string userMessage,
        AgentProfile deterministicProfile,
        bool explicitProfileRequested,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || explicitProfileRequested)
        {
            return deterministicProfile;
        }

        var candidates = (await _profileStore.ListAsync(cancellationToken))
            .Where(profile => profile.IsEnabled && profile.Kind == ProfileKind.Standard)
            .GroupBy(profile => profile.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(profile => profile.Name, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();

        if (deterministicProfile.IsEnabled &&
            deterministicProfile.Kind == ProfileKind.Standard &&
            candidates.All(profile =>
                !profile.Name.Equals(deterministicProfile.Name, StringComparison.Ordinal)))
        {
            if (candidates.Count == MaxCandidates)
            {
                candidates.RemoveAt(candidates.Count - 1);
            }

            candidates.Add(deterministicProfile);
        }

        if (candidates.Count < 2)
        {
            return deterministicProfile;
        }

        var decisionRequest = new AgentProfileDecisionRequest(
            userMessage,
            candidates.Select(profile => new AgentProfileDecisionCandidate(
                profile.Name,
                Describe(profile))).ToList());

        AgentProfileDecisionResult result;
        try
        {
            result = await _decisionService.DecideAsync(decisionRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Agent profile decision service threw {ExceptionType}; using deterministic profile {EffectiveProfile}",
                ex.GetType().Name,
                deterministicProfile.Name);
            return deterministicProfile;
        }

        var recommended = result.HasRecommendation
            ? candidates.FirstOrDefault(
                profile => profile.Name.Equals(result.RecommendedProfile, StringComparison.Ordinal))
            : null;

        var effective = !_options.Shadow && recommended is not null
            ? recommended
            : deterministicProfile;

        _logger.LogInformation(
            "Agent profile decision: Mode={Mode}, Status={Status}, RecommendedProfile={RecommendedProfile}, EffectiveProfile={EffectiveProfile}, MatchesDeterministic={MatchesDeterministic}, Confidence={Confidence}, DurationMs={DurationMs}, CandidateCount={CandidateCount}, FallbackCode={FallbackCode}",
            _options.Shadow ? "shadow" : "apply",
            result.Status,
            recommended?.Name,
            effective.Name,
            recommended?.Name.Equals(deterministicProfile.Name, StringComparison.Ordinal),
            result.Confidence,
            result.Duration.TotalMilliseconds,
            candidates.Count,
            result.FailureCode);

        return effective;
    }

    private static string Describe(AgentProfile profile)
    {
        var displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.Name
            : profile.DisplayName;
        var provider = string.IsNullOrWhiteSpace(profile.Provider)
            ? "default provider"
            : profile.Provider;
        var model = string.IsNullOrWhiteSpace(profile.Model)
            ? "provider default model"
            : profile.Model;

        return $"{displayName}; provider: {provider}; model: {model}";
    }
}
