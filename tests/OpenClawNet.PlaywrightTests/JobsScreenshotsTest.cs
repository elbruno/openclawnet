using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Screenshot capture test for the Jobs manual (30-jobs.md).
/// Captures the Jobs list (with demo templates), the Create Job form,
/// the form filled with a cron expression, and the form switched to a one-shot trigger.
/// </summary>
[Collection("AspireHost")]
[Trait("Category", "Screenshots")]
public class JobsScreenshotsTest : AspireHostPlaywrightTestBase
{
    private readonly string _screenshotDir;

    public JobsScreenshotsTest(AspireHostFixture fixture) : base(fixture)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _screenshotDir = Path.Combine(repoRoot, "docs", "manuals", "images", "30-jobs");
        Directory.CreateDirectory(_screenshotDir);
    }

    [SkippableFact]
    public async Task CaptureJobsScreenshots()
    {
        await WithScreenshotOnFailure(async () =>
        {
            // Step 1: Home (warm up)
            await Page.GotoAsync(Fixture.WebBaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            });
            await Page.WaitForTimeoutAsync(2_000);

            // Step 2: Jobs list page (includes demo templates section)
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/jobs", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });
            await Page.WaitForTimeoutAsync(2_500);
            await CaptureScreenshotAsync("01-jobs-list.png", "Scheduled Jobs list page (with demo templates)");

            // Step 3: Create Job form (Manual trigger by default)
            var newJobLink = Page.Locator("a:has-text('+ New Job')").First;
            if (await newJobLink.IsVisibleAsync())
            {
                await newJobLink.ClickAsync();
            }
            else
            {
                await Page.GotoAsync($"{Fixture.WebBaseUrl}/jobs/new", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30_000
                });
            }
            await Page.WaitForTimeoutAsync(2_000);
            await CaptureScreenshotAsync("02-create-job-form.png", "Create Job form — Manual trigger (default)");

            // Step 4: Fill name + prompt then switch to Cron trigger
            var nameInput = Page.Locator("input.form-control[placeholder*='Daily standup']").First;
            if (await nameInput.IsVisibleAsync())
            {
                await nameInput.FillAsync("Morning Standup Summary");
            }
            var promptInput = Page.Locator("textarea.form-control").First;
            if (await promptInput.IsVisibleAsync())
            {
                await promptInput.FillAsync("Summarize yesterday's git commits and write the result to notes/standup-{yyyy-MM-dd}.md");
            }

            // Trigger Type select — second .form-select (Agent Profile is first)
            var triggerSelect = Page.Locator("select.form-select").Nth(1);
            if (await triggerSelect.IsVisibleAsync())
            {
                await triggerSelect.SelectOptionAsync(new SelectOptionValue { Value = "Cron" });
                await Page.WaitForTimeoutAsync(1_000);

                var cronInput = Page.Locator("input.form-control[placeholder='0 9 * * 1-5']").First;
                if (await cronInput.IsVisibleAsync())
                {
                    await cronInput.FillAsync("0 9 * * 1-5");
                    await Page.WaitForTimeoutAsync(800);
                }
                await CaptureScreenshotAsync("03-create-job-cron.png", "Create Job form — Cron schedule filled in");

                // Step 5: Switch to Webhook trigger
                await triggerSelect.SelectOptionAsync(new SelectOptionValue { Value = "Webhook" });
                await Page.WaitForTimeoutAsync(1_000);
                await CaptureScreenshotAsync("04-create-job-webhook.png", "Create Job form — Webhook trigger option");
            }

            // Step 6: Back to jobs list
            await Page.GotoAsync($"{Fixture.WebBaseUrl}/jobs", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });
            await Page.WaitForTimeoutAsync(2_000);

            // Click first job to open detail/history (if any exist)
            var firstJobLink = Page.Locator("table tbody tr td a[href^='/jobs/']").First;
            if (await firstJobLink.IsVisibleAsync())
            {
                await firstJobLink.ClickAsync();
                await Page.WaitForTimeoutAsync(2_500);
                await CaptureScreenshotAsync("05-job-detail-history.png", "Job detail page — execution history");
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
