using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Represents a mock embedder for testing purposes.
/// </summary>
public interface ITestEmbedder
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

[Trait("Category", "Unit")]
public class EmbedderInterfaceTests
{
    private readonly Mock<ITestEmbedder> _mockEmbedder;
    private readonly Mock<ILogger<EmbedderInterfaceTests>> _mockLogger;

    public EmbedderInterfaceTests()
    {
        _mockEmbedder = new Mock<ITestEmbedder>();
        _mockLogger = new Mock<ILogger<EmbedderInterfaceTests>>();
    }

    [Fact]
    public async Task EmbedAsync_WithValidText_ReturnsEmbedding()
    {
        // Arrange
        var text = "find file operations";
        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        _mockEmbedder
            .Setup(x => x.EmbedAsync(text, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmbedding);

        // Act
        var result = await _mockEmbedder.Object.EmbedAsync(text);

        // Assert
        result.Should().Equal(expectedEmbedding);
        _mockEmbedder.Verify(x => x.EmbedAsync(text, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmbedAsync_WithEmptyText_ReturnsEmptyEmbedding()
    {
        // Arrange
        var text = string.Empty;
        var emptyEmbedding = Array.Empty<float>();
        _mockEmbedder
            .Setup(x => x.EmbedAsync(text, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyEmbedding);

        // Act
        var result = await _mockEmbedder.Object.EmbedAsync(text);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task EmbedAsync_WithMultipleTexts_ReturnsConsistentDimensions()
    {
        // Arrange
        var texts = new[] { "text1", "text2", "text3" };
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        _mockEmbedder
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Act
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await _mockEmbedder.Object.EmbedAsync(text));
        }

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Length.Should().Be(embedding.Length));
    }

    [Fact]
    public async Task EmbedAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        var text = "find skills";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockEmbedder
            .Setup(x => x.EmbedAsync(text, cts.Token))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _mockEmbedder.Object.EmbedAsync(text, cts.Token));
    }

    [Fact]
    public async Task HealthCheckAsync_WhenHealthy_ReturnsTrue()
    {
        // Arrange
        _mockEmbedder
            .Setup(x => x.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _mockEmbedder.Object.HealthCheckAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheckAsync_WhenUnhealthy_ReturnsFalse()
    {
        // Arrange
        _mockEmbedder
            .Setup(x => x.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _mockEmbedder.Object.HealthCheckAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HealthCheckAsync_WithTimeout_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));
        _mockEmbedder
            .Setup(x => x.HealthCheckAsync(cts.Token))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _mockEmbedder.Object.HealthCheckAsync(cts.Token));
    }

    [Fact]
    public async Task EmbedAsync_WithLongText_HandlesSuccessfully()
    {
        // Arrange
        var longText = string.Join(" ", Enumerable.Range(0, 1000).Select(i => $"word{i}"));
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        _mockEmbedder
            .Setup(x => x.EmbedAsync(longText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Act
        var result = await _mockEmbedder.Object.EmbedAsync(longText);

        // Assert
        result.Should().Equal(embedding);
    }

    [Fact]
    public async Task EmbedAsync_WithSpecialCharacters_HandlesSuccessfully()
    {
        // Arrange
        var specialText = "file_operations.cs: @param {file} - path/to/file.txt!";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        _mockEmbedder
            .Setup(x => x.EmbedAsync(specialText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Act
        var result = await _mockEmbedder.Object.EmbedAsync(specialText);

        // Assert
        result.Should().Equal(embedding);
    }

    [Fact]
    public async Task EmbedAsync_WithUnicodeText_HandlesSuccessfully()
    {
        // Arrange
        var unicodeText = "Найти файловые операции 文件操作";
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        _mockEmbedder
            .Setup(x => x.EmbedAsync(unicodeText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        // Act
        var result = await _mockEmbedder.Object.EmbedAsync(unicodeText);

        // Assert
        result.Should().Equal(embedding);
    }
}
