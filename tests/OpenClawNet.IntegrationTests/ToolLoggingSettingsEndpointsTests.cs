using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace OpenClawNet.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class ToolLoggingSettingsEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetToolLoggingSettings_ReturnsExpectedPayload()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/settings/tool-logging");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        dto.TryGetProperty("enabled", out _).Should().BeTrue();
        dto.TryGetProperty("argumentPreviewLength", out _).Should().BeTrue();
        dto.TryGetProperty("outputPreviewLength", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PutToolLoggingSettings_UpdatesEnabledFlag_AndRoundTrips()
    {
        var client = factory.CreateClient();

        var baseline = await client.GetFromJsonAsync<JsonElement>("/api/settings/tool-logging", JsonOpts);
        var originalEnabled = baseline.GetProperty("enabled").GetBoolean();
        var toggledEnabled = !originalEnabled;

        try
        {
            var putResp = await client.PutAsJsonAsync("/api/settings/tool-logging", new { enabled = toggledEnabled });
            putResp.EnsureSuccessStatusCode();

            var putPayload = await putResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            putPayload.GetProperty("enabled").GetBoolean().Should().Be(toggledEnabled);

            var getAfter = await client.GetFromJsonAsync<JsonElement>("/api/settings/tool-logging", JsonOpts);
            getAfter.GetProperty("enabled").GetBoolean().Should().Be(toggledEnabled);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/settings/tool-logging", new { enabled = originalEnabled });
        }
    }
}
