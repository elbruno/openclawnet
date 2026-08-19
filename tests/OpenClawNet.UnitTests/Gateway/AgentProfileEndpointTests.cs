using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
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
using OpenClawNet.Skills;
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

    // ── Test-endpoint transient override regression tests (Issue #236) ────────

    [Fact]
    public async Task PostTest_WithProviderOverride_ResolvesOverriddenProviderDefinition()
    {
        // Issue #236: editing the Model Provider combo box without saving must actually
        // change which provider is used for Test Agent. Two definitions with different
        // models let us prove the override provider name — not the stored one — is
        // resolved and forwarded to the agent provider.
        var (app, capturer) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama-a", new
            {
                providerType = "ollama",
                model = "model-a"
            });
            await client.PutAsJsonAsync("/api/model-providers/ollama-b", new
            {
                providerType = "ollama",
                model = "model-b"
            });
            await client.PutAsJsonAsync("/api/agent-profiles/switch-agent", new
            {
                displayName = "Switch Agent",
                provider = "ollama-a",
                isDefault = false
            });

            await client.PostAsJsonAsync("/api/agent-profiles/switch-agent/test", new
            {
                provider = "ollama-b"
            });
        }

        capturer.LastCapturedProfile.Should().NotBeNull("the agent provider must be called");
        capturer.LastCapturedProfile!.Model.Should().Be("model-b",
            "the overridden provider name must resolve 'ollama-b's definition, not the stored 'ollama-a'");
    }

    [Fact]
    public async Task PostTest_WithOverrides_OverrideValuesNotPersistedOnFailure()
    {
        // On a failed test the override values must NOT be written to the stored profile;
        // only test-result metadata (LastTestedAt, LastTestSucceeded) is persisted.
        // CapturingAgentProvider always throws → exercises the failure path.
        var (app, _) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama-a", new
            {
                providerType = "ollama",
                model = "model-a"
            });
            await client.PutAsJsonAsync("/api/model-providers/ollama-b", new
            {
                providerType = "ollama",
                model = "model-b"
            });
            await client.PutAsJsonAsync("/api/agent-profiles/persist-check-fail", new
            {
                displayName = "Persist Check Fail",
                provider = "ollama-a",
                instructions = "Original instructions.",
                isDefault = false
            });

            await client.PostAsJsonAsync("/api/agent-profiles/persist-check-fail/test", new
            {
                provider = "ollama-b",
                instructions = "Ephemeral test-only instructions."
            });

            var getResponse = await client.GetAsync("/api/agent-profiles/persist-check-fail");
            var response = await getResponse.Content.ReadFromJsonAsync<AgentProfileResponse>(JsonOpts);

            response!.Provider.Should().Be("ollama-a",
                "override provider must NOT be persisted to the stored profile after a failed test");
            response.Instructions.Should().Be("Original instructions.",
                "override instructions must NOT be persisted to the stored profile after a failed test");
            response.LastTestSucceeded.Should().BeFalse(
                "test-result flag (LastTestSucceeded) must still be persisted on failure");
            response.LastTestedAt.Should().NotBeNull(
                "test-result timestamp (LastTestedAt) must still be persisted on failure");
        }
    }

    [Fact]
    public async Task PostTest_WithOverrides_OverrideValuesNotPersistedOnSuccess()
    {
        // On a successful test the override values must NOT be written to the stored profile;
        // only test-result metadata is persisted. Uses SucceedingCapturingAgentProvider so the
        // test endpoint reaches the success branch.
        var (app, _) = await CreateTestAppWithSucceedingFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama-a", new
            {
                providerType = "ollama",
                model = "model-a"
            });
            await client.PutAsJsonAsync("/api/model-providers/ollama-b", new
            {
                providerType = "ollama",
                model = "model-b"
            });
            await client.PutAsJsonAsync("/api/agent-profiles/persist-check-success", new
            {
                displayName = "Persist Check Success",
                provider = "ollama-a",
                instructions = "Original instructions.",
                isDefault = false
            });

            var testResponse = await client.PostAsJsonAsync("/api/agent-profiles/persist-check-success/test", new
            {
                provider = "ollama-b",
                instructions = "Ephemeral test-only instructions."
            });
            var testBody = await testResponse.Content.ReadAsStringAsync();
            var testResult = JsonSerializer.Deserialize<JsonElement>(testBody, JsonOpts);
            testResult.GetProperty("success").GetBoolean().Should().BeTrue(
                "SucceedingCapturingAgentProvider must produce success=true to exercise the success path");

            var getResponse = await client.GetAsync("/api/agent-profiles/persist-check-success");
            var response = await getResponse.Content.ReadFromJsonAsync<AgentProfileResponse>(JsonOpts);

            response!.Provider.Should().Be("ollama-a",
                "override provider must NOT be persisted to the stored profile after a successful test");
            response.Instructions.Should().Be("Original instructions.",
                "override instructions must NOT be persisted to the stored profile after a successful test");
            response.LastTestSucceeded.Should().BeTrue(
                "test-result flag (LastTestSucceeded=true) must be persisted on success");
            response.LastTestedAt.Should().NotBeNull(
                "test-result timestamp (LastTestedAt) must be persisted on success");
        }
    }

    [Fact]
    public async Task PostTest_NoBody_UsesStoredProviderAndModel_AndListViewUnaffected()
    {
        // When no override body is supplied, the stored profile's provider/model reach the
        // agent provider unchanged, and the GET list endpoint still returns the profile.
        var (app, capturer) = await CreateTestAppWithFullStoresAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama-a", new
            {
                providerType = "ollama",
                model = "model-a"
            });
            await client.PutAsJsonAsync("/api/agent-profiles/no-body-agent", new
            {
                displayName = "No Body Agent",
                provider = "ollama-a",
                isDefault = false
            });

            // No JSON body — overrides parameter is null in the endpoint handler
            await client.PostAsync("/api/agent-profiles/no-body-agent/test", null);

            capturer.LastCapturedProfile.Should().NotBeNull(
                "the agent provider must still be called when no override body is supplied");
            capturer.LastCapturedProfile!.Model.Should().Be("model-a",
                "stored model must reach the provider when no overrides are provided");

            var listResponse = await client.GetAsync("/api/agent-profiles");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var profiles = await listResponse.Content.ReadFromJsonAsync<List<AgentProfileResponse>>(JsonOpts);
            profiles!.Select(p => p.Name).Should().Contain("no-body-agent",
                "GET list must include the profile after calling the test endpoint");
        }
    }

    [Fact]
    public async Task PostHostedAgentExport_ReturnsZipBundleWithSelectedProfiles()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        await client.PutAsJsonAsync("/api/agent-profiles/alpha", new
        {
            displayName = "Alpha",
            provider = "ollama",
            instructions = "Alpha instructions.",
            isDefault = false
        });
        await client.PutAsJsonAsync("/api/agent-profiles/beta", new
        {
            displayName = "Beta",
            provider = "ollama",
            instructions = "Beta instructions.",
            isDefault = false
        });

        var response = await client.PostAsJsonAsync("/api/agent-profiles/export/hosted-agent", new
        {
            profileNames = new[] { "alpha", "beta" },
            namePrefix = "openclaw-hosted",
            location = "eastus",
            containerImage = "ghcr.io/elbruno/openclawnet-hosted-agent:latest",
            containerPort = 8080
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        await using var zipStream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        zip.GetEntry("main.bicep").Should().NotBeNull();
        zip.GetEntry("main.parameters.json").Should().NotBeNull();
        zip.GetEntry("profiles.json").Should().NotBeNull();
        zip.GetEntry("README.md").Should().NotBeNull();

        using var profilesEntryStream = zip.GetEntry("profiles.json")!.Open();
        using var reader = new StreamReader(profilesEntryStream);
        var profilesJson = await reader.ReadToEndAsync();
        profilesJson.Should().Contain("alpha").And.Contain("beta");
    }

    [Fact]
    public async Task PostHostedAgentExport_WhenProfileIsMissing_ReturnsNotFound()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/agent-profiles/export/hosted-agent", new
        {
            profileNames = new[] { "missing" },
            namePrefix = "openclaw-hosted",
            location = "eastus",
            containerImage = "ghcr.io/elbruno/openclawnet-hosted-agent:latest",
            containerPort = 8080
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        // Registered app-wide in production (see Program.cs) and required for the /test
        // endpoint's route metadata to resolve correctly even in tests that never call it.
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();
        builder.Services.AddSingleton<IAgentSkillAssignmentService, NullAgentSkillAssignmentService>();

        var app = builder.Build();
        app.MapAgentProfileEndpoints();
        app.MapHostedAgentExportEndpoints();
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
        builder.Services.AddSingleton<IAgentSkillAssignmentService, NullAgentSkillAssignmentService>();

        var app = builder.Build();
        app.MapAgentProfileEndpoints();
        app.MapHostedAgentExportEndpoints();
        app.MapModelProviderEndpoints();
        await app.StartAsync();
        return (app, capturer);
    }

    /// <summary>
    /// Creates a test app with both stores plus a <see cref="SucceedingCapturingAgentProvider"/>
    /// so tests can exercise the success path of the test endpoint and verify that only
    /// test-result metadata is persisted (not override values).
    /// </summary>
    private static async Task<(WebApplication app, SucceedingCapturingAgentProvider capturer)>
        CreateTestAppWithSucceedingFullStoresAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-apt-full-succ-" + Guid.NewGuid()));
        builder.Services.AddScoped<IAgentProfileStore, AgentProfileStore>();
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

        var capturer = new SucceedingCapturingAgentProvider("ollama");
        builder.Services.AddSingleton<IAgentProvider>(capturer);
        builder.Services.AddSingleton<IAgentSkillAssignmentService, NullAgentSkillAssignmentService>();

        var app = builder.Build();
        app.MapAgentProfileEndpoints();
        app.MapHostedAgentExportEndpoints();
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

    /// <summary>
    /// Fake IAgentProvider that captures the profile and returns an <see cref="AlwaysSucceedingChatClient"/>,
    /// allowing tests to exercise the success path of the test endpoint without a real LLM.
    /// </summary>
    private sealed class SucceedingCapturingAgentProvider(string providerName) : IAgentProvider
    {
        public string ProviderName => providerName;
        public AgentProfile? LastCapturedProfile { get; private set; }

        public IChatClient CreateChatClient(AgentProfile profile)
        {
            LastCapturedProfile = profile;
            return new AlwaysSucceedingChatClient();
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }

    /// <summary>Stub IChatClient that always returns a trivial successful one-word response.</summary>
    private sealed class AlwaysSucceedingChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("stub", null);

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                [new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Streaming not needed for test stubs.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    /// <summary>
    /// Null-object stub so the skill-assignment endpoints registered by
    /// <see cref="AgentProfileEndpoints.MapAgentProfileEndpoints"/> have a
    /// resolvable service in the test app. Profile CRUD tests don't exercise
    /// skill assignment, so a no-op is sufficient.
    /// </summary>
    private sealed class NullAgentSkillAssignmentService : IAgentSkillAssignmentService
    {
        public Task<bool> AssignAsync(string skillName, string agentName, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task UnassignAsync(string skillName, string agentName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetAssignedAsync(string agentName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<SkillSyncResult> SyncAssignmentsAsync(string agentName, IEnumerable<string> skillNames, CancellationToken ct = default)
            => Task.FromResult(new SkillSyncResult([], [], []));
    }
}
