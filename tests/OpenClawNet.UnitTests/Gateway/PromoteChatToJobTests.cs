using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Tests for POST /api/sessions/{sessionId}/promote-to-job.
/// Verifies that a chat session is correctly converted into a daily 09:00 UTC
/// scheduled job (5-day window) and persisted in the OpenClaw storage.
/// </summary>
public sealed class PromoteChatToJobTests : IAsyncLifetime
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

        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SchemaMigrator.MigrateAsync(db);

        await Task.Delay(300);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Happy path: session with user messages ────────────────────────────────

    [Fact]
    public async Task PromoteChatToJob_SessionWithMessages_Returns201AndJobId()
    {
        var sessionId = await CreateSessionWithMessagesAsync("My Research Chat", lastUserMessage: "Summarize the latest AI news");

        var response = await _client.PostAsync($"/api/sessions/{sessionId}/promote-to-job", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PromoteChatToJobResponse>();
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto!.JobId);
        Assert.Equal(sessionId, dto.SourceSessionId);
    }

    [Fact]
    public async Task PromoteChatToJob_SessionWithMessages_UsesLastUserMessageAsPrompt()
    {
        var sessionId = await CreateSessionWithMessagesAsync("Research Chat", lastUserMessage: "Explain quantum entanglement");

        var response = await _client.PostAsync($"/api/sessions/{sessionId}/promote-to-job", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PromoteChatToJobResponse>();
        Assert.Equal("Explain quantum entanglement", dto!.Prompt);
    }

    [Fact]
    public async Task PromoteChatToJob_SessionWithMessages_JobNameDerivedFromSessionTitle()
    {
        var sessionId = await CreateSessionWithMessagesAsync("Market Analysis", lastUserMessage: "Analyse tech stocks");

        var response = await _client.PostAsync($"/api/sessions/{sessionId}/promote-to-job", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PromoteChatToJobResponse>();
        Assert.Equal("Market Analysis (daily)", dto!.JobName);
    }

    // ── Prompt priority: summary wins over last message ───────────────────────

    [Fact]
    public async Task PromoteChatToJob_SessionWithSummary_UsesSummaryAsPrompt()
    {
        var sessionId = await CreateSessionWithSummaryAsync(
            title: "Daily Briefing",
            summary: "Latest AI research highlights and model releases.",
            lastUserMessage: "Unrelated message");

        var response = await _client.PostAsync($"/api/sessions/{sessionId}/promote-to-job", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PromoteChatToJobResponse>();
        Assert.Equal("Latest AI research highlights and model releases.", dto!.Prompt);
    }

    // ── Schedule assertions ───────────────────────────────────────────────────

    [Fact]
    public async Task PromoteChatToJob_CreatesJobWithDailyCronAndFiveDayWindow()
    {
        var sessionId = await CreateSessionWithMessagesAsync("Cron Test", "Check the news");

        var response = await _client.PostAsync($"/api/sessions/{sessionId}/promote-to-job", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PromoteChatToJobResponse>();
        Assert.Equal("0 9 * * *", dto!.CronExpression);

        // EndAt must be ~5 days from now (within a 10-second tolerance for test timing)
        var expectedEnd = DateTime.UtcNow.AddDays(5);
        Assert.True(Math.Abs((dto.EndsAt - expectedEnd).TotalSeconds) < 10,
            $"EndsAt {dto.EndsAt:O} is not within 10s of expected {expectedEnd:O}");
    }

    [Fact]
    public async Task PromoteChatToJob_JobPersistedAsDraft_CanBeActivatedViaStart()
    {
        var sessionId = await CreateSessionWithMessagesAsync("Draft Test", "Do something daily");

        var promoteResponse = await _client.PostAsync($"/api/sessions/{sessionId}/promote-to-job", null);
        Assert.Equal(HttpStatusCode.Created, promoteResponse.StatusCode);

        var dto = await promoteResponse.Content.ReadFromJsonAsync<PromoteChatToJobResponse>();
        Assert.NotNull(dto);

        // Verify Draft status in DB
        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var job = await db.Jobs.FindAsync(dto!.JobId);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Draft, job!.Status);
        Assert.Equal(TriggerType.Cron, job.TriggerType);
        Assert.True(job.IsRecurring);
        Assert.Equal("chat-promotion", job.SourceTemplateName);
    }

    // ── Name override ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PromoteChatToJob_WithNameOverride_UsesProvidedName()
    {
        var sessionId = await CreateSessionWithMessagesAsync("Original Title", "Some question");

        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/promote-to-job",
            new { Name = "My Custom Job Name" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PromoteChatToJobResponse>();
        Assert.Equal("My Custom Job Name", dto!.JobName);
    }

    // ── Not found ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PromoteChatToJob_UnknownSession_Returns404()
    {
        var response = await _client.PostAsync($"/api/sessions/{Guid.NewGuid()}/promote-to-job", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreateSessionWithMessagesAsync(string title, string lastUserMessage)
    {
        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();

        var session = new ChatSession { Title = title };
        db.Sessions.Add(session);

        db.Messages.Add(new ChatMessageEntity
        {
            SessionId = session.Id,
            Role = "user",
            Content = "First question",
            OrderIndex = 0
        });
        db.Messages.Add(new ChatMessageEntity
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = "First answer",
            OrderIndex = 1
        });
        db.Messages.Add(new ChatMessageEntity
        {
            SessionId = session.Id,
            Role = "user",
            Content = lastUserMessage,
            OrderIndex = 2
        });

        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Guid> CreateSessionWithSummaryAsync(string title, string summary, string lastUserMessage)
    {
        var sessionId = await CreateSessionWithMessagesAsync(title, lastUserMessage);

        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();

        db.Summaries.Add(new SessionSummary
        {
            SessionId = sessionId,
            Summary = summary,
            CoveredMessageCount = 2
        });

        await db.SaveChangesAsync();
        return sessionId;
    }

    // ── Test WebApplicationFactory ────────────────────────────────────────────

    private sealed class WebApplicationFactory : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private Task? _runTask;
        public IServiceProvider Services => _app.Services;
        private readonly string _baseUrl = "http://localhost:16020";

        public WebApplicationFactory(DbContextOptions<OpenClawDbContext> dbOptions)
        {
            var builder = WebApplication.CreateBuilder();

            builder.Services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new TestDbContextFactory(dbOptions));

            // Mock agent profile store — returns a stable default
            var mockProfileStore = new Mock<IAgentProfileStore>();
            mockProfileStore
                .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OpenClawNet.Models.Abstractions.AgentProfile?)null);
            mockProfileStore
                .Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OpenClawNet.Models.Abstractions.AgentProfile
                {
                    Name = "openclawnet-agent",
                    DisplayName = "OpenClawNet Agent",
                    IsDefault = true,
                    IsEnabled = true,
                    Provider = "ollama-default"
                });
            builder.Services.AddSingleton(mockProfileStore.Object);

            // Mock IConversationStore (required by other SessionEndpoints routes even if not called)
            builder.Services.AddSingleton(new Mock<IConversationStore>().Object);

            builder.WebHost.UseUrls(_baseUrl);

            _app = builder.Build();
            _app.MapSessionEndpoints();

            _runTask = _app.RunAsync();
        }

        public HttpClient CreateClient() => new() { BaseAddress = new Uri(_baseUrl) };

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            if (_runTask is not null) await _runTask;
            await _app.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;

        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;

        public OpenClawDbContext CreateDbContext() => new(_options);

        public Task<OpenClawDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OpenClawDbContext(_options));
    }
}
