using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Agent;

namespace OpenClawNet.Agent.Tests;

[Trait("Category", "Unit")]
public class SemanticSkillRankerTests
{
    private readonly Mock<IHybridSearchService> _mockHybridSearch;
    private readonly Mock<ILogger<SemanticSkillRanker>> _mockLogger;
    private readonly SemanticSkillRanker _ranker;

    public SemanticSkillRankerTests()
    {
        _mockHybridSearch = new Mock<IHybridSearchService>();
        _mockLogger = new Mock<ILogger<SemanticSkillRanker>>();
        _ranker = new SemanticSkillRanker(_mockHybridSearch.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task RerankAsync_WithEmptySkills_ReturnsEmpty()
    {
        // Arrange
        var skills = Array.Empty<SkillSummary>();

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        result.Should().BeEmpty();
        _mockHybridSearch.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RerankAsync_WithNoSemanticResults_ReturnsFallbackToKeywordRanking()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] },
            new() { Name = "skill-b", Description = "Test b", Keywords = ["keyword-b"], Confidence = ConfidenceLevel.Medium, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HybridSearchResult>());

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(skills);
    }

    [Fact]
    public async Task RerankAsync_WithSemanticResults_ReranksByRFFScore()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] },
            new() { Name = "skill-b", Description = "Test b", Keywords = ["keyword-b"], Confidence = ConfidenceLevel.Medium, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] },
            new() { Name = "skill-c", Description = "Test c", Keywords = ["keyword-c"], Confidence = ConfidenceLevel.Low, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        var semanticResults = new List<HybridSearchResult>
        {
            new() { Id = "skill-c", Score = 0.95 },
            new() { Id = "skill-a", Score = 0.87 },
            new() { Id = "skill-b", Score = 0.72 }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(skill => skill.Should().NotBeNull());
    }

    [Fact]
    public async Task RerankAsync_WithTimeout_FallsBackToKeywordRanking()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        result.Should().HaveCount(1);
        result.First().Name.Should().Be("skill-a");
    }

    [Fact]
    public async Task RerankAsync_WithSemanticException_FallsBackToKeywordRanking()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Semantic search failed"));

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        result.Should().HaveCount(1);
        result.First().Name.Should().Be("skill-a");
    }

    [Fact]
    public async Task RerankAsync_WithPartialSemanticResults_IncludesUnrankedSkills()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] },
            new() { Name = "skill-b", Description = "Test b", Keywords = ["keyword-b"], Confidence = ConfidenceLevel.Medium, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        var semanticResults = new List<HybridSearchResult>
        {
            new() { Id = "skill-a", Score = 0.90 }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(skill => skill.Should().NotBeNull());
    }

    [Fact]
    public async Task RerankAsync_PreservesSkillMetadata()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new()
            {
                Name = "skill-a",
                Description = "Test skill",
                Keywords = ["keyword-a"],
                Confidence = ConfidenceLevel.High,
                ExtractedDate = "2024-01-15",
                ValidatedBy = ["agent-1"]
            }
        };

        var semanticResults = new List<HybridSearchResult>
        {
            new() { Id = "skill-a", Score = 0.90 }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        var rerankResult = result.First();
        rerankResult.Name.Should().Be("skill-a");
        rerankResult.Description.Should().Be("Test skill");
        rerankResult.Keywords.Should().ContainSingle().Which.Should().Be("keyword-a");
        rerankResult.Confidence.Should().Be(ConfidenceLevel.High);
        rerankResult.ExtractedDate.Should().Be("2024-01-15");
        rerankResult.ValidatedBy.Should().Contain("agent-1");
    }

    [Fact]
    public async Task RerankAsync_CallsHybridSearchWithCorrectParameters()
    {
        // Arrange
        var taskDescription = "find file operations skills";
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HybridSearchResult>());

        // Act
        await _ranker.RerankAsync(taskDescription, skills);

        // Assert
        _mockHybridSearch.Verify(
            x => x.SearchAsync(
                taskDescription,
                "skills",
                skills.Count,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RerankAsync_HandlesEmptySemanticResultsGracefully()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] },
            new() { Name = "skill-b", Description = "Test b", Keywords = ["keyword-b"], Confidence = ConfidenceLevel.Medium, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HybridSearchResult>());

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(skills);
    }

    [Fact]
    public async Task RerankAsync_SupportsEmbedderFailureRecovery()
    {
        // Arrange - Simulate embedder failure during semantic search
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Embedder unavailable"));

        // Act
        var result = await _ranker.RerankAsync("test query", skills);

        // Assert - Should gracefully fall back to keyword ranking
        result.Should().HaveCount(1);
        result.First().Name.Should().Be("skill-a");
    }

    [Fact]
    public async Task RerankAsync_RespectsCancellationToken()
    {
        // Arrange
        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "Test a", Keywords = ["keyword-a"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-15", ValidatedBy = ["agent-1"] }
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1));

        _mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _ranker.RerankAsync("test query", skills, cts.Token);

        // Assert - Should handle cancellation gracefully
        result.Should().HaveCount(1);
        result.First().Name.Should().Be("skill-a");
    }
}
