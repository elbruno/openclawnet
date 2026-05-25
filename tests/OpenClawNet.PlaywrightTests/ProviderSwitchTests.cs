using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// API-level tests for runtime provider switching — covers demo-06 provider-switch scenarios.
/// Validates that the settings API exposes provider info and supports live updates.
/// </summary>
[Collection("AspireHost")]
public class ProviderSwitchTests : IAsyncLifetime
{
    private readonly AspireHostFixture _fixture;
    private HttpClient _client = null!;

    public ProviderSwitchTests(AspireHostFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _client = _fixture.CreateGatewayHttpClient();
        _client.Timeout = TimeSpan.FromMinutes(3);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [SkippableFact]
    public async Task Settings_GetCurrentSettings_ReturnsProviderInfo()
    {
        var response = await _client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("provider", out var provider), "Settings should contain provider");
        Assert.False(string.IsNullOrEmpty(provider.GetString()), "Provider should not be empty");
        Assert.True(json.TryGetProperty("model", out _), "Settings should contain model");
        Assert.True(json.TryGetProperty("endpoint", out _), "Settings should contain endpoint");
        Assert.True(json.TryGetProperty("authMode", out _), "Settings should contain authMode");
        Assert.True(json.TryGetProperty("hasApiKey", out _), "Settings should contain hasApiKey");
    }

    [SkippableFact]
    public async Task Settings_PutSettings_UpdatesProvider()
    {
        // Capture current settings for restore
        var getResp = await _client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var original = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var originalProvider = original.GetProperty("provider").GetString();

        // Switch to a different provider
        var targetProvider = originalProvider == "ollama" ? "azure-openai" : "ollama";
        var putResp = await _client.PutAsJsonAsync("/api/settings", new
        {
            provider = targetProvider,
            model = "test-switch-model",
            endpoint = "http://localhost:9999"
        });
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var putJson = await putResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(targetProvider, putJson.GetProperty("provider").GetString());

        // Verify via GET
        var verifyResp = await _client.GetAsync("/api/settings");
        var verified = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(targetProvider, verified.GetProperty("provider").GetString());

        // Restore original
        await _client.PutAsJsonAsync("/api/settings", new
        {
            provider = originalProvider,
            model = original.GetProperty("model").GetString(),
            endpoint = original.GetProperty("endpoint").GetString()
        });
    }
}
