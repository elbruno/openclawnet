using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests.Audit;

/// <summary>
/// Integration tests validating that job state transitions write JobDefinitionStateChange records.
/// Story 5: Audit Trail Integration Tests (Feature 2).
/// </summary>
[Trait("Category", "Integration")]
public sealed class JobStateChangeTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task StartJob_WritesStateChangeRecord_FromDraftToActive()
    {
        // Arrange: Create a draft job
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync(client, "audit-start-test");

        // Act: Transition Draft → Active via POST /api/jobs/{id}/start
        var startResp = await client.PostAsync($"/api/jobs/{jobId}/start", content: null);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Verify JobDefinitionStateChange record exists
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var stateChanges = await db.Set<JobDefinitionStateChange>()
            .Where(sc => sc.JobId == jobId)
            .OrderBy(sc => sc.ChangedAt)
            .ToListAsync();

        stateChanges.Should().NotBeEmpty();
        var change = stateChanges.Should().ContainSingle(sc => 
            sc.FromStatus == JobStatus.Draft && 
            sc.ToStatus == JobStatus.Active).Subject;

        change.JobId.Should().Be(jobId);
        change.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task PauseJob_WritesStateChangeRecord_FromActiveToPaused()
    {
        // Arrange: Create and start a job
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync(client, "audit-pause-test");
        await client.PostAsync($"/api/jobs/{jobId}/start", content: null);

        // Act: Transition Active → Paused
        var pauseResp = await client.PostAsync($"/api/jobs/{jobId}/pause", content: null);
        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Verify state change records
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var stateChanges = await db.Set<JobDefinitionStateChange>()
            .Where(sc => sc.JobId == jobId)
            .OrderBy(sc => sc.ChangedAt)
            .ToListAsync();

        stateChanges.Should().HaveCountGreaterThanOrEqualTo(2);
        
        // Should have Draft → Active
        stateChanges.Should().Contain(sc => 
            sc.FromStatus == JobStatus.Draft && 
            sc.ToStatus == JobStatus.Active);

        // Should have Active → Paused
        var pauseChange = stateChanges.Should().ContainSingle(sc => 
            sc.FromStatus == JobStatus.Active && 
            sc.ToStatus == JobStatus.Paused).Subject;

        pauseChange.JobId.Should().Be(jobId);
        pauseChange.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ResumeJob_WritesStateChangeRecord_FromPausedToActive()
    {
        // Arrange: Create, start, and pause a job
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync(client, "audit-resume-test");
        await client.PostAsync($"/api/jobs/{jobId}/start", content: null);
        await client.PostAsync($"/api/jobs/{jobId}/pause", content: null);

        // Act: Transition Paused → Active
        var resumeResp = await client.PostAsync($"/api/jobs/{jobId}/resume", content: null);
        resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Verify all state change records
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var stateChanges = await db.Set<JobDefinitionStateChange>()
            .Where(sc => sc.JobId == jobId)
            .OrderBy(sc => sc.ChangedAt)
            .ToListAsync();

        stateChanges.Should().HaveCountGreaterThanOrEqualTo(3);

        // Verify the resume transition
        var resumeChange = stateChanges.Should().ContainSingle(sc => 
            sc.FromStatus == JobStatus.Paused && 
            sc.ToStatus == JobStatus.Active).Subject;

        resumeChange.JobId.Should().Be(jobId);
        resumeChange.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task StateChangeRecords_ContainCorrectForeignKey()
    {
        // Arrange & Act: Create and start a job
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync(client, "fk-test");
        await client.PostAsync($"/api/jobs/{jobId}/start", content: null);

        // Assert: Verify foreign key relationship
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var stateChange = await db.Set<JobDefinitionStateChange>()
            .Where(sc => sc.JobId == jobId)
            .Include(sc => sc.Job)
            .FirstAsync();

        stateChange.JobId.Should().Be(jobId);
        stateChange.Job.Should().NotBeNull();
        stateChange.Job!.Id.Should().Be(jobId);
    }

    [Fact]
    public async Task StateChangeRecords_ContainTimestamps()
    {
        // Arrange & Act: Create and start a job
        var client = factory.CreateClient();
        var beforeCreate = DateTime.UtcNow.AddSeconds(-1);
        var jobId = await CreateDraftJobAsync(client, "timestamp-test");
        await client.PostAsync($"/api/jobs/{jobId}/start", content: null);
        var afterStart = DateTime.UtcNow.AddSeconds(1);

        // Assert: Verify timestamps are within expected range
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var stateChange = await db.Set<JobDefinitionStateChange>()
            .Where(sc => sc.JobId == jobId)
            .FirstAsync();

        stateChange.ChangedAt.Should().BeAfter(beforeCreate);
        stateChange.ChangedAt.Should().BeBefore(afterStart);
    }

    private static async Task<Guid> CreateDraftJobAsync(HttpClient client, string name)
    {
        var body = new CreateJobRequest { Name = name, Prompt = "test prompt" };
        var resp = await client.PostAsJsonAsync("/api/jobs", body);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return dto.GetProperty("id").GetGuid();
    }
}
