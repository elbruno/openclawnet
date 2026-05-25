using Microsoft.Playwright;
using Xunit;

namespace OpenClawNet.PlaywrightTests.Demos;

/// <summary>
/// Demo-only Playwright base that reuses the shared <see cref="AspireHostFixture"/>.
/// </summary>
public abstract class AspireHostAttachedDemoTestBase : IAsyncLifetime
{
    private IPage? _page;

    protected AspireHostAttachedDemoTestBase(AspireHostFixture fixture)
    {
        Fixture = fixture;
    }

    protected AspireHostFixture Fixture { get; }

    protected IPage Page
    {
        get
        {
            EnsureReadyOrSkip();
            return _page ?? throw new InvalidOperationException("Page not initialized.");
        }
    }

    protected string WebBaseUrl => Fixture.WebBaseUrl;

    protected string GatewayBaseUrl => Fixture.GatewayBaseUrl;

    public async Task InitializeAsync()
    {
        EnsureReadyOrSkip();
        _page = await Fixture.Browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_page is not null)
        {
            await _page.CloseAsync();
            _page = null;
        }
    }

    protected HttpClient CreateGatewayHttpClient()
    {
        EnsureReadyOrSkip();
        return Fixture.CreateGatewayHttpClient();
    }

    protected virtual Task LogStepAsync(string message)
    {
        return Task.CompletedTask;
    }

    protected async Task WaitForWithTicksAsync(ILocator locator, int timeoutMs, string description)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var tickInterval = TimeSpan.FromSeconds(5);
        var nextTick = DateTime.UtcNow.Add(tickInterval);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await locator.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 1000
                });
                return;
            }
            catch (TimeoutException)
            {
                if (DateTime.UtcNow >= nextTick)
                {
                    await LogStepAsync($"⏱ Still waiting for {description}...");
                    nextTick = DateTime.UtcNow.Add(tickInterval);
                }
            }
        }

        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    protected async Task WithScreenshotOnFailure(Func<Task> action)
    {
        EnsureReadyOrSkip();

        try
        {
            await action();
        }
        catch (Xunit.SkipException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var screenshotRoot = Path.Combine(
                Directory.GetCurrentDirectory(),
                "artifacts",
                "playwright-demo-failures");
            Directory.CreateDirectory(screenshotRoot);

            var screenshotPath = Path.Combine(
                screenshotRoot,
                $"demo-failure-{DateTime.UtcNow:yyyyMMddHHmmss}.png");

            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
            await LogStepAsync($"❌ Test failed. Screenshot saved: {screenshotPath}");
            throw new Exception($"Test failed. Screenshot: {screenshotPath}", ex);
        }
    }

    private void EnsureReadyOrSkip()
    {
        Skip.IfNot(Fixture.IsReady, Fixture.StartupSkipReason ?? "AspireHostFixture is not ready.");
    }
}
