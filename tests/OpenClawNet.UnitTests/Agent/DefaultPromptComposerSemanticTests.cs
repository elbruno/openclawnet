using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;
using System.Diagnostics;

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Unit tests for semantic skill enrichment in DefaultPromptComposer.
/// Story 3: Wire SemanticSkillRanker into DefaultPromptComposer.EnrichSkillsAsync()
/// 
/// Tests verify:
/// - SemanticSkillRanker integration with keyword candidate re-ranking
/// - Confidence score propagation (HIGH/MEDIUM/LOW)
/// - Non-blocking fallback when embedder unavailable
/// - P95 latency <100ms maintained
/// </summary>
[Trait("Category", "Unit")]
[Trait("Story", "Story3-SemanticEnrichment")]
public class DefaultPromptComposerSemanticTests
{
    private static readonly IWorkspaceLoader NoOpWorkspaceLoader = new FakeWorkspaceLoader(
        new BootstrapContext(null, null, null));
    private static readonly IOptions<WorkspaceOptions> DefaultWorkspaceOptions = 
        Options.Create(new WorkspaceOptions());

    // ── Success Path: Semantic Re-ranking Applied ──────────────────────────────────

    /// <summary>
    /// When keyword search returns candidate skills, semantic re-ranking should apply.
    /// Verifies that SemanticSkillRanker.RerankAsync() is called and results are re-ordered.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_WithKeywordCandidates_AppliesSemanticReranking()
    {
        throw new NotImplementedException("Mark: Waiting for integration point definition");
    }

    /// <summary>
    /// When semantic ranking completes successfully, the prompt should include
    /// re-ranked skills with HIGH confidence level propagated.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_WithHighConfidenceSkills_IncludesInPrompt()
    {
        throw new NotImplementedException("Mark: Waiting for integration point definition");
    }

    /// <summary>
    /// When semantic ranking completes successfully, MEDIUM confidence skills
    /// should appear in the prompt with confidence metadata visible.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_WithMediumConfidenceSkills_PropagatesToPrompt()
    {
        throw new NotImplementedException("Mark: Waiting for integration point definition");
    }

    /// <summary>
    /// Verify that the top N re-ranked skills appear in the prompt in correct order.
    /// Low-confidence skills may be excluded or downranked (depends on filtering threshold).
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_RanksSkillsBySemanticScore()
    {
        throw new NotImplementedException("Mark: Waiting for integration point definition");
    }

    // ── Confidence Score Propagation ─────────────────────────────────────────────────

    /// <summary>
    /// Verify that confidence levels (HIGH/MEDIUM/LOW) are propagated from
    /// SemanticSkillRanker to MAF skill objects in the final prompt.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_PropagatesConfidenceScores()
    {
        throw new NotImplementedException("Mark: Waiting for MAF model integration");
    }

    /// <summary>
    /// When a skill is marked as HIGH confidence, the prompt should reflect this
    /// and the agent should prioritize using it.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_HighConfidenceSkill_VisibleInPrompt()
    {
        throw new NotImplementedException("Mark: Waiting for MAF model integration");
    }

    /// <summary>
    /// When a skill is marked as LOW confidence, verify it is either:
    /// A) Filtered out entirely, or
    /// B) Included with a warning/note that it may be unreliable
    /// (Mark to define which behavior is expected)
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_LowConfidenceSkill_HandledAccordingToThreshold()
    {
        throw new NotImplementedException("Mark: Define minimum confidence threshold");
    }

    /// <summary>
    /// When multiple skills share the same semantic score, confidence level
    /// should be used as tiebreaker (HIGH > MEDIUM > LOW).
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_ConfidenceAsTiebreaker_OrdersSkillsCorrectly()
    {
        throw new NotImplementedException("Mark: Waiting for ranking tiebreaker definition");
    }

    // ── Non-Blocking Fallback: Embedder/Ranker Unavailable ────────────────────────────

    /// <summary>
    /// When SemanticSkillRanker is unavailable (null dependency),
    /// ComposeAsync should fall back gracefully to keyword-only ranking.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_RankerUnavailable_FallsBackToKeywordRanking()
    {
        throw new NotImplementedException("Mark: Waiting for dependency injection pattern");
    }

