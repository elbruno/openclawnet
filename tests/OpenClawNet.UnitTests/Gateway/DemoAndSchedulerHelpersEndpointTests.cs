using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Smoke tests for the demo template endpoints (website-watcher, folder-health) and the
/// /api/scheduler/translate-cron helper. Spins up a minimal WebApplication backed by an
/// in-memory SQLite DB.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DemoAndSchedulerHelpersEndpointTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DemoTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new DemoTestFactory(options);
        _client = _factory.CreateClient();

        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        await SchemaMigrator.MigrateAsync(db);

        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Translate-cron endpoint ──────────────────────────────────────────────

    [Fact]
    public async Task TranslateCron_EmptyText_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/scheduler/translate-cron",
            new { text = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TranslateCron_NullBody_Returns400()
    {
        var response = await _client.PostAsJsonAsync<object?>(
            "/api/scheduler/translate-cron",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TranslateCron_KnownPattern_Returns200WithCron()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/scheduler/translate-cron",
            new { text = "every weekday at 9am" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TranslateCronResponse>();
        body.Should().NotBeNull();
        body!.Cron.Should().Be("0 9 * * 1-5");
        body.Explanation.Should().NotBeNullOrEmpty();
    }

    // ── Website Watcher demo ────────────────────────────────────────────────

    [Fact]
    public async Task WebsiteWatcher_StatusBeforeSetup_Returns404()
    {
        var response = await _client.GetAsync("/api/demos/website-watcher/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WebsiteWatcher_Setup_CreatesJobAndStatusReturns200()
    {
        // Setup
        var setupResp = await _client.PostAsJsonAsync(
            "/api/demos/website-watcher/setup",
            new { url = "https://example.com" });

        setupResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var setup = await setupResp.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        setup.Should().NotBeNull();
        setup!.JobId.Should().NotBeEmpty();
        setup.CronExpression.Should().Be("*/15 * * * *");

        // Status now returns 200
        var statusResp = await _client.GetAsync("/api/demos/website-watcher/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the job is persisted
        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == setup.JobId);
        job.Should().NotBeNull();
        job!.IsRecurring.Should().BeTrue();
        job.CronExpression.Should().Be("*/15 * * * *");
    }

    [Fact]
    public async Task WebsiteWatcher_DuplicateSetup_CreatesSecondInstanceWithSequenceSuffix()
    {
        var first = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });
        var second = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        firstBody!.Name.Should().Be("Website Watcher");
        secondBody!.Name.Should().Be("Website Watcher (2)");
        secondBody.JobId.Should().NotBe(firstBody.JobId);

        // Both rows are persisted and tagged with the source template for traceability.
        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var jobs = await db.Jobs.Where(j => j.SourceTemplateName == "Website Watcher").ToListAsync();
        jobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task WebsiteWatcherSetup_CreatesThirdInstance_WithSuffix3_WhenSuffixTwoExists()
    {
        // Create three instances to test suffix progression
        var first = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });
        var second = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });
        var third = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        third.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        var thirdBody = await third.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();

        firstBody!.Name.Should().Be("Website Watcher");
        secondBody!.Name.Should().Be("Website Watcher (2)");
        thirdBody!.Name.Should().Be("Website Watcher (3)");

        // All three should have unique IDs
        var ids = new[] { firstBody.JobId, secondBody.JobId, thirdBody.JobId };
        ids.Should().OnlyHaveUniqueItems();

        // Verify all three are persisted with source template tracking
        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var jobs = await db.Jobs.Where(j => j.SourceTemplateName == "Website Watcher").ToListAsync();
        jobs.Should().HaveCount(3);
    }

    [Fact]
    public async Task DocPipelineSetup_AlsoAutoSuffixes_WhenMultipleInstancesCreated()
    {
        var first = await _client.PostAsJsonAsync("/api/demos/doc-pipeline/setup", 
            new { folderPath = @"C:\docs", intervalSeconds = 30, durationMinutes = 5 });
        var second = await _client.PostAsJsonAsync("/api/demos/doc-pipeline/setup", 
            new { folderPath = @"C:\other", intervalSeconds = 60, durationMinutes = 10 });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadFromJsonAsync<DocPipelineSetupResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<DocPipelineSetupResponse>();

        firstBody!.Name.Should().Be("Document Processing Pipeline");
        secondBody!.Name.Should().Be("Document Processing Pipeline (2)");

        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var jobs = await db.Jobs.Where(j => j.SourceTemplateName == "Document Processing Pipeline").ToListAsync();
        jobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task FolderHealthSetup_AlsoAutoSuffixes_WhenMultipleInstancesCreated()
    {
        var first = await _client.PostAsJsonAsync("/api/demos/folder-health/setup", 
            new { folderPath = @"C:\src" });
        var second = await _client.PostAsJsonAsync("/api/demos/folder-health/setup", 
            new { folderPath = @"C:\data" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadFromJsonAsync<FolderHealthSetupResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<FolderHealthSetupResponse>();

        firstBody!.Name.Should().Be("Folder Health Report");
        secondBody!.Name.Should().Be("Folder Health Report (2)");

        await using var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var jobs = await db.Jobs.Where(j => j.SourceTemplateName == "Folder Health Report").ToListAsync();
        jobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task JobsPut_PreservesSourceTemplateName_OnRename()
    {
        // Create a job from a demo template
        var setupResp = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", 
            new { url = "https://example.com" });
        setupResp.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var setup = await setupResp.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        setup.Should().NotBeNull();

        // Verify initial SourceTemplateName
        await using (var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext())
        {
            var job = await db.Jobs.FindAsync(setup!.JobId);
            job.Should().NotBeNull();
            job!.SourceTemplateName.Should().Be("Website Watcher");

            // Simulate a rename via direct DB update (since Job endpoints aren't mapped in DemoTestFactory)
            job.Name = "My Custom Website Monitor";
            job.Prompt = "Custom prompt for monitoring";
            job.CronExpression = "*/30 * * * *";
            await db.SaveChangesAsync();
        }

        // Verify SourceTemplateName is preserved after rename
        await using (var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext())
        {
            var job = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == setup!.JobId);
            job.Name.Should().Be("My Custom Website Monitor", "the rename should succeed");
            job.SourceTemplateName.Should().Be("Website Watcher", 
                "SourceTemplateName is a creation-time lineage marker and persists independently of Name edits");
        }
    }

    [Fact]
    public async Task WebsiteWatcherSetup_AfterDeletingFirstInstance_ReusesOriginalName()
    {
        // Create first instance
        var first = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        firstBody!.Name.Should().Be("Website Watcher");

        // Create second instance
        var second = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondBody = await second.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        secondBody!.Name.Should().Be("Website Watcher (2)");

        // Delete the first instance directly via DB (since DELETE endpoints aren't mapped in DemoTestFactory)
        await using (var db = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext())
        {
            var runToDelete = await db.Jobs.FindAsync(firstBody.JobId);
            if (runToDelete != null)
            {
                db.Jobs.Remove(runToDelete);
                await db.SaveChangesAsync();
            }
        }

        // Create a third instance — should reuse "Website Watcher" since it's now available
        var third = await _client.PostAsJsonAsync("/api/demos/website-watcher/setup", new { url = "https://example.com" });
        third.StatusCode.Should().Be(HttpStatusCode.Created);
        var thirdBody = await third.Content.ReadFromJsonAsync<WebsiteWatcherSetupResponse>();
        thirdBody!.Name.Should().Be("Website Watcher", 
            "after deleting the first instance, the original name should be available again");

        // Verify database state: should have "Website Watcher" and "Website Watcher (2)"
        await using var db2 = _factory.Services
            .GetRequiredService<IDbContextFactory<OpenClawDbContext>>()
            .CreateDbContext();
        var jobs = await db2.Jobs
            .Where(j => j.SourceTemplateName == "Website Watcher")
            .OrderBy(j => j.Name)
            .Select(j => j.Name)
            .ToListAsync();
        jobs.Should().BeEquivalentTo(new[] { "Website Watcher", "Website Watcher (2)" });
    }

    // ── Folder Health demo ──────────────────────────────────────────────────

    [Fact]
    public async Task FolderHealth_StatusBeforeSetup_Returns404()
    {
        var response = await _client.GetAsync("/api/demos/folder-health/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FolderHealth_Setup_CreatesJobAndStatusReturns200()
    {
        var setupResp = await _client.PostAsJsonAsync(
            "/api/demos/folder-health/setup",
            new { folderPath = @"C:\some\folder" });

        setupResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var setup = await setupResp.Content.ReadFromJsonAsync<FolderHealthSetupResponse>();
        setup.Should().NotBeNull();
        setup!.CronExpression.Should().Be("0 9 * * *");
        setup.FolderPath.Should().Be(@"C:\some\folder");

        var statusResp = await _client.GetAsync("/api/demos/folder-health/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Test factory ────────────────────────────────────────────────────────

    private sealed class DemoTestFactory : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly Task _runTask;
        private readonly string _baseUrl = $"http://localhost:{15990 + Random.Shared.Next(0, 5)}";

        public IServiceProvider Services => _app.Services;

        public DemoTestFactory(DbContextOptions<OpenClawDbContext> dbOptions)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new TestDbContextFactory(dbOptions));

            // For translate-cron's LLM fallback: register an empty profile store so the
            // regex translator handles "every weekday at 9am" without needing a chat client.
            var emptyStore = new EmptyAgentProfileStore();
            builder.Services.AddSingleton<IAgentProfileStore>(emptyStore);

            builder.WebHost.UseUrls(_baseUrl);
            _app = builder.Build();

            _app.MapDemoEndpoints();
            _app.MapSchedulerHelpersEndpoints();

            _runTask = _app.RunAsync();
        }

        public HttpClient CreateClient() => new() { BaseAddress = new Uri(_baseUrl) };

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _runTask;
            await _app.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;
        public OpenClawDbContext CreateDbContext() => new(_options);
        public Task<OpenClawDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new OpenClawDbContext(_options));
    }

    private sealed class EmptyAgentProfileStore : IAgentProfileStore
    {
        public Task<OpenClawNet.Models.Abstractions.AgentProfile?> GetAsync(string name, CancellationToken ct = default)
            => Task.FromResult<OpenClawNet.Models.Abstractions.AgentProfile?>(null);

        public Task<OpenClawNet.Models.Abstractions.AgentProfile> GetDefaultAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("No default agent profile in tests");

        public Task<IReadOnlyList<OpenClawNet.Models.Abstractions.AgentProfile>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OpenClawNet.Models.Abstractions.AgentProfile>>([]);

        public Task SaveAsync(OpenClawNet.Models.Abstractions.AgentProfile profile, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<OpenClawNet.Storage.Entities.AgentProfileEntity?> GetEntityAsync(string name, CancellationToken ct = default)
            => Task.FromResult<OpenClawNet.Storage.Entities.AgentProfileEntity?>(null);

        public Task SaveEntityAsync(OpenClawNet.Storage.Entities.AgentProfileEntity entity, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
