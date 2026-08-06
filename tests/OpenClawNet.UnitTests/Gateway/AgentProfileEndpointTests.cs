using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Tests for the agent profile CRUD + import endpoints mapped by
/// <see cref="AgentProfileEndpoints"/>. Uses the same minimal test-server
/// pattern as <see cref="ChatStreamEndpointTests"/>, but wires up a real
/// InMemory EF Core <see cref="AgentProfileStore"/> instead of mocks.
/// </summary>
public sealed class AgentProfileEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Import endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task PostImport_ValidMarkdown_ReturnsProfile()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/agent-profiles/import", new
        {
            markdown = "# Code Reviewer\nReview code carefully.",
            fallbackName = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        profile.Should().NotBeNull();
        profile!.Name.Should().Be("code-reviewer");
        profile.Instructions.Should().Contain("Review code carefully.");
    }

    [Fact]
    public async Task PostImport_WithYamlFrontMatter_ParsedCorrectly()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var markdown = "---\nname: yaml-agent\nprovider: azure-openai\nmodel: gpt-4o\ntemperature: 0.5\n---\nYou are a YAML-configured agent.";

        var response = await client.PostAsJsonAsync("/api/agent-profiles/import", new
        {
            markdown,
            fallbackName = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        profile.Should().NotBeNull();
        profile!.Name.Should().Be("yaml-agent");
        profile.Provider.Should().Be("azure-openai");
        profile.Temperature.Should().Be(0.5);
        profile.Instructions.Should().Contain("YAML-configured agent");
    }

    // ── List endpoint ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetList_ReturnsAllProfiles()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        // Seed two profiles via import
        await client.PostAsJsonAsync("/api/agent-profiles/import", new { markdown = "# Alpha\nAlpha instructions." });
        await client.PostAsJsonAsync("/api/agent-profiles/import", new { markdown = "# Beta\nBeta instructions." });

        var response = await client.GetAsync("/api/agent-profiles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profiles = await response.Content.ReadFromJsonAsync<List<AgentProfile>>(JsonOpts);
        profiles.Should().NotBeNull();
        profiles!.Count.Should().BeGreaterThanOrEqualTo(2);
        profiles.Select(p => p.Name).Should().Contain("alpha").And.Contain("beta");
    }

    // ── Put (upsert) endpoint ────────────────────────────────────────────────

    [Fact]
    public async Task PutProfile_CreatesNewProfile()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.PutAsJsonAsync("/api/agent-profiles/new-agent", new
        {
            displayName = "New Agent",
            provider = "ollama",
            instructions = "Be concise.",
            enabledTools = (string?)null,
            temperature = 0.8,
            maxTokens = 2048,
            isDefault = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        profile.Should().NotBeNull();
        profile!.Name.Should().Be("new-agent");
        profile.Provider.Should().Be("ollama");
    }

    [Fact]
    public async Task PutProfile_UpdatesExistingProfile()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        // Create
        await client.PutAsJsonAsync("/api/agent-profiles/updatable", new
        {
            displayName = "Original",
            provider = "ollama",
            instructions = "First version.",
            enabledTools = (string?)null,
            temperature = (double?)null,
            maxTokens = (int?)null,
            isDefault = false
        });

        // Update
        var response = await client.PutAsJsonAsync("/api/agent-profiles/updatable", new
        {
            displayName = "Updated",
            provider = "ollama",
            instructions = "Second version.",
            enabledTools = (string?)null,
            temperature = (double?)null,
            maxTokens = (int?)null,
            isDefault = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        profile!.DisplayName.Should().Be("Updated");
    }

    // ── Delete endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteProfile_RemovesProfile()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        // Seed a profile
        await client.PutAsJsonAsync("/api/agent-profiles/deletable", new
        {
            displayName = "Delete Me",
            provider = (string?)null,
            instructions = "Temporary.",
            enabledTools = (string?)null,
            temperature = (double?)null,
            maxTokens = (int?)null,
            isDefault = false
        });

        // Delete it
        var deleteResponse = await client.DeleteAsync("/api/agent-profiles/deletable");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await client.GetAsync("/api/agent-profiles/deletable");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProfile_NonExistent_ReturnsNoContent()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.DeleteAsync("/api/agent-profiles/does-not-exist");

        // DeleteAsync is idempotent — no error for missing profiles
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Get by name endpoint ─────────────────────────────────────────────────

    [Fact]
    public async Task GetByName_ExistingProfile_ReturnsOk()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        await client.PostAsJsonAsync("/api/agent-profiles/import", new
        {
            markdown = "# Lookup Test\nSome instructions."
        });

        var response = await client.GetAsync("/api/agent-profiles/lookup-test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        profile!.Name.Should().Be("lookup-test");
    }

    [Fact]
    public async Task GetByName_NonExistent_ReturnsNotFound()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/agent-profiles/no-such-profile");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Set Default endpoint ─────────────────────────────────────────────────

    [Fact]
    public async Task SetDefault_ExistingProfile_ClearsOtherDefaults()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        // Seed two profiles, the first as default.
        await client.PutAsJsonAsync("/api/agent-profiles/first", new
        {
            displayName = "First",
            provider = "ollama",
            instructions = "First.",
            isDefault = true,
            isEnabled = true
        });
        await client.PutAsJsonAsync("/api/agent-profiles/second", new
        {
            displayName = "Second",
            provider = "ollama",
            instructions = "Second.",
            isDefault = false,
            isEnabled = true
        });

        // Promote 'second'.
        var response = await client.PostAsync("/api/agent-profiles/second/set-default", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var promoted = await response.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        promoted!.IsDefault.Should().BeTrue();

        // 'first' should no longer be default.
        var firstResp = await client.GetAsync("/api/agent-profiles/first");
        var first = await firstResp.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        first!.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task SetDefault_NonExistent_ReturnsNotFound()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync("/api/agent-profiles/missing/set-default", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetDefault_DisabledProfile_ReturnsBadRequest()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        await client.PutAsJsonAsync("/api/agent-profiles/disabled-one", new
        {
            displayName = "Disabled",
            provider = "ollama",
            instructions = "Off.",
            isDefault = false,
            isEnabled = false
        });

        var response = await client.PostAsync("/api/agent-profiles/disabled-one/set-default", null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<WebApplication> CreateTestAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        // Use InMemory EF Core provider with a unique database per test
        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-" + Guid.NewGuid()));
        builder.Services.AddScoped<IAgentProfileStore, AgentProfileStore>();

        var app = builder.Build();
        app.MapAgentProfileEndpoints();
        await app.StartAsync();
        return app;
    }
}