    /// <summary>
    /// When the embedder (e.g., Ollama) times out (>100ms),
    /// semantic ranking should be skipped and keyword ranking used instead.
    /// Verifies that prompt is still composed with keyword-ranked skills.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_EmbedderTimeout_ReturnsKeywordRankedSkills()
    {
        throw new NotImplementedException("Mark: Waiting for timeout handling implementation");
    }

    /// <summary>
    /// When Ollama embedder is unavailable (connection refused),
    /// skill enrichment should complete without crashing.
    /// Prompt should include keyword-ranked skills as fallback.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_OllamaUnavailable_GracefulFallback()
    {
        throw new NotImplementedException("Mark: Waiting for error handling implementation");
    }

    /// <summary>
    /// When semantic re-ranking throws an exception (e.g., invalid embedding model),
    /// DefaultPromptComposer should log a warning and continue with keyword results.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_SemanticRankerException_LogsWarningAndContinues()
    {
        throw new NotImplementedException("Mark: Waiting for exception handling implementation");
    }

    /// <summary>
    /// When semantic ranking fails, verify the composed prompt does NOT contain
    /// partial/corrupted semantic metadata.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_RankerFailure_PromptConsistency()
    {
        throw new NotImplementedException("Mark: Waiting for consistency validation");
    }

    // ── Performance SLA: <100ms P95 Latency ──────────────────────────────────────────

    /// <summary>
    /// Measure latency of ComposeAsync() with semantic skill enrichment.
    /// P95 latency should remain <100ms (dominated by SemanticSkillRanker timeout).
    /// Requires mock SemanticSkillRanker that completes within ~50ms.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_PerformanceTarget_P95Below100ms()
    {
        throw new NotImplementedException("Mark: Waiting for performance baseline setup");
    }

    /// <summary>
    /// When semantic ranking takes 50ms (normal case), total prompt composition
    /// should complete <100ms.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_WithNormalLatency_CompletesWithinSLA()
    {
        throw new NotImplementedException("Mark: Waiting for latency measurement setup");
    }

    /// <summary>
    /// When semantic ranking is skipped (embedder timeout), ComposeAsync() should
    /// complete in <50ms (keyword ranking only).
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_WithFallback_CompletesQuickly()
    {
        throw new NotImplementedException("Mark: Waiting for fallback latency baseline");
    }

    // ── Idempotency & State Management ──────────────────────────────────────────────

    /// <summary>
    /// Calling EnrichSkillsAsync() (indirectly via ComposeAsync) multiple times
    /// with the same input should return identical results.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_Idempotency_IdenticalInputsYieldIdenticalResults()
    {
        throw new NotImplementedException("Mark: Waiting for state isolation verification");
    }

    /// <summary>
    /// Verify that semantic ranking does not mutate the original skill list.
    /// Re-ranking should return a new ordered list, not modify in-place.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_NoMutation_OriginalSkillsPreserved()
    {
        throw new NotImplementedException("Mark: Waiting for immutability verification");
    }

    /// <summary>
    /// When running ComposeAsync concurrently with different user messages,
    /// each should get correctly re-ranked skills specific to their query.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_ConcurrentRequests_IsolatedResults()
    {
        throw new NotImplementedException("Mark: Waiting for concurrency safety verification");
    }

    // ── Integration with Existing DefaultPromptComposer Behavior ────────────────────

    /// <summary>
    /// Verify that semantic enrichment doesn't break existing system prompt injection.
    /// System prompt should still be first message in composed prompt.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_PreservesSystemPrompt()
    {
        throw new NotImplementedException("Mark: Waiting for integration verification");
    }

    /// <summary>
    /// When semantic enrichment is enabled, existing session summary should
    /// still be included in system prompt without modification.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_PreservesSessionSummary()
    {
        throw new NotImplementedException("Mark: Waiting for integration verification");
    }

    /// <summary>
    /// Verify that conversation history is unaffected by semantic skill enrichment.
    /// History messages should appear in composed prompt in correct order.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_PreservesConversationHistory()
    {
        throw new NotImplementedException("Mark: Waiting for integration verification");
    }

    /// <summary>
    /// When no relevant skills are found (keyword search returned empty),
    /// EnrichSkillsAsync should return empty string, not crash.
    /// ComposeAsync should still compose a valid prompt without skills section.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_NoRelevantSkills_EmptySkillsSection()
    {
        throw new NotImplementedException("Mark: Waiting for empty case handling");
    }

