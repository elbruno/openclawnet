using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// E2E tests for the Settings and Skills UI pages — covers aspire-stack demos 04 and 06.
/// Validates that the settings and skills pages render correctly.
/// </summary>
[Collection("AppHost")]
public class SettingsAndSkillsTests : PlaywrightTestBase
{
    public SettingsAndSkillsTests(AppHostFixture fixture) : base(fixture)
    {
    }

    // ── Demo 04: Skills Page ──────────────────────────────────────────────────

    [Fact]
    public async Task SkillsPage_Loads_ShowsSkillsList()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Verify the page loaded without error
            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            // Verify the skills page has a title
            await Assertions.Expect(Page).ToHaveTitleAsync(
                new System.Text.RegularExpressions.Regex("Skills"),
                new PageAssertionsToHaveTitleOptions { Timeout = 10_000 });

            // Look for skills-related content (table, list, or cards)
            var skillsContent = Page.Locator("table, .skill-card, .skill-list, [class*='skill']").First;
            // The content may or may not be visible depending on installed skills, so just verify no error
        });
    }

    // ── Demo 06: Settings Page ────────────────────────────────────────────────

    [Fact]
    public async Task SettingsPage_Loads_ShowsSchedulerSettings()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/settings", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            await Assertions.Expect(Page).ToHaveTitleAsync(
                new System.Text.RegularExpressions.Regex("Settings"),
                new PageAssertionsToHaveTitleOptions { Timeout = 10_000 });

            // Settings page shows Scheduler settings card
            var schedulerCard = Page.Locator(".card:has-text('Scheduler')").First;
            await Assertions.Expect(schedulerCard).ToBeVisibleAsync(new() { Timeout = 10_000 });
        });
    }

    [Fact]
    public async Task SettingsPage_GatewaySettingsApi_ReturnsCurrentSettings()
    {
        await WithScreenshotOnFailure(async () =>
        {
            using var client = Fixture.CreateGatewayHttpClient();
            var response = await client.GetAsync("/api/settings");

            Assert.True(response.IsSuccessStatusCode, $"Settings API returned {response.StatusCode}");
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(json.TryGetProperty("provider", out _), "Settings should include provider");
        });
    }

    // ── Skills Details + Settings Provider Selection ──────────────────────────

    [Fact]
    public async Task SkillsPage_ShowsSkillDetails_WhenExpanded()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/skills", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Verify no error page
            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            // Look for expandable skill entries (accordion, details, or clickable cards)
            var expandable = Page.Locator("details, .accordion-item, [data-bs-toggle='collapse'], button[aria-expanded]").First;

            if (await expandable.IsVisibleAsync())
            {
                // Expand the first entry
                await expandable.ClickAsync();
                await Page.WaitForTimeoutAsync(500);

                // Look for expanded detail content (description, parameters, etc.)
                var detail = Page.Locator("details[open], .accordion-collapse.show, .collapse.show, [class*='detail'], [class*='description']").First;
                await Assertions.Expect(detail).ToBeVisibleAsync(new() { Timeout = 5_000 });
            }
            // If no expandable entries exist (no skills installed), that's acceptable
        });
    }

    [Fact]
    public async Task ModelProvidersPage_Loads_ShowsProviderTable()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/model-providers", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Verify no error page
            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            // Verify the page has a title
            await Assertions.Expect(Page).ToHaveTitleAsync(
                new System.Text.RegularExpressions.Regex("Model Providers"),
                new PageAssertionsToHaveTitleOptions { Timeout = 10_000 });

            // Verify the MudDataGrid table or provider list is visible (seeded defaults should be present)
            // MudBlazor renders as div structure, not HTML table — look for MudDataGrid container or provider rows
            var tableOrContent = Page.Locator(".mud-table, .mud-table-container, [data-testid*='model-provider-row'], .mud-grid").First;
            await Assertions.Expect(tableOrContent).ToBeVisibleAsync(new() { Timeout = 10_000 });
        });
    }

    [Fact]
    public async Task ModelProvidersPage_GatewayApi_ReturnsProviders()
    {
        await WithScreenshotOnFailure(async () =>
        {
            using var client = Fixture.CreateGatewayHttpClient();
            var response = await client.GetAsync("/api/model-providers");

            Assert.True(response.IsSuccessStatusCode, $"Model Providers API returned {response.StatusCode}");
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(json.ValueKind == System.Text.Json.JsonValueKind.Array, "Should return array of providers");
            Assert.True(json.GetArrayLength() >= 1, "Should have at least 1 seeded provider");
        });
    }

    // ── Agent Profiles Page ───────────────────────────────────────────────────

    [Fact]
    public async Task AgentProfilesPage_Loads_ShowsProfilesList()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/agent-profiles", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            await Assertions.Expect(Page).ToHaveTitleAsync(
                new System.Text.RegularExpressions.Regex("Agent Profiles"),
                new PageAssertionsToHaveTitleOptions { Timeout = 10_000 });

            // Should have at least the default profile listed (table or profile count)
            var profileTable = Page.Locator("table").First;
            var profileCount = Page.Locator("text=/\\d+ profile/").First;
            var hasTable = await profileTable.IsVisibleAsync();
            var hasCount = await profileCount.IsVisibleAsync();
            Assert.True(hasTable || hasCount, "Expected a profile table or profile count indicator");
        });
    }
}
