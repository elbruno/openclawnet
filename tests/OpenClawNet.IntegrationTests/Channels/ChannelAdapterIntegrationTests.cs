using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClawNet.Channels.Adapters;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OpenClawNet.IntegrationTests.Channels;

/// <summary>
/// Integration tests for channel adapter system - Phase 2A Story 8.
/// Tests: Factory resolution, webhook delivery, error handling, and audit trail.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Phase", "2A")]
[Trait("Story", "8")]
public sealed class ChannelAdapterIntegrationTests : IClassFixture<GatewayWebAppFactory>, IAsyncLifetime
{
    private readonly GatewayWebAppFactory _factory;
    private WireMockServer? _mockServer;

    public ChannelAdapterIntegrationTests(GatewayWebAppFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        // Start WireMock server for HTTP testing
        _mockServer = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _mockServer?.Stop();
        _mockServer?.Dispose();
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 1: Factory Resolution
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FactoryResolution_ResolveTeamsAdapter_ReturnsCorrectType()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        // Act
        var adapter = factory.CreateAdapter("Teams");

        // Assert
        adapter.Should().NotBeNull();
        adapter.Should().BeOfType<TeamsProactiveAdapter>();
        adapter.Name.Should().Be("Teams");
    }

    [Fact]
    public async Task FactoryResolution_ResolveSlackAdapter_ReturnsCorrectType()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        // Act
        var adapter = factory.CreateAdapter("Slack");

