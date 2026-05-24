using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Screenshot capture test for the Hello World manual (02-hello-world.md).
/// Drives the UI through the first-time user tutorial flow and captures screenshots
/// at each step for inclusion in the manual.
/// </summary>
[Collection("AppHost")]
[Trait("Category", "Screenshots")]
public class HelloWorldScreenshotsTest : PlaywrightTestBase
{
    private readonly string _screenshotDir;

    public HelloWorldScreenshotsTest(AppHostFixture fixture) : base(fixture)
    {
        // Save to docs/manuals/images/02-hello-world/ (relative to repo root)
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _screenshotDir = Path.Combine(repoRoot, "docs", "manuals", "images", "02-hello-world");
        Directory.CreateDirectory(_screenshotDir);
    }

    [SkippableFact]
    public async Task CaptureHelloWorldTutorialScreenshots()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Step 1: Home page tour
            await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });

            // Wait for the page to fully render
            await Page.WaitForTimeoutAsync(2_000);

            await CaptureScreenshotAsync("01-home-page.png", "Home page of OpenClaw .NET");

            // Step 2: Navigate to Settings (General)
            var settingsLink = Page.Locator("a.nav-link:has-text('General')");
            await settingsLink.ClickAsync();
            await Page.WaitForURLAsync("**/settings", new PageWaitForURLOptions { Timeout = 15_000 });
            await Page.WaitForTimeoutAsync(2_000);

            await CaptureScreenshotAsync("02-settings-page.png", "Settings page showing model provider configuration");

            // Step 3: Navigate to Agent Profiles
            var profilesLink = Page.Locator("a.nav-link:has-text('Agent Profiles')");
            await profilesLink.ClickAsync();
            await Page.WaitForURLAsync("**/agent-profiles", new PageWaitForURLOptions { Timeout = 15_000 });
            await Page.WaitForTimeoutAsync(2_000);

            await CaptureScreenshotAsync("03-agent-profiles-list.png", "Agent Profiles page before creating new profile");

            // Step 4: Click New Profile button (if it exists)
            var newProfileBtn = Page.Locator("button:has-text('New Profile'), a:has-text('New Profile'), button:has-text('Create')").First;
            
            if (await newProfileBtn.IsVisibleAsync())
            {
                await newProfileBtn.ClickAsync();
                await Page.WaitForTimeoutAsync(2_000);

                await CaptureScreenshotAsync("04-create-agent-profile-form.png", "Create Agent Profile form");

                // Fill in the form
                var nameInput = Page.Locator("input[id*='name'], input[name*='name'], input[placeholder*='Name']").First;
                if (await nameInput.IsVisibleAsync())
                {
                    await nameInput.FillAsync("Hello World Agent");
                }

                var instructionsInput = Page.Locator("textarea[id*='instruction'], textarea[name*='instruction'], textarea[placeholder*='instruction']").First;
                if (await instructionsInput.IsVisibleAsync())
                {
                    await instructionsInput.FillAsync("You are a friendly assistant who answers in a single sentence.");
                }

                await Page.WaitForTimeoutAsync(1_000);

                await CaptureScreenshotAsync("05-agent-profile-filled.png", "Agent Profile form filled in");

                // Save the profile (if Save button exists)
                var saveBtn = Page.Locator("button:has-text('Save'), button:has-text('Create'), button[type='submit']").First;
                if (await saveBtn.IsVisibleAsync())
                {
                    await saveBtn.ClickAsync();
                    await Page.WaitForTimeoutAsync(2_000);

                    await CaptureScreenshotAsync("06-agent-profiles-with-new.png", "Agent Profiles list with new profile");
                }
            }

            // Step 5: Navigate to Chat
            var chatLink = Page.Locator("a.nav-link:has-text('Chat')");
            await chatLink.ClickAsync();
            await Page.WaitForURLAsync("**/", new PageWaitForURLOptions { Timeout = 15_000 });
            await Page.WaitForTimeoutAsync(2_000);

            await CaptureScreenshotAsync("07-chat-page.png", "Chat page ready for first message");

            // Step 6: Send a test message (if model is available)
            var chatInput = Page.Locator("textarea, input[type='text']").Last;
            if (await chatInput.IsVisibleAsync())
            {
                await chatInput.FillAsync("Hello, who are you?");
                await Page.WaitForTimeoutAsync(500);

                await CaptureScreenshotAsync("08-chat-message-typed.png", "Chat with message typed");

                // Try to send the message
                var sendBtn = Page.Locator("button[type='submit'], button:has-text('Send')").First;
                if (await sendBtn.IsVisibleAsync())
                {
                    await sendBtn.ClickAsync();

                    // Wait a bit for response to start streaming
                    await Page.WaitForTimeoutAsync(3_000);

                    await CaptureScreenshotAsync("09-chat-response.png", "Chat showing streaming response");
                }
            }

            // Step 7: Navigate to Jobs page
            var jobsLink = Page.Locator("a.nav-link:has-text('Jobs')");
            await jobsLink.ClickAsync();
            await Page.WaitForURLAsync("**/jobs", new PageWaitForURLOptions { Timeout = 15_000 });
            await Page.WaitForTimeoutAsync(2_000);

            await CaptureScreenshotAsync("10-jobs-page.png", "Jobs page");

            // Step 8: Create a job (if UI supports it)
            var newJobBtn = Page.Locator("button:has-text('New Job'), a:has-text('New Job'), button:has-text('Create')").First;
            
            if (await newJobBtn.IsVisibleAsync())
            {
                await newJobBtn.ClickAsync();
                await Page.WaitForTimeoutAsync(2_000);

                await CaptureScreenshotAsync("11-create-job-form.png", "Create Job form");
            }
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
