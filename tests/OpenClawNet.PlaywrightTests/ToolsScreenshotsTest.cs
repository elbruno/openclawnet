using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Screenshot capture test for the Tools manual (20-tools.md).
/// Captures the Tools catalog page and a focused
/// shot of each individual tool card (file_system, shell, web_fetch, schedule).
/// </summary>
[Collection("AppHost")]
[Trait("Category", "Screenshots")]
public class ToolsScreenshotsTest : PlaywrightTestBase
{
    private readonly string _screenshotDir;

    public ToolsScreenshotsTest(AppHostFixture fixture) : base(fixture)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _screenshotDir = Path.Combine(repoRoot, "docs", "manuals", "images", "20-tools");
        Directory.CreateDirectory(_screenshotDir);
    }

    [Fact]
    public async Task CaptureToolsScreenshots()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Step 1: Home (warm up nav)
            await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });
            await Page.WaitForTimeoutAsync(2_000);

            // Step 2: Tools catalog page
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/tools", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });
            await Page.WaitForTimeoutAsync(2_000);
            await CaptureScreenshotAsync("01-tools-page.png", "Tools catalog — every tool the agent can call");

            // Step 3: Focused shots of each tool card
            string[] toolNames = { "file_system", "shell", "web_fetch", "schedule" };
            int idx = 2;
            foreach (var tool in toolNames)
            {
                var card = Page.Locator($".card:has(h5.card-title:text-is('{tool}'))").First;
                if (await card.IsVisibleAsync())
                {
                    var safe = tool.Replace("_", "-");
                    var path = Path.Combine(_screenshotDir, $"{idx:D2}-tool-{safe}.png");
                    await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
                    Console.WriteLine($"✓ Screenshot captured: {Path.GetFileName(path)} — {tool} tool card");
                    idx++;
                }
            }

            // Step 4 removed: Tool Log page was non-functional and has been removed
        });
    }

    private async Task CaptureScreenshotAsync(string filename, string description)
    {
        var fullPath = Path.Combine(_screenshotDir, filename);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true
        });
        Console.WriteLine($"✓ Screenshot captured: {filename} — {description}");
    }
}