        // Assert
        adapter.Should().NotBeNull();
        adapter.Should().BeOfType<SlackWebhookAdapter>();
        adapter.Name.Should().Be("Slack");
    }

    [Fact]
    public async Task FactoryResolution_ResolveWebhookAdapter_ReturnsCorrectType()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        // Act
        var adapter = factory.CreateAdapter("GenericWebhook");

        // Assert
        adapter.Should().NotBeNull();
        adapter.Should().BeOfType<GenericWebhookAdapter>();
        adapter.Name.Should().Be("GenericWebhook");
    }

    [Fact]
    public async Task FactoryResolution_UnknownChannel_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        // Act
        var act = () => factory.CreateAdapter("UnknownAdapter");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown adapter type: UnknownAdapter*");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 2: Webhook Adapter Delivery
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WebhookDelivery_ValidEndpoint_PostsCorrectPayload()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GenericWebhookAdapter>>();
        var httpClient = httpClientFactory.CreateClient();

        var adapter = new GenericWebhookAdapter(httpClient, logger);

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = $"{_mockServer!.Urls[0]}/webhook";

        // Setup mock endpoint
        _mockServer!.Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        // Act
        var result = await adapter.DeliverAsync(
            jobId,
            "TestJob",
            artifactId,
            "markdown",
            webhookUrl,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNullOrEmpty();

        // Verify HTTP POST was made
        var requests = _mockServer.LogEntries;
        requests.Should().HaveCount(1);
        requests.First().RequestMessage.Path.Should().Be("/webhook");
        requests.First().RequestMessage.Method.Should().Be("POST");
    }

    [Fact]
    public async Task WebhookDelivery_PayloadSerialization_ContainsExpectedFields()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GenericWebhookAdapter>>();
        var httpClient = httpClientFactory.CreateClient();

        var adapter = new GenericWebhookAdapter(httpClient, logger);

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = $"{_mockServer!.Urls[0]}/webhook";

        _mockServer!.Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        // Act
        await adapter.DeliverAsync(
            jobId,
            "TestJob",
            artifactId,
            "json",
            webhookUrl,
            CancellationToken.None);

        // Assert - verify payload structure
        var request = _mockServer.LogEntries.First();
        var body = request.RequestMessage.Body;
        body.Should().NotBeNullOrEmpty();

        var payload = JsonSerializer.Deserialize<JsonElement>(body!);
        payload.GetProperty("JobId").GetGuid().Should().Be(jobId);
        payload.GetProperty("JobName").GetString().Should().Be("TestJob");
        payload.GetProperty("ArtifactId").GetGuid().Should().Be(artifactId);
        payload.GetProperty("ArtifactType").GetString().Should().Be("json");
        payload.TryGetProperty("DeliveredAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task WebhookDelivery_TimeoutScenario_ReturnsFailureResult()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GenericWebhookAdapter>>();
        var httpClient = httpClientFactory.CreateClient();

        var adapter = new GenericWebhookAdapter(httpClient, logger);

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = $"{_mockServer!.Urls[0]}/slow-endpoint";

        // Setup slow endpoint (delays 60 seconds to force timeout)
        _mockServer!.Given(Request.Create().WithPath("/slow-endpoint").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromSeconds(60)));

        // Act - use cancellation token to simulate timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await adapter.DeliverAsync(
            jobId,
            "TestJob",
            artifactId,
            "markdown",
            webhookUrl,
            cts.Token);

        // Assert - should fail due to cancellation
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task WebhookDelivery_TransientFailure_ReturnedInResult()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GenericWebhookAdapter>>();
        var httpClient = httpClientFactory.CreateClient();

        var adapter = new GenericWebhookAdapter(httpClient, logger);

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = $"{_mockServer!.Urls[0]}/transient-error";

        // Setup endpoint that returns 503
        _mockServer!.Given(Request.Create().WithPath("/transient-error").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        // Act
        var result = await adapter.DeliverAsync(
            jobId,
            "TestJob",
            artifactId,
            "markdown",
            webhookUrl,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.ErrorMessage.Should().Contain("503");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 3: Error Handling
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ErrorHandling_NetworkError_LoggedAndReturned()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GenericWebhookAdapter>>();
        var httpClient = httpClientFactory.CreateClient();

        var adapter = new GenericWebhookAdapter(httpClient, logger);

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = "http://invalid-domain-that-does-not-exist.local/webhook";

        // Act
        var result = await adapter.DeliverAsync(
            jobId,
            "TestJob",
            artifactId,
            "markdown",
            webhookUrl,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ErrorHandling_InvalidPayload_ReturnsError()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GenericWebhookAdapter>>();
        var httpClient = httpClientFactory.CreateClient();

        var adapter = new GenericWebhookAdapter(httpClient, logger);

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        // Act - pass empty/invalid URL
        var result = await adapter.DeliverAsync(
            jobId,
            "TestJob",
            artifactId,
            "markdown",
            "",
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Webhook");
    }

    [Fact]
    public async Task ErrorHandling_DeliveryResultContainsErrorDetails()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GenericWebhookAdapter>>();
        var httpClient = httpClientFactory.CreateClient();

        var adapter = new GenericWebhookAdapter(httpClient, logger);

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var webhookUrl = $"{_mockServer!.Urls[0]}/error-endpoint";

        _mockServer!.Given(Request.Create().WithPath("/error-endpoint").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));

        // Act
        var result = await adapter.DeliverAsync(
            jobId,
            "TestJob",
            artifactId,
            "markdown",
            webhookUrl,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test 4: Audit Trail
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuditTrail_SuccessfulDelivery_LoggedWithTimestamp()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        // Create test job
        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "AuditTrailJob",
            Prompt = "Test audit trail",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);
        await db.SaveChangesAsync();

        // Act - Create successful delivery log
        var log = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = "GenericWebhook",
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}",
            Status = DeliveryStatus.Success,
            DeliveredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AdapterDeliveryLogs.Add(log);
        await db.SaveChangesAsync();

        // Assert
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var savedLog = await verifyDb.AdapterDeliveryLogs
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        savedLog.Should().NotBeNull();
        savedLog!.Status.Should().Be(DeliveryStatus.Success);
        savedLog.DeliveredAt.Should().NotBeNull();
        savedLog.DeliveredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        savedLog.ErrorMessage.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task AuditTrail_FailedDelivery_LoggedWithErrorMessage()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "FailedDeliveryJob",
            Prompt = "Test failed delivery",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);
        await db.SaveChangesAsync();

        // Act - Create failed delivery log
        var log = new AdapterDeliveryLog
        {
            JobId = jobId,
            ChannelType = "Slack",
            ChannelConfig = "{\"webhookUrl\":\"https://hooks.slack.com/invalid\"}",
            Status = DeliveryStatus.Failed,
            ErrorMessage = "HTTP error: 404 Not Found",
            CreatedAt = DateTime.UtcNow
        };
        db.AdapterDeliveryLogs.Add(log);
        await db.SaveChangesAsync();

        // Assert
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var savedLog = await verifyDb.AdapterDeliveryLogs
            .FirstOrDefaultAsync(l => l.JobId == jobId);

        savedLog.Should().NotBeNull();
        savedLog!.Status.Should().Be(DeliveryStatus.Failed);
        savedLog.ErrorMessage.Should().Be("HTTP error: 404 Not Found");
        savedLog.DeliveredAt.Should().BeNull();
    }

    [Fact]
    public async Task AuditTrail_QueryEndpoint_ReturnsAllAttempts()
    {
        // Arrange
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var jobId = Guid.NewGuid();

        await using var db = await dbFactory.CreateDbContextAsync();

        var job = new ScheduledJob
        {
            Id = jobId,
            Name = "MultiAttemptJob",
            Prompt = "Test multiple attempts",
            CronExpression = "0 0 * * *",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Add(job);

        // Create multiple delivery logs
        var logs = new[]
        {
            new AdapterDeliveryLog
            {
                JobId = jobId,
                ChannelType = "Teams",
                ChannelConfig = "{}",
                Status = DeliveryStatus.Failed,
                ErrorMessage = "First attempt failed",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new AdapterDeliveryLog
            {
                JobId = jobId,
                ChannelType = "Slack",
                ChannelConfig = "{}",
                Status = DeliveryStatus.Success,
                DeliveredAt = DateTime.UtcNow.AddMinutes(-2),
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new AdapterDeliveryLog
            {
                JobId = jobId,
                ChannelType = "GenericWebhook",
                ChannelConfig = "{}",
                Status = DeliveryStatus.Success,
                DeliveredAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            }
        };
        db.AdapterDeliveryLogs.AddRange(logs);
        await db.SaveChangesAsync();

        // Act - Query all logs for this job
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var allLogs = await verifyDb.AdapterDeliveryLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        // Assert
        allLogs.Should().HaveCount(3);
        allLogs[0].Status.Should().Be(DeliveryStatus.Failed);
        allLogs[1].Status.Should().Be(DeliveryStatus.Success);
        allLogs[2].Status.Should().Be(DeliveryStatus.Success);
    }
}
