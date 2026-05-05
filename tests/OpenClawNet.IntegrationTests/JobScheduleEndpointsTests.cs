using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for job schedule endpoints from commit 734baee.
/// Covers: GET /api/jobs/{jobId}/schedule, PUT /api/jobs/{jobId}/schedule,
/// GET /api/jobs/{jobId}/next-run, GET /api/jobs/by-schedule
/// </summary>
[Trait("Category", "Integration")]
public sealed class JobScheduleEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ═══════════════════════════════════════════════════════════════
    // GET /api/jobs/{jobId}/schedule
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobSchedule_ReturnsNotFound_WhenJobDoesNotExist()
    {
        var client = factory.CreateClient();
        var jobId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/jobs/{jobId}/schedule");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetJobSchedule_ReturnsScheduleConfig()
    {
        var client = factory.CreateClient();
        var jobId = await CreateScheduledJobAsync("daily-job", "0 0 * * *");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/schedule");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(jobId, dto.GetProperty("id").GetGuid());
        Assert.Equal("daily-job", dto.GetProperty("name").GetString());
        Assert.Equal("0 0 * * *", dto.GetProperty("cronExpression").GetString());
        Assert.True(dto.GetProperty("isRecurring").GetBoolean());
    }

    // ═══════════════════════════════════════════════════════════════
    // PUT /api/jobs/{jobId}/schedule
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateJobSchedule_ReturnsNotFound_WhenJobDoesNotExist()
    {
        var client = factory.CreateClient();
        var jobId = Guid.NewGuid();
        var body = new { CronExpression = "0 12 * * *" };

        var resp = await client.PutAsJsonAsync($"/api/jobs/{jobId}/schedule", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateJobSchedule_UpdatesCronExpression()
    {
        var client = factory.CreateClient();
        var jobId = await CreateScheduledJobAsync("update-test-job", "0 0 * * *");

        var body = new { CronExpression = "0 12 * * *", IsRecurring = true };
        var resp = await client.PutAsJsonAsync($"/api/jobs/{jobId}/schedule", body);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal("0 12 * * *", dto.GetProperty("cronExpression").GetString());

        // Verify persistence
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
            Assert.NotNull(job);
            Assert.Equal("0 12 * * *", job.CronExpression);
        }
    }

    [Fact]
    public async Task UpdateJobSchedule_UpdatesTimeWindow()
    {
        var client = factory.CreateClient();
        var jobId = await CreateScheduledJobAsync("window-test-job", "0 0 * * *");

        var startAt = DateTime.UtcNow.AddDays(1);
        var endAt = DateTime.UtcNow.AddDays(7);
        var body = new
        {
            CronExpression = "0 0 * * *",
            StartAt = startAt,
            EndAt = endAt,
            TimeZone = "America/New_York"
        };

        var resp = await client.PutAsJsonAsync($"/api/jobs/{jobId}/schedule", body);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal("America/New_York", dto.GetProperty("timeZone").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /api/jobs/{jobId}/next-run
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobNextRun_ReturnsNotFound_WhenJobDoesNotExist()
    {
        var client = factory.CreateClient();
        var jobId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/jobs/{jobId}/next-run");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetJobNextRun_ReturnsErrorWhenNoCronExpression()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync("no-schedule-job");

        var resp = await client.GetAsync($"/api/jobs/{jobId}/next-run");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(jobId, dto.GetProperty("id").GetGuid());
        
        // For a draft job without cron, NextRunAt might have a default value (1hr future) or null
        // The key test is that an error message is returned
        Assert.True(dto.TryGetProperty("error", out var errorProp));
        Assert.NotEqual(JsonValueKind.Null, errorProp.ValueKind);
        Assert.Contains("no cron expression", errorProp.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetJobNextRun_ReturnsNextRunAtWhenScheduled()
    {
        var client = factory.CreateClient();
        var jobId = await CreateScheduledJobAsync("next-run-job", "0 0 * * *");

        // Set NextRunAt
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (job is not null)
            {
                job.NextRunAt = DateTime.UtcNow.AddHours(1);
                await db.SaveChangesAsync();
            }
        }

        var resp = await client.GetAsync($"/api/jobs/{jobId}/next-run");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(jobId, dto.GetProperty("id").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("nextRunAt").ValueKind);
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /api/jobs/by-schedule?expression=...
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobsBySchedule_ReturnsBadRequest_WhenExpressionMissing()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/jobs/by-schedule");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetJobsBySchedule_ReturnsEmptyList_WhenNoMatches()
    {
        var client = factory.CreateClient();
        var expression = "0 0 1 1 *"; // Jan 1 yearly - unlikely to exist

        var resp = await client.GetAsync($"/api/jobs/by-schedule?expression={Uri.EscapeDataString(expression)}");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(expression, dto.GetProperty("expression").GetString());
        Assert.Equal(0, dto.GetProperty("count").GetInt32());
        Assert.Empty(dto.GetProperty("jobs").EnumerateArray());
    }

    [Fact]
    public async Task GetJobsBySchedule_ReturnsMatchingJobs()
    {
        var client = factory.CreateClient();
        var expression = "0 3 * * *"; // 3am daily
        var job1Id = await CreateScheduledJobAsync("job-3am-1", expression);
        var job2Id = await CreateScheduledJobAsync("job-3am-2", expression);
        await CreateScheduledJobAsync("job-different", "0 6 * * *"); // different schedule

        var resp = await client.GetAsync($"/api/jobs/by-schedule?expression={Uri.EscapeDataString(expression)}");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(expression, dto.GetProperty("expression").GetString());
        Assert.Equal(2, dto.GetProperty("count").GetInt32());

        var jobs = dto.GetProperty("jobs").EnumerateArray().ToList();
        Assert.Equal(2, jobs.Count);
        var jobIds = jobs.Select(j => j.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(job1Id, jobIds);
        Assert.Contains(job2Id, jobIds);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════

    private async Task<Guid> CreateDraftJobAsync(string name)
    {
        var client = factory.CreateClient();
        var body = new { Name = name, Prompt = "test prompt" };
        var resp = await client.PostAsJsonAsync("/api/jobs", body);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return dto.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateScheduledJobAsync(string name, string cronExpression)
    {
        var jobId = await CreateDraftJobAsync(name);

        // Set schedule
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job is not null)
        {
            job.CronExpression = cronExpression;
            job.IsRecurring = true;
            await db.SaveChangesAsync();
        }

        return jobId;
    }
}
