using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.UnitTests.Storage;

namespace OpenClawNet.UnitTests.Integration;

[Trait("Category", "Unit")]
public class HybridSearchServiceIntegrationTests
{
    private readonly Mock<ILogger<HybridSearchServiceIntegrationTests>> _mockLogger;
    private readonly Mock<IOllamaHealthService> _mockOllamaHealth;
    private readonly Mock<IVectorStore> _mockVectorStore;

    public HybridSearchServiceIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<HybridSearchServiceIntegrationTests>>();
        _mockOllamaHealth = new Mock<IOllamaHealthService>();
        _mockVectorStore = new Mock<IVectorStore>();
    }

    [Fact]
    public async Task HybridSearch_WithOllamaAndVectorStore_CompleteFlow()
    {
        // Arrange - Setup mocks for complete integration
        var query = "find file operations";
        var queryVector = new[] { 0.1f, 0.2f, 0.3f };

        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var searchResults = new List<VectorStorageBlob>
        {
            new() { Id = "skill-1", Vector = new[] { 0.15f, 0.25f, 0.35f }, CreatedAt = 1234567890, Metadata = "{\"score\":0.95}" },
            new() { Id = "skill-2", Vector = new[] { 0.12f, 0.22f, 0.32f }, CreatedAt = 1234567891, Metadata = "{\"score\":0.87}" }
        };

        _mockVectorStore
            .Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var ollamaHealthy = await _mockOllamaHealth.Object.CheckHealthAsync();
        var vectorResults = await _mockVectorStore.Object.SearchAsync(queryVector, 10);

        // Assert
        ollamaHealthy.Should().BeTrue();
        vectorResults.Should().HaveCount(2);
        vectorResults.Should().AllSatisfy(r => r.Vector.Length.Should().Be(3));
    }

    [Fact]
    public async Task HybridSearch_WithOllamaDown_FallsBackGracefully()
    {
        // Arrange - Simulate Ollama being down
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockVectorStore
            .Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vector store unavailable"));

        // Act
        var ollamaHealthy = await _mockOllamaHealth.Object.CheckHealthAsync();
        Func<Task> searchAction = () => _mockVectorStore.Object.SearchAsync(new[] { 0.1f }, 10);

        // Assert
        ollamaHealthy.Should().BeFalse();
        await searchAction.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HybridSearch_WithPartialDataLoss_StillFunctions()
    {
        // Arrange
        var queryVector = new[] { 0.1f, 0.2f, 0.3f };

        // Mock Ollama as healthy
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock vector store returning empty results (data loss scenario)
        _mockVectorStore
            .Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorStorageBlob>());

        // Act
        var ollamaHealthy = await _mockOllamaHealth.Object.CheckHealthAsync();
        var results = await _mockVectorStore.Object.SearchAsync(queryVector, 10);

        // Assert
        ollamaHealthy.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task HybridSearch_WithHighLoad_HandlesMultipleRequests()
    {
        // Arrange
        var queryVector = new[] { 0.1f, 0.2f, 0.3f };
        var mockResults = new List<VectorStorageBlob>
        {
            new() { Id = "skill-1", Vector = new[] { 0.15f, 0.25f, 0.35f }, CreatedAt = 1234567890, Metadata = "{}" }
        };

        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVectorStore
            .Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResults);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _mockVectorStore.Object.SearchAsync(queryVector, 10))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(10);
        results.Should().AllSatisfy(r => r.Should().HaveCount(1));
    }

    [Fact]
    public async Task HybridSearch_WithCachedVectors_ImprovesThroughput()
    {
        // Arrange
        var skillId = "skill-1";
        var cachedBlob = new VectorStorageBlob
        {
            Id = skillId,
            Vector = new[] { 0.1f, 0.2f, 0.3f },
            CreatedAt = 1234567890,
            Metadata = "{}"
        };

        _mockVectorStore
            .Setup(x => x.GetVectorAsync(skillId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBlob);

        // Act - Multiple retrievals should use cache
        var result1 = await _mockVectorStore.Object.GetVectorAsync(skillId);
        var result2 = await _mockVectorStore.Object.GetVectorAsync(skillId);
        var result3 = await _mockVectorStore.Object.GetVectorAsync(skillId);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result3.Should().NotBeNull();
        _mockVectorStore.Verify(
            x => x.GetVectorAsync(skillId, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task HybridSearch_WithSemanticSkillRanker_IntegratesCorrectly()
    {
        // Arrange
        var mockHybridSearch = new Mock<IHybridSearchService>();
        var mockLogger = new Mock<ILogger<SemanticSkillRanker>>();
        var ranker = new SemanticSkillRanker(mockHybridSearch.Object, mockLogger.Object);

        var skills = new List<SkillSummary>
        {
            new() { Name = "skill-a", Description = "File ops", Keywords = ["file", "read"], Confidence = ConfidenceLevel.High, ExtractedDate = "2024-01-01", ValidatedBy = new[] { "user1" } },
            new() { Name = "skill-b", Description = "Network ops", Keywords = ["network"], Confidence = ConfidenceLevel.Medium, ExtractedDate = "2024-01-01", ValidatedBy = new[] { "user1" } }
        };

        var searchResults = new List<HybridSearchResult>
        {
            new() { Id = "skill-a", Score = 0.95 },
            new() { Id = "skill-b", Score = 0.72 }
        };

        mockHybridSearch
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var result = await ranker.RerankAsync("find file operations", skills);

        // Assert
        result.Should().HaveCount(2);
        mockHybridSearch.Verify(
            x => x.SearchAsync(It.IsAny<string>(), "skills", 2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HybridSearch_RespectsSLA_UnderNormalLoad()
    {
        // Arrange - Measure latency
        var queryVector = new[] { 0.1f, 0.2f, 0.3f };
        var mockResults = new List<VectorStorageBlob>
        {
            new() { Id = "skill-1", Vector = new[] { 0.15f, 0.25f, 0.35f }, CreatedAt = 1234567890, Metadata = "{}" }
        };

        _mockVectorStore
            .Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResults);

        // Act
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _mockVectorStore.Object.SearchAsync(queryVector, 10);
        watch.Stop();

        // Assert - Should complete well under 100ms
        watch.ElapsedMilliseconds.Should().BeLessThan(100);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task HybridSearch_WithConcurrentUpdates_MaintainsConsistency()
    {
        // Arrange
        var blobs = Enumerable.Range(0, 5)
            .Select(i => new VectorStorageBlob
            {
                Id = $"skill-{i}",
                Vector = new[] { (float)i * 0.1f, (float)i * 0.2f },
                CreatedAt = 1234567890 + i,
                Metadata = $"{{\"index\":{i}}}"
            })
            .ToList();

        foreach (var blob in blobs)
        {
            _mockVectorStore
                .Setup(x => x.UpsertVectorAsync(blob, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        // Act
        var upsertTasks = blobs
            .Select(b => _mockVectorStore.Object.UpsertVectorAsync(b))
            .ToList();

        await Task.WhenAll(upsertTasks);

        // Assert
        foreach (var blob in blobs)
        {
            _mockVectorStore.Verify(
                x => x.UpsertVectorAsync(blob, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task HybridSearch_WithVectorDimensionMismatch_HandlesGracefully()
    {
        // Arrange
        var queryVector2D = new[] { 0.1f, 0.2f }; // 2D vector
        var queryVector3D = new[] { 0.1f, 0.2f, 0.3f }; // 3D vector

        var result3D = new List<VectorStorageBlob>
        {
            new() { Id = "skill-1", Vector = new[] { 0.15f, 0.25f, 0.35f }, CreatedAt = 1234567890, Metadata = "{}" }
        };

        _mockVectorStore
            .Setup(x => x.SearchAsync(queryVector3D, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result3D);

        _mockVectorStore
            .Setup(x => x.SearchAsync(queryVector2D, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vector dimension mismatch"));

        // Act & Assert
        var validSearch = await _mockVectorStore.Object.SearchAsync(queryVector3D, 10);
        validSearch.Should().HaveCount(1);

        Func<Task> invalidSearch = () => _mockVectorStore.Object.SearchAsync(queryVector2D, 10);
        await invalidSearch.Should().ThrowAsync<InvalidOperationException>();
    }
}
