using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenClawNet.Channels.Adapters;
using System.Net;

namespace OpenClawNet.UnitTests.Channels;

public sealed class GenericWebhookAdapterTests
{
    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockLogger<GenericWebhookAdapter> _logger;
    private readonly GenericWebhookAdapter _adapter;

    public GenericWebhookAdapterTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _logger = new MockLogger<GenericWebhookAdapter>();
        _adapter = new GenericWebhookAdapter(_httpClientFactory.CreateClient(), _logger);
    }

    [Fact]
    public void Name_ReturnsGenericWebhook()
    {
        _adapter.Name.Should().Be("GenericWebhook");
    }

    [Fact]
    public async Task DeliverAsync_WithValidWebhookUrl_PostsSuccessfully()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://webhook.example.com/notify";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var adapter = new GenericWebhookAdapter(httpClient, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeTrue();
        handler.RequestUri.Should().Be(webhookUrl);
        handler.RequestContent.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeliverAsync_WithHttpError_ReturnsFailureResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://webhook.example.com/notify";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);

        var adapter = new GenericWebhookAdapter(httpClient, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task DeliverAsync_WithMissingWebhookUrl_ReturnsFailureResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "";

        // Act
        var result = await _adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeliverAsync_WithInvalidWebhookUrl_ReturnsFailureResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "not-a-valid-url";

        // Act
        var result = await _adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid webhook URL");
    }

    [Fact]
    public async Task DeliverAsync_WithNetworkError_ReturnsFailureResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://webhook.example.com/notify";

        var handler = new MockHttpMessageHandler();
        handler.SetupThrowException(new HttpRequestException("Network error"));
        var httpClient = new HttpClient(handler);

        var adapter = new GenericWebhookAdapter(httpClient, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeliverAsync_LogsSuccessfulDelivery()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://webhook.example.com/notify";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var adapter = new GenericWebhookAdapter(httpClient, _logger);

        // Act
        await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        _logger.LogCalls.Should().Contain(c => c.Level == LogLevel.Information);
    }

    [Fact]
    public async Task DeliverAsync_LogsErrorOnFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "not-a-valid-url";

        // Act
        await _adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        _logger.LogCalls.Should().Contain(c => c.Level == LogLevel.Error);
    }

    [Fact]
    public async Task DeliverAsync_SerializesPayloadToJson()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://webhook.example.com/notify";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var adapter = new GenericWebhookAdapter(httpClient, _logger);

        // Act
        await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        handler.RequestContent.Should().NotBeNullOrEmpty();
        handler.RequestContent.Should().Contain(jobId.ToString());
        handler.RequestContent.Should().Contain(jobName);
    }
}

/// <summary>
/// Mock HttpClient factory for testing
/// </summary>
internal class MockHttpClientFactory
{
    public HttpClient CreateClient()
    {
        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        return new HttpClient(handler);
    }
}

/// <summary>
/// Mock HttpMessageHandler for testing HTTP calls
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseContent = string.Empty;
    private Exception? _exception;
    public string? RequestUri { get; private set; }
    public string? RequestContent { get; private set; }
    public string? AuthorizationHeader { get; private set; }

    public void Setup(HttpStatusCode statusCode, string responseContent = "")
    {
        _statusCode = statusCode;
        _responseContent = responseContent;
        _exception = null;
    }

    public void SetupThrowException(Exception exception)
    {
        _exception = exception;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_exception != null)
            throw _exception;

        RequestUri = request.RequestUri?.ToString();

        // Capture authorization header
        if (request.Headers.TryGetValues("Authorization", out var authValues))
        {
            AuthorizationHeader = authValues.FirstOrDefault();
        }

        if (request.Content != null)
        {
            RequestContent = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var response = new HttpResponseMessage(_statusCode);
        if (!string.IsNullOrEmpty(_responseContent))
        {
            response.Content = new StringContent(_responseContent);
        }
        return response;
    }
}

/// <summary>
/// Mock logger for testing logging calls
/// </summary>
internal class MockLogger<T> : ILogger<T>
{
    public List<LogCall> LogCalls { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        LogCalls.Add(new LogCall { Level = logLevel, Message = formatter(state, exception) });
    }

    public class LogCall
    {
        public LogLevel Level { get; set; }
        public string? Message { get; set; }
    }
}
