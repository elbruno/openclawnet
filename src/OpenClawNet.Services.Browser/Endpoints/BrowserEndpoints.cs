using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace OpenClawNet.Services.Browser.Endpoints;

public static class BrowserEndpoints
{
    public static void MapBrowserEndpoints(this WebApplication app)
    {
        app.MapPost("/api/browser/execute", async (
            BrowserExecuteRequest request,
            ILogger<BrowserExecuteRequest> logger,
            IOptions<BrowserOptions> browserOptions) =>
        {
            var options = browserOptions.Value;
            try
            {
                return request.Action?.ToLowerInvariant() switch
                {
                    "navigate"     => await NavigateAsync(request, logger, options),
                    "extract-text" => await ExtractTextAsync(request, logger, options),
                    "screenshot"   => await ScreenshotAsync(request, logger, options),
                    "click"        => await ClickAsync(request, logger, options),
                    "fill"         => await FillAsync(request, logger, options),
                    _ => Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "Unknown action. Supported: navigate, extract-text, screenshot, click, fill" })
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Browser execution error");
                return Results.Ok(new BrowserExecuteResponse { Success = false, Output = $"Browser error: {ex.Message}" });
            }
        })
        .WithTags("Browser")
        .WithName("ExecuteBrowser");
    }

    private static async Task<IResult> NavigateAsync(BrowserExecuteRequest req, ILogger logger, BrowserOptions options)
    {
        if (string.IsNullOrEmpty(req.Url)) return Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "'url' is required" });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        logger.LogInformation("Navigating to {Url}", req.Url);
        var response = await page.GotoAsync(req.Url, new() { Timeout = options.NavigationTimeoutMs });
        var title = await page.TitleAsync();
        return Results.Ok(new BrowserExecuteResponse { Success = true, Output = $"Navigated to: {req.Url}\nTitle: {title}\nStatus: {response?.Status}" });
    }

    private static async Task<IResult> ExtractTextAsync(BrowserExecuteRequest req, ILogger logger, BrowserOptions options)
    {
        if (string.IsNullOrEmpty(req.Url)) return Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "'url' is required" });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(req.Url, new() { Timeout = options.NavigationTimeoutMs });
        string text;
        if (!string.IsNullOrEmpty(req.Selector))
            text = await page.Locator(req.Selector).InnerTextAsync();
        else
        {
            text = await page.InnerTextAsync("body");
            if (text.Length > options.MaxExtractedTextLength)
                text = text[..options.MaxExtractedTextLength] + $"\n\n... (truncated at {options.MaxExtractedTextLength} chars)";
        }
        logger.LogInformation("Extracted {Length} chars from {Url}", text.Length, req.Url);
        return Results.Ok(new BrowserExecuteResponse { Success = true, Output = text });
    }

    private static async Task<IResult> ScreenshotAsync(BrowserExecuteRequest req, ILogger logger, BrowserOptions options)
    {
        if (string.IsNullOrEmpty(req.Url)) return Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "'url' is required" });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(req.Url, new() { Timeout = options.NavigationTimeoutMs });
        var path = Path.Combine(Path.GetTempPath(), $"browser-{Guid.NewGuid():N}.png");
        await page.ScreenshotAsync(new() { Path = path, FullPage = false });
        logger.LogInformation("Screenshot saved: {Path}", path);
        return Results.Ok(new BrowserExecuteResponse { Success = true, Output = $"Screenshot saved: {path}" });
    }

    private static async Task<IResult> ClickAsync(BrowserExecuteRequest req, ILogger logger, BrowserOptions options)
    {
        if (string.IsNullOrEmpty(req.Url)) return Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "'url' is required" });
        if (string.IsNullOrEmpty(req.Selector)) return Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "'selector' required for click" });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(req.Url, new() { Timeout = options.NavigationTimeoutMs });
        await page.ClickAsync(req.Selector);
        return Results.Ok(new BrowserExecuteResponse { Success = true, Output = $"Clicked '{req.Selector}'. Current URL: {page.Url}" });
    }

    private static async Task<IResult> FillAsync(BrowserExecuteRequest req, ILogger logger, BrowserOptions options)
    {
        if (string.IsNullOrEmpty(req.Url)) return Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "'url' is required" });
        if (string.IsNullOrEmpty(req.Selector)) return Results.BadRequest(new BrowserExecuteResponse { Success = false, Output = "'selector' required for fill" });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(req.Url, new() { Timeout = options.NavigationTimeoutMs });
        await page.FillAsync(req.Selector, req.Value ?? "");
        return Results.Ok(new BrowserExecuteResponse { Success = true, Output = $"Filled '{req.Selector}' on {req.Url}" });
    }
}

public sealed record BrowserExecuteRequest(string? Action, string? Url, string? Selector = null, string? Value = null);
public sealed record BrowserExecuteResponse { public bool Success { get; init; } public string Output { get; init; } = ""; }
