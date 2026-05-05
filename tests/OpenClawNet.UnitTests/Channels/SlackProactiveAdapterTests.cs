using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Channels.Adapters;
using OpenClawNet.Channels.Configuration;
using System.Net;
using System.Text.Json;

namespace OpenClawNet.UnitTests.Channels;

public sealed class SlackProactiveAdapterTests
{
    private readonly SlackClientOptions _validOptions;
    private readonly MockLogger<SlackProactiveAdapter> _logger;

    public SlackProactiveAdapterTests()
    {
        _validOptions = new SlackClientOptions
        {
            EndpointUrl = "https://slack.com/api/chat.postMessage",
            BotToken = "xoxb-test-token-12345",
            Timeout = TimeSpan.FromSeconds(10)
        };
        _logger = new MockLogger<SlackProactiveAdapter>();
    }

    [Fact]
    public void Name_ReturnsSlackProactive()
    {
        // Arrange
        var httpClient = new HttpClient(new MockHttpMessageHandler());
        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act & Assert
        adapter.Name.Should().Be("SlackProactive");
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        // Arrange
        var options = Options.Create(_validOptions);

        // Act
        var act = () => new SlackProactiveAdapter(null!, options, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var httpClient = new HttpClient(new MockHttpMessageHandler());

        // Act
        var act = () => new SlackProactiveAdapter(httpClient, null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var httpClient = new HttpClient(new MockHttpMessageHandler());
        var options = Options.Create(_validOptions);

        // Act
        var act = () => new SlackProactiveAdapter(httpClient, options, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithMissingEndpointUrl_ThrowsInvalidOperationException()
    {
        // Arrange
        var httpClient = new HttpClient(new MockHttpMessageHandler());
        var invalidOptions = new SlackClientOptions
        {
            EndpointUrl = "", // Missing
            BotToken = "xoxb-test-token"
        };
        var options = Options.Create(invalidOptions);

        // Act
        var act = () => new SlackProactiveAdapter(httpClient, options, _logger);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EndpointUrl*");
    }

    [Fact]
    public void Constructor_WithMissingBotToken_ThrowsInvalidOperationException()
    {
        // Arrange
        var httpClient = new HttpClient(new MockHttpMessageHandler());
        var invalidOptions = new SlackClientOptions
        {
            EndpointUrl = "https://slack.com/api/chat.postMessage",
            BotToken = "" // Missing
        };
        var options = Options.Create(invalidOptions);

        // Act
        var act = () => new SlackProactiveAdapter(httpClient, options, _logger);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BotToken*");
    }

    [Fact]
    public async Task DeliverAsync_WithValidChannelId_PostsSuccessfully()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK, JsonSerializer.Serialize(new { ok = true, ts = "1234567890.123456" }));
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNullOrEmpty();
        result.ExternalId.Should().Be("1234567890.123456");
        handler.RequestUri.Should().Be(_validOptions.EndpointUrl);
        handler.RequestContent.Should().NotBeNullOrEmpty();
        handler.RequestContent.Should().Contain(channelId);
    }

    [Fact]
    public async Task DeliverAsync_WithMissingChannelId_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "text";
        var channelConfig = "{}"; // No channelId property

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("channel ID not found");
    }

    [Fact]
    public async Task DeliverAsync_WithEmptyChannelConfig_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "text";
        var channelConfig = "";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("channel ID not found");
    }

    [Fact]
    public async Task DeliverAsync_WithHttpError_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "error";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("HTTP error");
    }

    [Fact]
    public async Task DeliverAsync_WithNetworkError_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.SetupThrowException(new HttpRequestException("Network error"));
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeliverAsync_FormatsSlackBlockKitMessage()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "BlockKitJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "json";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK, JsonSerializer.Serialize(new { ok = true }));
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeTrue();
        handler.RequestContent.Should().NotBeNullOrEmpty();
        handler.RequestContent.Should().Contain("blocks"); // Slack Block Kit
        handler.RequestContent.Should().Contain(jobName);
        handler.RequestContent.Should().Contain(artifactType);
        handler.RequestContent.Should().Contain(channelId);
    }

    [Fact]
    public async Task DeliverAsync_IncludesAuthorizationHeader()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK, JsonSerializer.Serialize(new { ok = true }));
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeTrue();
        handler.AuthorizationHeader.Should().Be($"Bearer {_validOptions.BotToken}");
    }

    [Fact]
    public async Task DeliverAsync_LogsSuccessfulDelivery()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK, JsonSerializer.Serialize(new { ok = true }));
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        _logger.LogCalls.Should().Contain(c => 
            c.Level == LogLevel.Information && 
            c.Message != null && c.Message.Contains("delivered successfully"));
    }

    [Fact]
    public async Task DeliverAsync_LogsErrorOnFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "text";
        var channelConfig = ""; // Invalid config

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        _logger.LogCalls.Should().Contain(c => c.Level == LogLevel.Error);
    }

    [Fact]
    public async Task DeliverAsync_NeverThrows_FireAndForget()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "text";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.SetupThrowException(new Exception("Unexpected error"));
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var act = async () => await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert - should NOT throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeliverAsync_WithTimeout_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "text";
        var channelId = "C0123456789";
        var channelConfig = JsonSerializer.Serialize(new { channelId });

        var handler = new MockHttpMessageHandler();
        handler.SetupThrowException(new TaskCanceledException("Timeout")); // Simulate timeout
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task DeliverAsync_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "text";
        var channelConfig = "{ invalid json }"; // Invalid JSON

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var options = Options.Create(_validOptions);
        var adapter = new SlackProactiveAdapter(httpClient, options, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, channelConfig);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("channel ID not found");
    }
}
