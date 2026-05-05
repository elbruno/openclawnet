using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;

namespace OpenClawNet.UnitTests.Services;

public sealed class JobExecutorTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private IDbContextFactory<OpenClawDbContext> _dbFactory = null!;
    private readonly string _tempDir;

    public JobExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ocn-test-" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbFactory = new TestDbContextFactory(options);

        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await SchemaMigrator.MigrateAsync(db);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private RuntimeModelSettings CreateTestRuntimeSettings(string provider = "ollama", string? model = "llama3.2:3b")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Model:Provider"] = provider,
                ["Model:Model"] = model
            })
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);
        return new RuntimeModelSettings(config, mockEnv.Object, NullLogger<RuntimeModelSettings>.Instance);
    }

    private IAgentProfileStore CreateMockProfileStore(AgentProfile? profileToReturn = null)
    {
        var mock = new Mock<IAgentProfileStore>();
        mock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profileToReturn);
        return mock.Object;
    }

    private RuntimeAgentProvider CreateMockRuntimeAgentProvider()
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        var runtimeSettings = CreateTestRuntimeSettings();
        return new RuntimeAgentProvider(mockServiceProvider.Object, runtimeSettings, NullLogger<RuntimeAgentProvider>.Instance);
    }

    [Fact]
    public async Task ExecuteJobAsync_CreatesJobRun_AndRecordsResult()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Test Job",
            Prompt = "Hello, world!",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Test response", totalTokens: 42);
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore();
        var runtimeSettings = CreateTestRuntimeSettings();
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RunId);
        Assert.Equal("Test response", result.Output);
        Assert.Equal(42, result.TokensUsed);
        Assert.False(result.WasDryRun);

        // Verify JobRun persisted
        await using var dbVerify = await _dbFactory.CreateDbContextAsync();
        var run = await dbVerify.JobRuns.FirstOrDefaultAsync(r => r.Id == result.RunId);
        Assert.NotNull(run);
        Assert.Equal("completed", run.Status);
        Assert.Equal("Test response", run.Result);
        Assert.Equal(42, run.TokensUsed);
    }

    [Fact]
    public async Task ExecuteJobAsync_WithInputParameters_SubstitutesPrompt()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Parameterized Job",
            Prompt = "Hello, {name}! Your order {order_id} is ready.",
            Status = JobStatus.Active,
            InputParametersJson = JsonSerializer.Serialize(new { name = "Alice", order_id = 123 }),
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Order confirmed");
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore();
        var runtimeSettings = CreateTestRuntimeSettings();
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Hello, Alice! Your order 123 is ready.", mockRuntime.LastPrompt);
    }

    [Fact]
    public async Task ExecuteJobAsync_WithRuntimeOverrides_MergesInputs()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Override Job",
            Prompt = "Customer: {customer_id}, Region: {region}",
            Status = JobStatus.Active,
            InputParametersJson = JsonSerializer.Serialize(new { customer_id = "C123", region = "US" }),
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("OK");
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore();
        var runtimeSettings = CreateTestRuntimeSettings();
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        var overrides = new Dictionary<string, object> { ["region"] = "EU" };

        // Act
        var result = await executor.ExecuteJobAsync(job.Id, overrides);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Customer: C123, Region: EU", mockRuntime.LastPrompt);
    }

    [Fact]
    public async Task ExecuteJobAsync_DryRun_DoesNotPersistJobRun()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Dry Run Test",
            Prompt = "Test",
            Status = JobStatus.Draft,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Dry run output");
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore();
        var runtimeSettings = CreateTestRuntimeSettings();
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id, dryRun: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.RunId);  // No run created
        Assert.Equal("Dry run output", result.Output);
        Assert.True(result.WasDryRun);

        // Verify no JobRun persisted
        await using var dbVerify = await _dbFactory.CreateDbContextAsync();
        var runCount = await dbVerify.JobRuns.CountAsync();
        Assert.Equal(0, runCount);
    }

    [Fact]
    public async Task ExecuteJobAsync_JobNotFound_ReturnsNotFound()
    {
        // Arrange
        var mockRuntime = new MockAgentRuntime("irrelevant");
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore();
        var runtimeSettings = CreateTestRuntimeSettings();
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error!);
    }

    [Fact]
    public async Task ExecuteJobAsync_HandlesExecutionFailure_RecordsError()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Failing Job",
            Prompt = "Fail",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime(throwException: true);
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore();
        var runtimeSettings = CreateTestRuntimeSettings();
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.RunId);

        // Verify JobRun marked as failed
        await using var dbVerify = await _dbFactory.CreateDbContextAsync();
        var run = await dbVerify.JobRuns.FirstOrDefaultAsync(r => r.Id == result.RunId);
        Assert.NotNull(run);
        Assert.Equal("failed", run.Status);
        Assert.NotNull(run.Error);

        // Bug fix: persist the FULL exception chain (ex.ToString()) on JobRun.Error
        // so the Channels detail page and live console can show stack traces.
        // ex.ToString() always begins with the exception type's full name.
        Assert.Contains("System.InvalidOperationException", run.Error);
        Assert.Contains("Simulated execution failure", run.Error);
        Assert.Contains("at ", run.Error); // stack-trace marker
    }

    [Fact]
    public async Task JobExecutor_UsesAgentProfile_WhenProfileNameSet()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Profile-Based Job",
            Prompt = "Test with profile",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual,
            AgentProfileName = "custom-profile"
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var profile = new AgentProfile
        {
            Name = "custom-profile",
            Provider = "azure-openai"
        };

        var mockRuntime = new MockAgentRuntime("Response from custom profile");
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore(profile);
        var runtimeSettings = CreateTestRuntimeSettings();
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Response from custom profile", result.Output);
    }

    [Fact]
    public async Task JobExecutor_FallsBackToRuntimeSettings_WhenProfileNotFound()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Missing Profile Job",
            Prompt = "Test with missing profile",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual,
            AgentProfileName = "missing-profile"
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Fallback response");
        var mockProvider = CreateMockRuntimeAgentProvider();
        var profileStore = CreateMockProfileStore(null);  // Profile not found
        var runtimeSettings = CreateTestRuntimeSettings("ollama", "llama3.2:3b");
        var executor = new JobExecutor(_dbFactory, mockProvider, mockRuntime, profileStore, runtimeSettings, NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Fallback response", result.Output);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test Doubles
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteJobAsync_RecordsJobRunEvents_ForToolCalls()
    {
        // Arrange — agent ran two tools: file_system + markdown_convert
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Tool-using job",
            Prompt = "Do the thing",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var calls = new List<OpenClawNet.Agent.ToolCall>
        {
            new() { Id = "c1", Name = "file_system", Arguments = "{\"action\":\"list\",\"path\":\"c:/temp\"}" },
            new() { Id = "c2", Name = "markdown_convert", Arguments = "{\"url\":\"https://elbruno.com\"}" }
        };
        var results = new List<OpenClawNet.Tools.Abstractions.ToolResult>
        {
            OpenClawNet.Tools.Abstractions.ToolResult.Ok("file_system", "[\"a.txt\",\"b.txt\"]", TimeSpan.FromMilliseconds(15)),
            OpenClawNet.Tools.Abstractions.ToolResult.Ok("markdown_convert", "# Hello\nbody", TimeSpan.FromMilliseconds(220))
        };

        var runtime = new MockAgentRuntime("done", totalTokens: 99, toolCalls: calls, toolResults: results);
        var executor = new JobExecutor(_dbFactory, CreateMockRuntimeAgentProvider(), runtime,
            CreateMockProfileStore(), CreateTestRuntimeSettings(), NullLogger<JobExecutor>.Instance);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        await using var verify = await _dbFactory.CreateDbContextAsync();
        var events = await verify.JobRunEvents
            .Where(e => e.JobRunId == result.RunId)
            .OrderBy(e => e.Sequence)
            .ToListAsync();

        // started + 2 tool_calls + completed = 4
        Assert.Equal(4, events.Count);
        Assert.Equal(JobRunEventKind.AgentStarted, events[0].Kind);
        Assert.Equal(JobRunEventKind.ToolCall, events[1].Kind);
        Assert.Equal("file_system", events[1].ToolName);
        Assert.Contains("c:/temp", events[1].ArgumentsJson);
        Assert.Equal("[\"a.txt\",\"b.txt\"]", events[1].ResultJson);
        Assert.Equal(15, events[1].DurationMs);
        Assert.Equal(JobRunEventKind.ToolCall, events[2].Kind);
        Assert.Equal("markdown_convert", events[2].ToolName);
        Assert.Equal(JobRunEventKind.AgentCompleted, events[3].Kind);
        Assert.Equal(99, events[3].TokensUsed);
    }

    [Fact]
    public async Task ExecuteJobAsync_RecordsAgentFailedEvent_OnException()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Failing job",
            Prompt = "go",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var runtime = new MockAgentRuntime(throwException: true);
        var executor = new JobExecutor(_dbFactory, CreateMockRuntimeAgentProvider(), runtime,
            CreateMockProfileStore(), CreateTestRuntimeSettings(), NullLogger<JobExecutor>.Instance);

        var result = await executor.ExecuteJobAsync(job.Id);

        Assert.False(result.IsSuccess);
        await using var verify = await _dbFactory.CreateDbContextAsync();
        var events = await verify.JobRunEvents
            .Where(e => e.JobRunId == result.RunId)
            .OrderBy(e => e.Sequence)
            .ToListAsync();

        Assert.Equal(2, events.Count); // started + failed (no tool calls)
        Assert.Equal(JobRunEventKind.AgentStarted, events[0].Kind);
        Assert.Equal(JobRunEventKind.AgentFailed, events[1].Kind);
        Assert.Contains("Simulated execution failure", events[1].Message);
    }

    [Fact]
    public async Task ExecuteJobAsync_TruncatesOversizePayloads_InEvents()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Huge result job",
            Prompt = "go",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var huge = new string('x', JobRunEvent.MaxPayloadBytes + 1000);
        var calls = new List<OpenClawNet.Agent.ToolCall>
        {
            new() { Id = "c1", Name = "noisy_tool", Arguments = huge }
        };
        var results = new List<OpenClawNet.Tools.Abstractions.ToolResult>
        {
            OpenClawNet.Tools.Abstractions.ToolResult.Ok("noisy_tool", huge, TimeSpan.Zero)
        };

        var runtime = new MockAgentRuntime("done", toolCalls: calls, toolResults: results);
        var executor = new JobExecutor(_dbFactory, CreateMockRuntimeAgentProvider(), runtime,
            CreateMockProfileStore(), CreateTestRuntimeSettings(), NullLogger<JobExecutor>.Instance);

        var result = await executor.ExecuteJobAsync(job.Id);

        await using var verify = await _dbFactory.CreateDbContextAsync();
        var toolEvent = await verify.JobRunEvents
            .Where(e => e.JobRunId == result.RunId && e.Kind == JobRunEventKind.ToolCall)
            .SingleAsync();

        Assert.NotNull(toolEvent.ArgumentsJson);
        Assert.NotNull(toolEvent.ResultJson);
        Assert.Contains("[truncated", toolEvent.ArgumentsJson);
        Assert.Contains("[truncated", toolEvent.ResultJson);
        // Each is bounded: original size + truncation suffix
        Assert.True(toolEvent.ArgumentsJson!.Length < huge.Length);
    }

    // ═══════════════════════════════════════════════════════════════
    // Phase 2 Story 6: Multi-Channel Delivery Integration Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteJobAsync_NoChannelConfigs_CompletesWithoutCallingDeliveryService()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "No Channel Job",
            Prompt = "Test",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Success");
        var mockDeliveryService = new Mock<OpenClawNet.Channels.Services.IChannelDeliveryService>();
        var executor = new JobExecutor(
            _dbFactory,
            CreateMockRuntimeAgentProvider(),
            mockRuntime,
            CreateMockProfileStore(),
            CreateTestRuntimeSettings(),
            NullLogger<JobExecutor>.Instance,
            invocationLogger: null,
            deliveryService: mockDeliveryService.Object);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        // Delivery service should NOT be called (no configs)
        mockDeliveryService.Verify(
            s => s.DeliverAsync(It.IsAny<ScheduledJob>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteJobAsync_WithEnabledChannels_CallsDeliveryService()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Channel Job",
            Prompt = "Test",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        
        // Add 2 enabled channel configurations
        db.JobChannelConfigurations.Add(new JobChannelConfiguration
        {
            JobId = job.Id,
            ChannelType = "GenericWebhook",
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook1\"}",
            IsEnabled = true
        });
        db.JobChannelConfigurations.Add(new JobChannelConfiguration
        {
            JobId = job.Id,
            ChannelType = "Teams",
            ChannelConfig = "{\"webhookUrl\":\"https://teams.example.com/webhook\"}",
            IsEnabled = true
        });
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Job completed successfully");
        var deliveryResult = new OpenClawNet.Channels.Dtos.DeliveryResult
        {
            JobId = job.Id.ToString(),
            TotalAttempted = 2,
            SuccessCount = 2,
            FailureCount = 0,
            Duration = TimeSpan.FromMilliseconds(100),
            CompletedAt = DateTime.UtcNow
        };

        var mockDeliveryService = new Mock<OpenClawNet.Channels.Services.IChannelDeliveryService>();
        mockDeliveryService
            .Setup(s => s.DeliverAsync(
                It.Is<ScheduledJob>(j => j.Id == job.Id),
                It.IsAny<Guid>(),
                "text",
                "Job completed successfully",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deliveryResult);

        var executor = new JobExecutor(
            _dbFactory,
            CreateMockRuntimeAgentProvider(),
            mockRuntime,
            CreateMockProfileStore(),
            CreateTestRuntimeSettings(),
            NullLogger<JobExecutor>.Instance,
            invocationLogger: null,
            deliveryService: mockDeliveryService.Object);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        
        // Verify delivery service called with correct parameters
        mockDeliveryService.Verify(
            s => s.DeliverAsync(
                It.Is<ScheduledJob>(j => j.Id == job.Id),
                It.Is<Guid>(id => id == result.RunId),
                "text",
                "Job completed successfully",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_DeliveryServiceThrows_JobStillSucceeds()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Delivery Failure Job",
            Prompt = "Test",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        
        db.JobChannelConfigurations.Add(new JobChannelConfiguration
        {
            JobId = job.Id,
            ChannelType = "GenericWebhook",
            ChannelConfig = "{\"webhookUrl\":\"https://broken.example.com\"}",
            IsEnabled = true
        });
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Job output");
        var mockDeliveryService = new Mock<OpenClawNet.Channels.Services.IChannelDeliveryService>();
        mockDeliveryService
            .Setup(s => s.DeliverAsync(It.IsAny<ScheduledJob>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delivery system unavailable"));

        var executor = new JobExecutor(
            _dbFactory,
            CreateMockRuntimeAgentProvider(),
            mockRuntime,
            CreateMockProfileStore(),
            CreateTestRuntimeSettings(),
            NullLogger<JobExecutor>.Instance,
            invocationLogger: null,
            deliveryService: mockDeliveryService.Object);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert - Fire-and-forget: job still succeeds despite delivery failure
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RunId);
        
        // Verify job run marked as completed
        await using var dbVerify = await _dbFactory.CreateDbContextAsync();
        var run = await dbVerify.JobRuns.FirstOrDefaultAsync(r => r.Id == result.RunId);
        Assert.NotNull(run);
        Assert.Equal("completed", run.Status);
    }

    [Fact]
    public async Task ExecuteJobAsync_PartialDeliveryFailure_JobStillSucceeds()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Partial Delivery Job",
            Prompt = "Test",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        
        db.JobChannelConfigurations.Add(new JobChannelConfiguration
        {
            JobId = job.Id,
            ChannelType = "GenericWebhook",
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}",
            IsEnabled = true
        });
        db.JobChannelConfigurations.Add(new JobChannelConfiguration
        {
            JobId = job.Id,
            ChannelType = "Teams",
            ChannelConfig = "{\"webhookUrl\":\"https://teams.example.com/webhook\"}",
            IsEnabled = true
        });
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Output");
        var deliveryResult = new OpenClawNet.Channels.Dtos.DeliveryResult
        {
            JobId = job.Id.ToString(),
            TotalAttempted = 2,
            SuccessCount = 1,
            FailureCount = 1,
            Failures = new List<OpenClawNet.Channels.Dtos.DeliveryFailure>
            {
                new() { ChannelType = "Teams", ErrorMessage = "Timeout" }
            },
            Duration = TimeSpan.FromMilliseconds(200),
            CompletedAt = DateTime.UtcNow
        };

        var mockDeliveryService = new Mock<OpenClawNet.Channels.Services.IChannelDeliveryService>();
        mockDeliveryService
            .Setup(s => s.DeliverAsync(It.IsAny<ScheduledJob>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deliveryResult);

        var executor = new JobExecutor(
            _dbFactory,
            CreateMockRuntimeAgentProvider(),
            mockRuntime,
            CreateMockProfileStore(),
            CreateTestRuntimeSettings(),
            NullLogger<JobExecutor>.Instance,
            invocationLogger: null,
            deliveryService: mockDeliveryService.Object);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert - Job succeeds even with partial delivery failure
        Assert.True(result.IsSuccess);
        
        await using var dbVerify = await _dbFactory.CreateDbContextAsync();
        var run = await dbVerify.JobRuns.FirstOrDefaultAsync(r => r.Id == result.RunId);
        Assert.NotNull(run);
        Assert.Equal("completed", run.Status);
    }

    [Fact]
    public async Task ExecuteJobAsync_OnlyEnabledChannels_AreUsedForDelivery()
    {
        // Arrange
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = new ScheduledJob
        {
            Name = "Mixed Channel Job",
            Prompt = "Test",
            Status = JobStatus.Active,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        
        // Add 1 enabled and 1 disabled channel
        db.JobChannelConfigurations.Add(new JobChannelConfiguration
        {
            JobId = job.Id,
            ChannelType = "GenericWebhook",
            ChannelConfig = "{\"webhookUrl\":\"https://example.com/webhook\"}",
            IsEnabled = true
        });
        db.JobChannelConfigurations.Add(new JobChannelConfiguration
        {
            JobId = job.Id,
            ChannelType = "Teams",
            ChannelConfig = "{\"webhookUrl\":\"https://teams.example.com/webhook\"}",
            IsEnabled = false  // Disabled
        });
        await db.SaveChangesAsync();

        var mockRuntime = new MockAgentRuntime("Output");
        var mockDeliveryService = new Mock<OpenClawNet.Channels.Services.IChannelDeliveryService>();

        var executor = new JobExecutor(
            _dbFactory,
            CreateMockRuntimeAgentProvider(),
            mockRuntime,
            CreateMockProfileStore(),
            CreateTestRuntimeSettings(),
            NullLogger<JobExecutor>.Instance,
            invocationLogger: null,
            deliveryService: mockDeliveryService.Object);

        // Act
        var result = await executor.ExecuteJobAsync(job.Id);

        // Assert
        Assert.True(result.IsSuccess);
        
        // Verify delivery service called (1 enabled channel found)
        mockDeliveryService.Verify(
            s => s.DeliverAsync(It.IsAny<ScheduledJob>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test Doubles
    // ═══════════════════════════════════════════════════════════════

    private sealed class MockAgentRuntime : IAgentRuntime
    {
        private readonly string _response;
        private readonly int _totalTokens;
        private readonly bool _throwException;
        private readonly IReadOnlyList<OpenClawNet.Agent.ToolCall> _toolCalls;
        private readonly IReadOnlyList<OpenClawNet.Tools.Abstractions.ToolResult> _toolResults;

        public string? LastPrompt { get; private set; }

        public MockAgentRuntime(
            string response = "Mock response",
            int totalTokens = 10,
            bool throwException = false,
            IReadOnlyList<OpenClawNet.Agent.ToolCall>? toolCalls = null,
            IReadOnlyList<OpenClawNet.Tools.Abstractions.ToolResult>? toolResults = null)
        {
            _response = response;
            _totalTokens = totalTokens;
            _throwException = throwException;
            _toolCalls = toolCalls ?? [];
            _toolResults = toolResults ?? [];
        }

        public Task<AgentContext> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
        {
            if (_throwException)
                throw new InvalidOperationException("Simulated execution failure");

            LastPrompt = context.UserMessage;
            context.FinalResponse = _response;
            context.TotalTokens = _totalTokens;
            context.ExecutedToolCalls = _toolCalls;
            context.ToolResults = _toolResults;
            context.IsComplete = true;
            context.CompletedAt = DateTime.UtcNow;
            return Task.FromResult(context);
        }

        public IAsyncEnumerable<AgentStreamEvent> ExecuteStreamAsync(AgentContext context, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;

        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options)
        {
            _options = options;
        }

        public OpenClawDbContext CreateDbContext() => new(_options);

        public async Task<OpenClawDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return new OpenClawDbContext(_options);
        }
    }
}
