using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Tests specifically for the gateway-only demo track — validates health detail,
/// session isolation, and gateway behaviour without requiring the Blazor UI.
/// </summary>
[Collection("AppHost")]
public class GatewayOnlyDemoTests : IAsyncLifetime
{
    private readonly AppHostFixture _fixture;
    private HttpClient _client = null!;

    public GatewayOnlyDemoTests(AppHostFixture fixture)
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

    private static bool AssertSuccessOrServiceUnavailable(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return false;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return true;
    }

    [SkippableFact]
    public async Task Gateway_HealthEndpoint_ReturnsDetailedStatus()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", json.GetProperty("status").GetString());
        Assert.True(json.TryGetProperty("timestamp", out var ts), "Health response should include timestamp");
        // Timestamp should be a valid date-time
        Assert.True(DateTime.TryParse(ts.GetString(), out _), "Timestamp should be a parseable date-time");
    }

    [SkippableFact]
    [Trait("Category", "RequiresModel")]
    public async Task Gateway_ParallelSessions_AreIsolated()
    {
        // Create two separate sessions
        var session1Resp = await _client.PostAsJsonAsync("/api/sessions", new { title = "Isolation Session A" });
        var session1 = await session1Resp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId1 = session1.GetProperty("id").GetString();

        var session2Resp = await _client.PostAsJsonAsync("/api/sessions", new { title = "Isolation Session B" });
        var session2 = await session2Resp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId2 = session2.GetProperty("id").GetString();

        // Send different messages to each session
        var chat1 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId = sessionId1,
            message = "Remember: the secret word for session A is 'pineapple'."
        });
        if (!AssertSuccessOrServiceUnavailable(chat1))
            return;

        var chat2 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId = sessionId2,
            message = "Remember: the secret word for session B is 'umbrella'."
        });
        AssertSuccessOrServiceUnavailable(chat2);

        // Verify message histories are independent
        var msgs1Resp = await _client.GetAsync($"/api/sessions/{sessionId1}/messages");
        var msgs1 = await msgs1Resp.Content.ReadFromJsonAsync<JsonElement>();

        var msgs2Resp = await _client.GetAsync($"/api/sessions/{sessionId2}/messages");
        var msgs2 = await msgs2Resp.Content.ReadFromJsonAsync<JsonElement>();

        // Both should have messages, but content should be specific to each session
        Assert.True(msgs1.GetArrayLength() >= 2, "Session A should have at least 2 messages");
        Assert.True(msgs2.GetArrayLength() >= 2, "Session B should have at least 2 messages");

        // Verify first user messages are unique to each session
        var msg1Content = msgs1[0].GetProperty("content").GetString()!;
        var msg2Content = msgs2[0].GetProperty("content").GetString()!;
        Assert.Contains("pineapple", msg1Content);
        Assert.Contains("umbrella", msg2Content);

        // Cleanup
        await _client.DeleteAsync($"/api/sessions/{sessionId1}");
        await _client.DeleteAsync($"/api/sessions/{sessionId2}");
    }
}
