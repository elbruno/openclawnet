using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenClawNet.Gateway.Endpoints;

namespace OpenClawNet.IntegrationTests;

public sealed class AgentProfilesBulkDeleteTests(GatewayWebAppFactory factory) : IClassFixture<GatewayWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private async Task PutProfile(string name, bool isDefault = false)
    {
        var req = new AgentProfileRequest(
            DisplayName: name,
            Provider: null,
            Model: null,
            Endpoint: null,
            ApiKey: null,
            DeploymentName: null,
            AuthMode: null,
            Instructions: null,
            EnabledTools: null,
            Temperature: null,
            MaxTokens: null,
            IsDefault: isDefault);
        var resp = await _client.PutAsJsonAsync($"/api/agent-profiles/{name}", req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BulkDelete_NoBody_Returns400()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/agent-profiles")
        {
            Content = JsonContent.Create(new BulkDeleteAgentProfilesRequest { Names = [] }),
        };
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkDelete_DeletesNonDefault_AndSkipsDefaultAndMissing()
    {
        await PutProfile("bulk-a");
        await PutProfile("bulk-b");
        await PutProfile("bulk-default", isDefault: true);

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/agent-profiles")
        {
            Content = JsonContent.Create(new BulkDeleteAgentProfilesRequest
            {
                Names = ["bulk-a", "bulk-b", "bulk-default", "does-not-exist"]
            }),
        };
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BulkDeleteAgentProfilesResponse>(body, JsonOpts);
        result.Should().NotBeNull();
        result!.Deleted.Should().BeEquivalentTo(new[] { "bulk-a", "bulk-b" });
        result.Skipped.Should().HaveCount(2);
        result.Skipped.Should().Contain(s => s.Name == "bulk-default" && s.Reason == "default-profile");
        result.Skipped.Should().Contain(s => s.Name == "does-not-exist" && s.Reason == "not-found");

        // Verify deletion took effect
        var getA = await _client.GetAsync("/api/agent-profiles/bulk-a");
        getA.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var getDefault = await _client.GetAsync("/api/agent-profiles/bulk-default");
        getDefault.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
