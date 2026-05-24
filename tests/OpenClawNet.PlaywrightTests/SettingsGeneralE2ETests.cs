using System.Net.Http.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

[Collection("AppHost")]
public sealed class SettingsGeneralE2ETests : PlaywrightTestBase
{
    public SettingsGeneralE2ETests(AppHostFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task SettingsPage_Loads_AllGeneralCardsVisible()
    {
        await WithScreenshotOnFailure(async () =>
        {
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/settings", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            await Assertions.Expect(Page).ToHaveTitleAsync(
                new System.Text.RegularExpressions.Regex("Settings"),
                new PageAssertionsToHaveTitleOptions { Timeout = 10_000 });

            await Assertions.Expect(Page.Locator(".card:has-text('Scheduler Settings')")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(Page.Locator(".card:has-text('Tool Execution Logging')")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(Page.Locator(".card:has-text('Storage Location')")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        });
    }

    [SkippableFact]
    public async Task SettingsPage_SaveStorageLocation_SavesAndRestoresConfiguration()
    {
        var settingsPath = GetGatewayAppSettingsPath();
        var originalJson = await File.ReadAllTextAsync(settingsPath);
        var driveRoot = Path.GetPathRoot(Path.GetTempPath()) ?? "C:\\";
        var targetPath = Path.Combine(driveRoot, "openclawnet-storage-e2e-ui");

        try
        {
            await WithScreenshotOnFailure(async () =>
            {
                await Page.GotoAsync($"{Fixture.WebBaseUrl}/settings", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60_000
                });

                await Assertions.Expect(Page.Locator(".card:has-text('Storage Location')")).ToBeVisibleAsync(new() { Timeout = 10_000 });

                var input = Page.Locator(".card:has-text('Storage Location') input[type='text']").Last;
                await input.FillAsync(targetPath);

                await Page.GetByRole(AriaRole.Button, new() { Name = "Save Storage Location" }).ClickAsync();

                await Assertions.Expect(Page.Locator(".card:has-text('Storage Location') .badge.bg-success")).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await Assertions.Expect(Page.GetByText("Restart required to apply changes")).ToBeVisibleAsync(new() { Timeout = 10_000 });

                var updatedJson = await File.ReadAllTextAsync(settingsPath);
                Assert.Contains(targetPath, updatedJson, StringComparison.OrdinalIgnoreCase);
            });
        }
        finally
        {
            await File.WriteAllTextAsync(settingsPath, originalJson);
        }
    }

    private static string GetGatewayAppSettingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "OpenClawNet.Gateway", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate src/OpenClawNet.Gateway/appsettings.json");
    }
}
