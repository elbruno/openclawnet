using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Memory;

namespace OpenClawNet.IntegrationTests.Gateway;

public sealed class AgentMemoryEndpointsTests(GatewayWebAppFactory factory) : IClassFixture<GatewayWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task SearchEndpoint_ReturnsSeededMemoryHits()
    {
        var agentId = $"memory-endpoint-{Guid.NewGuid():N}";
        var seededId = await SeedMemoryAsync(factory, agentId, "Bruno likes pizza and pasta.");

        var response = await _client.PostAsJsonAsync(
            $"/api/agents/{agentId}/memory/search",
            new { query = "pizza", topK = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("agentId").GetString().Should().Be(agentId);
        payload.RootElement.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
        payload.RootElement
            .GetProperty("hits")
            .EnumerateArray()
            .Select(h => h.GetProperty("id").GetString())
            .Should()
            .Contain(seededId);
    }

    [Fact]
    public async Task DeleteEndpoint_RemovesSpecificMemory()
    {
        var agentId = $"memory-endpoint-{Guid.NewGuid():N}";
        var memoryId = await SeedMemoryAsync(factory, agentId, "Secret to delete");

        var delete = await _client.DeleteAsync($"/api/agents/{agentId}/memory/{memoryId}");
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        var search = await _client.PostAsJsonAsync(
            $"/api/agents/{agentId}/memory/search",
            new { query = "secret", topK = 5 });

        search.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        payload.RootElement
            .GetProperty("hits")
            .EnumerateArray()
            .Select(h => h.GetProperty("id").GetString())
            .Should()
            .NotContain(memoryId);
    }

    [Fact]
    public async Task SearchEndpoint_WithoutQuery_ReturnsBadRequest()
    {
        var agentId = $"memory-endpoint-{Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync(
            $"/api/agents/{agentId}/memory/search",
            new { query = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> SeedMemoryAsync(GatewayWebAppFactory factory, string agentId, string content)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentMemoryStore>();
        var entry = new MemoryEntry(
            Content: content,
            Kind: "fact",
            Importance: 0.8,
            Timestamp: DateTimeOffset.UtcNow,
            SourceSessionId: "integration-test",
            Metadata: null);
        return await store.StoreAsync(agentId, entry);
    }
}
