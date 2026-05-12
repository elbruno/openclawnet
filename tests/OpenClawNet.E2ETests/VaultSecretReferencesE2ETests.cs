using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;
using Xunit.Abstractions;

namespace OpenClawNet.E2ETests;

/// <summary>
/// E2E validation of GitHub issue #151: vault secret references in model providers
/// and agent profiles. These tests exercise the real gateway HTTP surface and the
/// runtime provider/profile resolution paths rather than calling IVault directly.
/// </summary>
[Trait("Category", "Vault")]
[Trait("Category", "VaultReferences")]
[Trait("Layer", "E2E")]
[Trait("Issue", "151")]
public sealed class VaultSecretReferencesE2ETests : IClassFixture<GatewayE2EFactory>, IDisposable
{
    private readonly GatewayE2EFactory _factory;
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public VaultSecretReferencesE2ETests(GatewayE2EFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _output = output;
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task ModelProvider_AzureOpenAI_StoresVaultReferenceNotPlaintext()
    {
        var endpointSecretName = UniqueName("azure-openai-endpoint");
        var apiKeySecretName = UniqueName("azure-openai-apikey");
        var endpointValue = "https://test-openai.openai.azure.com/";
        var apiKeyValue = "sk-test-key-12345";
        var providerName = UniqueName("azure-openai-provider");

        await PutSecretAsync(endpointSecretName, endpointValue, "Issue 151 endpoint secret");
        await PutSecretAsync(apiKeySecretName, apiKeyValue, "Issue 151 API key secret");
        await PutModelProviderAsync(
            providerName,
            $"vault://{endpointSecretName}",
            $"vault://{apiKeySecretName}",
            "gpt-4o");

        await using var db = await CreateDbContextAsync();
        var stored = await db.ModelProviders.FindAsync(providerName);

        Assert.NotNull(stored);
        Assert.Equal($"vault://{endpointSecretName}", stored.Endpoint);
        Assert.Equal($"vault://{apiKeySecretName}", stored.ApiKey);
        Assert.DoesNotContain(endpointValue, stored.Endpoint);
        Assert.DoesNotContain(apiKeyValue, stored.ApiKey ?? string.Empty);
    }

    [SkippableFact]
    public async Task ModelProvider_AzureOpenAI_ResolvesVaultReferencesAtRuntime()
    {
        var (endpoint, apiKey, deployment, authMode) = RequireAzureOpenAi();
        var endpointSecretName = UniqueName("runtime-endpoint");
        var apiKeySecretName = UniqueName("runtime-apikey");
        var providerName = UniqueName("runtime-provider");

        await PutSecretAsync(endpointSecretName, endpoint!);
        await PutSecretAsync(apiKeySecretName, apiKey!);
        await PutModelProviderAsync(
            providerName,
            $"vault://{endpointSecretName}",
            $"vault://{apiKeySecretName}",
            deployment!,
            authMode);

        var response = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId = Guid.NewGuid(),
            message = "Reply with exactly one short word.",
            provider = providerName
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected chat success, got {response.StatusCode}: {body}");

        var payload = await response.Content.ReadFromJsonAsync<ChatResponse>();
        Assert.False(string.IsNullOrWhiteSpace(payload?.Content));

        _output.WriteLine($"Resolved provider '{providerName}' through /api/chat with runtime vault references.");
    }

    [Fact]
    public async Task ModelProvider_AzureOpenAI_FailsSafelyForMissingSecret()
    {
        var providerName = UniqueName("missing-provider");
        var missingSecretName = UniqueName("missing-endpoint");

        await PutModelProviderAsync(
            providerName,
            $"vault://{missingSecretName}",
            $"vault://{UniqueName("missing-apikey")}",
            "gpt-4o");

        var response = await _client.PostAsync($"/api/model-providers/{providerName}/test", content: null);
        var payload = await response.Content.ReadFromJsonAsync<TestInvocationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(payload.Success);
        Assert.Contains("Failed to resolve vault reference", payload.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"vault://{missingSecretName}", payload.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModelProvider_AzureOpenAI_FailsSafelyForDeletedSecret()
    {
        var secretName = UniqueName("deleted-endpoint");
        var providerName = UniqueName("deleted-provider");

        await PutSecretAsync(secretName, "https://deleted.openai.azure.com/");
        await PutModelProviderAsync(
            providerName,
            $"vault://{secretName}",
            "vault://unused-apikey",
            "gpt-4o");

        var deleteResponse = await _client.DeleteAsync($"/api/secrets/{secretName}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var response = await _client.PostAsync($"/api/model-providers/{providerName}/test", content: null);
        var payload = await response.Content.ReadFromJsonAsync<TestInvocationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(payload.Success);
        Assert.Contains("Failed to resolve vault reference", payload.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"vault://{secretName}", payload.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModelProvider_VaultReference_NoPlaintextInApiOrStorage()
    {
        var endpointSecretName = UniqueName("secure-endpoint");
        var apiKeySecretName = UniqueName("secure-apikey");
        var sensitiveApiKey = "super-secret-api-key-SHOULD-NOT-APPEAR";
        var providerName = UniqueName("secure-provider");

        await PutSecretAsync(endpointSecretName, "https://secure.openai.azure.com/");
        await PutSecretAsync(apiKeySecretName, sensitiveApiKey);
        await PutModelProviderAsync(
            providerName,
            $"vault://{endpointSecretName}",
            $"vault://{apiKeySecretName}",
            "gpt-4o");

        var getResponse = await _client.GetAsync($"/api/model-providers/{providerName}");
        var responseBody = await getResponse.Content.ReadAsStringAsync();

        Assert.True(getResponse.IsSuccessStatusCode, $"Expected provider fetch success, got {getResponse.StatusCode}: {responseBody}");
        Assert.Contains($"vault://{endpointSecretName}", responseBody);
        Assert.DoesNotContain(sensitiveApiKey, responseBody);
        Assert.DoesNotContain($"vault://{apiKeySecretName}", responseBody);

        await using var db = await CreateDbContextAsync();
        var stored = await db.ModelProviders.FindAsync(providerName);
        Assert.NotNull(stored);
        Assert.Equal($"vault://{apiKeySecretName}", stored.ApiKey);
        Assert.DoesNotContain(sensitiveApiKey, stored.ApiKey ?? string.Empty);
    }

    [Fact]
    public async Task AgentProfile_StoresProviderReferenceNotSecretValues()
    {
        var providerName = UniqueName("profile-provider");
        var profileName = UniqueName("profile");

        await PutModelProviderAsync(
            providerName,
            "https://profile-provider.openai.azure.com/",
            "vault://profile-provider-apikey",
            "gpt-4o");
        await PutAgentProfileAsync(profileName, providerName);

        await using var db = await CreateDbContextAsync();
        var stored = await db.AgentProfiles.FindAsync(profileName);

        Assert.NotNull(stored);
        Assert.Equal(providerName, stored.Provider);
        Assert.Null(stored.ApiKey);
        Assert.Null(stored.Endpoint);
        Assert.Null(stored.DeploymentName);
    }

    [SkippableFact]
    public async Task AgentProfile_UsesProviderVaultReferencesAtRuntime()
    {
        var (endpoint, apiKey, deployment, authMode) = RequireAzureOpenAi();
        var endpointSecretName = UniqueName("profile-runtime-endpoint");
        var apiKeySecretName = UniqueName("profile-runtime-apikey");
        var providerName = UniqueName("profile-runtime-provider");
        var profileName = UniqueName("profile-runtime");

        await PutSecretAsync(endpointSecretName, endpoint!);
        await PutSecretAsync(apiKeySecretName, apiKey!);
        await PutModelProviderAsync(
            providerName,
            $"vault://{endpointSecretName}",
            $"vault://{apiKeySecretName}",
            deployment!,
            authMode);
        await PutAgentProfileAsync(profileName, providerName);

        var response = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId = Guid.NewGuid(),
            message = "Reply with exactly one short word.",
            agentProfileName = profileName
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Expected chat success, got {response.StatusCode}: {body}");

        var payload = await response.Content.ReadFromJsonAsync<ChatResponse>();
        Assert.False(string.IsNullOrWhiteSpace(payload?.Content));

        _output.WriteLine($"Resolved agent profile '{profileName}' through /api/chat using provider '{providerName}'.");
    }

    [Fact]
    public async Task AgentProfile_FailsSafelyForMissingSecret()
    {
        var providerName = UniqueName("profile-missing-provider");
        var profileName = UniqueName("profile-missing");
        var missingSecretName = UniqueName("profile-missing-endpoint");

        await PutModelProviderAsync(
            providerName,
            $"vault://{missingSecretName}",
            $"vault://{UniqueName("profile-missing-apikey")}",
            "gpt-4o");
        await PutAgentProfileAsync(profileName, providerName);

        var response = await _client.PostAsync($"/api/agent-profiles/{profileName}/test", content: null);
        var payload = await response.Content.ReadFromJsonAsync<TestInvocationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(payload.Success);
        Assert.Contains("Failed to resolve vault reference", payload.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"vault://{missingSecretName}", payload.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentProfile_VaultReference_NoPlaintextInResponse()
    {
        var providerName = UniqueName("profile-secure-provider");
        var profileName = UniqueName("profile-secure");
        var secretName = UniqueName("profile-secure-apikey");
        var sensitiveValue = "agent-secret-SHOULD-NOT-LEAK";

        await PutSecretAsync(secretName, sensitiveValue);
        await PutModelProviderAsync(
            providerName,
            "https://profile-secure.openai.azure.com/",
            $"vault://{secretName}",
            "gpt-4o");
        await PutAgentProfileAsync(profileName, providerName);

        var getResponse = await _client.GetAsync($"/api/agent-profiles/{profileName}");
        var responseBody = await getResponse.Content.ReadAsStringAsync();

        Assert.True(getResponse.IsSuccessStatusCode, $"Expected profile fetch success, got {getResponse.StatusCode}: {responseBody}");
        Assert.Contains(providerName, responseBody);
        Assert.DoesNotContain(sensitiveValue, responseBody);
        Assert.DoesNotContain($"vault://{secretName}", responseBody);
    }

    [Fact]
    public async Task VaultReference_CacheInvalidatedOnSecretRotation()
    {
        var secretName = UniqueName("cache-secret");
        var initialValue = "initial-value";
        var rotatedValue = "rotated-value";

        await PutSecretAsync(secretName, initialValue);

        using var scope = _factory.Services.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();

        var firstResolution = await vault.ResolveAsync(
            secretName,
            new VaultCallerContext(VaultCallerType.Configuration, "CacheTest", null));
        Assert.Equal(initialValue, firstResolution);

        var rotateResponse = await _client.PostAsJsonAsync($"/api/secrets/{secretName}/rotate", new { newValue = rotatedValue });
        Assert.Equal(HttpStatusCode.NoContent, rotateResponse.StatusCode);

        await Task.Delay(100);

        var secondResolution = await vault.ResolveAsync(
            secretName,
            new VaultCallerContext(VaultCallerType.Configuration, "CacheTest", null));

        Assert.Equal(rotatedValue, secondResolution);
    }

    private (string Endpoint, string ApiKey, string Deployment, string? AuthMode) RequireAzureOpenAi()
    {
        Skip.IfNot(E2EEnvironment.HasAzureOpenAi, E2EEnvironment.SkipReason);

        var (endpoint, apiKey, deployment, authMode) = E2EEnvironment.ReadAzureOpenAi();
        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        Assert.False(string.IsNullOrWhiteSpace(deployment));

        return (endpoint!, apiKey!, deployment!, authMode);
    }

    private async Task PutSecretAsync(string name, string value, string? description = null)
    {
        var response = await _client.PutAsJsonAsync($"/api/secrets/{name}", new { value, description });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task PutModelProviderAsync(
        string name,
        string endpoint,
        string apiKey,
        string deploymentName,
        string? authMode = "api-key")
    {
        var response = await _client.PutAsJsonAsync($"/api/model-providers/{name}", new
        {
            providerType = "azure-openai",
            displayName = name,
            endpoint,
            apiKey,
            deploymentName,
            authMode
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected provider upsert success, got {response.StatusCode}: {body}");
    }

    private async Task PutAgentProfileAsync(string name, string provider)
    {
        var response = await _client.PutAsJsonAsync($"/api/agent-profiles/{name}", new
        {
            displayName = name,
            provider,
            instructions = "Issue 151 agent profile test",
            isDefault = false,
            requireToolApproval = true,
            isEnabled = true,
            kind = "Standard"
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected profile upsert success, got {response.StatusCode}: {body}");
    }

    private async Task<OpenClawDbContext> CreateDbContextAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private sealed record ChatResponse(string? Content);
    private sealed record TestInvocationResponse(bool Success, string? Message);
}
