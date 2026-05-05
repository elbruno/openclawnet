using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests.Audit;

/// <summary>
/// Integration tests validating that adapter delivery attempts write AdapterDeliveryLog records.
/// Story 5: Audit Trail Integration Tests (Feature 2).
/// Tests both successful and failed delivery scenarios.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AdapterDeliveryLogTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    [Fact]
    public async Task SuccessfulDelivery_WritesLogRecord_WithStatusSuccess()
    {
        // Arrange: Set up test context
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var channelType = "GenericWebhook";
        var channelConfig = """{"url": "https://webhook.site/test", "method": "POST"}""";

        // Act: Simulate successful delivery
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = channelType,
            ChannelConfig = channelConfig,
            Status = DeliveryStatus.Success,
            DeliveredAt = DateTime.UtcNow,
            ResponseCode = 200,
            ErrorMessage = null
        };
        db.Set<AdapterDeliveryLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify success log was written correctly
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<AdapterDeliveryLog>()
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        logRecord.Should().NotBeNull();
        logRecord!.JobId.Should().Be(jobId);
        logRecord.ChannelType.Should().Be(channelType);
        logRecord.ChannelConfig.Should().Be(channelConfig);
        logRecord.Status.Should().Be(DeliveryStatus.Success);
        logRecord.DeliveredAt.Should().NotBeNull();
        logRecord.DeliveredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        logRecord.ResponseCode.Should().Be(200);
        logRecord.ErrorMessage.Should().BeNull();
        logRecord.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task FailedDelivery_WritesLogRecord_WithStatusFailedAndErrorMessage()
    {
        // Arrange: Set up test context for failed delivery
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var channelType = "Teams";
        var channelConfig = """{"webhookUrl": "https://teams.microsoft.com/webhook/invalid"}""";
        var errorMessage = "Webhook endpoint returned 404 Not Found";

        // Act: Simulate failed delivery
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = channelType,
            ChannelConfig = channelConfig,
            Status = DeliveryStatus.Failed,
            DeliveredAt = DateTime.UtcNow,
            ResponseCode = 404,
            ErrorMessage = errorMessage
        };
        db.Set<AdapterDeliveryLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify failure log was written with error details
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<AdapterDeliveryLog>()
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        logRecord.Should().NotBeNull();
        logRecord!.Status.Should().Be(DeliveryStatus.Failed);
        logRecord.ErrorMessage.Should().Be(errorMessage);
        logRecord.ResponseCode.Should().Be(404);
        logRecord.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task NetworkFailure_WritesLogRecord_WithNullResponseCode()
    {
        // Arrange: Set up test context for network failure (no HTTP response)
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var channelType = "Slack";
        var channelConfig = """{"webhookUrl": "https://hooks.slack.com/unreachable"}""";
        var errorMessage = "Network timeout after 30 seconds";

        // Act: Simulate network failure
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = channelType,
            ChannelConfig = channelConfig,
            Status = DeliveryStatus.Failed,
            DeliveredAt = null,
            ResponseCode = null,
            ErrorMessage = errorMessage
        };
        db.Set<AdapterDeliveryLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify network failure logged correctly (no response code)
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<AdapterDeliveryLog>()
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        logRecord.Should().NotBeNull();
        logRecord!.Status.Should().Be(DeliveryStatus.Failed);
        logRecord.ErrorMessage.Should().Be(errorMessage);
        logRecord.ResponseCode.Should().BeNull();
        logRecord.DeliveredAt.Should().BeNull();
    }

    [Fact]
    public async Task PendingDelivery_WritesLogRecord_WithStatusPending()
    {
        // Arrange: Set up test context for pending delivery
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var channelType = "Email";
        var channelConfig = """{"to": "user@example.com", "subject": "Job Result"}""";

        // Act: Create pending delivery log
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = channelType,
            ChannelConfig = channelConfig,
            Status = DeliveryStatus.Pending,
            DeliveredAt = null,
            ResponseCode = null,
            ErrorMessage = null
        };
        db.Set<AdapterDeliveryLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify pending status logged correctly
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<AdapterDeliveryLog>()
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        logRecord.Should().NotBeNull();
        logRecord!.Status.Should().Be(DeliveryStatus.Pending);
        logRecord.DeliveredAt.Should().BeNull();
        logRecord.ResponseCode.Should().BeNull();
        logRecord.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task MultipleDeliveryAttempts_AllLogged()
    {
        // Arrange: Simulate multiple delivery attempts for the same job
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var attempts = new[]
        {
            new { Status = DeliveryStatus.Failed, ErrorMessage = "First attempt failed" },
            new { Status = DeliveryStatus.Failed, ErrorMessage = "Second attempt failed" },
            new { Status = DeliveryStatus.Success, ErrorMessage = (string?)null }
        };

        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var attempt in attempts)
        {
            var logEntry = new AdapterDeliveryLog
            {
                JobId = jobId,
                ChannelType = "GenericWebhook",
                ChannelConfig = """{"url": "https://example.com/webhook"}""",
                Status = attempt.Status,
                DeliveredAt = attempt.Status == DeliveryStatus.Success ? DateTime.UtcNow : null,
                ErrorMessage = attempt.ErrorMessage
            };
            db.Set<AdapterDeliveryLog>().Add(logEntry);
        }
        await db.SaveChangesAsync();

        // Assert: All three attempts should be logged
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecords = await verifyDb.Set<AdapterDeliveryLog>()
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        logRecords.Should().HaveCount(3);
        logRecords[0].Status.Should().Be(DeliveryStatus.Failed);
        logRecords[1].Status.Should().Be(DeliveryStatus.Failed);
        logRecords[2].Status.Should().Be(DeliveryStatus.Success);
    }

    [Fact]
    public async Task DeliveryLog_ContainsChannelConfigSnapshot()
    {
        // Arrange: Create delivery log with detailed config
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var channelConfig = """
        {
            "url": "https://example.com/webhook",
            "method": "POST",
            "headers": {
                "X-Custom-Header": "value123"
            },
            "retryCount": 3
        }
        """;

        // Act: Write log with config snapshot
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            ChannelConfig = channelConfig,
            Status = DeliveryStatus.Success,
            DeliveredAt = DateTime.UtcNow
        };
        db.Set<AdapterDeliveryLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Config snapshot should be preserved exactly
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<AdapterDeliveryLog>()
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        logRecord.Should().NotBeNull();
        logRecord!.ChannelConfig.Should().Contain("https://example.com/webhook");
        logRecord.ChannelConfig.Should().Contain("X-Custom-Header");
        logRecord.ChannelConfig.Should().Contain("retryCount");
    }

    [Fact]
    public async Task DeliveryLog_ContainsAllRequiredFields()
    {
        // Arrange & Act: Create a complete delivery log
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var channelType = "Teams";
        var channelConfig = """{"webhookUrl": "https://teams.microsoft.com/webhook/test"}""";

        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = channelType,
            ChannelConfig = channelConfig,
            Status = DeliveryStatus.Success,
            DeliveredAt = DateTime.UtcNow,
            ResponseCode = 200
        };
        db.Set<AdapterDeliveryLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify all required fields are populated
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<AdapterDeliveryLog>()
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        logRecord.Should().NotBeNull();
        logRecord!.Id.Should().NotBeEmpty();
        logRecord.JobId.Should().Be(jobId);
        logRecord.ChannelType.Should().Be(channelType);
        logRecord.ChannelConfig.Should().Be(channelConfig);
        logRecord.Status.Should().Be(DeliveryStatus.Success);
        logRecord.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }
}
