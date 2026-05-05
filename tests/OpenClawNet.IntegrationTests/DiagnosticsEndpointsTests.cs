using System.Net.Http.Json;
using System.Text.Json;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for diagnostics endpoints from commit 734baee.
/// Covers: GET /api/diagnostics/db, GET /api/diagnostics/info
/// </summary>
[Trait("Category", "Integration")]
public sealed class DiagnosticsEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ═══════════════════════════════════════════════════════════════
    // GET /api/diagnostics/db
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDatabaseDiagnostics_ReturnsDbInfo()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/diagnostics/db");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        
        // Verify structure - in-memory DB may have error, but should still return counts
        Assert.True(dto.TryGetProperty("jobCount", out var jobCount));
        Assert.True(dto.TryGetProperty("runCount", out var runCount));
        Assert.True(dto.TryGetProperty("sessionCount", out var sessionCount));
        
        // Counts should be non-negative integers
        Assert.True(jobCount.GetInt32() >= 0);
        Assert.True(runCount.GetInt32() >= 0);
        Assert.True(sessionCount.GetInt32() >= 0);
    }

    [Fact]
    public async Task GetDatabaseDiagnostics_ContainsExpectedFields()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/diagnostics/db");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        
        // Verify all expected fields exist
        Assert.True(dto.TryGetProperty("databasePath", out _));
        Assert.True(dto.TryGetProperty("sizeBytes", out _));
        Assert.True(dto.TryGetProperty("lastWriteTime", out _));
        Assert.True(dto.TryGetProperty("error", out _));
        Assert.True(dto.TryGetProperty("jobCount", out _));
        Assert.True(dto.TryGetProperty("runCount", out _));
        Assert.True(dto.TryGetProperty("sessionCount", out _));
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /api/diagnostics/info
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSystemInfo_ReturnsSystemInformation()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/diagnostics/info");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        
        // Verify required fields
        Assert.Equal("OpenClawNet", dto.GetProperty("name").GetString());
        
        var version = dto.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version));
        
        var environment = dto.GetProperty("environment").GetString();
        Assert.False(string.IsNullOrWhiteSpace(environment));
    }

    [Fact]
    public async Task GetSystemInfo_ContainsUptimeAndStartTime()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/diagnostics/info");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        
        // Verify all expected fields exist
        Assert.True(dto.TryGetProperty("name", out _));
        Assert.True(dto.TryGetProperty("version", out _));
        Assert.True(dto.TryGetProperty("buildDate", out _));
        Assert.True(dto.TryGetProperty("environment", out _));
        Assert.True(dto.TryGetProperty("startedAt", out _));
        Assert.True(dto.TryGetProperty("uptimeSeconds", out var uptimeSeconds));
        
        // Uptime should be positive
        Assert.True(uptimeSeconds.GetDouble() > 0);
    }
}
