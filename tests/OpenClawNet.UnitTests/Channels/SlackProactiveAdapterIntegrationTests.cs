using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Channels.Adapters;
using OpenClawNet.Channels.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using System.Net;
using System.Text.Json;
using Xunit;

namespace OpenClawNet.UnitTests.Channels;

/// <summary>
/// Integration tests for Slack proactive message delivery.
/// Tests the end-to-end flow from job completion to Slack webhook delivery.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SlackProactiveAdapterIntegrationTests : IDisposable
{
    private readonly OpenClawDbContext _dbContext;
    private readonly Mock<IChannelDeliveryAdapterFactory> _mockFactory;
    private readonly Mock<ILogger<ChannelDeliveryService>> _mockLogger;
    private readonly ChannelDeliveryService _deliveryService;

    public SlackProactiveAdapterIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new OpenClawDbContext(options);

        _mockFactory = new Mock<IChannelDeliveryAdapterFactory>();
        _mockLogger = new Mock<ILogger<ChannelDeliveryService>>();
        _deliveryService = new ChannelDeliveryService(_mockFactory.Object, _dbContext, _mockLogger.Object);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    /// <summary>
    /// Test Case 1: Happy Path — Valid Slack Delivery
    /// Mock Slack webhook returns 200 OK
    /// Assert: ChannelDeliveryResult.Success = true
    /// </summary>
    [Fact]
    public async Task DeliverAsync_ValidSlackWebhook_ReturnsSuccess()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = "https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXX";
        var channelConfig = JsonSerializer.Serialize(new { webhookUrl });

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Slack Job",
            Prompt = "Test prompt",
            Status = JobStatus.Active
        };
        _dbContext.Jobs.Add(job);

        var channel = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            IsEnabled = true,
            ChannelConfig = channelConfig
        };
        _dbContext.JobChannelConfigurations.Add(channel);
        await _dbContext.SaveChangesAsync();

        // Setup mock adapter with successful HTTP response
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(mockHandler);

        var configuration = new ConfigurationBuilder().Build();
        var adapterLogger = new MockLogger<SlackWebhookAdapter>();
        var slackAdapter = new SlackWebhookAdapter(httpClient, configuration, adapterLogger);

        _mockFactory.Setup(f => f.CreateAdapter("Slack")).Returns(slackAdapter);

        // Act
        var result = await _deliveryService.DeliverAsync(job, artifactId, "markdown", channelConfig);

        // Assert
        result.TotalAttempted.Should().Be(1);
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(0);
        result.Failures.Should().BeEmpty();

        // Verify log entry in database
        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Status.Should().Be(DeliveryStatus.Success);
        logs[0].DeliveredAt.Should().NotBeNull();
        logs[0].ErrorMessage.Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Test Case 2: Slack Endpoint Unreachable
    /// Mock Slack throws HttpRequestException
    /// Assert: Success = false, error captured
    /// </summary>
    [Fact]
    public async Task DeliverAsync_SlackEndpointUnreachable_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = "https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXX";
        var channelConfig = JsonSerializer.Serialize(new { webhookUrl });

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Unreachable Job",
            Prompt = "Test prompt",
            Status = JobStatus.Active
        };
        _dbContext.Jobs.Add(job);

        var channel = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            IsEnabled = true,
            ChannelConfig = channelConfig
        };
        _dbContext.JobChannelConfigurations.Add(channel);
        await _dbContext.SaveChangesAsync();

        // Setup mock adapter with network error
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupThrowException(new HttpRequestException("Network unreachable"));
        var httpClient = new HttpClient(mockHandler);

        var configuration = new ConfigurationBuilder().Build();
        var adapterLogger = new MockLogger<SlackWebhookAdapter>();
        var slackAdapter = new SlackWebhookAdapter(httpClient, configuration, adapterLogger);

        _mockFactory.Setup(f => f.CreateAdapter("Slack")).Returns(slackAdapter);

        // Act
        var result = await _deliveryService.DeliverAsync(job, artifactId, "markdown", channelConfig);

        // Assert
        result.TotalAttempted.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);
        result.Failures.Should().ContainSingle();
        result.Failures[0].ErrorMessage.Should().Contain("HTTP error");

        // Verify error logged in database
        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Status.Should().Be(DeliveryStatus.Failed);
        logs[0].ErrorMessage.Should().NotBeNullOrEmpty();
        logs[0].ErrorMessage.Should().Contain("HTTP error");
    }

    /// <summary>
    /// Test Case 3: Slack Returns 500 Error
    /// Mock returns 500 status
    /// Assert: Success = false, error includes status code
    /// </summary>
    [Fact]
    public async Task DeliverAsync_SlackReturns500Error_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = "https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXX";
        var channelConfig = JsonSerializer.Serialize(new { webhookUrl });

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test 500 Error Job",
            Prompt = "Test prompt",
            Status = JobStatus.Active
        };
        _dbContext.Jobs.Add(job);

        var channel = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            IsEnabled = true,
            ChannelConfig = channelConfig
        };
        _dbContext.JobChannelConfigurations.Add(channel);
        await _dbContext.SaveChangesAsync();

        // Setup mock adapter with 500 error
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(mockHandler);

        var configuration = new ConfigurationBuilder().Build();
        var adapterLogger = new MockLogger<SlackWebhookAdapter>();
        var slackAdapter = new SlackWebhookAdapter(httpClient, configuration, adapterLogger);

        _mockFactory.Setup(f => f.CreateAdapter("Slack")).Returns(slackAdapter);

        // Act
        var result = await _deliveryService.DeliverAsync(job, artifactId, "json", channelConfig);

        // Assert
        result.TotalAttempted.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);
        result.Failures.Should().ContainSingle();
        result.Failures[0].ErrorMessage.Should().Contain("HTTP error");

        // Verify error logged
        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Status.Should().Be(DeliveryStatus.Failed);
        logs[0].ErrorMessage.Should().Contain("HTTP error");
    }

    /// <summary>
    /// Test Case 4: Multiple Adapters (Slack + Teams)
    /// Deliver to both adapters
    /// Assert: Both logged, job succeeds even if one fails
    /// </summary>
    [Fact]
    public async Task DeliverAsync_MultipleAdapters_BothDeliveredIndependently()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var slackWebhookUrl = "https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXX";
        var teamsWebhookUrl = "https://outlook.office.com/webhook/12345";
        var slackConfig = JsonSerializer.Serialize(new { webhookUrl = slackWebhookUrl });
        var teamsConfig = JsonSerializer.Serialize(new { webhookUrl = teamsWebhookUrl });

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Multi-Channel Job",
            Prompt = "Test prompt",
            Status = JobStatus.Active
        };
        _dbContext.Jobs.Add(job);

        // Add both Slack and Teams channels
        var slackChannel = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            IsEnabled = true,
            ChannelConfig = slackConfig
        };
        var teamsChannel = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook", // Use generic for Teams
            IsEnabled = true,
            ChannelConfig = teamsConfig
        };
        _dbContext.JobChannelConfigurations.AddRange(slackChannel, teamsChannel);
        await _dbContext.SaveChangesAsync();

        // Setup Slack adapter (succeeds)
        var slackHandler = new MockHttpMessageHandler();
        slackHandler.Setup(HttpStatusCode.OK);
        var slackHttpClient = new HttpClient(slackHandler);
        var slackAdapterLogger = new MockLogger<SlackWebhookAdapter>();
        var slackAdapter = new SlackWebhookAdapter(slackHttpClient, new ConfigurationBuilder().Build(), slackAdapterLogger);

        // Setup Teams adapter (fails)
        var teamsHandler = new MockHttpMessageHandler();
        teamsHandler.SetupThrowException(new HttpRequestException("Teams unreachable"));
        var teamsHttpClient = new HttpClient(teamsHandler);
        var teamsAdapterLogger = new MockLogger<GenericWebhookAdapter>();
        var teamsAdapter = new GenericWebhookAdapter(teamsHttpClient, teamsAdapterLogger);

        _mockFactory.Setup(f => f.CreateAdapter("Slack")).Returns(slackAdapter);
        _mockFactory.Setup(f => f.CreateAdapter("GenericWebhook")).Returns(teamsAdapter);

        // Act
        var result = await _deliveryService.DeliverAsync(job, artifactId, "text", slackConfig);

        // Assert
        result.TotalAttempted.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(1);

        // Verify both logged
        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        logs.Should().HaveCount(2);
        logs.Should().Contain(l => l.ChannelType == "Slack" && l.Status == DeliveryStatus.Success);
        logs.Should().Contain(l => l.ChannelType == "GenericWebhook" && l.Status == DeliveryStatus.Failed);
    }

    /// <summary>
    /// Test Case 5: Timeout Handling
    /// Mock delays 15s (exceeds 10s timeout)
    /// Assert: Times out gracefully, returns success=false
    /// </summary>
    [Fact]
    public async Task DeliverAsync_SlackTimeout_ReturnsFailureGracefully()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = "https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXX";
        var channelConfig = JsonSerializer.Serialize(new { webhookUrl });

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Timeout Test Job",
            Prompt = "Test prompt",
            Status = JobStatus.Active
        };
        _dbContext.Jobs.Add(job);

        var channel = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            IsEnabled = true,
            ChannelConfig = channelConfig
        };
        _dbContext.JobChannelConfigurations.Add(channel);
        await _dbContext.SaveChangesAsync();

        // Setup mock adapter with timeout (TaskCanceledException simulates timeout)
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetupThrowException(new TaskCanceledException("Request timed out"));
        var httpClient = new HttpClient(mockHandler);

        var configuration = new ConfigurationBuilder().Build();
        var adapterLogger = new MockLogger<SlackWebhookAdapter>();
        var slackAdapter = new SlackWebhookAdapter(httpClient, configuration, adapterLogger);

        _mockFactory.Setup(f => f.CreateAdapter("Slack")).Returns(slackAdapter);

        // Act
        var result = await _deliveryService.DeliverAsync(job, artifactId, "markdown", channelConfig);

        // Assert
        result.TotalAttempted.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);
        result.Failures.Should().ContainSingle();
        result.Failures[0].ErrorMessage.Should().Contain("timed out");

        // Verify timeout logged
        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Status.Should().Be(DeliveryStatus.Failed);
        logs[0].ErrorMessage.Should().Contain("timed out");
    }

    /// <summary>
    /// Test Case 6: Configuration Missing
    /// No Slack config in appsettings
    /// Assert: InvalidOperationException or graceful fallback
    /// </summary>
    [Fact]
    public async Task DeliverAsync_MissingSlackConfiguration_ReturnsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var emptyConfig = "{}"; // No webhookUrl

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Missing Config Job",
            Prompt = "Test prompt",
            Status = JobStatus.Active
        };
        _dbContext.Jobs.Add(job);

        var channel = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            IsEnabled = true,
            ChannelConfig = emptyConfig
        };
        _dbContext.JobChannelConfigurations.Add(channel);
        await _dbContext.SaveChangesAsync();

        // Setup mock adapter with no webhook URL in config
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(HttpStatusCode.OK);
        var httpClient = new HttpClient(mockHandler);

        var configuration = new ConfigurationBuilder().Build();
        var adapterLogger = new MockLogger<SlackWebhookAdapter>();
        var slackAdapter = new SlackWebhookAdapter(httpClient, configuration, adapterLogger);

        _mockFactory.Setup(f => f.CreateAdapter("Slack")).Returns(slackAdapter);

        // Act
        var result = await _deliveryService.DeliverAsync(job, artifactId, "text", emptyConfig);

        // Assert
        result.TotalAttempted.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(1);
        result.Failures.Should().ContainSingle();
        result.Failures[0].ErrorMessage.Should().Contain("webhook URL not found");

        // Verify error logged
        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Status.Should().Be(DeliveryStatus.Failed);
        logs[0].ErrorMessage.Should().Contain("webhook URL not found");
    }
}
