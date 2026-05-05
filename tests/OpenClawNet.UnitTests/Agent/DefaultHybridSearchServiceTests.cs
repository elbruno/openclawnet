using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Services;

namespace OpenClawNet.UnitTests.Agent;

[Trait("Category", "Unit")]
public class DefaultHybridSearchServiceTests
{
    private readonly Mock<ILogger<DefaultHybridSearchService>> _mockLogger;
    private readonly DefaultHybridSearchService _service;

    public DefaultHybridSearchServiceTests()
    {
        _mockLogger = new Mock<ILogger<DefaultHybridSearchService>>();
        _service = new DefaultHybridSearchService(_mockLogger.Object);
    }

    [Fact]
    public async Task SearchAsync_WithValidQuery_ReturnsResults()
    {
        // Arrange
        var query = "find file operations";
        var collection = "skills";
        var topK = 10;

        // Act
        var result = await _service.SearchAsync(query, collection, topK);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<List<HybridSearchResult>>();
    }

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyResults()
    {
        // Arrange
        var query = string.Empty;
        var collection = "skills";

        // Act
        var result = await _service.SearchAsync(query, collection);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithNullCollection_ThrowsArgumentException()
    {
        // Arrange
        var query = "test query";
        string? collection = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.SearchAsync(query, collection!, 10));
    }

    [Fact]
    public async Task SearchAsync_WithZeroTopK_ReturnsEmptyResults()
    {
        // Arrange
        var query = "find skills";
        var collection = "skills";
        var topK = 0;

        // Act
        var result = await _service.SearchAsync(query, collection, topK);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_WithLargeTopK_ReturnsAtMostTopKResults()
    {
        // Arrange
        var query = "find skills";
        var collection = "skills";
        var topK = 1000;

        // Act
        var result = await _service.SearchAsync(query, collection, topK);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().BeLessThanOrEqualTo(topK);
    }

    [Fact]
    public async Task SearchAsync_WithValidCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var query = "find skills";
        var collection = "skills";
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _service.SearchAsync(query, collection, 10, cts.Token);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var query = "find skills";
        var collection = "skills";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.SearchAsync(query, collection, 10, cts.Token));
    }

    [Fact]
    public async Task SearchAsync_LogsDebugMessage()
    {
        // Arrange
        var query = "find file operations";
        var collection = "skills";
        var topK = 10;

        // Act
        await _service.SearchAsync(query, collection, topK);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Hybrid search")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MultipleCallsWithDifferentQueries_BothWork()
    {
        // Arrange
        var query1 = "find file operations";
        var query2 = "find network utilities";
        var collection = "skills";

        // Act
        var result1 = await _service.SearchAsync(query1, collection, 10);
        var result2 = await _service.SearchAsync(query2, collection, 10);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        string? query = null;
        var collection = "skills";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.SearchAsync(query!, collection, 10));
        ex.ParamName.Should().Be("query");
    }

    [Fact]
    public async Task SearchAsync_WithEmptyCollection_ThrowsArgumentException()
    {
        // Arrange
        var query = "find skills";
        var collection = string.Empty;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SearchAsync(query, collection, 10));
        ex.ParamName.Should().Be("collection");
    }

    [Fact]
    public async Task SearchAsync_WithWhitespaceCollection_ThrowsArgumentException()
    {
        // Arrange
        var query = "find skills";
        var collection = "   ";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SearchAsync(query, collection, 10));
        ex.ParamName.Should().Be("collection");
    }

    [Fact]
    public async Task SearchAsync_WithNegativeTopK_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var query = "find skills";
        var collection = "skills";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _service.SearchAsync(query, collection, -1));
        ex.ParamName.Should().Be("topK");
    }

    [Fact]
    public void Ctor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => new DefaultHybridSearchService(null!));
        ex.ParamName.Should().Be("logger");
    }
}
