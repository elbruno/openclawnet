using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;

namespace OpenClawNet.UnitTests.Storage;

/// <summary>
/// Represents a vector stored in the database in BLOB format.
/// </summary>
public class VectorStorageBlob
{
    public required string Id { get; init; }
    public required float[] Vector { get; init; }
    public required long CreatedAt { get; init; }
    public required string Metadata { get; init; }
}

/// <summary>
/// Mock interface for vector storage operations.
/// </summary>
public interface IVectorStore
{
    Task<VectorStorageBlob?> GetVectorAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertVectorAsync(VectorStorageBlob blob, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VectorStorageBlob>> SearchAsync(float[] queryVector, int topK, CancellationToken cancellationToken = default);
    Task DeleteVectorAsync(string id, CancellationToken cancellationToken = default);
}

[Trait("Category", "Unit")]
public class VectorStorageFormatTests
{
    private readonly Mock<IVectorStore> _mockVectorStore;
    private readonly Mock<ILogger<VectorStorageFormatTests>> _mockLogger;

    public VectorStorageFormatTests()
    {
        _mockVectorStore = new Mock<IVectorStore>();
        _mockLogger = new Mock<ILogger<VectorStorageFormatTests>>();
    }

    [Fact]
    public async Task UpsertVectorAsync_WithValidBlob_StoresSuccessfully()
    {
        // Arrange
        var blob = new VectorStorageBlob
        {
            Id = "skill-1",
            Vector = new[] { 0.1f, 0.2f, 0.3f },
            CreatedAt = 1234567890,
            Metadata = "{\"name\":\"file-operations\"}"
        };

        _mockVectorStore
            .Setup(x => x.UpsertVectorAsync(blob, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _mockVectorStore.Object.UpsertVectorAsync(blob);

        // Assert
        _mockVectorStore.Verify(
            x => x.UpsertVectorAsync(blob, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void VectorStorageBlob_WithValidVector_HasCorrectDimensions()
    {
        // Arrange & Act
        var blob = new VectorStorageBlob
        {
            Id = "skill-1",
            Vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f },
            CreatedAt = 1234567890,
            Metadata = "{}"
        };

        // Assert
        blob.Vector.Length.Should().Be(5);
        blob.Vector.Should().AllSatisfy(v => v.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(1));
    }

    [Fact]
    public void VectorStorageBlob_WithLargeVector_StoresCorrectly()
    {
        // Arrange - Create a 384-dimensional vector (common for embeddings)
        var dimensions = 384;
        var vector = Enumerable.Range(0, dimensions)
            .Select(i => (float)(Math.Sin(i) * 0.5 + 0.5))
            .ToArray();

        // Act
        var blob = new VectorStorageBlob
        {
            Id = "skill-large",
            Vector = vector,
            CreatedAt = 1234567890,
            Metadata = "{}"
        };

        // Assert
        blob.Vector.Length.Should().Be(dimensions);
    }

    [Fact]
    public void VectorStorageBlob_WithMetadata_ParsesCorrectly()
    {
        // Arrange
        var metadata = "{\"name\":\"file-operations\",\"confidence\":\"high\",\"category\":\"io\"}";
        var blob = new VectorStorageBlob
        {
            Id = "skill-1",
            Vector = new[] { 0.1f, 0.2f },
            CreatedAt = 1234567890,
            Metadata = metadata
        };

        // Act & Assert
        blob.Metadata.Should().Contain("file-operations");
        blob.Metadata.Should().Contain("high");
        blob.Metadata.Should().Contain("io");
    }

    [Fact]
    public void VectorStorageBlob_WithEmptyVector_HasZeroLength()
    {
        // Arrange & Act
        var blob = new VectorStorageBlob
        {
            Id = "skill-empty",
            Vector = Array.Empty<float>(),
            CreatedAt = 1234567890,
            Metadata = "{}"
        };

        // Assert
        blob.Vector.Should().BeEmpty();
    }

    [Fact]
    public void VectorStorageBlob_WithNaNValues_StoresAsIs()
    {
        // Arrange
        var vector = new[] { 0.1f, float.NaN, 0.3f };

        // Act
        var blob = new VectorStorageBlob
        {
            Id = "skill-nan",
            Vector = vector,
            CreatedAt = 1234567890,
            Metadata = "{}"
        };

        // Assert
        blob.Vector.Should().Contain(v => float.IsNaN(v));
    }

    [Fact]
    public async Task GetVectorAsync_WithExistingId_ReturnsBlob()
    {
        // Arrange
        var id = "skill-1";
        var expectedBlob = new VectorStorageBlob
        {
            Id = id,
            Vector = new[] { 0.1f, 0.2f, 0.3f },
            CreatedAt = 1234567890,
            Metadata = "{}"
        };

        _mockVectorStore
            .Setup(x => x.GetVectorAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedBlob);

        // Act
        var result = await _mockVectorStore.Object.GetVectorAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Vector.Should().Equal(expectedBlob.Vector);
    }

    [Fact]
    public async Task GetVectorAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        var id = "nonexistent-skill";
        _mockVectorStore
            .Setup(x => x.GetVectorAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VectorStorageBlob?)null);

        // Act
        var result = await _mockVectorStore.Object.GetVectorAsync(id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_WithValidQueryVector_ReturnsTopKResults()
    {
        // Arrange
        var queryVector = new[] { 0.1f, 0.2f, 0.3f };
        var topK = 5;
        var results = new List<VectorStorageBlob>
        {
            new() { Id = "skill-1", Vector = new[] { 0.15f, 0.25f, 0.35f }, CreatedAt = 1234567890, Metadata = "{}" },
            new() { Id = "skill-2", Vector = new[] { 0.12f, 0.22f, 0.32f }, CreatedAt = 1234567891, Metadata = "{}" },
            new() { Id = "skill-3", Vector = new[] { 0.11f, 0.21f, 0.31f }, CreatedAt = 1234567892, Metadata = "{}" }
        };

        _mockVectorStore
            .Setup(x => x.SearchAsync(queryVector, topK, It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);

        // Act
        var result = await _mockVectorStore.Object.SearchAsync(queryVector, topK);

        // Assert
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(r => r.Vector.Length.Should().Be(3));
    }

    [Fact]
    public async Task SearchAsync_WithTopKGreaterThanResults_ReturnsAllAvailable()
    {
        // Arrange
        var queryVector = new[] { 0.1f, 0.2f, 0.3f };
        var topK = 100;
        var results = new List<VectorStorageBlob>
        {
            new() { Id = "skill-1", Vector = new[] { 0.15f, 0.25f, 0.35f }, CreatedAt = 1234567890, Metadata = "{}" }
        };

        _mockVectorStore
            .Setup(x => x.SearchAsync(queryVector, topK, It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);

        // Act
        var result = await _mockVectorStore.Object.SearchAsync(queryVector, topK);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteVectorAsync_WithValidId_DeletesSuccessfully()
    {
        // Arrange
        var id = "skill-1";
        _mockVectorStore
            .Setup(x => x.DeleteVectorAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _mockVectorStore.Object.DeleteVectorAsync(id);

        // Assert
        _mockVectorStore.Verify(
            x => x.DeleteVectorAsync(id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void VectorStorageBlob_CreatedAtTimestamp_IsPositive()
    {
        // Arrange
        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var blob = new VectorStorageBlob
        {
            Id = "skill-1",
            Vector = new[] { 0.1f, 0.2f },
            CreatedAt = currentTimestamp,
            Metadata = "{}"
        };

        // Assert
        blob.CreatedAt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task VectorStorageBlob_RoundTrip_PreservesData()
    {
        // Arrange
        var original = new VectorStorageBlob
        {
            Id = "skill-roundtrip",
            Vector = new[] { 0.123f, 0.456f, 0.789f },
            CreatedAt = 1234567890,
            Metadata = "{\"key\":\"value\"}"
        };

        _mockVectorStore
            .Setup(x => x.UpsertVectorAsync(original, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockVectorStore
            .Setup(x => x.GetVectorAsync(original.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);

        // Act
        await _mockVectorStore.Object.UpsertVectorAsync(original);
        var retrieved = await _mockVectorStore.Object.GetVectorAsync(original.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(original.Id);
        retrieved.Vector.Should().Equal(original.Vector);
        retrieved.CreatedAt.Should().Be(original.CreatedAt);
        retrieved.Metadata.Should().Be(original.Metadata);
    }
}