    // ── Logging & Observability ──────────────────────────────────────────────────────

    /// <summary>
    /// Verify that semantic re-ranking logs an information message indicating
    /// how many skills were re-ranked.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_LogsSemanticRankingInfo()
    {
        throw new NotImplementedException("Mark: Waiting for logging implementation");
    }

    /// <summary>
    /// When semantic ranking fails and fallback is used, a warning should be logged.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_LogsWarningOnFallback()
    {
        throw new NotImplementedException("Mark: Waiting for fallback logging implementation");
    }

    /// <summary>
    /// When a low-confidence skill is filtered out, verify it is logged
    /// (so operators can see what was excluded).
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_LogsFilteredLowConfidenceSkills()
    {
        throw new NotImplementedException("Mark: Define filtering logging behavior");
    }

    // ── Edge Cases & Boundary Conditions ─────────────────────────────────────────────

    /// <summary>
    /// When there is exactly 1 relevant skill, semantic re-ranking should
    /// still apply (even if it's a no-op for ordering).
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_SingleSkill_StillRanks()
    {
        throw new NotImplementedException("Mark: Waiting for single-item handling");
    }

    /// <summary>
    /// When there are many (>50) relevant skills, semantic re-ranking should
    /// still complete within P95 latency SLA.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_ManySkills_PerformanceAcceptable()
    {
        throw new NotImplementedException("Mark: Waiting for bulk handling verification");
    }

    /// <summary>
    /// When a skill has very long description/keywords, semantic re-ranking
    /// should handle it without truncation or corruption.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_LongSkillMetadata_HandledCorrectly()
    {
        throw new NotImplementedException("Mark: Waiting for large data handling");
    }

    /// <summary>
    /// When task description is empty string, semantic re-ranking should
    /// fall back gracefully (may return keyword order or empty).
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_EmptyTaskDescription_FallsBackGracefully()
    {
        throw new NotImplementedException("Mark: Waiting for empty input handling");
    }

    /// <summary>
    /// When task description contains special characters or multi-byte unicode,
    /// semantic re-ranking should handle without errors.
    /// </summary>
    [Fact(Skip = "Awaiting Story 3: SemanticSkillRanker integration into DefaultPromptComposer.EnrichSkillsAsync — see issue #89")]
    public async Task EnrichSkillsAsync_SpecialCharactersInTask_HandledCorrectly()
    {
        throw new NotImplementedException("Mark: Waiting for unicode handling");
    }

    // ── Optional: Azure OpenAI Embedder Support ──────────────────────────────────────

    /// <summary>
    /// (OPTIONAL - Mark to define scope)
    /// When using Azure OpenAI embedder instead of Ollama,
    /// semantic re-ranking should complete successfully.
    /// </summary>
    [Fact(Skip = "Optional: Awaiting Azure OpenAI embedder scope definition")]
    public async Task EnrichSkillsAsync_WithAzureOpenAIEmbedder_Succeeds()
    {
        throw new NotImplementedException("Mark: Define Azure OpenAI embedder support scope");
    }

    /// <summary>
    /// (OPTIONAL - Mark to define scope)
    /// Verify latency SLA is maintained when using Azure OpenAI embedder.
    /// </summary>
    [Fact(Skip = "Optional: Awaiting Azure OpenAI embedder scope definition")]
    public async Task EnrichSkillsAsync_WithAzureOpenAIEmbedder_MaintainsLatencySLA()
    {
        throw new NotImplementedException("Mark: Define Azure OpenAI latency baseline");
    }

    // ── Test Fixtures ────────────────────────────────────────────────────────────────

    private sealed class FakeWorkspaceLoader : IWorkspaceLoader
    {
        private readonly BootstrapContext _context;
        public FakeWorkspaceLoader(BootstrapContext context) => _context = context;
        public Task<BootstrapContext> LoadAsync(string workspacePath, CancellationToken ct = default)
            => Task.FromResult(_context);
    }
}

// ── Shared Test Fixtures (used by both unit and E2E tests) ─────────────────────────

/// <summary>
/// Mock SemanticSkillRanker for unit tests.
/// Allows controlled test scenarios:
/// - Success with custom ranking
/// - Timeout simulation
/// - Exception injection
/// - Latency measurement
/// </summary>
public sealed class MockSemanticSkillRanker : ISemanticSkillRanker
{
    public enum MockBehavior
    {
        SuccessWithReranking,
        Timeout,
        EmbedderUnavailable,
        InvalidModel,
        PartialResults
    }

