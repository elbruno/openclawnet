using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Integration tests for MCP server tools endpoint from commit 734baee.
/// Covers: GET /api/mcp-servers/{id}/tools
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpServerToolsEndpointsTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ═══════════════════════════════════════════════════════════════
    // GET /api/mcp-servers/{id}/tools
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMcpServerTools_ReturnsNotFound_WhenServerDoesNotExist()
    {
        var client = factory.CreateClient();
        var serverId = Guid.NewGuid();

        var resp = await client.GetAsync($"/api/mcp-servers/{serverId}/tools");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetMcpServerTools_ReturnsEmptyList_WhenServerDisabled()
    {
        var client = factory.CreateClient();
        var serverId = await CreateDisabledMcpServerAsync("disabled-server");

        var resp = await client.GetAsync($"/api/mcp-servers/{serverId}/tools");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(serverId, dto.GetProperty("serverId").GetGuid());
        Assert.Equal("disabled-server", dto.GetProperty("serverName").GetString());
        Assert.False(dto.GetProperty("success").GetBoolean());
        Assert.Contains("disabled", dto.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dto.GetProperty("tools").EnumerateArray());
    }

    [Fact]
    public async Task GetMcpServerTools_ReturnsEmptyList_WhenServerNotRunning()
    {
        var client = factory.CreateClient();
        var serverId = await CreateEnabledButNotRunningMcpServerAsync("not-running-server");

        var resp = await client.GetAsync($"/api/mcp-servers/{serverId}/tools");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(serverId, dto.GetProperty("serverId").GetGuid());
        Assert.False(dto.GetProperty("success").GetBoolean());
        Assert.Contains("not running", dto.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dto.GetProperty("tools").EnumerateArray());
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════

    private async Task<Guid> CreateDisabledMcpServerAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var server = new McpServerDefinitionEntity
        {
            Name = name,
            Command = "test-command",
            Enabled = false
        };

        db.McpServerDefinitions.Add(server);
        await db.SaveChangesAsync();

        return server.Id;
    }

    private async Task<Guid> CreateEnabledButNotRunningMcpServerAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var server = new McpServerDefinitionEntity
        {
            Name = name,
            Command = "test-command",
            Enabled = true
        };

        db.McpServerDefinitions.Add(server);
        await db.SaveChangesAsync();

        return server.Id;
    }
}
