/// <summary>
/// Shared test fixtures for both unit and E2E tests of Story 3 semantic skill enrichment.
/// These fixtures support:
/// - Mock SemanticSkillRanker with behavior injection
/// - Skill factory for creating test data
/// - Timeout/latency scenarios for performance testing
/// - Confidence-aware skill builders
/// </summary>

using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Tests.Fixtures;

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

    /// <summary>
    /// Create a realistic set of file-operation related skills.
    /// Useful for semantic ranking tests that verify domain-specific matching.
    /// </summary>
    public static List<SkillSummary> CreateFileOperationSkillSet()
    {
        return new List<SkillSummary>
        {
            CreateSkill(
                "file-read-operations",
                "Techniques for reading files safely and efficiently",
                ConfidenceLevel.High,
                new[] { "file", "read", "I/O" }),
            CreateSkill(
                "file-write-operations",
                "Best practices for writing files with error handling",
                ConfidenceLevel.High,
                new[] { "file", "write", "I/O" }),
            CreateSkill(
                "file-permissions",
                "Managing file permissions and access control",
                ConfidenceLevel.Medium,
                new[] { "file", "permissions", "security" }),
            CreateSkill(
                "batch-file-processing",
                "Processing multiple files in bulk operations",
                ConfidenceLevel.High,
                new[] { "file", "batch", "processing" })
        };
    }

    /// <summary>
    /// Create a realistic set of security-related skills.
    /// Useful for semantic ranking tests that verify domain-specific matching.
    /// </summary>
    public static List<SkillSummary> CreateSecuritySkillSet()
    {
        return new List<SkillSummary>
        {
            CreateSkill(
                "encryption-basics",
                "Understanding and implementing encryption",
                ConfidenceLevel.High,
                new[] { "encryption", "security", "cryptography" }),
            CreateSkill(
                "authentication-patterns",
                "Implementing secure authentication mechanisms",
                ConfidenceLevel.High,
                new[] { "authentication", "security", "auth" }),
            CreateSkill(
                "input-validation",
                "Validating user input to prevent injection attacks",
                ConfidenceLevel.High,
                new[] { "validation", "security", "input" }),
            CreateSkill(
                "secret-management",
                "Securely managing secrets and credentials",
                ConfidenceLevel.Medium,
                new[] { "secrets", "credentials", "security" })
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
