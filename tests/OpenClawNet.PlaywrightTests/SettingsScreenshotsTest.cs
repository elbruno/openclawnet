using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Screenshot capture test for the Settings manual (10-settings.md).
/// Walks through the General/Settings page, Model Providers (with the add-provider
/// form for several provider types), and the Agent Profiles model picker.
/// </summary>
[Collection("AspireHost")]
[Trait("Category", "Screenshots")]
public class SettingsScreenshotsTest : AspireHostPlaywrightTestBase
{
    private readonly string _screenshotDir;

    public SettingsScreenshotsTest(AspireHostFixture fixture) : base(fixture)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _screenshotDir = Path.Combine(repoRoot, "docs", "manuals", "images", "10-settings");
        Directory.CreateDirectory(_screenshotDir);
    }

    [SkippableFact]
    public async Task CaptureSettingsScreenshots()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Step 1: Land on home so the nav menu is rendered
            await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });
            await Page.WaitForTimeoutAsync(2_000);

            // Step 2: General Settings page
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/settings", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });
            await Page.WaitForTimeoutAsync(2_000);
            await CaptureScreenshotAsync("01-general-page.png", "Settings → General page (scheduler runtime settings + system info)");

            // Step 3: Model Providers list
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/model-providers", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });
            await Page.WaitForTimeoutAsync(2_000);
            await CaptureScreenshotAsync("02-model-providers-list.png", "Model Providers list page");

            // Step 4: Add Provider form (default: Ollama)
            var addBtn = Page.Locator("button:has-text('Add Provider')").First;
            if (await addBtn.IsVisibleAsync())
            {
                await addBtn.ClickAsync();
                await Page.WaitForTimeoutAsync(1_500);
                await CaptureScreenshotAsync("03-provider-form-ollama.png", "Add Provider form — Ollama (default)");

                // Step 5: Switch to Azure OpenAI
                var typeSelect = Page.Locator("select.form-select").First;
                if (await typeSelect.IsVisibleAsync())
                {
                    await typeSelect.SelectOptionAsync(new SelectOptionValue { Value = "azure-openai" });
                    await Page.WaitForTimeoutAsync(1_000);
                    await CaptureScreenshotAsync("04-provider-form-azure-openai.png", "Add Provider form — Azure OpenAI fields");

                    // Step 6: GitHub Copilot
                    await typeSelect.SelectOptionAsync(new SelectOptionValue { Value = "github-copilot" });
                    await Page.WaitForTimeoutAsync(1_000);
                    await CaptureScreenshotAsync("05-provider-form-github-copilot.png", "Add Provider form — GitHub Copilot fields");

                    // Step 7: Foundry
                    await typeSelect.SelectOptionAsync(new SelectOptionValue { Value = "foundry" });
                    await Page.WaitForTimeoutAsync(1_000);
                    await CaptureScreenshotAsync("06-provider-form-foundry.png", "Add Provider form — Microsoft Foundry fields");
                }
            }

            // Step 8: Agent Profiles (where the model picker per profile lives)
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/agent-profiles", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });
            await Page.WaitForTimeoutAsync(2_000);
            await CaptureScreenshotAsync("07-agent-profiles-model-picker.png", "Agent Profiles page — choose model/provider per profile");
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
