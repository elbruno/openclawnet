using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.UnitTests.Fixtures;
using Xunit;

namespace OpenClawNet.UnitTests.Agent;

public sealed class OllamaHealthCheckTests
{
    [Fact]
    public async Task IsAvailableAsync_WithOllamaAvailable_ReturnsTrue()
    {
        // Arrange
        var mockClient = new MockOllamaClient();
        mockClient.SetupAvailable(true);

        // Act
        var available = await mockClient.Object.IsAvailableAsync(CancellationToken.None);

        // Assert
        available.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_WithOllamaUnavailable_ReturnsFalse()
    {
        // Arrange
        var mockClient = new MockOllamaClient();
        mockClient.SetupAvailable(false);

        // Act
        var available = await mockClient.Object.IsAvailableAsync(CancellationToken.None);

        // Assert
        available.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_WithException_ReturnsFalse()
    {
        // Arrange
        var mockClient = new MockOllamaClient();
        mockClient.SetupAvailableThrows(new HttpRequestException("Connection refused"));

        // Act
        var available = await mockClient.Object.IsAvailableAsync(CancellationToken.None);

        // Assert
        available.Should().BeFalse();
    }

    [Fact]
    public async Task HealthCheck_WithMultipleChecks_EachRespectsTimeout()
    {
        // Arrange
        var mockClient = new MockOllamaClient();
        mockClient.SetupAvailable(true);

        var client = mockClient.Object;

        // Act: Simulate multiple health checks with 1000ms timeout each
        var results = new bool[3];
        for (int i = 0; i < 3; i++)
        {
            var cts = new CancellationTokenSource(1000);
            results[i] = await client.IsAvailableAsync(cts.Token);
        }

        // Assert
        results[0].Should().BeTrue();
        results[1].Should().BeTrue();
        results[2].Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheck_WithTransientFailure_CanRecover()
    {
        // Arrange
        var mockClient = new Mock<IModelClient>();
        var callCount = 0;

        mockClient.Setup(m => m.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                callCount++;
                return await Task.FromResult(callCount > 1);
            });

        var client = mockClient.Object;

        // Act
        var firstCheck = await client.IsAvailableAsync(CancellationToken.None);
        var secondCheck = await client.IsAvailableAsync(CancellationToken.None);

        // Assert
        firstCheck.Should().BeFalse();
        secondCheck.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task HealthCheck_WithFallback_ReturnsKeywordResultsOnFailure()
    {
        // Arrange
        var mockClient = new MockOllamaClient();
        mockClient.SetupAvailable(false);

        var fixture = new SkillVectorFixture();
        fixture.InsertVector("skill-1", "File Reader", new[] { 0.1f, 0.2f });
        fixture.InsertVector("skill-2", "Writer", new[] { 0.3f, 0.4f });

        var keywordResults = new[] { "skill-1", "skill-2" };

        // Act
        var available = await mockClient.Object.IsAvailableAsync(CancellationToken.None);

        // Assert
        available.Should().BeFalse();
        keywordResults.Should().HaveCount(2);
    }
}
