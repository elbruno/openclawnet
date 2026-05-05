using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Channels.Adapters;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests.Channels;

/// <summary>
/// Integration tests for SlackWebhookAdapter validating end-to-end job delivery pipeline.
/// Story 8: Slack Adapter Integration Tests (Feature 3).
/// Tests adapter factory registration, resolution, and full job pipeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SlackAdapterIntegrationTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    /// <summary>
    /// Test 1: Adapter registered in factory + resolves from job delivery context
    /// </summary>
    [Fact]
    public async Task AdapterFactory_ResolvesSlackAdapter_Successfully()
    {
        // Arrange: Get factory from DI container
        await using var scope = factory.Services.CreateAsyncScope();
        var adapterFactory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        // Act: Resolve Slack adapter from factory
        var adapter = adapterFactory.CreateAdapter("Slack");

        // Assert: Adapter resolved correctly
        adapter.Should().NotBeNull();
        adapter.Should().BeOfType<SlackWebhookAdapter>();
        adapter.Name.Should().Be("Slack");
    }

    /// <summary>
    /// Test 2: Full job-to-Slack pipeline via database setup
    /// </summary>
    [Fact]
    public async Task FullJobToSlackPipeline_JobConfiguredInDatabase_FactoryResolves()
    {
        // Arrange: Set up integration test context with database
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        // Create test job and channel configuration in database
        var jobId = Guid.NewGuid();
        var jobName = "E2EPipelineJob";
        var webhookUrl = "https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXX";

        await using var db = await dbFactory.CreateDbContextAsync();

        // Insert job
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = jobName,
            Prompt = "E2E pipeline integration test",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        // Insert Slack channel configuration
        var channelConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            ChannelConfig = JsonSerializer.Serialize(new { webhookUrl }),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(channelConfig);
        await db.SaveChangesAsync();

        // Act: Verify adapter factory can resolve Slack
        var adapterFactory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();
        var adapter = adapterFactory.CreateAdapter("Slack");

        // Assert: Pipeline infrastructure ready
        adapter.Should().NotBeNull();
        adapter.Name.Should().Be("Slack");
        
        // Verify job configuration persisted
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var savedConfig = await verifyDb.JobChannelConfigurations
            .FirstOrDefaultAsync(c => c.JobId == jobId && c.ChannelType == "Slack");
        
        savedConfig.Should().NotBeNull();
        savedConfig!.IsEnabled.Should().BeTrue();
        savedConfig.ChannelConfig.Should().Contain("hooks.slack.com");
    }

    /// <summary>
    /// Test 3: Adapter registration validates all expected adapters present
    /// </summary>
    [Fact]
    public async Task AdapterFactory_AllKnownAdaptersRegistered()
    {
        // Arrange
        await using var scope = factory.Services.CreateAsyncScope();
        var adapterFactory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        // Act & Assert: Verify expected adapters can be resolved
        var slackAdapter = adapterFactory.CreateAdapter("Slack");
        slackAdapter.Should().NotBeNull();
        slackAdapter.Name.Should().Be("Slack");

        var webhookAdapter = adapterFactory.CreateAdapter("GenericWebhook");
        webhookAdapter.Should().NotBeNull();
        webhookAdapter.Name.Should().Be("GenericWebhook");
    }

    /// <summary>
    /// Test 4: Unknown adapter type throws InvalidOperationException
    /// </summary>
    [Fact]
    public async Task AdapterFactory_UnknownAdapterType_ThrowsException()
    {
        // Arrange
        await using var scope = factory.Services.CreateAsyncScope();
        var adapterFactory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        // Act
        var act = () => adapterFactory.CreateAdapter("UnknownAdapter");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown adapter type*");
    }

    /// <summary>
    /// Test 5: Multiple Slack configurations for same job all persisted
    /// </summary>
    [Fact]
    public async Task MultipleSlackConfigs_SameJob_AllPersisted()
    {
        // Arrange
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        // Insert job
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "MultiChannelJob",
            Prompt = "Job with multiple Slack channels",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        // Insert multiple Slack configs
        var config1 = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = "https://hooks.slack.com/services/T1/B1/X1" }),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var config2 = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = "https://hooks.slack.com/services/T2/B2/X2" }),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(config1, config2);
        await db.SaveChangesAsync();

        // Act: Query all configs
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var configs = await verifyDb.JobChannelConfigurations
            .Where(c => c.JobId == jobId && c.ChannelType == "Slack")
            .ToListAsync();

        // Assert: Both configs persisted
        configs.Should().HaveCount(2);
        configs.Should().AllSatisfy(c =>
        {
            c.ChannelType.Should().Be("Slack");
            c.IsEnabled.Should().BeTrue();
        });
    }
}
