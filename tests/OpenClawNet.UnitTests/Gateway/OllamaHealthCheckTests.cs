using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using OpenClawNet.Gateway.Services;

namespace OpenClawNet.UnitTests.Gateway;

public sealed class OllamaHealthCheckTests
{
    [Fact]
    public async Task IsHealthyAsync_WhenOllamaReturnsSuccess_ReturnsTrue()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.OK })
            .Verifiable();

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var logger = NullLogger<OllamaHealthCheck>.Instance;
        var healthCheck = new OllamaHealthCheck(httpClient, logger);

        // Act
        var result = await healthCheck.IsHealthyAsync();

        // Assert
        result.Should().BeTrue();
        mockHttpMessageHandler.Verify();
    }

    [Fact]
    public async Task IsHealthyAsync_WhenOllamaReturnsError_ReturnsFalse()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.ServiceUnavailable })
            .Verifiable();

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var logger = NullLogger<OllamaHealthCheck>.Instance;
        var healthCheck = new OllamaHealthCheck(httpClient, logger);

        // Act
        var result = await healthCheck.IsHealthyAsync();

        // Assert
        result.Should().BeFalse();
        mockHttpMessageHandler.Verify();
    }

    [Fact]
    public async Task IsHealthyAsync_WhenOllamaThrowsException_ReturnsFalse()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"))
            .Verifiable();

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var logger = NullLogger<OllamaHealthCheck>.Instance;
        var healthCheck = new OllamaHealthCheck(httpClient, logger);

        // Act
        var result = await healthCheck.IsHealthyAsync();

        // Assert
        result.Should().BeFalse();
        mockHttpMessageHandler.Verify();
    }

    [Fact]
    public async Task IsHealthyAsync_QueuesOllamaTagsEndpoint()
    {
        // Arrange
        var capturedRequest = (HttpRequestMessage?)null;
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.OK })
            .Verifiable();

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var logger = NullLogger<OllamaHealthCheck>.Instance;
        var healthCheck = new OllamaHealthCheck(httpClient, logger);

        // Act
        await healthCheck.IsHealthyAsync();

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri.Should().Be(new Uri("http://localhost:11434/api/tags"));
        mockHttpMessageHandler.Verify();
    }
}
