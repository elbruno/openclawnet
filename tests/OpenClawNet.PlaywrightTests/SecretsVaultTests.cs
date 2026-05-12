using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

[Collection("AppHost")]
public sealed class SecretsVaultTests : PlaywrightTestBase
{
    public SecretsVaultTests(AppHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task SecretsVaultPage_SecretLifecycle_WorksEndToEnd()
    {
        var secretName = $"ui-secret-{Guid.NewGuid():N}";
        using var client = Fixture.CreateGatewayHttpClient();

        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/secrets-vault", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            await Page.GetByTestId("vault-name").FillAsync(secretName);
            await Page.GetByTestId("vault-value").FillAsync("phase-one");
            await Page.GetByTestId("vault-description").FillAsync("Playwright vault lifecycle");
            await Page.GetByTestId("vault-save").ClickAsync();

            var row = Page.GetByTestId($"vault-row-{secretName}");
            await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });

            await Page.GetByTestId($"vault-load-versions-{secretName}").ClickAsync();
            var versions = Page.GetByTestId($"vault-versions-{secretName}");
            await Assertions.Expect(versions).ToContainTextAsync("1", new() { Timeout = 20_000 });

            await Page.GetByTestId("vault-action-name").FillAsync(secretName);
            await Page.GetByTestId("vault-rotate-value").FillAsync("phase-two");
            await Page.GetByTestId("vault-rotate").ClickAsync();

            await Page.GetByTestId($"vault-load-versions-{secretName}").ClickAsync();
            await Assertions.Expect(versions).ToContainTextAsync("2", new() { Timeout = 20_000 });

            await Page.GetByTestId($"vault-delete-{secretName}").ClickAsync();
            await Assertions.Expect(row).Not.ToBeVisibleAsync(new() { Timeout = 20_000 });

            await Page.GetByTestId("vault-action-name").FillAsync(secretName);
            await Page.GetByTestId("vault-recover").ClickAsync();
            await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });

            await Page.GetByTestId("vault-audit-verify").ClickAsync();
            var success = Page.GetByTestId("vault-success");
            await Assertions.Expect(success).ToContainTextAsync("Audit chain is valid", new() { Timeout = 20_000 });
        });

        await client.DeleteAsync($"/api/secrets/{secretName}");
        using var purgeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/secrets/{secretName}/purge");
        purgeRequest.Headers.Add("X-Confirm-Purge", secretName);
        await client.SendAsync(purgeRequest);
    }

    [Fact]
    public async Task SecretsVaultPage_AzureOpenAITemplate_CreatesThreeSecrets()
    {
        using var client = Fixture.CreateGatewayHttpClient();

        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/secrets-vault", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            // Click the Azure OpenAI template button
            await Page.GetByTestId("vault-template-azureopenai").ClickAsync();

            // Fill in the template fields
            await Page.GetByTestId("vault-template-endpoint").FillAsync("https://test.openai.azure.com/");
            await Page.GetByTestId("vault-template-modelid").FillAsync("gpt-4");
            await Page.GetByTestId("vault-template-apikey").FillAsync("test-api-key-12345");

            // Save the template
            await Page.GetByTestId("vault-template-save").ClickAsync();

            // Verify success message appears
            var success = Page.GetByTestId("vault-success");
            await Assertions.Expect(success).ToContainTextAsync("Azure OpenAI secrets saved", new() { Timeout = 20_000 });

            // Verify all three secrets appear in the list
            var endpointRow = Page.GetByTestId("vault-row-AzureOpenAI_Endpoint");
            await Assertions.Expect(endpointRow).ToBeVisibleAsync(new() { Timeout = 20_000 });

            var modelRow = Page.GetByTestId("vault-row-AzureOpenAI_ModelId");
            await Assertions.Expect(modelRow).ToBeVisibleAsync(new() { Timeout = 20_000 });

            var apiKeyRow = Page.GetByTestId("vault-row-AzureOpenAI_ApiKey");
            await Assertions.Expect(apiKeyRow).ToBeVisibleAsync(new() { Timeout = 20_000 });
        });

        // Cleanup - delete the three secrets
        await client.DeleteAsync("/api/secrets/AzureOpenAI_Endpoint");
        await client.DeleteAsync("/api/secrets/AzureOpenAI_ModelId");
        await client.DeleteAsync("/api/secrets/AzureOpenAI_ApiKey");

        using var purgeRequest1 = new HttpRequestMessage(HttpMethod.Delete, "/api/secrets/AzureOpenAI_Endpoint/purge");
        purgeRequest1.Headers.Add("X-Confirm-Purge", "AzureOpenAI_Endpoint");
        await client.SendAsync(purgeRequest1);

        using var purgeRequest2 = new HttpRequestMessage(HttpMethod.Delete, "/api/secrets/AzureOpenAI_ModelId/purge");
        purgeRequest2.Headers.Add("X-Confirm-Purge", "AzureOpenAI_ModelId");
        await client.SendAsync(purgeRequest2);

        using var purgeRequest3 = new HttpRequestMessage(HttpMethod.Delete, "/api/secrets/AzureOpenAI_ApiKey/purge");
        purgeRequest3.Headers.Add("X-Confirm-Purge", "AzureOpenAI_ApiKey");
        await client.SendAsync(purgeRequest3);
    }
}
