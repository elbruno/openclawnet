using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

[Collection("AppHost")]
public class SessionsDeleteConfirmationTests : PlaywrightTestBase
{
    public SessionsDeleteConfirmationTests(AppHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Sessions_SingleDelete_ShowsConfirmationBeforeDeleting()
    {
        await WithScreenshotOnFailure(async () =>
        {
            using var client = Fixture.CreateGatewayHttpClient();
            var title = $"Single delete {Guid.NewGuid():N}";
            var createResp = await client.PostAsJsonAsync("/api/sessions", new { title });
            createResp.EnsureSuccessStatusCode();

            var sessionJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var sessionId = sessionJson.GetProperty("id").GetString()!;

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/sessions", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var row = Page.Locator($"[data-testid='session-row-{sessionId}']");
            await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });

            await Page.Locator($"[data-testid='session-delete-{sessionId}']").ClickAsync();

            var modal = Page.Locator("[data-testid='session-delete-dialog']");
            await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 5_000 });
            await Assertions.Expect(row).ToBeVisibleAsync();

            await Page.Locator("[data-testid='session-delete-confirm']").ClickAsync();

            await Assertions.Expect(row).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        });
    }

    [Fact]
    public async Task Sessions_BulkDelete_ShowsConfirmationBeforeDeleting()
    {
        await WithScreenshotOnFailure(async () =>
        {
            using var client = Fixture.CreateGatewayHttpClient();
            var titles = new[]
            {
                $"Bulk delete A {Guid.NewGuid():N}",
                $"Bulk delete B {Guid.NewGuid():N}"
            };

            var ids = new List<string>();
            foreach (var title in titles)
            {
                var createResp = await client.PostAsJsonAsync("/api/sessions", new { title });
                createResp.EnsureSuccessStatusCode();
                var sessionJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
                ids.Add(sessionJson.GetProperty("id").GetString()!);
            }

            await Page.GotoAsync($"{Fixture.WebBaseUrl}/sessions", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            foreach (var id in ids)
            {
                await Assertions.Expect(Page.Locator($"[data-testid='session-row-{id}']")).ToBeVisibleAsync(new() { Timeout = 15_000 });
                await Page.Locator($"[data-testid='session-select-{id}']").CheckAsync();
            }

            await Page.Locator("[data-testid='sessions-delete-selected']").ClickAsync();

            var modal = Page.Locator("[data-testid='session-delete-dialog']");
            await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 5_000 });
            await Assertions.Expect(Page.Locator("[data-testid='session-delete-title']")).ToContainTextAsync("Delete 2 sessions");

            await Page.Locator("[data-testid='session-delete-confirm']").ClickAsync();

            foreach (var id in ids)
            {
                await Assertions.Expect(Page.Locator($"[data-testid='session-row-{id}']")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
            }
        });
    }
}
