using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
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
            isDefault = false,
            retrievalLevel = "Hybrid"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<AgentProfile>(JsonOpts);
        profile.Should().NotBeNull();
        profile!.Name.Should().Be("new-agent");
        profile.Provider.Should().Be("ollama");
        profile.RetrievalLevel.Should().Be(RetrievalLevel.Hybrid);
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

    // ── Test endpoint (Issue #122) ────────────────────────────────────────────

    [Fact]
    public async Task PostTest_NonExistentProfile_ReturnsNotFound()
    {
        var (app, _) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            var response = await client.PostAsync("/api/agent-profiles/does-not-exist/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task PostTest_WhenProviderDefinitionNotFound_ReturnsSuccessFalseWithMessage()
    {
        // Profile references a provider that hasn't been registered
        var (app, _) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/agent-profiles/orphan-profile", new
            {
                displayName = "Orphan Profile",
                provider = "ollama-missing",
                instructions = "Test agent.",
                isDefault = false
            });

            var response = await client.PostAsync("/api/agent-profiles/orphan-profile/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("message").GetString().Should().Contain("ollama-missing");
        }
    }

    [Fact]
    public async Task PostTest_WithDefinitionModel_PassesModelToAgentProvider()
    {
        // Issue #122: the test profile built inside the endpoint must carry Model from
        // the provider definition so OllamaAgentProvider doesn't fall back to its default.
        var (app, capturer) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            // Seed the provider definition with a specific model
            await client.PutAsJsonAsync("/api/model-providers/ollama", new
            {
                providerType = "ollama",
                displayName = "Local Ollama",
                endpoint = "http://localhost:11434",
                model = "gemma4:e2b"
            });

            // Seed the agent profile referencing that provider
            await client.PutAsJsonAsync("/api/agent-profiles/my-ollama-agent", new
            {
                displayName = "My Ollama Agent",
                provider = "ollama",
                instructions = "You are a helpful assistant.",
                isDefault = false
            });

            await client.PostAsync("/api/agent-profiles/my-ollama-agent/test", null);
        }

        capturer.LastCapturedProfile.Should().NotBeNull("the agent provider must be called");
        capturer.LastCapturedProfile!.Model.Should().Be("gemma4:e2b",
            "definition.Model must be forwarded to the test profile (fix for issue #122)");
    }

    [Fact]
    public async Task PostTest_ModelIsNotNull_WhenDefinitionHasModel()
    {
        // Regression: null model caused Ollama 404 — definition model must always reach the provider.
        var (app, capturer) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama", new
            {
                providerType = "ollama",
                model = "llama3.2"
            });
            await client.PutAsJsonAsync("/api/agent-profiles/null-model-agent", new
            {
                displayName = "Null Model Agent",
                provider = "ollama",
                isDefault = false
            });

            await client.PostAsync("/api/agent-profiles/null-model-agent/test", null);
        }

        capturer.LastCapturedProfile.Should().NotBeNull();
        capturer.LastCapturedProfile!.Model.Should().NotBeNullOrEmpty(
            "null model causes Ollama 404; definition model must always be forwarded");
    }

    [Fact]
    public async Task PostTest_ResponseIsOk_WithSuccessFalse_WhenProviderThrows()
    {
        // Endpoint must handle provider exceptions gracefully — 200 OK with success=false.
        var (app, _) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama", new
            {
                providerType = "ollama",
                model = "gemma4:e2b"
            });
            await client.PutAsJsonAsync("/api/agent-profiles/throw-agent", new
            {
                displayName = "Throw Agent",
                provider = "ollama",
                isDefault = false
            });

            var response = await client.PostAsync("/api/agent-profiles/throw-agent/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
            result.GetProperty("success").GetBoolean().Should().BeFalse();
        }
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

    /// <summary>
    /// Creates a test app with both profile and provider definition stores plus a
    /// <see cref="CapturingAgentProvider"/> for asserting on model forwarding (issue #122).
    /// </summary>
    private static async Task<(WebApplication app, CapturingAgentProvider capturer)>
        CreateTestAppWithFullStoresAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-apt-full-" + Guid.NewGuid()));
        builder.Services.AddScoped<IAgentProfileStore, AgentProfileStore>();
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

        var capturer = new CapturingAgentProvider("ollama");
        builder.Services.AddSingleton<IAgentProvider>(capturer);

        var app = builder.Build();
        app.MapAgentProfileEndpoints();
        app.MapModelProviderEndpoints();
        await app.StartAsync();
        return (app, capturer);
    }

    /// <summary>
    /// Fake IAgentProvider that records the profile passed to CreateChatClient then throws,
    /// so tests can assert on model propagation without needing a real LLM.
    /// The throw is swallowed by the endpoint's catch-all (returns 200 success=false).
    /// </summary>
    private sealed class CapturingAgentProvider(string providerName) : IAgentProvider
    {
        public string ProviderName => providerName;
        public AgentProfile? LastCapturedProfile { get; private set; }

        public IChatClient CreateChatClient(AgentProfile profile)
        {
            LastCapturedProfile = profile;
            throw new InvalidOperationException(
                $"CapturingAgentProvider: profile captured for '{providerName}', no real chat client.");
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
