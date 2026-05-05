using System.Net.Http.Json;
using System.Text.Json;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for runtime settings endpoint from commit 734baee.
/// Covers: GET /api/runtime-settings
/// </summary>
[Trait("Category", "Integration")]
public sealed class RuntimeSettingsEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ═══════════════════════════════════════════════════════════════
    // GET /api/runtime-settings
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRuntimeSettings_ReturnsConfiguration()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/runtime-settings");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        
        // Verify required fields are present
        Assert.True(dto.TryGetProperty("provider", out _));
        Assert.True(dto.TryGetProperty("hasApiKey", out _));
        
        // Provider should be a non-empty string
        var provider = dto.GetProperty("provider").GetString();
        Assert.False(string.IsNullOrWhiteSpace(provider));
    }

    [Fact]
    public async Task GetRuntimeSettings_ContainsExpectedFields()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/runtime-settings");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        
        // Verify all expected fields exist
        Assert.True(dto.TryGetProperty("provider", out _));
        Assert.True(dto.TryGetProperty("model", out _));
        Assert.True(dto.TryGetProperty("endpoint", out _));
        Assert.True(dto.TryGetProperty("hasApiKey", out _));
        Assert.True(dto.TryGetProperty("authMode", out _));
        Assert.True(dto.TryGetProperty("deploymentName", out _));

        // HasApiKey should be a boolean
        var hasApiKey = dto.GetProperty("hasApiKey").GetBoolean();
        Assert.IsType<bool>(hasApiKey);
    }
}
