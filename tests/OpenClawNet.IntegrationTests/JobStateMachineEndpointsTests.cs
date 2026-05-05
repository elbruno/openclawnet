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
/// Concept-review §4b: covers the new state-machine surfaces — status-change history,
/// archive transition, archived-jobs filter, and the create-and-activate template path.
/// </summary>
[Trait("Category", "Integration")]
public sealed class JobStateMachineEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task StartJob_RecordsStateChange()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync(client, "history-test");

        var start = await client.PostAsync($"/api/jobs/{jobId}/start", content: null);
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var historyResp = await client.GetAsync($"/api/jobs/{jobId}/history");
        historyResp.EnsureSuccessStatusCode();
        var rows = await historyResp.Content.ReadFromJsonAsync<List<HistoryRow>>(JsonOpts);

        rows.Should().NotBeNull();
        rows!.Should().ContainSingle(r => r.From == "Draft" && r.To == "Active");
    }

    [Fact]
    public async Task ArchiveJob_OnDraft_ReturnsConflict()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync(client, "archive-conflict");

        var archive = await client.PostAsync($"/api/jobs/{jobId}/archive", content: null);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ListJobs_HidesArchivedByDefault_AndIncludesWithFlag()
    {
        var client = factory.CreateClient();

        // Create a job and force it into Archived state via the DB so we don't
        // depend on the runner producing a Completed state.
        var jobId = await CreateDraftJobAsync(client, "archived-listing");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var job = await db.Jobs.FindAsync(jobId);
            job!.Status = JobStatus.Archived;
            await db.SaveChangesAsync();
        }

        var defaultResp = await client.GetAsync("/api/jobs");
        defaultResp.EnsureSuccessStatusCode();
        var defaultJobs = await defaultResp.Content.ReadFromJsonAsync<List<JobIdRow>>(JsonOpts);
        defaultJobs!.Should().NotContain(j => j.Id == jobId);

        var includeResp = await client.GetAsync("/api/jobs?includeArchived=true");
        includeResp.EnsureSuccessStatusCode();
        var allJobs = await includeResp.Content.ReadFromJsonAsync<List<JobIdRow>>(JsonOpts);
        allJobs!.Should().Contain(j => j.Id == jobId);
    }

    [Fact]
    public async Task CreateAndActivateFromTemplate_StartsInActiveState()
    {
        var client = factory.CreateClient();
        var body = new CreateJobRequest
        {
            Name = "ca-test",
            Prompt = "hello",
        };
        var resp = await client.PostAsJsonAsync(
            "/api/jobs/from-template/research-and-archive/activate", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        dto.GetProperty("status").GetString().Should().BeEquivalentTo("Active");

        // History should have a Draft → Active row.
        var jobId = dto.GetProperty("id").GetGuid();
        var historyResp = await client.GetAsync($"/api/jobs/{jobId}/history");
        var rows = await historyResp.Content.ReadFromJsonAsync<List<HistoryRow>>(JsonOpts);
        rows!.Should().Contain(r => r.From == "Draft" && r.To == "Active");
    }

    private static async Task<Guid> CreateDraftJobAsync(HttpClient client, string name)
    {
        var body = new CreateJobRequest { Name = name, Prompt = "p" };
        var resp = await client.PostAsJsonAsync("/api/jobs", body);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return dto.GetProperty("id").GetGuid();
    }

    private sealed record HistoryRow(Guid Id, string From, string To, string? Reason, string? ChangedBy, DateTime ChangedAt);
    private sealed record JobIdRow(Guid Id);
}
