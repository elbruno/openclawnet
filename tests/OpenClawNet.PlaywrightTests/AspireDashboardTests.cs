using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// E2E tests for Aspire dashboard reachability — covers aspire-stack demo 01 and 07.
/// Validates that the Aspire dashboard and web UI are accessible.
/// </summary>
[Collection("AppHost")]
public class AspireDashboardTests : PlaywrightTestBase
{
    public AspireDashboardTests(AppHostFixture fixture) : base(fixture)
    {
    }

    // ── Demo 01: Aspire Dashboard Reachability ────────────────────────────────

    [Fact]
    public async Task Dashboard_WebAppHomePage_IsReachable()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // The web app should be reachable — this validates the Aspire stack is running
            var response = await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            Assert.NotNull(response);
            Assert.True(response.Ok, $"Web app returned {response.Status}");
        });
    }

    [Fact]
    public async Task Dashboard_GatewayHealth_IsAccessibleFromWeb()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Verify the gateway /health endpoint is reachable via the web app's gateway reference
            using var client = Fixture.CreateGatewayHttpClient();
            var response = await client.GetAsync("/health");

            Assert.True(response.IsSuccessStatusCode,
                $"Gateway health returned {response.StatusCode}");
        });
    }

    // ── Demo 07: Web UI reflects chat interactions ────────────────────────────

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task WebUI_AfterChatInteraction_SessionsListUpdates()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Create a chat session via API
            using var client = Fixture.CreateGatewayHttpClient();
            client.Timeout = TimeSpan.FromMinutes(3);

            var sessionResp = await client.PostAsJsonAsync("/api/sessions",
                new { title = "Dashboard Verify Session" });
            sessionResp.EnsureSuccessStatusCode();

            // Navigate to sessions page — the new session should appear
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/sessions", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Look for the session title in the MudDataGrid — scope to MudDataGrid rows or session links
            var sessionItem = Page.Locator("a:has-text('Dashboard Verify Session'), [data-testid*='session-row']:has-text('Dashboard Verify Session'), .mud-table-row:has-text('Dashboard Verify Session')").First;
            await Assertions.Expect(sessionItem).ToBeVisibleAsync(new() { Timeout = 15_000 });
        });
    }
}
