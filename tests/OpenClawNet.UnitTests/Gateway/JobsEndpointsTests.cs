using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;

namespace OpenClawNet.UnitTests.Gateway;

public sealed class JobsEndpointsTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new WebApplicationFactory(options);
        _client = _factory.CreateClient();

        await using var db = _factory.Services.GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SchemaMigrator.MigrateAsync(db);

        // Give the server a moment to start listening
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteJob_CreatesJobRun_AndReturnsResult()
    {
        // Arrange
        var jobId = await CreateTestJobAsync();

        // Act
        var response = await _client.PostAsync($"/api/jobs/{jobId}/execute", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JobExecutionResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.RunId);
        Assert.NotEmpty(result.Output!);
        Assert.True(result.TokensUsed > 0);
    }

    [Fact]
    public async Task ExecuteJob_WithInputParameters_SubstitutesIntoPrompt()
    {
        // Arrange
        var jobId = await CreateTestJobAsync(prompt: "Hello, {name}!");
        var request = new JobExecutionRequest
        {
            InputParameters = new Dictionary<string, object> { ["name"] = "World" }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/jobs/{jobId}/execute", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JobExecutionResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task StartJob_TransitionsDraftToActive()
    {
        // Arrange
        var jobId = await CreateTestJobAsync(status: JobStatus.Draft);

        // Act
        var response = await _client.PostAsync($"/api/jobs/{jobId}/start", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.NotNull(job);
        Assert.Equal("active", job.Status);
    }

    [Fact]
    public async Task PauseJob_TransitionsActiveToTest()
    {
        // Arrange
        var jobId = await CreateTestJobAsync(status: JobStatus.Active);

        // Act
        var response = await _client.PostAsync($"/api/jobs/{jobId}/pause", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.NotNull(job);
        Assert.Equal("paused", job.Status);
    }

    [Fact]
    public async Task ResumeJob_TransitionsPausedToActive()
    {
        // Arrange
        var jobId = await CreateTestJobAsync(status: JobStatus.Paused);

        // Act
        var response = await _client.PostAsync($"/api/jobs/{jobId}/resume", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.NotNull(job);
        Assert.Equal("active", job.Status);
    }

    [Fact]
    public async Task RunNowJob_CreatesJobRun_AndReturnsResult()
    {
        // Arrange
        var jobId = await CreateTestJobAsync();

        // Act
        var response = await _client.PostAsync($"/api/jobs/{jobId}/run-now", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JobExecutionResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.RunId);
        Assert.False(result.WasDryRun);
        Assert.NotEmpty(result.Output!);
    }

    [Fact]
    public async Task RunNowJob_UnknownJob_ReturnsNotFound()
    {
        var response = await _client.PostAsync($"/api/jobs/{Guid.NewGuid()}/run-now", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DryRunJob_ExecutesWithoutCreatingJobRun()
    {
        // Arrange
        var jobId = await CreateTestJobAsync();

        // Act
        var response = await _client.PostAsync($"/api/jobs/{jobId}/dry-run", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JobExecutionResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Null(result.RunId);  // No run created
        Assert.True(result.WasDryRun);

        // Verify no runs persisted
        await using var db = _factory.Services.GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var runCount = await db.JobRuns.CountAsync(r => r.JobId == jobId);
        Assert.Equal(0, runCount);
    }

    [Fact]
    public async Task GetJobStats_ReturnsAggregatedMetrics()
    {
        // Arrange
        var jobId = await CreateTestJobAsync();
        
        // Execute job 3 times
        await _client.PostAsync($"/api/jobs/{jobId}/execute", null);
        await _client.PostAsync($"/api/jobs/{jobId}/execute", null);
        await _client.PostAsync($"/api/jobs/{jobId}/execute", null);

        // Act
        var response = await _client.GetAsync($"/api/jobs/{jobId}/stats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await response.Content.ReadFromJsonAsync<JobStatsResponse>();
        Assert.NotNull(stats);
        Assert.Equal(jobId, stats.JobId);
        Assert.Equal(3, stats.TotalRuns);
        Assert.Equal(3, stats.CompletedRuns);
        Assert.Equal(0, stats.FailedRuns);
        Assert.True(stats.TotalTokensUsed > 0);
        Assert.True(stats.AverageTokensPerRun > 0);
    }

    [Fact]
    public async Task StartJob_InvalidTransition_ReturnsConflict()
    {
        // Arrange
        var jobId = await CreateTestJobAsync(status: JobStatus.Completed);

        // Act
        var response = await _client.PostAsync($"/api/jobs/{jobId}/start", null);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // PATCH /api/jobs/{id} — inline-rename happy path + validation.
    // PATCH is allowed in any status (including Active), unlike PUT.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchJob_RenamesActiveJob_AndPersistsChange()
    {
        // Arrange — Active job (PUT would 409 here, PATCH must succeed)
        var jobId = await CreateTestJobAsync(name: "Old Name", status: JobStatus.Active);

        var response = await _client.PatchAsJsonAsync(
            $"/api/jobs/{jobId}",
            new { Name = "New Name" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.NotNull(dto);
        Assert.Equal("New Name", dto.Name);
        Assert.Equal("active", dto.Status);

        // Verify persisted
        await using var db = _factory.Services.GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var stored = await db.Jobs.FindAsync(jobId);
        Assert.NotNull(stored);
        Assert.Equal("New Name", stored!.Name);
    }

    [Fact]
    public async Task PatchJob_TrimsWhitespaceAroundName()
    {
        var jobId = await CreateTestJobAsync(status: JobStatus.Active);

        var response = await _client.PatchAsJsonAsync(
            $"/api/jobs/{jobId}",
            new { Name = "  Trimmed  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal("Trimmed", dto!.Name);
    }

    [Fact]
    public async Task PatchJob_EmptyName_ReturnsBadRequest()
    {
        var jobId = await CreateTestJobAsync(status: JobStatus.Active);

        var response = await _client.PatchAsJsonAsync(
            $"/api/jobs/{jobId}",
            new { Name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchJob_NoFields_IsNoOp_ReturnsCurrentDto()
    {
        var jobId = await CreateTestJobAsync(name: "Stay", status: JobStatus.Active);

        var response = await _client.PatchAsJsonAsync(
            $"/api/jobs/{jobId}",
            new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal("Stay", dto!.Name);
    }

    [Fact]
    public async Task PatchJob_UnknownId_ReturnsNotFound()
    {
        var response = await _client.PatchAsJsonAsync(
            $"/api/jobs/{Guid.NewGuid()}",
            new { Name = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // POST /api/jobs — default-agent-profile snapshotting
    // (regression test: jobs created from a template were created with
    // AgentProfileName=null, so the UI showed "—" and JobExecutor fell
    // back to RuntimeModelSettings instead of the user's default profile.)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateJob_WithoutAgentProfile_AssignsDefaultProfileName()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new CreateJobRequest
        {
            Name = "Job without explicit profile",
            Prompt = "Hello"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.NotNull(dto);
        Assert.Equal("openclawnet-agent", dto!.AgentProfileName);
    }

    [Fact]
    public async Task CreateJob_WithExplicitAgentProfile_PreservesIt()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new CreateJobRequest
        {
            Name = "Job with explicit profile",
            Prompt = "Hello",
            AgentProfileName = "my-custom-profile"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal("my-custom-profile", dto!.AgentProfileName);
    }

    [Fact]
    public async Task CreateJobFromTemplate_WithoutAgentProfile_AssignsDefaultProfileName()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs/from-template/website-watcher/activate",
            new CreateJobRequest
            {
                Name = "Watcher from template",
                Prompt = "Watch https://example.com"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JobDto>();
        Assert.NotNull(dto);
        Assert.Equal("openclawnet-agent", dto!.AgentProfileName);
        Assert.Equal("website-watcher", dto.SourceTemplateName);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<Guid> CreateTestJobAsync(
        string name = "Test Job",
        string prompt = "Test prompt",
        JobStatus status = JobStatus.Active)
    {
        await using var db = _factory.Services.GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        
        var job = new ScheduledJob
        {
            Name = name,
            Prompt = prompt,
            Status = status,
            TriggerType = TriggerType.Manual
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    // ═══════════════════════════════════════════════════════════════
    // Test WebApplicationFactory
    // ═══════════════════════════════════════════════════════════════

    private sealed class WebApplicationFactory : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private Task? _runTask;
        public IServiceProvider Services => _app.Services;
        private readonly string _baseUrl = "http://localhost:15998"; // Random high port

        public WebApplicationFactory(DbContextOptions<OpenClawDbContext> dbOptions)
        {
            var builder = WebApplication.CreateBuilder();
            
            // Register minimal services for testing
            builder.Services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new TestDbContextFactory(dbOptions));
            
            // Mock agent runtime
            builder.Services.AddSingleton<IAgentRuntime, MockAgentRuntime>();
            
            // Create RuntimeModelSettings for tests
            var tempDir = Path.Combine(Path.GetTempPath(), "ocn-jobtest-" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Model:Provider"] = "ollama",
                    ["Model:Model"] = "llama3.2:3b"
                })
                .Build();
            var mockEnv = new Mock<IHostEnvironment>();
            mockEnv.Setup(e => e.ContentRootPath).Returns(tempDir);
            var runtimeSettings = new RuntimeModelSettings(config, mockEnv.Object, NullLogger<RuntimeModelSettings>.Instance);
            builder.Services.AddSingleton(runtimeSettings);
            
            // Mock agent profile store
            var mockProfileStore = new Mock<OpenClawNet.Storage.IAgentProfileStore>();
            mockProfileStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OpenClawNet.Models.Abstractions.AgentProfile?)null);
            mockProfileStore.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OpenClawNet.Models.Abstractions.AgentProfile
                {
                    Name = "openclawnet-agent",
                    DisplayName = "OpenClawNet Agent",
                    IsDefault = true,
                    IsEnabled = true,
                    Provider = "ollama-default"
                });
            builder.Services.AddSingleton(mockProfileStore.Object);
            
            // RuntimeAgentProvider with mocks
            builder.Services.AddSingleton<RuntimeAgentProvider>();
            builder.Services.AddScoped<JobExecutor>();
            builder.Services.AddSingleton<OpenClawNet.Gateway.Services.JobTemplates.JobTemplatesProvider>();

            builder.WebHost.UseUrls(_baseUrl);

            _app = builder.Build();
            _app.MapJobEndpoints();

            // Start the server in the background
            _runTask = _app.RunAsync();
        }

        public HttpClient CreateClient()
        {
            return new HttpClient { BaseAddress = new Uri(_baseUrl) };
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            if (_runTask is not null)
                await _runTask;
            await _app.DisposeAsync();
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

    private sealed class MockAgentRuntime : IAgentRuntime
    {
        public Task<AgentContext> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
        {
            context.FinalResponse = "Mock response for: " + context.UserMessage;
            context.TotalTokens = 15;
            context.IsComplete = true;
            context.CompletedAt = DateTime.UtcNow;
            return Task.FromResult(context);
        }

        public IAsyncEnumerable<AgentStreamEvent> ExecuteStreamAsync(AgentContext context, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
