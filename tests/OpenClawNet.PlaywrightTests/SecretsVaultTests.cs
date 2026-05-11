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
}
