using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.Channels.Adapters;
using OpenClawNet.Channels.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;

namespace OpenClawNet.UnitTests.Services;

public class ChannelDeliveryServiceTests
{
    private readonly Mock<IChannelDeliveryAdapterFactory> _mockFactory;
    private readonly OpenClawDbContext _dbContext;
    private readonly Mock<ILogger<ChannelDeliveryService>> _mockLogger;
    private readonly ChannelDeliveryService _service;

    public ChannelDeliveryServiceTests()
    {
        _mockFactory = new Mock<IChannelDeliveryAdapterFactory>();
        _mockLogger = new Mock<ILogger<ChannelDeliveryService>>();

        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new OpenClawDbContext(options);

        _service = new ChannelDeliveryService(_mockFactory.Object, _dbContext, _mockLogger.Object);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeliverAsync_SingleEnabledChannel_DeliverSuccessfully_LogsSuccess()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Job",
            Prompt = "Test prompt"
        };
        _dbContext.Jobs.Add(job);

        var channelConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            IsEnabled = true,
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}"
        };
        _dbContext.JobChannelConfigurations.Add(channelConfig);
        await _dbContext.SaveChangesAsync();

        var mockAdapter = new Mock<IChannelDeliveryAdapter>();
        mockAdapter.Setup(x => x.Name).Returns("GenericWebhook");
        mockAdapter.Setup(x => x.DeliverAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenClawNet.Channels.Adapters.DeliveryResult(Success: true));

        _mockFactory.Setup(x => x.CreateAdapter("GenericWebhook")).Returns(mockAdapter.Object);

        // Act
        var result = await _service.DeliverAsync(job, artifactId, "text", "Test content");

        // Assert
        Assert.Equal(1, result.TotalAttempted);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.Failures);

        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Single(logs);
        Assert.Equal(DeliveryStatus.Success, logs[0].Status);
        Assert.NotNull(logs[0].DeliveredAt);
        Assert.Null(logs[0].ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeliverAsync_SingleEnabledChannel_AdapterThrows_LogsFailure_DoesNotThrow()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Job",
            Prompt = "Test prompt"
        };
        _dbContext.Jobs.Add(job);

        var channelConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            IsEnabled = true,
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}"
        };
        _dbContext.JobChannelConfigurations.Add(channelConfig);
        await _dbContext.SaveChangesAsync();

        var mockAdapter = new Mock<IChannelDeliveryAdapter>();
        mockAdapter.Setup(x => x.Name).Returns("GenericWebhook");
        mockAdapter.Setup(x => x.DeliverAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        _mockFactory.Setup(x => x.CreateAdapter("GenericWebhook")).Returns(mockAdapter.Object);

        // Act
        var result = await _service.DeliverAsync(job, artifactId, "text", "Test content");

        // Assert
        Assert.Equal(1, result.TotalAttempted);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(result.Failures);
        Assert.Equal("GenericWebhook", result.Failures[0].ChannelType);
        Assert.Equal("Network error", result.Failures[0].ErrorMessage);

        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Single(logs);
        Assert.Equal(DeliveryStatus.Failed, logs[0].Status);
        Assert.Null(logs[0].DeliveredAt);
        Assert.Equal("Network error", logs[0].ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeliverAsync_MultipleChannels_MixedSuccessFailure_LogsAll_ReturnsAggregate()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Job",
            Prompt = "Test prompt"
        };
        _dbContext.Jobs.Add(job);

        var webhookConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            IsEnabled = true,
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}"
        };
        var teamsConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Teams",
            IsEnabled = true,
            ChannelConfig = "{\"webhookUrl\":\"https://teams.example.com/webhook\"}"
        };
        var slackConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            IsEnabled = true,
            ChannelConfig = "{\"webhookUrl\":\"https://slack.example.com/webhook\"}"
        };
        _dbContext.JobChannelConfigurations.AddRange(webhookConfig, teamsConfig, slackConfig);
        await _dbContext.SaveChangesAsync();

        var mockWebhookAdapter = new Mock<IChannelDeliveryAdapter>();
        mockWebhookAdapter.Setup(x => x.Name).Returns("GenericWebhook");
        mockWebhookAdapter.Setup(x => x.DeliverAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenClawNet.Channels.Adapters.DeliveryResult(Success: true));

        var mockTeamsAdapter = new Mock<IChannelDeliveryAdapter>();
        mockTeamsAdapter.Setup(x => x.Name).Returns("Teams");
        mockTeamsAdapter.Setup(x => x.DeliverAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenClawNet.Channels.Adapters.DeliveryResult(Success: false, ErrorMessage: "Teams API error"));

        var mockSlackAdapter = new Mock<IChannelDeliveryAdapter>();
        mockSlackAdapter.Setup(x => x.Name).Returns("Slack");
        mockSlackAdapter.Setup(x => x.DeliverAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenClawNet.Channels.Adapters.DeliveryResult(Success: true));

        _mockFactory.Setup(x => x.CreateAdapter("GenericWebhook")).Returns(mockWebhookAdapter.Object);
        _mockFactory.Setup(x => x.CreateAdapter("Teams")).Returns(mockTeamsAdapter.Object);
        _mockFactory.Setup(x => x.CreateAdapter("Slack")).Returns(mockSlackAdapter.Object);

        // Act
        var result = await _service.DeliverAsync(job, artifactId, "text", "Test content");

        // Assert
        Assert.Equal(3, result.TotalAttempted);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(result.Failures);
        Assert.Equal("Teams", result.Failures[0].ChannelType);
        Assert.Equal("Teams API error", result.Failures[0].ErrorMessage);

        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Equal(2, logs.Count(l => l.Status == DeliveryStatus.Success));
        Assert.Equal(1, logs.Count(l => l.Status == DeliveryStatus.Failed));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeliverAsync_AllChannelsDisabled_ReturnsZeroAttempts()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Job",
            Prompt = "Test prompt"
        };
        _dbContext.Jobs.Add(job);

        var channelConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            IsEnabled = false, // Disabled
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}"
        };
        _dbContext.JobChannelConfigurations.Add(channelConfig);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.DeliverAsync(job, artifactId, "text", "Test content");

        // Assert
        Assert.Equal(0, result.TotalAttempted);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.Failures);

        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Empty(logs);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeliverAsync_FactoryThrowsUnknownAdapter_LogsFailure_Continues()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Job",
            Prompt = "Test prompt"
        };
        _dbContext.Jobs.Add(job);

        var channelConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "UnknownAdapter",
            IsEnabled = true,
            ChannelConfig = "{\"config\":\"value\"}"
        };
        _dbContext.JobChannelConfigurations.Add(channelConfig);
        await _dbContext.SaveChangesAsync();

        _mockFactory.Setup(x => x.CreateAdapter("UnknownAdapter"))
            .Throws(new InvalidOperationException("Unknown adapter type: UnknownAdapter"));

        // Act
        var result = await _service.DeliverAsync(job, artifactId, "text", "Test content");

        // Assert
        Assert.Equal(1, result.TotalAttempted);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(result.Failures);
        Assert.Equal("UnknownAdapter", result.Failures[0].ChannelType);
        Assert.Contains("Unknown adapter type", result.Failures[0].ErrorMessage);

        var logs = await _dbContext.AdapterDeliveryLogs.Where(l => l.JobId == jobId).ToListAsync();
        Assert.Single(logs);
        Assert.Equal(DeliveryStatus.Failed, logs[0].Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeliverAsync_VerifyDbPersistence_AllLogsCreated()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Test Job",
            Prompt = "Test prompt"
        };
        _dbContext.Jobs.Add(job);

        var webhookConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            IsEnabled = true,
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}"
        };
        var teamsConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Teams",
            IsEnabled = true,
            ChannelConfig = "{\"webhookUrl\":\"https://teams.example.com/webhook\"}"
        };
        _dbContext.JobChannelConfigurations.AddRange(webhookConfig, teamsConfig);
        await _dbContext.SaveChangesAsync();

        var mockWebhookAdapter = new Mock<IChannelDeliveryAdapter>();
        mockWebhookAdapter.Setup(x => x.Name).Returns("GenericWebhook");
        mockWebhookAdapter.Setup(x => x.DeliverAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenClawNet.Channels.Adapters.DeliveryResult(Success: true));

        var mockTeamsAdapter = new Mock<IChannelDeliveryAdapter>();
        mockTeamsAdapter.Setup(x => x.Name).Returns("Teams");
        mockTeamsAdapter.Setup(x => x.DeliverAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenClawNet.Channels.Adapters.DeliveryResult(Success: false, ErrorMessage: "Teams error"));

        _mockFactory.Setup(x => x.CreateAdapter("GenericWebhook")).Returns(mockWebhookAdapter.Object);
        _mockFactory.Setup(x => x.CreateAdapter("Teams")).Returns(mockTeamsAdapter.Object);

        // Act
        var result = await _service.DeliverAsync(job, artifactId, "text", "Test content");

        // Assert
        var logs = await _dbContext.AdapterDeliveryLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.ChannelType)
            .ToListAsync();

        Assert.Equal(2, logs.Count);

        // Verify Webhook log
        var webhookLog = logs.First(l => l.ChannelType == "GenericWebhook");
        Assert.Equal(DeliveryStatus.Success, webhookLog.Status);
        Assert.NotNull(webhookLog.DeliveredAt);
        Assert.Null(webhookLog.ErrorMessage);
        Assert.Equal("{\"webhookUrl\":\"https://example.com/webhook\"}", webhookLog.ChannelConfig);

        // Verify Teams log
        var teamsLog = logs.First(l => l.ChannelType == "Teams");
        Assert.Equal(DeliveryStatus.Failed, teamsLog.Status);
        Assert.Null(teamsLog.DeliveredAt);
        Assert.Equal("Teams error", teamsLog.ErrorMessage);
        Assert.Equal("{\"webhookUrl\":\"https://teams.example.com/webhook\"}", teamsLog.ChannelConfig);
    }
}
