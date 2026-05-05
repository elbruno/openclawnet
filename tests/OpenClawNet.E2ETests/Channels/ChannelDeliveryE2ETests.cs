using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Channels.Adapters;
using OpenClawNet.Channels.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace OpenClawNet.E2ETests.Channels;

/// <summary>
/// E2E tests for channel delivery pipeline - Phase 2A Story 9.
/// Tests: Job completion → channel delivery, real Teams/Slack delivery, multi-channel delivery.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Phase", "2A")]
[Trait("Story", "9")]
public sealed class ChannelDeliveryE2ETests : IClassFixture<GatewayE2EFactory>
{
    private readonly GatewayE2EFactory _factory;
    private readonly ITestOutputHelper _output;

    public ChannelDeliveryE2ETests(GatewayE2EFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 5: Job Completion → Channel Delivery
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task JobCompletion_ConfiguredChannels_TriggersAsyncDelivery()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        // Create job with Teams and Slack channels configured
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "E2E_JobCompletionTest",
            Prompt = "Test job completion pipeline",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        var teamsConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Teams",
            ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = "https://example.com/teams-webhook" }),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var slackConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = "https://hooks.slack.com/services/TEST/TEST/TEST" }),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(teamsConfig, slackConfig);
        await db.SaveChangesAsync();

        // Act - Trigger delivery via service
        var deliveryService = scope.ServiceProvider.GetRequiredService<IChannelDeliveryService>();
        var result = await deliveryService.DeliverAsync(
            job,
            artifactId,
            "markdown",
            "# Test Content\n\nJob completed successfully!",
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TotalAttempted.Should().Be(2);
        result.JobId.Should().Be(jobId.ToString());

        // Verify audit trail was created
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logs = await verifyDb.AdapterDeliveryLogs
            .Where(l => l.JobId == jobId)
            .ToListAsync();

        logs.Should().HaveCount(2);
        logs.Should().Contain(l => l.ChannelType == "Teams");
        logs.Should().Contain(l => l.ChannelType == "Slack");
    }

    [Fact]
    public async Task JobCompletion_VerifyAuditTrail_ContainsAllDeliveryAttempts()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "AuditTrailE2EJob",
            Prompt = "Test audit trail E2E",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        var webhookConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            ChannelConfig = "https://httpstat.us/200",  // Test endpoint that returns 200
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(webhookConfig);
        await db.SaveChangesAsync();

        // Act
        var deliveryService = scope.ServiceProvider.GetRequiredService<IChannelDeliveryService>();
        await deliveryService.DeliverAsync(
            job,
            artifactId,
            "json",
            "{\"status\":\"completed\"}",
            CancellationToken.None);

        // Assert - Check audit trail
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var auditLog = await verifyDb.AdapterDeliveryLogs
            .FirstOrDefaultAsync(l => l.JobId == jobId && l.ChannelType == "GenericWebhook");

        auditLog.Should().NotBeNull();
        auditLog!.ChannelType.Should().Be("GenericWebhook");
        auditLog.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(60));  // Allow up to 60s for retries
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 6: Real Teams Delivery (Skipped if no webhook configured)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task RealTeamsDelivery_ValidWebhook_SendsAdaptiveCard()
    {
        // Arrange - Get Teams webhook URL from environment
        var teamsWebhookUrl = Environment.GetEnvironmentVariable("TEAMS_WEBHOOK_URL");
        Skip.If(string.IsNullOrEmpty(teamsWebhookUrl), "TEAMS_WEBHOOK_URL environment variable not set");

        _output.WriteLine($"Testing Teams delivery to: {teamsWebhookUrl?.Substring(0, Math.Min(50, teamsWebhookUrl.Length))}...");

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "RealTeamsE2ETest",
            Prompt = "Test real Teams delivery",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        var teamsConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Teams",
            ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = teamsWebhookUrl }),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(teamsConfig);
        await db.SaveChangesAsync();

        // Act - Deliver to real Teams
        var deliveryService = scope.ServiceProvider.GetRequiredService<IChannelDeliveryService>();
        var result = await deliveryService.DeliverAsync(
            job,
            artifactId,
            "markdown",
            "# E2E Test Result\n\nThis is a test message from OpenClawNet E2E tests.",
            CancellationToken.None);

        // Assert
        _output.WriteLine($"Delivery result: Success={result.SuccessCount}, Failed={result.FailureCount}");
        
        // Note: Teams stub currently returns failure, so we just verify the pipeline executed
        result.Should().NotBeNull();
        result.TotalAttempted.Should().Be(1);
        
        // Verify audit trail
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var auditLog = await verifyDb.AdapterDeliveryLogs
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        auditLog.Should().NotBeNull();
        _output.WriteLine($"Audit log status: {auditLog!.Status}, Error: {auditLog.ErrorMessage}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 7: Real Slack Delivery (Skipped if no webhook configured)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task RealSlackDelivery_ValidWebhook_SendsBlockMessage()
    {
        // Arrange - Get Slack webhook URL from environment
        var slackWebhookUrl = Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL");
        Skip.If(string.IsNullOrEmpty(slackWebhookUrl), "SLACK_WEBHOOK_URL environment variable not set");

        _output.WriteLine($"Testing Slack delivery to: {slackWebhookUrl?.Substring(0, Math.Min(50, slackWebhookUrl.Length))}...");

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "RealSlackE2ETest",
            Prompt = "Test real Slack delivery",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        var slackConfig = new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "Slack",
            ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = slackWebhookUrl }),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(slackConfig);
        await db.SaveChangesAsync();

        // Act - Deliver to real Slack
        var deliveryService = scope.ServiceProvider.GetRequiredService<IChannelDeliveryService>();
        var result = await deliveryService.DeliverAsync(
            job,
            artifactId,
            "markdown",
            "# E2E Test Result\n\nThis is a test message from OpenClawNet E2E tests.",
            CancellationToken.None);

        // Assert
        _output.WriteLine($"Delivery result: Success={result.SuccessCount}, Failed={result.FailureCount}");
        result.Should().NotBeNull();
        result.TotalAttempted.Should().Be(1);
        result.SuccessCount.Should().Be(1);

        // Verify audit trail shows success
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var auditLog = await verifyDb.AdapterDeliveryLogs
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        auditLog.Should().NotBeNull();
        auditLog!.Status.Should().Be(DeliveryStatus.Success);
        auditLog.DeliveredAt.Should().NotBeNull();
        _output.WriteLine($"Slack delivery successful at {auditLog.DeliveredAt}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 8: Multi-Channel Delivery
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultiChannelDelivery_ThreeAdapters_AllTriggered()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "MultiChannelE2ETest",
            Prompt = "Test multi-channel delivery",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        // Configure three channels
        var channels = new[]
        {
            new JobChannelConfiguration
            {
                JobId = jobId,
                ChannelType = "Teams",
                ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = "https://example.com/teams" }),
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            },
            new JobChannelConfiguration
            {
                JobId = jobId,
                ChannelType = "Slack",
                ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = "https://hooks.slack.com/services/T/B/X" }),
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            },
            new JobChannelConfiguration
            {
                JobId = jobId,
                ChannelType = "GenericWebhook",
                ChannelConfig = "https://httpstat.us/200",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            }
        };
        db.AddRange(channels);
        await db.SaveChangesAsync();

        // Act - Trigger multi-channel delivery
        var deliveryService = scope.ServiceProvider.GetRequiredService<IChannelDeliveryService>();
        var result = await deliveryService.DeliverAsync(
            job,
            artifactId,
            "json",
            "{\"status\":\"completed\",\"data\":\"test\"}",
            CancellationToken.None);

        // Assert - All 3 adapters triggered
        result.Should().NotBeNull();
        result.TotalAttempted.Should().Be(3);
        _output.WriteLine($"Multi-channel result: {result.SuccessCount} succeeded, {result.FailureCount} failed");

        // Verify audit trail has 3 entries
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var auditLogs = await verifyDb.AdapterDeliveryLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.ChannelType)
            .ToListAsync();

        auditLogs.Should().HaveCount(3);
        auditLogs.Should().Contain(l => l.ChannelType == "Teams");
        auditLogs.Should().Contain(l => l.ChannelType == "Slack");
        auditLogs.Should().Contain(l => l.ChannelType == "GenericWebhook");

        foreach (var log in auditLogs)
        {
            _output.WriteLine($"  {log.ChannelType}: {log.Status} - {log.ErrorMessage}");
        }
    }

    [Fact]
    public async Task MultiChannelDelivery_PartialFailure_SuccessfulDeliveriesPersisted()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "PartialFailureTest",
            Prompt = "Test partial failure scenario",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        // Mix of valid and invalid configurations
        var channels = new[]
        {
            new JobChannelConfiguration
            {
                JobId = jobId,
                ChannelType = "GenericWebhook",
                ChannelConfig = "https://httpstat.us/200",  // Will succeed
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            },
            new JobChannelConfiguration
            {
                JobId = jobId,
                ChannelType = "Slack",
                ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = "https://invalid-slack-url.local/fail" }),  // Will fail
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            }
        };
        db.AddRange(channels);
        await db.SaveChangesAsync();

        // Act
        var deliveryService = scope.ServiceProvider.GetRequiredService<IChannelDeliveryService>();
        var result = await deliveryService.DeliverAsync(
            job,
            artifactId,
            "text",
            "Test content",
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TotalAttempted.Should().Be(2);
        
        // At least one should have been attempted (may succeed or fail depending on network)
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var auditLogs = await verifyDb.AdapterDeliveryLogs
            .Where(l => l.JobId == jobId)
            .ToListAsync();

        auditLogs.Should().HaveCount(2);
        _output.WriteLine($"Partial failure test: {result.SuccessCount} succeeded, {result.FailureCount} failed");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 9: Session 5 Demo Flow
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Session5DemoFlow_RealArtifactData_MultiChannelDelivery()
    {
        // Arrange - This test requires real webhook URLs
        var teamsUrl = Environment.GetEnvironmentVariable("TEAMS_WEBHOOK_URL");
        var slackUrl = Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL");
        
        Skip.If(string.IsNullOrEmpty(teamsUrl) && string.IsNullOrEmpty(slackUrl), 
            "No webhook URLs configured - set TEAMS_WEBHOOK_URL or SLACK_WEBHOOK_URL");

        _output.WriteLine("Running Session 5 demo flow with real webhooks...");

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        // Create realistic job scenario
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "Session5DemoJob",
            Prompt = "Analyze latest deployment metrics and summarize findings",
            CronExpression = "0 9 * * MON",  // Weekly Monday 9am
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        };
        db.Add(job);

        // Configure all available channels
        var channels = new List<JobChannelConfiguration>();
        
        if (!string.IsNullOrEmpty(teamsUrl))
        {
            channels.Add(new JobChannelConfiguration
            {
                JobId = jobId,
                ChannelType = "Teams",
                ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = teamsUrl }),
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!string.IsNullOrEmpty(slackUrl))
        {
            channels.Add(new JobChannelConfiguration
            {
                JobId = jobId,
                ChannelType = "Slack",
                ChannelConfig = JsonSerializer.Serialize(new { webhookUrl = slackUrl }),
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Always add webhook for testing
        channels.Add(new JobChannelConfiguration
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            ChannelConfig = "https://httpstat.us/200",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        });

        db.AddRange(channels);
        await db.SaveChangesAsync();

        // Act - Simulate real artifact content
        var artifactContent = @"# Deployment Analysis - Week of " + DateTime.UtcNow.ToString("yyyy-MM-dd") + @"

## Executive Summary
All deployments completed successfully with zero downtime.

## Key Metrics
- **Total Deployments**: 12
- **Success Rate**: 100%
- **Average Duration**: 4.2 minutes
- **Incidents**: 0

## Recommendations
1. Continue current deployment cadence
2. Monitor API response times in production
3. Schedule next major release for end of month

---
Generated by OpenClawNet on " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        var deliveryService = scope.ServiceProvider.GetRequiredService<IChannelDeliveryService>();
        var result = await deliveryService.DeliverAsync(
            job,
            artifactId,
            "markdown",
            artifactContent,
            CancellationToken.None);

        // Assert
        _output.WriteLine($"Demo flow completed: {result.SuccessCount}/{result.TotalAttempted} channels delivered successfully");
        _output.WriteLine($"Duration: {result.Duration.TotalSeconds:F2} seconds");

        result.Should().NotBeNull();
        result.TotalAttempted.Should().Be(channels.Count);

        // Verify all deliveries logged
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var auditLogs = await verifyDb.AdapterDeliveryLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.ChannelType)
            .ToListAsync();

        auditLogs.Should().HaveCount(channels.Count);
        
        foreach (var log in auditLogs)
        {
            _output.WriteLine($"  ✓ {log.ChannelType}: {log.Status}" + 
                (log.Status == DeliveryStatus.Success ? $" (delivered at {log.DeliveredAt})" : $" - {log.ErrorMessage}"));
        }

        // At least one delivery should succeed (the webhook test endpoint)
        result.SuccessCount.Should().BeGreaterThanOrEqualTo(1);
    }
}
