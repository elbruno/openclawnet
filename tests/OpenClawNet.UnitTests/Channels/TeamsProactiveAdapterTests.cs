using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenClawNet.Channels.Adapters;
using System.Net;
using System.Text.Json;

namespace OpenClawNet.UnitTests.Channels;

public sealed class TeamsProactiveAdapterTests
{
    private readonly MockLogger<TeamsProactiveAdapter> _logger;
    private readonly IConfiguration _configuration;

    public TeamsProactiveAdapterTests()
    {
        _logger = new MockLogger<TeamsProactiveAdapter>();
        _configuration = new ConfigurationBuilder().Build();
    }

    [Fact]
    public void Name_ReturnsTeams()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var adapter = new TeamsProactiveAdapter(httpClient, _configuration, _logger);

        // Act & Assert
        adapter.Name.Should().Be("Teams");
    }

    [Fact]
    public async Task DeliverAsync_WithValidWebhookUrl_PostsSuccessfully()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://outlook.office.com/webhook/abc123/IncomingWebhook/def456/ghi789";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var adapter = new TeamsProactiveAdapter(httpClient, _configuration, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeTrue();
        handler.RequestUri.Should().Be(webhookUrl);
        handler.RequestContent.Should().NotBeNullOrEmpty();

        // Verify it's valid JSON
        var json = handler.RequestContent;
        var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetProperty("type").GetString().Should().Be("message");
        parsed.RootElement.GetProperty("attachments").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DeliverAsync_WithJsonConfig_ExtractsWebhookUrl()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://outlook.office.com/webhook/abc123/IncomingWebhook/def456/ghi789";
        var configJson = $@"{{ ""webhookUrl"": ""{webhookUrl}"" }}";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var adapter = new TeamsProactiveAdapter(httpClient, _configuration, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, configJson);

        // Assert
        result.Success.Should().BeTrue();
        handler.RequestUri.Should().Be(webhookUrl);
    }

    [Fact]
    public async Task DeliverAsync_WithHttpError_ReturnsFailureResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        var webhookUrl = "https://outlook.office.com/webhook/abc123/IncomingWebhook/def456/ghi789";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);

        var adapter = new TeamsProactiveAdapter(httpClient, _configuration, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("HTTP error");
    }

    [Fact]
    public async Task DeliverAsync_WithMissingWebhookUrl_ReturnsFailureResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var adapter = new TeamsProactiveAdapter(httpClient, _configuration, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, "");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("webhook URL not found");
    }

    [Fact]
    public async Task DeliverAsync_WithInvalidWebhookUrl_ReturnsFailureResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "TestJob";
        var artifactId = Guid.NewGuid();
        var artifactType = "markdown";
        // Use a config JSON with a non-Teams URL
        var configJson = @"{""webhookUrl"": ""https://example.com/webhook""}";

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var adapter = new TeamsProactiveAdapter(httpClient, _configuration, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, configJson);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid Teams webhook URL");
    }

    [Fact]
    public async Task DeliverAsync_SendsAdaptiveCardFormat()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var jobName = "Test Analytics Job";
        var artifactId = Guid.NewGuid();
        var artifactType = "json";
        var webhookUrl = "https://outlook.office.com/webhook/abc123/IncomingWebhook/def456/ghi789";

        var handler = new MockHttpMessageHandler();
        handler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var adapter = new TeamsProactiveAdapter(httpClient, _configuration, _logger);

        // Act
        var result = await adapter.DeliverAsync(jobId, jobName, artifactId, artifactType, webhookUrl);

        // Assert
        result.Success.Should().BeTrue();

        var json = handler.RequestContent;
        var parsed = JsonDocument.Parse(json!);

        // Verify Adaptive Card structure
        var attachments = parsed.RootElement.GetProperty("attachments");
        attachments.GetArrayLength().Should().Be(1);

        var card = attachments[0].GetProperty("content");
        card.GetProperty("type").GetString().Should().Be("AdaptiveCard");
        card.GetProperty("version").GetString().Should().Be("1.4");

        // Verify card body contains job name
        var body = card.GetProperty("body");
        body.GetArrayLength().Should().BeGreaterThan(0);

        var titleBlock = body[0];
        titleBlock.GetProperty("text").GetString().Should().Contain(jobName);
    }
}
