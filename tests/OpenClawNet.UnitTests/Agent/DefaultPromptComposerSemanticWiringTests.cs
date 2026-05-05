using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Story 3 / plan #89 — verifies that DefaultPromptComposer.EnrichSkillsAsync
/// invokes ISemanticSkillRanker when registered, and falls back gracefully
/// to keyword-only ranking when the ranker is missing or fails.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Story", "Story3-SemanticEnrichment")]
public class DefaultPromptComposerSemanticWiringTests
{
    private static readonly IWorkspaceLoader NoOpWorkspaceLoader =
        new FakeWorkspaceLoader(new BootstrapContext(null, null, null));
    private static readonly IOptions<WorkspaceOptions> DefaultWorkspaceOptions =
        Options.Create(new WorkspaceOptions());

    [Fact]
    public async Task EnrichSkillsAsync_WithRanker_ReturnsSkillsInRerankedOrder()
    {
        var keywordOrdered = new[]
        {
            SkillFactory.CreateSkill("alpha", confidence: ConfidenceLevel.Low),
            SkillFactory.CreateSkill("beta",  confidence: ConfidenceLevel.Medium),
            SkillFactory.CreateSkill("gamma", confidence: ConfidenceLevel.High),
        };

        var skillService = new RecordingSkillService(keywordOrdered);
        var ranker = new ReversingRanker();

        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            skillService,
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions,
            ranker);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "deploy alpha beta gamma"
        };

        var messages = await composer.ComposeAsync(context);
        var systemContent = messages[0].Content!;

        ranker.Invocations.Should().Be(1, "the composer must invoke the ranker exactly once");
        ranker.LastInputOrder.Should().Equal(new[] { "alpha", "beta", "gamma" });

        var alphaIdx = systemContent.IndexOf("**alpha**", StringComparison.Ordinal);
        var gammaIdx = systemContent.IndexOf("**gamma**", StringComparison.Ordinal);
        gammaIdx.Should().BePositive();
        alphaIdx.Should().BePositive();
        gammaIdx.Should().BeLessThan(alphaIdx,
            "ranker reverses the keyword order so gamma should appear before alpha in the prompt");
        systemContent.Should().Contain("[semantic-ranked]");
    }

    [Fact]
    public async Task EnrichSkillsAsync_WithoutRanker_FallsBackToKeywordOrder()
    {
        var keywordOrdered = new[]
        {
            SkillFactory.CreateSkill("alpha", confidence: ConfidenceLevel.High),
            SkillFactory.CreateSkill("beta",  confidence: ConfidenceLevel.Medium),
        };

        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            new RecordingSkillService(keywordOrdered),
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions,
            semanticRanker: null);

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "alpha beta"
        };

        var messages = await composer.ComposeAsync(context);
        var systemContent = messages[0].Content!;

        var alphaIdx = systemContent.IndexOf("**alpha**", StringComparison.Ordinal);
        var betaIdx  = systemContent.IndexOf("**beta**",  StringComparison.Ordinal);
        alphaIdx.Should().BePositive();
        betaIdx.Should().BePositive();
        alphaIdx.Should().BeLessThan(betaIdx, "without a ranker, keyword order must be preserved");
        systemContent.Should().NotContain("[semantic-ranked]",
            "no semantic metadata should be emitted on the keyword-only fallback path");
    }

    [Fact]
    public async Task EnrichSkillsAsync_WhenRankerThrows_FallsBackToKeywordOrder()
    {
        var keywordOrdered = new[]
        {
            SkillFactory.CreateSkill("alpha", confidence: ConfidenceLevel.High),
            SkillFactory.CreateSkill("beta",  confidence: ConfidenceLevel.Medium),
        };

        var composer = new DefaultPromptComposer(
            NoOpWorkspaceLoader,
            new RecordingSkillService(keywordOrdered),
            NullLogger<DefaultPromptComposer>.Instance,
            DefaultWorkspaceOptions,
            new MockSemanticSkillRanker(MockSemanticSkillRanker.MockBehavior.EmbedderUnavailable));

        var context = new PromptContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "alpha beta"
        };

        var messages = await composer.ComposeAsync(context);
        var systemContent = messages[0].Content!;

        var alphaIdx = systemContent.IndexOf("**alpha**", StringComparison.Ordinal);
        var betaIdx  = systemContent.IndexOf("**beta**",  StringComparison.Ordinal);
        alphaIdx.Should().BePositive();
        betaIdx.Should().BePositive();
        alphaIdx.Should().BeLessThan(betaIdx, "ranker failure must not crash and must preserve keyword order");
        systemContent.Should().NotContain("[semantic-ranked]");
    }

    // ── Test doubles ────────────────────────────────────────────────────────

    private sealed class FakeWorkspaceLoader : IWorkspaceLoader
    {
        private readonly BootstrapContext _context;
        public FakeWorkspaceLoader(BootstrapContext context) => _context = context;
        public Task<BootstrapContext> LoadAsync(string workspacePath, CancellationToken ct = default)
            => Task.FromResult(_context);
    }

    private sealed class RecordingSkillService : ISkillService
    {
        private readonly IReadOnlyList<SkillSummary> _skills;
        public RecordingSkillService(IReadOnlyList<SkillSummary> skills) => _skills = skills;
        public Task<IReadOnlyList<SkillSummary>> FindRelevantSkillsAsync(
            string taskDescription, int topN = 3, CancellationToken cancellationToken = default)
            => Task.FromResult(_skills);
    }

    /// <summary>
    /// Test ranker that reverses the input order and stamps semantic metadata.
    /// Acts as the IEmbeddingGenerator-adapter equivalent for the composer-level
    /// wiring contract: we only need to prove the composer hands off to the ranker
    /// and consumes its output, not exercise the underlying embedder.
    /// </summary>
    private sealed class ReversingRanker : ISemanticSkillRanker
    {
        public int Invocations { get; private set; }
        public IReadOnlyList<string> LastInputOrder { get; private set; } = Array.Empty<string>();

        public Task<IReadOnlyList<SkillSummary>> RerankAsync(
            string taskDescription,
            IReadOnlyList<SkillSummary> skills,
            CancellationToken cancellationToken = default)
        {
            Invocations++;
            LastInputOrder = skills.Select(s => s.Name).ToList();
            var reversed = skills.Reverse().Select((s, i) => new SkillSummary
            {
                Name = s.Name,
                Description = s.Description,
                Keywords = s.Keywords,
                Confidence = s.Confidence,
                ExtractedDate = s.ExtractedDate,
                ValidatedBy = s.ValidatedBy,
                RelevanceScore = s.RelevanceScore,
                SemanticScore = 1.0 / (60.0 + i),
                IsSemanticRanked = true
            }).ToList();
            return Task.FromResult<IReadOnlyList<SkillSummary>>(reversed);
        }
    }
}
