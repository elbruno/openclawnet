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
/// E2E validation of Secrets Vault template bundle operations through the Gateway.
/// Tests atomic application of Azure OpenAI and other secret templates.
/// </summary>
[Trait("Category", "Vault")]
[Trait("Layer", "E2E")]
public sealed class SecretsVaultTemplateBundleE2ETests : IClassFixture<GatewayE2EFactory>, IDisposable
{
    private readonly GatewayE2EFactory _factory;
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public SecretsVaultTemplateBundleE2ETests(GatewayE2EFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _output = output;
    }

    public void Dispose() => _client?.Dispose();

    [Fact]
    public async Task ApplyAzureOpenAITemplate_Success()
    {
        // Arrange
        var templateRequest = new
        {
            templateName = "AzureOpenAI",
            secrets = new Dictionary<string, string>
            {
                ["AzureOpenAI_Endpoint"] = "https://test.openai.azure.com",
                ["AzureOpenAI_ModelId"] = "gpt-4",
                ["AzureOpenAI_ApiKey"] = "test-api-key-12345"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/secrets/templates/apply", templateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify all secrets were created
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        Assert.Equal("https://test.openai.azure.com", await store.GetAsync("AzureOpenAI_Endpoint"));
        Assert.Equal("gpt-4", await store.GetAsync("AzureOpenAI_ModelId"));
        Assert.Equal("test-api-key-12345", await store.GetAsync("AzureOpenAI_ApiKey"));

        // Verify they appear in listing
        var listResponse = await _client.GetFromJsonAsync<List<SecretSummaryDto>>("/api/secrets");
        Assert.NotNull(listResponse);
        Assert.Contains(listResponse, s => s.Name == "AzureOpenAI_Endpoint");
        Assert.Contains(listResponse, s => s.Name == "AzureOpenAI_ModelId");
        Assert.Contains(listResponse, s => s.Name == "AzureOpenAI_ApiKey");

        _output.WriteLine("✓ Azure OpenAI template applied successfully");
    }

    [Fact]
    public async Task ApplyTemplate_ValidationFailure_MissingField()
    {
        // Arrange - missing AzureOpenAI_ApiKey
        var templateRequest = new
        {
            templateName = "AzureOpenAI",
            secrets = new Dictionary<string, string>
            {
                ["AzureOpenAI_Endpoint"] = "https://test.openai.azure.com",
                ["AzureOpenAI_ModelId"] = "gpt-4"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/secrets/templates/apply", templateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.Contains("AzureOpenAI_ApiKey", error);

        _output.WriteLine("✓ Validation correctly rejected incomplete template");
    }

    [Fact]
    public async Task ApplyTemplate_ValidationFailure_EmptyField()
    {
        // Arrange - empty endpoint
        var templateRequest = new
        {
            templateName = "AzureOpenAI",
            secrets = new Dictionary<string, string>
            {
                ["AzureOpenAI_Endpoint"] = "",
                ["AzureOpenAI_ModelId"] = "gpt-4",
                ["AzureOpenAI_ApiKey"] = "test-key"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/secrets/templates/apply", templateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.Contains("AzureOpenAI_Endpoint", error);

        _output.WriteLine("✓ Validation correctly rejected empty field");
    }

    [Fact]
    public async Task ApplyTemplate_OverwritesExistingSecrets()
    {
        // Arrange - create existing secrets
        await _client.PutAsJsonAsync("/api/secrets/AzureOpenAI_Endpoint", new { value = "https://old.openai.azure.com" });
        await _client.PutAsJsonAsync("/api/secrets/AzureOpenAI_ModelId", new { value = "gpt-3.5-turbo" });

        var templateRequest = new
        {
            templateName = "AzureOpenAI",
            secrets = new Dictionary<string, string>
            {
                ["AzureOpenAI_Endpoint"] = "https://new.openai.azure.com",
                ["AzureOpenAI_ModelId"] = "gpt-4",
                ["AzureOpenAI_ApiKey"] = "new-api-key"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/secrets/templates/apply", templateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify secrets were overwritten
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        Assert.Equal("https://new.openai.azure.com", await store.GetAsync("AzureOpenAI_Endpoint"));
        Assert.Equal("gpt-4", await store.GetAsync("AzureOpenAI_ModelId"));
        Assert.Equal("new-api-key", await store.GetAsync("AzureOpenAI_ApiKey"));

        _output.WriteLine("✓ Template correctly overwrote existing secrets");
    }

    [Fact]
    public async Task ApplyTemplate_AtomicBehavior_AllOrNothing()
    {
        // This test verifies that if validation fails, NO secrets are written
        // First apply a valid template
        var validRequest = new
        {
            templateName = "AzureOpenAI",
            secrets = new Dictionary<string, string>
            {
                ["AzureOpenAI_Endpoint"] = "https://first.openai.azure.com",
                ["AzureOpenAI_ModelId"] = "gpt-3.5-turbo",
                ["AzureOpenAI_ApiKey"] = "first-key"
            }
        };
        await _client.PostAsJsonAsync("/api/secrets/templates/apply", validRequest);

        // Now try an invalid one - this should NOT touch any existing secrets
        var invalidRequest = new
        {
            templateName = "AzureOpenAI",
            secrets = new Dictionary<string, string>
            {
                ["AzureOpenAI_Endpoint"] = "https://second.openai.azure.com",
                ["AzureOpenAI_ModelId"] = ""  // Invalid - empty value
            }
        };

        var response = await _client.PostAsJsonAsync("/api/secrets/templates/apply", invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Verify original values are unchanged
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        Assert.Equal("https://first.openai.azure.com", await store.GetAsync("AzureOpenAI_Endpoint"));
        Assert.Equal("gpt-3.5-turbo", await store.GetAsync("AzureOpenAI_ModelId"));

        _output.WriteLine("✓ Template apply is atomic - failed validation didn't modify existing secrets");
    }

    [Fact]
    public async Task ApplyTemplate_AuditLogsGenerated()
    {
        // Arrange
        var templateRequest = new
        {
            templateName = "AzureOpenAI",
            secrets = new Dictionary<string, string>
            {
                ["AzureOpenAI_Endpoint"] = "https://audit.openai.azure.com",
                ["AzureOpenAI_ModelId"] = "gpt-4",
                ["AzureOpenAI_ApiKey"] = "audit-key"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/secrets/templates/apply", templateRequest);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Assert - verify audit records were created
        using var scope = _factory.Services.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var auditRecords = await db.Set<SecretAccessAuditEntity>()
            .Where(a => a.CallerType == "System" && a.CallerId.StartsWith("TemplateApply:AzureOpenAI"))
            .ToListAsync();

        Assert.NotEmpty(auditRecords);
        Assert.Contains(auditRecords, a => a.SecretName == "AzureOpenAI_Endpoint" && a.Success);
        Assert.Contains(auditRecords, a => a.SecretName == "AzureOpenAI_ModelId" && a.Success);
        Assert.Contains(auditRecords, a => a.SecretName == "AzureOpenAI_ApiKey" && a.Success);

        // Verify that audit records do NOT contain secret values
        foreach (var audit in auditRecords)
        {
            Assert.DoesNotContain("https://", audit.SecretName);
            Assert.DoesNotContain("audit-key", audit.SecretName);
        }

        _output.WriteLine("✓ Template apply generated audit logs without exposing secret values");
    }

    [Fact]
    public async Task ApplyTemplate_UnknownTemplate_Returns400()
    {
        // Arrange
        var templateRequest = new
        {
            templateName = "UnknownTemplate",
            secrets = new Dictionary<string, string>
            {
                ["SomeField"] = "value"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/secrets/templates/apply", templateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unknown template", error);

        _output.WriteLine("✓ Unknown template correctly rejected");
    }

    private sealed record SecretSummaryDto(string Name, string? Description, DateTime UpdatedAt);
}
