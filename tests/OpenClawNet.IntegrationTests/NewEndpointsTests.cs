using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for new endpoint additions: latest run, run detail, logs download, global search, health.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NewEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetLatestRun_ReturnsNotFound_WhenNoRuns()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync(client, "no-runs-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/latest");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLatestRun_ReturnsLatestRun_WithEventCount()
    {
        var client = factory.CreateClient();
        var (jobId, _) = await CreateJobWithRunAsync(client, "latest-run-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/latest");
        resp.EnsureSuccessStatusCode();
        
        var run = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        run.GetProperty("jobId").GetGuid().Should().Be(jobId);
        run.GetProperty("status").GetString().Should().NotBeNullOrEmpty();
        run.GetProperty("eventCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetRunDetail_ReturnsFullDetail_WithEventStats()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync(client, "run-detail-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}");
        resp.EnsureSuccessStatusCode();
        
        var run = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        run.GetProperty("id").GetGuid().Should().Be(runId);
        run.GetProperty("jobId").GetGuid().Should().Be(jobId);
        run.GetProperty("eventCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        run.GetProperty("toolCallCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        run.GetProperty("errorEventCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task DownloadRunLogs_ReturnsTextFile_WithCorrectFormat()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync(client, "logs-download-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/logs?format=txt");
        resp.EnsureSuccessStatusCode();
        resp.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        resp.Content.Headers.ContentDisposition?.FileName.Should().NotBeNullOrEmpty();

        var content = await resp.Content.ReadAsStringAsync();
        content.Should().Contain("Job Run Log Export");
        content.Should().Contain(runId.ToString());
    }

    [Fact]
    public async Task DownloadRunLogs_ReturnsJsonFile_WithCorrectFormat()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync(client, "logs-json-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/runs/{runId}/logs?format=json");
        resp.EnsureSuccessStatusCode();
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var content = await resp.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("runId").GetGuid().Should().Be(runId);
        json.RootElement.GetProperty("jobId").GetGuid().Should().Be(jobId);
        json.RootElement.TryGetProperty("events", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SearchRuns_ReturnsEmptyList_WhenNoMatches()
    {
        var client = factory.CreateClient();
        
        var resp = await client.GetAsync("/api/runs/search?status=nonexistent");
        resp.EnsureSuccessStatusCode();
        
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        result.GetProperty("count").GetInt32().Should().Be(0);
        result.GetProperty("runs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task UpdateStorageLocation_AcceptsAbsoluteWindowsStylePath()
    {
        var client = factory.CreateClient();
        var driveRoot = Path.GetPathRoot(Path.GetTempPath()) ?? "C:\\";
        var targetPath = Path.Combine(driveRoot, "openclawnet-storage-e2e");

        var resp = await client.PutAsJsonAsync("/api/storage/location", new { rootPath = targetPath });
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<StorageUpdateResponse>(JsonOpts);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.NewPath.Should().NotBeNullOrWhiteSpace();
        Path.GetFullPath(result.NewPath!).Should().Be(Path.GetFullPath(targetPath));
    }

    [Fact]
    public async Task UpdateStorageLocation_ReturnsTypedErrorPayload_ForInvalidPath()
    {
        var client = factory.CreateClient();

        var resp = await client.PutAsJsonAsync("/api/storage/location", new { rootPath = "relative-path" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await resp.Content.ReadFromJsonAsync<StorageUpdateResponse>(JsonOpts);
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Message.ToLowerInvariant().Should().Contain("absolute");
    }

    [Fact]
    public async Task SearchRuns_FiltersByStatus()
    {
        var client = factory.CreateClient();
        var (jobId, runId) = await CreateJobWithRunAsync(client, "search-status-job");

        // Update run status to "completed"
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var run = await db.JobRuns.FindAsync(runId);
            run!.Status = "completed";
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync("/api/runs/search?status=completed");
        resp.EnsureSuccessStatusCode();
        
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        result.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
        
        var runs = result.GetProperty("runs");
        foreach (var run in runs.EnumerateArray())
        {
            run.GetProperty("status").GetString().Should().Be("completed");
        }
    }

    [Fact]
    public async Task SearchRuns_FiltersByJobId()
    {
        var client = factory.CreateClient();
        var (jobId, _) = await CreateJobWithRunAsync(client, "search-jobid-job");

        var resp = await client.GetAsync($"/api/runs/search?jobId={jobId}");
        resp.EnsureSuccessStatusCode();
        
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var runs = result.GetProperty("runs");
        
        foreach (var run in runs.EnumerateArray())
        {
            run.GetProperty("jobId").GetGuid().Should().Be(jobId);
        }
    }

    [Fact]
    public async Task SearchRuns_FiltersByDateRange()
    {
        var client = factory.CreateClient();
        var tomorrow = DateTime.UtcNow.AddDays(1).ToString("O");
        
        var resp = await client.GetAsync($"/api/runs/search?since={tomorrow}");
        resp.EnsureSuccessStatusCode();
        
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        result.GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task SchedulerHealth_ReturnsHealthyStatus()
    {
        // Note: This test hits the Scheduler service, not Gateway
        // We'd need a separate test fixture for Scheduler service integration tests
        // For now, this is a placeholder showing the contract
        
        // In a real test:
        // var client = schedulerFactory.CreateClient();
        // var resp = await client.GetAsync("/api/scheduler/health");
        // resp.EnsureSuccessStatusCode();
        // var health = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        // health.GetProperty("status").GetString().Should().Be("healthy");
        // health.GetProperty("timestamp").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    private static async Task<Guid> CreateDraftJobAsync(HttpClient client, string name)
    {
        var body = new CreateJobRequest { Name = name, Prompt = "test prompt" };
        var resp = await client.PostAsJsonAsync("/api/jobs", body);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return dto.GetProperty("id").GetGuid();
    }

    private async Task<(Guid jobId, Guid runId)> CreateJobWithRunAsync(HttpClient client, string name)
    {
        var jobId = await CreateDraftJobAsync(client, name);

        // Create a run directly in the database
        Guid runId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            
            var run = new JobRun
            {
                JobId = jobId,
                Status = "running",
                StartedAt = DateTime.UtcNow
            };
            db.JobRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        return (jobId, runId);
    }
}
