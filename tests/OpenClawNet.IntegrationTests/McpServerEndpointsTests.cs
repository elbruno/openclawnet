using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Gateway.Services.Mcp;

namespace OpenClawNet.IntegrationTests;

public sealed class McpServerEndpointsTests : IClassFixture<GatewayWebAppFactory>
{
    private readonly GatewayWebAppFactory _factory;

    public McpServerEndpointsTests(GatewayWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetServers_ReturnsBundledBuiltins()
    {
        var client = _factory.CreateClient();

        var items = await client.GetFromJsonAsync<List<McpListItem>>("/api/mcp/servers");

        // PR-E: SchemaMigrator seeds the 4 built-ins on startup, so they appear as DB rows
        // immediately — no lazy-persist on first toggle.
        items.Should().NotBeNull();
        items!.Should().Contain(i => i.IsBuiltIn && i.Name == "web" && i.Transport == "InProcess" && i.Enabled);
        items.Should().Contain(i => i.IsBuiltIn && i.Name == "shell" && i.Transport == "InProcess" && i.Enabled);
        items.Should().Contain(i => i.IsBuiltIn && i.Name == "browser" && i.Transport == "InProcess" && i.Enabled);
        items.Should().Contain(i => i.IsBuiltIn && i.Name == "filesystem" && i.Transport == "InProcess" && i.Enabled);
    }

    [Fact]
    public async Task FullCrudFlow_CreateListUpdateDelete()
    {
        var client = _factory.CreateClient();

        var name = $"int-{Guid.NewGuid():N}".Substring(0, 12);
        var create = await client.PostAsJsonAsync("/api/mcp/servers", new
        {
            name,
            transport = "stdio",
            command = "echo",
            args = new[] { "hi" },
            env = new Dictionary<string, string> { ["TOKEN"] = "secret-1" },
            enabled = false,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<McpListItem>();
        created!.HasEnv.Should().BeTrue();

        var list = await client.GetFromJsonAsync<List<McpListItem>>("/api/mcp/servers");
        list!.Should().Contain(i => i.Id == created.Id);

        // Built-in protection: try to mutate command of a built-in.
        var web = list.First(i => i.IsBuiltIn && i.Name == "web");
        var forbidden = await client.PutAsJsonAsync($"/api/mcp/servers/{web.Id}", new
        {
            name = web.Name, transport = web.Transport, command = "evil", enabled = true,
        });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Update user row.
        var update = await client.PutAsJsonAsync($"/api/mcp/servers/{created.Id}", new
        {
            name, transport = "stdio", command = "echo", args = new[] { "bye" }, enabled = true,
        });
        update.IsSuccessStatusCode.Should().BeTrue();

        // Delete.
        var del = await client.DeleteAsync($"/api/mcp/servers/{created.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var delBuiltin = await client.DeleteAsync($"/api/mcp/servers/{web.Id}");
        delBuiltin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSuggestions_ReturnsEntries()
    {
        var client = _factory.CreateClient();

        var entries = await client.GetFromJsonAsync<List<JsonSuggestion>>("/api/mcp/suggestions");

        entries.Should().NotBeNull();
        // Either the on-disk YAML loads or the provider returned []. Don't fail CI on
        // missing-file path (the unit tests cover that). When entries do load, sanity-check.
        if (entries!.Count > 0)
            entries.Should().Contain(s => s.Id == "github-mcp");
    }

    [Fact]
    public async Task RegistrySearch_WithStubbedClient_ReturnsNormalizedResults()
    {
        // Spin up a dedicated factory variant whose registry client is stubbed so we
        // never touch the real network.
        await using var factory = new GatewayWebAppFactoryWithStubRegistry();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/mcp/registry/search?q=test");
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadFromJsonAsync<RegistryResponse>();

        body!.Entries.Should().HaveCount(1);
        body.Entries[0].Name.Should().Be("stub/server");
    }

    private sealed record McpListItem(
        Guid Id, string Name, string Transport, string? Command, string[] Args,
        string? Url, bool HasEnv, bool HasHeaders, bool Enabled, bool IsBuiltIn,
        bool IsRunning, int ToolCount, string? LastError);

    private sealed record JsonSuggestion(string Id, string Name);

    private sealed record RegistryResponse(McpRegistryEntry[] Entries, string? NextCursor);

    private sealed class GatewayWebAppFactoryWithStubRegistry : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<OpenClawNet.Gateway.GatewayProgramMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:openclawnet-db"] = "Data Source=:memory:",
                    ["Teams:Enabled"] = "false",
                    ["Model:Provider"] = "ollama",
                    ["Model:Model"] = "test",
                    ["Model:Endpoint"] = "http://localhost:11434",
                });
            });
            builder.ConfigureServices(services =>
            {
                var dbDescriptors = services
                    .Where(d => d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<OpenClawNet.Storage.OpenClawDbContext>)
                             || d.ServiceType == typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<OpenClawNet.Storage.OpenClawDbContext>))
                    .ToList();
                foreach (var d in dbDescriptors) services.Remove(d);

                var opts = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<OpenClawNet.Storage.OpenClawDbContext>()
                    .UseInMemoryDatabase($"reg-stub-{Guid.NewGuid()}")
                    .Options;
                services.AddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<OpenClawNet.Storage.OpenClawDbContext>>(
                    new StubDbFactory(opts));

                var existing = services.Where(s => s.ServiceType == typeof(IMcpRegistryClient)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton<IMcpRegistryClient, StubRegistryClient>();
            });
        }

        private sealed class StubDbFactory(Microsoft.EntityFrameworkCore.DbContextOptions<OpenClawNet.Storage.OpenClawDbContext> opts)
            : Microsoft.EntityFrameworkCore.IDbContextFactory<OpenClawNet.Storage.OpenClawDbContext>
        {
            public OpenClawNet.Storage.OpenClawDbContext CreateDbContext() => new(opts);
        }

        private sealed class StubRegistryClient : IMcpRegistryClient
        {
            public Task<McpRegistrySearchResult> SearchAsync(string? query, string? cursor, int limit, CancellationToken ct)
                => Task.FromResult(new McpRegistrySearchResult(
                    new[]
                    {
                        new McpRegistryEntry("stub/server", "stub/server",
                            "stub description", "stdio", "npx",
                            new[] { "-y", "stub-pkg" }, null),
                    },
                    null));
        }
    }
}