    private readonly MockBehavior _behavior;
    private readonly TimeSpan? _customDelay;
    private Func<IReadOnlyList<SkillSummary>, Task<IReadOnlyList<SkillSummary>>>? _customReranker;

    public MockSemanticSkillRanker(MockBehavior behavior = MockBehavior.SuccessWithReranking, TimeSpan? customDelay = null)
    {
        _behavior = behavior;
        _customDelay = customDelay;
    }

    /// <summary>
    /// Set custom re-ranking logic for tests.
    /// </summary>
    public void SetCustomReranker(Func<IReadOnlyList<SkillSummary>, Task<IReadOnlyList<SkillSummary>>> reranker)
    {
        _customReranker = reranker;
    }

    public async Task<IReadOnlyList<SkillSummary>> RerankAsync(
        string taskDescription,
        IReadOnlyList<SkillSummary> skills,
        CancellationToken cancellationToken = default)
    {
        if (_customDelay.HasValue)
            await Task.Delay(_customDelay.Value, cancellationToken);

        return _behavior switch
        {
            MockBehavior.SuccessWithReranking =>
                _customReranker != null
                    ? await _customReranker(skills)
                    : skills.OrderByDescending(s => (int)s.Confidence).ToList(),

            MockBehavior.Timeout =>
                throw new OperationCanceledException("Semantic search timed out"),

            MockBehavior.EmbedderUnavailable =>
                throw new InvalidOperationException("Embedder (Ollama) unavailable"),

            MockBehavior.InvalidModel =>
                throw new InvalidOperationException("Invalid embedding model specified"),

            MockBehavior.PartialResults =>
                skills.Take(Math.Max(1, skills.Count / 2)).ToList(),

            _ => skills
        };
    }
}

/// <summary>
/// Skill factory for creating test skills with confidence levels.
/// Simplifies test setup with predefined templates.
/// </summary>
public sealed class SkillFactory
{
    public static SkillSummary CreateSkill(
        string name,
        string description = "Test skill",
        ConfidenceLevel confidence = ConfidenceLevel.High,
        string[]? keywords = null)
    {
        return new SkillSummary
        {
            Name = name,
            Description = description,
            Keywords = keywords ?? new[] { name },
            Confidence = confidence,
            ExtractedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ValidatedBy = new[] { "test" }
        };
    }

    public static List<SkillSummary> CreateSkillSet(int count, ConfidenceLevel baseConfidence = ConfidenceLevel.High)
    {
        var skills = new List<SkillSummary>();
        for (int i = 0; i < count; i++)
        {
            skills.Add(CreateSkill($"skill-{i}", $"Test skill {i}", baseConfidence, new[] { $"keyword-{i}" }));
        }
        return skills;
    }

    public static List<SkillSummary> CreateConfidenceMixedSkillSet()
    {
        return new List<SkillSummary>
        {
            CreateSkill("high-confidence-skill", "Important skill", ConfidenceLevel.High),
            CreateSkill("medium-confidence-skill", "Moderate skill", ConfidenceLevel.Medium),
            CreateSkill("low-confidence-skill", "Less reliable skill", ConfidenceLevel.Low)
        };
    }
}

/// <summary>
/// Test scenarios for latency and timeout behavior.
/// Helps test P95 SLA compliance without actual network calls.
/// </summary>
public sealed class TimeoutTestScenarios
{
    public static readonly TimeSpan NormalLatency = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan SlowLatency = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan TimeoutThreshold = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan VerySlowLatency = TimeSpan.FromMilliseconds(150);

    public static MockSemanticSkillRanker CreateNormalLatencyRanker()
        => new(MockSemanticSkillRanker.MockBehavior.SuccessWithReranking, NormalLatency);

    public static MockSemanticSkillRanker CreateSlowLatencyRanker()
        => new(MockSemanticSkillRanker.MockBehavior.SuccessWithReranking, SlowLatency);

    public static MockSemanticSkillRanker CreateTimeoutRanker()
        => new(MockSemanticSkillRanker.MockBehavior.Timeout);

    public static MockSemanticSkillRanker CreateVerySlowLatencyRanker()
        => new(MockSemanticSkillRanker.MockBehavior.SuccessWithReranking, VerySlowLatency);
}
