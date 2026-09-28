using System.Diagnostics;
using ElBruno.AI.Jev;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Integrations.Jev;

internal sealed class JevAgentProfileDecisionService : IAgentProfileDecisionService
{
    private static readonly JevQuestionKey<JevChoiceAnswer> ProfileQuestion = new("agent_profile");

    private readonly IJevDecisionClient _client;
    private readonly JevRoutingOptions _options;
    private readonly ILogger<JevAgentProfileDecisionService> _logger;

    public JevAgentProfileDecisionService(
        IJevDecisionClient client,
        IOptions<JevRoutingOptions> options,
        ILogger<JevAgentProfileDecisionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AgentProfileDecisionResult> DecideAsync(
        AgentProfileDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Candidates.Count == 0)
        {
            return new(
                AgentProfileDecisionStatus.InvalidResponse,
                null,
                null,
                TimeSpan.Zero,
                "no_candidates");
        }

        var criteria = request.Candidates.ToDictionary(
            candidate => candidate.Name,
            candidate => (string?)candidate.Description,
            StringComparer.Ordinal);

        var decisionRequest = new JevDecisionRequest(request.State)
            .WithQuestion(
                ProfileQuestion,
                new JevChoiceQuestion(
                    "Choose the best enabled agent profile for this request.",
                    criteria));

        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            var response = await _client.EvaluateAsync(decisionRequest, timeout.Token);
            var answer = response.GetAnswer(ProfileQuestion);
            stopwatch.Stop();

            if (!criteria.ContainsKey(answer.Choice))
            {
                return new(
                    AgentProfileDecisionStatus.InvalidResponse,
                    null,
                    answer.Confidence,
                    stopwatch.Elapsed,
                    "unknown_profile");
            }

            return new(
                AgentProfileDecisionStatus.Recommended,
                answer.Choice,
                answer.Confidence,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new(
                AgentProfileDecisionStatus.TimedOut,
                null,
                null,
                stopwatch.Elapsed,
                "timeout");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "JEV profile decision failed with exception type {ExceptionType}; deterministic routing will be used",
                ex.GetType().Name);
            return new(
                AgentProfileDecisionStatus.Failed,
                null,
                null,
                stopwatch.Elapsed,
                ex.GetType().Name);
        }
    }
}
