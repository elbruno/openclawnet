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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;
using OpenClawNet.Models.Foundry;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Gateway;

public sealed class ModelProviderEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetList_ReturnsAllProviders()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        // Seed two providers via PUT
        await client.PutAsJsonAsync("/api/model-providers/ollama-1", new
        {
            providerType = "ollama",
            displayName = "Ollama 1",
            endpoint = "http://localhost:11434",
            model = "gemma4:e2b"
        });
        await client.PutAsJsonAsync("/api/model-providers/azure-1", new
        {
            providerType = "azure-openai",
            displayName = "Azure 1",
            model = "gpt-4o"
        });

        var response = await client.GetAsync("/api/model-providers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var providers = await response.Content.ReadFromJsonAsync<List<ModelProviderResponse>>(JsonOpts);
        providers.Should().NotBeNull();
        providers!.Count.Should().BeGreaterThanOrEqualTo(2);
        providers.Select(p => p.Name).Should().Contain("ollama-1").And.Contain("azure-1");
    }

    [Fact]
    public async Task GetByName_ExistingProvider_ReturnsOk()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        await client.PutAsJsonAsync("/api/model-providers/lookup-test", new
        {
            providerType = "ollama",
            displayName = "Lookup Test",
            model = "llama3"
        });

        var response = await client.GetAsync("/api/model-providers/lookup-test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var provider = await response.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);
        provider!.Name.Should().Be("lookup-test");
        provider.ProviderType.Should().Be("ollama");
    }

    [Fact]
    public async Task GetByName_NonExistent_ReturnsNotFound()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/model-providers/no-such-provider");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutProvider_CreatesNewProvider()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.PutAsJsonAsync("/api/model-providers/my-ollama", new
        {
            providerType = "ollama",
            displayName = "My Ollama",
            endpoint = "http://localhost:11434",
            model = "gemma4:e2b"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var provider = await response.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);
        provider.Should().NotBeNull();
        provider!.Name.Should().Be("my-ollama");
        provider.ProviderType.Should().Be("ollama");
        provider.Model.Should().Be("gemma4:e2b");
    }

    [Fact]
    public async Task PutProvider_UpdatesExistingProvider()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        // Create
        await client.PutAsJsonAsync("/api/model-providers/updatable", new
        {
            providerType = "ollama",
            displayName = "Original",
            model = "v1"
        });

        // Update
        var response = await client.PutAsJsonAsync("/api/model-providers/updatable", new
        {
            providerType = "ollama",
            displayName = "Updated",
            model = "v2"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var provider = await response.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);
        provider!.DisplayName.Should().Be("Updated");
        provider.Model.Should().Be("v2");
    }

    [Fact]
    public async Task PutProvider_PreservesApiKey_WhenNotProvided()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        // Create with API key
        await client.PutAsJsonAsync("/api/model-providers/keyed", new
        {
            providerType = "azure-openai",
            displayName = "Azure Keyed",
            model = "gpt-4o",
            apiKey = "secret-key-123"
        });

        // Update without API key
        await client.PutAsJsonAsync("/api/model-providers/keyed", new
        {
            providerType = "azure-openai",
            displayName = "Azure Keyed Updated",
            model = "gpt-4o-mini"
        });

        // Verify key preserved by checking hasApiKey in response
        var response = await client.GetAsync("/api/model-providers/keyed");
        var provider = await response.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);
        provider!.HasApiKey.Should().BeTrue("API key should be preserved when not provided in update");
        provider.DisplayName.Should().Be("Azure Keyed Updated");
    }

    [Fact]
    public async Task DeleteProvider_RemovesProvider()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        await client.PutAsJsonAsync("/api/model-providers/deletable", new
        {
            providerType = "ollama",
            displayName = "Delete Me",
            model = "llama3"
        });

        var deleteResponse = await client.DeleteAsync("/api/model-providers/deletable");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync("/api/model-providers/deletable");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProvider_NonExistent_ReturnsNoContent()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        var response = await client.DeleteAsync("/api/model-providers/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetList_MasksApiKey_ReturnsHasApiKeyFlag()
    {
        await using var app = await CreateTestAppAsync();
        using var client = app.GetTestClient();

        await client.PutAsJsonAsync("/api/model-providers/secret-provider", new
        {
            providerType = "azure-openai",
            displayName = "Secret Provider",
            model = "gpt-4o",
            apiKey = "super-secret-key"
        });

        var response = await client.GetAsync("/api/model-providers");
        var body = await response.Content.ReadAsStringAsync();

        // The response should contain hasApiKey=true but NOT the actual key
        body.Should().NotContain("super-secret-key");

        var providers = JsonSerializer.Deserialize<List<ModelProviderResponse>>(body, JsonOpts);
        var secretProvider = providers!.First(p => p.Name == "secret-provider");
        secretProvider.HasApiKey.Should().BeTrue();
    }

    // ── Test endpoint (Issue #120) ────────────────────────────────────────────

    [Fact]
    public async Task PostTest_NonExistentProvider_ReturnsNotFound()
    {
        var (app, _) = await CreateTestAppWithCapturingProviderAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            var response = await client.PostAsync("/api/model-providers/does-not-exist/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task PostTest_WithModelInDefinition_PassesModelToAgentProvider()
    {
        // Issue #120: the test endpoint must forward def.Model to the AgentProfile
        // so the Ollama provider uses it instead of its own configured default.
        var (app, capturer) = await CreateTestAppWithCapturingProviderAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama-model-test", new
            {
                providerType = "ollama",
                displayName = "Ollama Model Test",
                endpoint = "http://localhost:11434",
                model = "gemma4:e2b"
            });

            await client.PostAsync("/api/model-providers/ollama-model-test/test", null);
        }

        capturer.LastCapturedProfile.Should().NotBeNull("provider should have been called");
        capturer.LastCapturedProfile!.Model.Should().Be("gemma4:e2b",
            "def.Model must be forwarded to the test profile (fix for issue #120)");
    }

    [Fact]
    public async Task PostTest_ModelIsNotNull_WhenDefinitionHasModel()
    {
        // Regression: model must never arrive as null at the provider — null caused 404 from Ollama.
        var (app, capturer) = await CreateTestAppWithCapturingProviderAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama-nonnull", new
            {
                providerType = "ollama",
                model = "llama3.2"
            });

            await client.PostAsync("/api/model-providers/ollama-nonnull/test", null);
        }

        capturer.LastCapturedProfile.Should().NotBeNull();
        capturer.LastCapturedProfile!.Model.Should().NotBeNullOrEmpty(
            "null model causes Ollama 404; the provider definition model must always be forwarded");
    }

    [Fact]
    public async Task PostTest_ResponseIsOk_WithSuccessFalse_WhenProviderThrows()
    {
        // CapturingAgentProvider always throws after capturing — endpoint must handle gracefully.
        var (app, _) = await CreateTestAppWithCapturingProviderAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/ollama-throws", new
            {
                providerType = "ollama",
                model = "gemma4:e2b"
            });

            var response = await client.PostAsync("/api/model-providers/ollama-throws/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("message").GetString().Should().Contain("Test failed");
        }
    }

    [Fact]
    public async Task PostTest_WhenNoProviderRegisteredForType_ReturnsSuccessFalseWithMessage()
    {
        var (app, _) = await CreateTestAppWithCapturingProviderAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            // Register a provider with a type that has no matching IAgentProvider
            await client.PutAsJsonAsync("/api/model-providers/unknown-type-provider", new
            {
                providerType = "does-not-exist",
                model = "some-model"
            });

            var response = await client.PostAsync("/api/model-providers/unknown-type-provider/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("message").GetString().Should().Contain("does-not-exist");
        }
    }

    // ── Credential forwarding to provider (Issue #230) ───────────────────────

    [Fact]
    public async Task PostTest_ForFoundry_ForwardsStoredEndpointAndApiKeyToProvider()
    {
        // Issue #230: endpoint and API key saved via Model Providers UI must reach
        // FoundryAgentProvider.CreateChatClient so the guard "Set Endpoint and ApiKey" never fires.
        var (app, capturer) = await CreateTestAppWithCapturingProviderAsync("foundry");
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/foundry-issue230", new
            {
                providerType = "foundry",
                endpoint = "https://ai-foundry-testia-swc.services.ai.azure.com/api/projects/proj-default",
                model = "Phi-4-reasoning",
                apiKey = "synthetic-foundry-key-issue230"
            });

            await client.PostAsync("/api/model-providers/foundry-issue230/test", null);
        }

        capturer.LastCapturedProfile.Should().NotBeNull(
            "the provider's CreateChatClient must be invoked");
        capturer.LastCapturedProfile!.Endpoint.Should().Be(
            "https://ai-foundry-testia-swc.services.ai.azure.com/api/projects/proj-default",
            "stored Endpoint must be forwarded from the definition to the provider (Issue #230)");
        capturer.LastCapturedProfile.ApiKey.Should().Be(
            "synthetic-foundry-key-issue230",
            "stored ApiKey must be forwarded from the definition to the provider (Issue #230)");
    }

    [Fact]
    public async Task PostTest_ForAzureOpenAI_ForwardsStoredEndpointAndApiKeyToProvider()
    {
        // Issue #230: same forwarding guarantee for the Azure OpenAI provider.
        var (app, capturer) = await CreateTestAppWithCapturingProviderAsync("azure-openai");
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/azure-issue230", new
            {
                providerType = "azure-openai",
                endpoint = "https://ai-foundry-testia-swc.openai.azure.com/openai/v1",
                deploymentName = "gpt-5-mini",
                authMode = "api-key",
                apiKey = "synthetic-azure-key-issue230"
            });

            await client.PostAsync("/api/model-providers/azure-issue230/test", null);
        }

        capturer.LastCapturedProfile.Should().NotBeNull(
            "the provider's CreateChatClient must be invoked");
        capturer.LastCapturedProfile!.Endpoint.Should().Be(
            "https://ai-foundry-testia-swc.openai.azure.com/openai/v1",
            "stored Endpoint must be forwarded from the definition to the provider (Issue #230)");
        capturer.LastCapturedProfile.ApiKey.Should().Be(
            "synthetic-azure-key-issue230",
            "stored ApiKey must be forwarded from the definition to the provider (Issue #230)");
    }

    [Fact]
    public async Task PostTest_ForFoundry_WithNoCredentials_ReportsConfigurationError()
    {
        // Confirming the error surface: when the definition has no endpoint/apiKey
        // (e.g. the seeded default before the user saves credentials), the test
        // reports a "not configured" failure. Uses the real FoundryAgentProvider
        // with empty DI opts so the guard at CreateChatClient fires.
        var app = await CreateTestAppWithRealFoundryProviderAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            // Create a definition with NO endpoint or apiKey
            await client.PutAsJsonAsync("/api/model-providers/foundry-unconfigured", new
            {
                providerType = "foundry",
                model = "Phi-4-reasoning"
                // No endpoint, no apiKey — matches the seeded "foundry-default" state
            });

            var response = await client.PostAsync("/api/model-providers/foundry-unconfigured/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("message").GetString().Should().Contain("not configured",
                "the 'Foundry is not configured' guard message must surface to the caller");
        }
    }

    [Fact]
    public async Task PostTest_ForAzureOpenAI_WithNoApiKey_ReportsApiKeyError()
    {
        // Confirming the error surface for the Azure OpenAI path of Issue #230:
        // definition has an endpoint but no API key → clear "no API key" failure message.
        var app = await CreateTestAppWithRealAzureOpenAIProviderAsync();
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/azure-no-key", new
            {
                providerType = "azure-openai",
                endpoint = "https://ai-foundry-testia-swc.openai.azure.com/openai/v1",
                deploymentName = "gpt-5-mini",
                authMode = "api-key"
                // No apiKey — matches the condition that produces Issue #230's error
            });

            var response = await client.PostAsync("/api/model-providers/azure-no-key/test", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("message").GetString().Should().Contain("API key",
                "the 'no API key configured' guard message must surface to the caller");
        }
    }

    // ── Override / persistence regression tests ──────────────────────────────

    [Fact]
    public async Task PostTest_WithOverrides_ForwardsOverrideEndpointAndApiKeyToProvider()
    {
        // (a) Inline overrides must reach the provider — not the stored definition values.
        var (app, capturer) = await CreateTestAppWithCapturingProviderAsync("ollama");
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/override-reach-test", new
            {
                providerType = "ollama",
                model = "gemma4:e2b",
                endpoint = "http://stored-endpoint:11434",
                apiKey = "synthetic-stored-key"
            });

            await client.PostAsJsonAsync("/api/model-providers/override-reach-test/test", new
            {
                endpoint = "http://override-endpoint:9999",
                apiKey = "synthetic-override-key"
            });
        }

        capturer.LastCapturedProfile.Should().NotBeNull("provider must be called with overridden values");
        capturer.LastCapturedProfile!.Endpoint.Should().Be(
            "http://override-endpoint:9999",
            "override endpoint must reach the provider (not the stored value)");
        capturer.LastCapturedProfile.ApiKey.Should().Be(
            "synthetic-override-key",
            "override API key must reach the provider (not the stored value)");
    }

    [Fact]
    public async Task PostTest_WithOverrides_OverrideValuesNotPersistedOnFailure()
    {
        // (b-failure) On a failed test the override values must NOT be written to the store;
        // only test-result metadata (LastTestedAt, LastTestSucceeded) must be saved.
        // CapturingAgentProvider always throws → exercises failure path.
        var (app, _) = await CreateTestAppWithCapturingProviderAsync("ollama");
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/persist-check-fail", new
            {
                providerType = "ollama",
                model = "gemma4:e2b",
                endpoint = "http://stored-endpoint:11434"
            });

            await client.PostAsJsonAsync("/api/model-providers/persist-check-fail/test", new
            {
                endpoint = "http://ephemeral-endpoint:9999",
                apiKey = "synthetic-ephemeral-key"
            });

            var getResponse = await client.GetAsync("/api/model-providers/persist-check-fail");
            var provider = await getResponse.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);

            provider!.Endpoint.Should().Be(
                "http://stored-endpoint:11434",
                "override endpoint must NOT be persisted to the store after a failed test");
            provider.HasApiKey.Should().BeFalse(
                "ephemeral API key override must NOT be persisted to the store after a failed test");
            provider.LastTestSucceeded.Should().BeFalse(
                "test-result flag (LastTestSucceeded) must still be persisted on failure");
            provider.LastTestedAt.Should().NotBeNull(
                "test-result timestamp (LastTestedAt) must still be persisted on failure");
        }
    }

    [Fact]
    public async Task PostTest_WithOverrides_OverrideValuesNotPersistedOnSuccess()
    {
        // (b-success) On a successful test the override values must NOT be written to the store;
        // only test-result metadata must be saved. Uses SucceedingCapturingAgentProvider so
        // the test endpoint reaches the success branch.
        var (app, _) = await CreateTestAppWithSucceedingProviderAsync("ollama");
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/persist-check-success", new
            {
                providerType = "ollama",
                model = "gemma4:e2b",
                endpoint = "http://stored-endpoint:11434"
            });

            var testResponse = await client.PostAsJsonAsync(
                "/api/model-providers/persist-check-success/test",
                new { endpoint = "http://ephemeral-endpoint:9999", apiKey = "synthetic-ephemeral-key" });
            var testBody = await testResponse.Content.ReadAsStringAsync();
            var testResult = JsonSerializer.Deserialize<JsonElement>(testBody, JsonOpts);
            testResult.GetProperty("success").GetBoolean().Should().BeTrue(
                "SucceedingCapturingAgentProvider must produce success=true to exercise the success path");

            var getResponse = await client.GetAsync("/api/model-providers/persist-check-success");
            var provider = await getResponse.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);

            provider!.Endpoint.Should().Be(
                "http://stored-endpoint:11434",
                "override endpoint must NOT be persisted to the store after a successful test");
            provider.HasApiKey.Should().BeFalse(
                "ephemeral API key override must NOT be persisted to the store after a successful test");
            provider.LastTestSucceeded.Should().BeTrue(
                "test-result flag (LastTestSucceeded=true) must be persisted on success");
            provider.LastTestedAt.Should().NotBeNull(
                "test-result timestamp (LastTestedAt) must be persisted on success");
        }
    }

    [Fact]
    public async Task PostTest_WithVaultBackedSentinel_PreservesStoredVaultReference()
    {
        // (c) When the UI sends "[vault-backed]" as the ApiKey override, the stored vault://
        // reference must not be downgraded. This is the exact regression exposed by the rejected
        // implementation: it would write "[vault-backed]" over "vault://...", breaking resolution.
        var (app, _) = await CreateTestAppWithCapturingProviderAsync("azure-openai");
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/vault-ref-provider", new
            {
                providerType = "azure-openai",
                model = "gpt-4o",
                endpoint = "https://ai-foundry-testia.openai.azure.com/openai/v1",
                apiKey = "vault://openclawnet-synthetic-secret"
            });

            // Confirm vault reference is visible as [vault-backed] before the test
            var beforeGet = await client.GetAsync("/api/model-providers/vault-ref-provider");
            var before = await beforeGet.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);
            before!.ApiKey.Should().Be("[vault-backed]",
                "stored vault:// reference must render as [vault-backed] before the test");

            // POST test with the UI sentinel — must not overwrite the stored vault reference
            await client.PostAsJsonAsync("/api/model-providers/vault-ref-provider/test", new
            {
                apiKey = "[vault-backed]"
            });

            var afterGet = await client.GetAsync("/api/model-providers/vault-ref-provider");
            var after = await afterGet.Content.ReadFromJsonAsync<ModelProviderResponse>(JsonOpts);
            after!.ApiKey.Should().Be("[vault-backed]",
                "vault:// reference must not be replaced by the [vault-backed] sentinel after test");
            after.HasApiKey.Should().BeTrue(
                "HasApiKey must remain true — vault reference must survive the test call");
        }
    }

    [Fact]
    public async Task PostTest_NoBody_UsesStoredValues_AndListViewUnaffected()
    {
        // (d) When no override body is supplied, stored definition values reach the provider
        // unchanged, and the GET list endpoint still returns the provider record.
        var (app, capturer) = await CreateTestAppWithCapturingProviderAsync("ollama");
        await using (app)
        {
            using var client = app.GetTestClient();

            await client.PutAsJsonAsync("/api/model-providers/no-body-test", new
            {
                providerType = "ollama",
                model = "phi4",
                endpoint = "http://stored-endpoint:11434"
            });

            // No JSON body — overrides parameter is null in the endpoint handler
            await client.PostAsync("/api/model-providers/no-body-test/test", null);

            capturer.LastCapturedProfile.Should().NotBeNull(
                "provider must still be called when no override body is supplied");
            capturer.LastCapturedProfile!.Endpoint.Should().Be("http://stored-endpoint:11434",
                "stored endpoint must reach the provider when no overrides are provided");
            capturer.LastCapturedProfile.Model.Should().Be("phi4",
                "stored model must reach the provider when no overrides are provided");

            // GET list view must continue to include the provider after a test call
            var listResponse = await client.GetAsync("/api/model-providers");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var providers = await listResponse.Content.ReadFromJsonAsync<List<ModelProviderResponse>>(JsonOpts);
            providers!.Select(p => p.Name).Should().Contain("no-body-test",
                "GET list must include the provider after calling the test endpoint");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<WebApplication> CreateTestAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-mpe-" + Guid.NewGuid()));
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

        // Mock IAgentProvider for test endpoint (not testing actual connectivity)
        var mockProvider = new Mock<IAgentProvider>();
        mockProvider.Setup(p => p.ProviderName).Returns("ollama");
        mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        builder.Services.AddSingleton<IAgentProvider>(mockProvider.Object);

        var app = builder.Build();
        app.MapModelProviderEndpoints();
        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Creates a test app wired with a <see cref="CapturingAgentProvider"/> so tests
    /// can verify which <see cref="AgentProfile"/> (and specifically which model) was
    /// forwarded to the provider's <c>CreateChatClient</c>.
    /// </summary>
    private static async Task<(WebApplication app, CapturingAgentProvider capturer)>
        CreateTestAppWithCapturingProviderAsync(string providerType = "ollama")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-mpe-cap-" + Guid.NewGuid()));
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

        var capturer = new CapturingAgentProvider(providerType);
        builder.Services.AddSingleton<IAgentProvider>(capturer);

        var app = builder.Build();
        app.MapModelProviderEndpoints();
        await app.StartAsync();
        return (app, capturer);
    }

    /// <summary>
    /// Creates a test app wired with a <see cref="SucceedingCapturingAgentProvider"/> so tests
    /// can exercise the success path of the test endpoint and verify that only test-result
    /// metadata is persisted (not the override values).
    /// </summary>
    private static async Task<(WebApplication app, SucceedingCapturingAgentProvider capturer)>
        CreateTestAppWithSucceedingProviderAsync(string providerType = "ollama")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-mpe-succ-" + Guid.NewGuid()));
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

        var capturer = new SucceedingCapturingAgentProvider(providerType);
        builder.Services.AddSingleton<IAgentProvider>(capturer);

        var app = builder.Build();
        app.MapModelProviderEndpoints();
        await app.StartAsync();
        return (app, capturer);
    }

    /// <summary>
    /// Creates a test app wired with the real <see cref="FoundryAgentProvider"/> and empty DI options,
    /// so tests can verify that the guard "Foundry is not configured" fires when credentials are missing.
    /// </summary>
    private static async Task<WebApplication> CreateTestAppWithRealFoundryProviderAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-mpe-real-foundry-" + Guid.NewGuid()));
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var foundryProvider = new FoundryAgentProvider(
            Options.Create(new FoundryOptions()), // empty opts — credentials come from profile
            mockFactory.Object,
            NullLoggerFactory.Instance,
            NullLogger<FoundryAgentProvider>.Instance);
        builder.Services.AddSingleton<IAgentProvider>(foundryProvider);

        var app = builder.Build();
        app.MapModelProviderEndpoints();
        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Creates a test app wired with the real <see cref="AzureOpenAIAgentProvider"/> and empty DI options,
    /// so tests can verify the "no API key configured" guard fires when API key is missing.
    /// </summary>
    private static async Task<WebApplication> CreateTestAppWithRealAzureOpenAIProviderAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseTestServer();

        builder.Services.AddDbContextFactory<OpenClawDbContext>(o =>
            o.UseInMemoryDatabase("test-mpe-real-azure-" + Guid.NewGuid()));
        builder.Services.AddScoped<IModelProviderDefinitionStore, ModelProviderDefinitionStore>();

        var fakeVault = new FakeVault();
        var configResolver = new VaultConfigurationResolver(TimeProvider.System, TimeSpan.FromMinutes(5));
        var vaultResolver = new RuntimeVaultResolver(fakeVault, configResolver, NullLogger<RuntimeVaultResolver>.Instance);

        var azureProvider = new AzureOpenAIAgentProvider(
            Options.Create(new AzureOpenAIOptions()), // empty opts — credentials come from profile
            vaultResolver,
            NullLogger<AzureOpenAIAgentProvider>.Instance);
        builder.Services.AddSingleton<IAgentProvider>(azureProvider);

        var app = builder.Build();
        app.MapModelProviderEndpoints();
        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Fake IVault used by the AzureOpenAI real-provider test helpers.
    /// </summary>
    private sealed class FakeVault : IVault
    {
        public Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Fake IAgentProvider that records the last profile it received and then throws,
    /// allowing tests to assert on the profile's model without needing a real LLM.
    /// The throw is handled by the endpoint's catch-all, which returns 200 success=false.
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

        public Microsoft.Extensions.AI.IChatClient CreateChatClient(AgentProfile profile)
        {
            LastCapturedProfile = profile;
            return new AlwaysSucceedingChatClient();
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }

    /// <summary>Stub IChatClient that always returns a trivial successful one-word response.</summary>
    private sealed class AlwaysSucceedingChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public Microsoft.Extensions.AI.ChatClientMetadata Metadata { get; } =
            new("stub", null);

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                [new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Streaming not needed for test stubs.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
