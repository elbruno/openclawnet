using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// E2E tests that validate the Blazor web app is running and all navigation
/// menu items are accessible via the Aspire-hosted distributed application.
/// </summary>
[Collection("AppHost")]
public class BlazorNavigationTests : PlaywrightTestBase
{
    public BlazorNavigationTests(AppHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task WebApp_HomePage_LoadsSuccessfully()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Verify the brand name appears in the sidebar
            var brand = Page.Locator("a.navbar-brand");
            await Assertions.Expect(brand).ToBeVisibleAsync();
            await Assertions.Expect(brand).ToContainTextAsync("OpenClaw .NET");
        });
    }

    [Fact]
    public async Task WebApp_AllNavMenuItems_AreVisible()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            var nav = Page.Locator("nav.nav");
            await Assertions.Expect(nav).ToBeVisibleAsync();

            // All 10 menu items should be present (including Model Providers and Agent Profiles)
            string[] expectedMenuLabels = ["Chat", "Sessions", "Tools", "Tool Log", "Jobs", "Health", "Skills", "Model Providers", "Agent Profiles", "General"];

            foreach (var label in expectedMenuLabels)
            {
                var link = nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true });
                await Assertions.Expect(link).ToBeVisibleAsync();
            }
        });
    }

    [Theory]
    [InlineData("/", "Chat")]
    [InlineData("/sessions", "Sessions")]
    [InlineData("/tools", "Tools")]
    [InlineData("/tool-log", "Tool Execution Log")]
    [InlineData("/jobs", "Jobs")]
    [InlineData("/health", "Health")]
    [InlineData("/skills", "Skills")]
    [InlineData("/model-providers", "Model Providers")]
    [InlineData("/agent-profiles", "Agent Profiles")]
    [InlineData("/settings", "Settings")]
    public async Task NavigateTo_Page_RendersWithoutError(string path, string expectedTitleFragment)
    {
        await WithScreenshotOnFailure(async () =>
        {
            var url = $"{Fixture.WebBaseUrl}{path}";
            await Page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Page should not show the error page
            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });

            // Title should contain the expected fragment
            await Assertions.Expect(Page).ToHaveTitleAsync(
                new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(expectedTitleFragment)),
                new PageAssertionsToHaveTitleOptions { Timeout = 10_000 });

            // The sidebar brand should remain visible (no broken layout)
            var brand = Page.Locator("a.navbar-brand");
            await Assertions.Expect(brand).ToBeVisibleAsync();
        });
    }

    [Theory]
    [InlineData("Chat", "/")]
    [InlineData("Sessions", "/sessions")]
    [InlineData("Tools", "/tools")]
    [InlineData("Tool Log", "/tool-log")]
    [InlineData("Jobs", "/jobs")]
    [InlineData("Health", "/health")]
    [InlineData("Skills", "/skills")]
    [InlineData("Model Providers", "/model-providers")]
    [InlineData("Agent Profiles", "/agent-profiles")]
    [InlineData("General", "/settings")]
    public async Task ClickNavLink_NavigatesToCorrectPage(string menuLabel, string expectedPath)
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Start from the home page
            await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Click the nav link
            var nav = Page.Locator("nav.nav");
            var link = nav.GetByRole(AriaRole.Link, new() { Name = menuLabel, Exact = true });
            await link.ClickAsync();

            // Blazor enhanced nav — wait for the URL to update
            await Page.WaitForURLAsync($"**{expectedPath}",
                new PageWaitForURLOptions { Timeout = 15_000 });

            // Verify no error UI
            var errorHeading = Page.Locator("h1:has-text('Error')");
            await Assertions.Expect(errorHeading).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });
        });
    }
}
