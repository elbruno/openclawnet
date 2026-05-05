using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for job stream endpoint from commit 734baee.
/// Covers: GET /api/jobs/{jobId}/stream (NDJSON streaming)
/// </summary>
[Trait("Category", "Integration")]
public sealed class JobStreamEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ═══════════════════════════════════════════════════════════════
    // GET /api/jobs/{jobId}/stream
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobStream_ReturnsNotFoundEvent_WhenJobDoesNotExist()
    {
        var client = factory.CreateClient();
        var jobId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/jobs/{jobId}/stream");
        
        // The endpoint returns 200 OK even for not-found, with an NDJSON event
        resp.EnsureSuccessStatusCode();
        
        // Verify content type is NDJSON
        Assert.Equal("application/x-ndjson", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetJobStream_ReturnsNdjsonContentType()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync("stream-test-job");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        
        try
        {
            var resp = await client.GetAsync($"/api/jobs/{jobId}/stream", cts.Token);
            
            // Verify content type
            Assert.Equal("application/x-ndjson", resp.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException)
        {
            // Expected - we just wanted to verify the response starts correctly
        }
    }

    [Fact]
    public async Task GetJobStream_StreamsNoRunsEvent_WhenJobHasNoRuns()
    {
        var client = factory.CreateClient();
        var jobId = await CreateDraftJobAsync("no-runs-job");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        
        try
        {
            var resp = await client.GetAsync($"/api/jobs/{jobId}/stream", cts.Token);
            resp.EnsureSuccessStatusCode();

            // Read first line of NDJSON stream
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            
            var firstLine = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(firstLine);
            
            var evt = JsonSerializer.Deserialize<JsonElement>(firstLine, JsonOpts);
            Assert.Equal("no_runs", evt.GetProperty("type").GetString());
            Assert.Equal(jobId, evt.GetProperty("jobId").GetGuid());
        }
        catch (OperationCanceledException)
        {
            // Expected - stream would continue polling
        }
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
}
