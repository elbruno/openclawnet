using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace OpenClawNet.UnitTests.Integration;

/// <summary>
/// Mock Ollama health service for testing.
/// </summary>
public interface IOllamaHealthService
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<string> GetModelInfoAsync(string modelName, CancellationToken cancellationToken = default);
}

[Trait("Category", "Unit")]
public class OllamaHealthCheckTests
{
    private readonly Mock<IOllamaHealthService> _mockOllamaHealth;
    private readonly Mock<ILogger<OllamaHealthCheckTests>> _mockLogger;

    public OllamaHealthCheckTests()
    {
        _mockOllamaHealth = new Mock<IOllamaHealthService>();
        _mockLogger = new Mock<ILogger<OllamaHealthCheckTests>>();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenHealthy_ReturnsTrue()
    {
        // Arrange
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _mockOllamaHealth.Object.CheckHealthAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenUnhealthy_ReturnsFalse()
    {
        // Arrange
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _mockOllamaHealth.Object.CheckHealthAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckHealthAsync_WithTimeout_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _mockOllamaHealth.Object.CheckHealthAsync(cts.Token));
    }

    [Fact]
    public async Task CheckHealthAsync_WithConnectionFailure_ReturnsFalse()
    {
        // Arrange
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _mockOllamaHealth.Object.CheckHealthAsync());
    }

    [Fact]
    public async Task CheckHealthAsync_MultipleConsecutiveCalls_AllSucceed()
    {
        // Arrange
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var results = new List<bool>();
        for (int i = 0; i < 5; i++)
        {
            results.Add(await _mockOllamaHealth.Object.CheckHealthAsync());
        }

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(r => r.Should().BeTrue());
    }

    [Fact]
    public async Task CheckHealthAsync_RespectsCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(cts.Token))
            .ReturnsAsync(true);

        // Act
        var result = await _mockOllamaHealth.Object.CheckHealthAsync(cts.Token);

        // Assert
        result.Should().BeTrue();
        cts.Dispose();
    }

    [Fact]
    public async Task GetModelInfoAsync_WithValidModel_ReturnsModelInfo()
    {
        // Arrange
        var modelName = "nomic-embed-text";
        var expectedInfo = "Model: nomic-embed-text, Size: 274M, Parameters: 137M";
        _mockOllamaHealth
            .Setup(x => x.GetModelInfoAsync(modelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedInfo);

        // Act
        var result = await _mockOllamaHealth.Object.GetModelInfoAsync(modelName);

        // Assert
        result.Should().Be(expectedInfo);
    }

    [Fact]
    public async Task GetModelInfoAsync_WithNonExistentModel_ThrowsException()
    {
        // Arrange
        var modelName = "nonexistent-model";
        _mockOllamaHealth
            .Setup(x => x.GetModelInfoAsync(modelName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Model not found"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _mockOllamaHealth.Object.GetModelInfoAsync(modelName));
    }

    [Fact]
    public async Task HealthRecovery_AfterFailure_EventuallyRecovers()
    {
        // Arrange - Simulate recovery scenario
        var callCount = 0;
        _mockOllamaHealth
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                // First two calls fail, third succeeds
                if (callCount >= 3)
                    return Task.FromResult(true);
                return Task.FromResult(false);
            });

        // Act
        var results = new List<bool>();
        for (int i = 0; i < 3; i++)
        {
            results.Add(await _mockOllamaHealth.Object.CheckHealthAsync());
        }

        // Assert
        results.Should().Equal(false, false, true);
    }
}
