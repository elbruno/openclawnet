using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for channel adapter endpoints from commit 734baee.
/// Covers: GET /api/channel-adapters/{name}, GET /api/channel-adapters/{name}/health
/// </summary>
[Trait("Category", "Integration")]
public sealed class ChannelAdapterEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ═══════════════════════════════════════════════════════════════
    // GET /api/channel-adapters/{name}
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetChannelAdapterDetail_ReturnsNotFound_WhenAdapterDoesNotExist()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/channel-adapters/nonexistent-adapter");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetChannelAdapterDetail_ReturnsAdapterInfo_WhenExists()
    {
        var client = factory.CreateClient();

        // List all adapters first to find a valid one
        var listResp = await client.GetAsync("/api/channel-adapters");
        if (listResp.StatusCode == HttpStatusCode.OK)
        {
            var adapters = await listResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            if (adapters.ValueKind == JsonValueKind.Array && adapters.GetArrayLength() > 0)
            {
                var firstAdapter = adapters[0].GetProperty("name").GetString();
                
                var resp = await client.GetAsync($"/api/channel-adapters/{firstAdapter}");
                resp.EnsureSuccessStatusCode();

                var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
                Assert.Equal(firstAdapter, dto.GetProperty("name").GetString());
                Assert.True(dto.TryGetProperty("isEnabled", out _));
                Assert.True(dto.TryGetProperty("typeName", out _));
                Assert.True(dto.TryGetProperty("description", out _));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /api/channel-adapters/{name}/health
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetChannelAdapterHealth_ReturnsNotFound_WhenAdapterDoesNotExist()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/channel-adapters/nonexistent-adapter/health");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetChannelAdapterHealth_ReturnsHealthStatus_WhenExists()
    {
        var client = factory.CreateClient();

        // List all adapters first to find a valid one
        var listResp = await client.GetAsync("/api/channel-adapters");
        if (listResp.StatusCode == HttpStatusCode.OK)
        {
            var adapters = await listResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            if (adapters.ValueKind == JsonValueKind.Array && adapters.GetArrayLength() > 0)
            {
                var firstAdapter = adapters[0].GetProperty("name").GetString();
                
                var resp = await client.GetAsync($"/api/channel-adapters/{firstAdapter}/health");
                resp.EnsureSuccessStatusCode();

                var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
                Assert.Equal(firstAdapter, dto.GetProperty("name").GetString());
                Assert.True(dto.TryGetProperty("status", out _));
                Assert.True(dto.TryGetProperty("isHealthy", out _));
                Assert.True(dto.TryGetProperty("message", out _));
                Assert.True(dto.TryGetProperty("checkedAt", out _));
            }
        }
    }
}
