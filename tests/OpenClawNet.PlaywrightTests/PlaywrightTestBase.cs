using System.Runtime.CompilerServices;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Base class for Playwright tests that provides screenshot-on-failure infrastructure.
/// Automatically captures full-page screenshots when tests fail.
/// </summary>
public abstract class PlaywrightTestBase : IAsyncLifetime
{
    private const string VideoOutputDirectoryEnvVar = "OPENCLAW_PLAYWRIGHT_VIDEO_DIR";
    private const string ScreenshotOutputDirectoryEnvVar = "OPENCLAW_PLAYWRIGHT_SCREENSHOT_DIR";

    private readonly AppHostFixture _fixture;
    private IBrowserContext? _context;
    private IPage? _page;

    protected PlaywrightTestBase(AppHostFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The Playwright page instance for this test. Available after InitializeAsync completes.
    /// </summary>
    protected IPage Page => _page ?? throw new InvalidOperationException("Page not initialized");

    /// <summary>
    /// The AppHost fixture for accessing base URLs and other test resources.
    /// </summary>
    protected AppHostFixture Fixture => _fixture;

    public virtual async Task InitializeAsync()
    {
        if (!_fixture.IsReady)
        {
            throw new Xunit.SkipException(
                _fixture.StartupSkipReason
                ?? "Playwright AppHost fixture did not initialize successfully.");
        }

        var contextOptions = new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        };

        var videoOutputDirectory = Environment.GetEnvironmentVariable(VideoOutputDirectoryEnvVar);
        if (!string.IsNullOrWhiteSpace(videoOutputDirectory))
        {
            videoOutputDirectory = ResolveOutputDirectory(videoOutputDirectory);
            Directory.CreateDirectory(videoOutputDirectory);
            contextOptions.RecordVideoDir = videoOutputDirectory;
            contextOptions.RecordVideoSize = new RecordVideoSize
            {
                Width = 1280,
                Height = 720
            };
            contextOptions.ViewportSize = new ViewportSize
            {
                Width = 1280,
                Height = 720
            };
        }

        _context = await _fixture.Browser.NewContextAsync(contextOptions);
        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(30_000);
    }

    public virtual async Task DisposeAsync()
    {
        if (_page is not null) await _page.CloseAsync();
        if (_context is not null) await _context.DisposeAsync();
    }

    /// <summary>
    /// Wraps a test action and captures a screenshot on failure.
    /// Screenshots are saved to TestResults/screenshots/ by default, or to
    /// OPENCLAW_PLAYWRIGHT_SCREENSHOT_DIR when set for video-production runs.
    /// </summary>
    /// <param name="testAction">The test code to execute</param>
    /// <param name="testMethodName">The name of the calling test method (auto-populated)</param>
    protected async Task WithScreenshotOnFailure(Func<Task> testAction, [CallerMemberName] string testMethodName = "")
    {
        try
        {
            await testAction();
        }
        catch (Exception)
        {
            await CaptureScreenshotAsync(testMethodName);
            throw;
        }
    }

    private async Task CaptureScreenshotAsync(string testMethodName)
    {
        if (_page is null) return;

        try
        {
            var className = GetType().Name;
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var screenshotDir = Environment.GetEnvironmentVariable(ScreenshotOutputDirectoryEnvVar);
            if (string.IsNullOrWhiteSpace(screenshotDir))
            {
                screenshotDir = Path.Combine("TestResults", "screenshots");
            }
            else
            {
                screenshotDir = ResolveOutputDirectory(screenshotDir);
            }
            var filename = $"{className}_{testMethodName}_{timestamp}.png";
            var fullPath = Path.Combine(screenshotDir, filename);

            Directory.CreateDirectory(screenshotDir);

            await _page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = fullPath,
                FullPage = true
            });

            Console.WriteLine($"Screenshot saved: {fullPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to capture screenshot: {ex.Message}");
        }
    }

    private static string ResolveOutputDirectory(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(repoRoot, path));
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Logs a test progress step with timestamp. Writes to stdout AND injects/updates
    /// a yellow banner at the top of the page so headed runs are watchable.
    /// </summary>
    protected async Task LogStepAsync(string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        Console.WriteLine($"[{stamp}] 🧪 {message}");
        if (_page is null) return;
        try
        {
            // Inject (or update) a fixed banner at top of page showing current test step.
            await _page.EvaluateAsync(@"(text) => {
                let el = document.getElementById('__e2e_banner__');
                if (!el) {
                    el = document.createElement('div');
                    el.id = '__e2e_banner__';
                    el.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:99999;'
                        + 'background:#ffeb3b;color:#000;font:600 14px/1.4 system-ui,sans-serif;'
                        + 'padding:8px 16px;border-bottom:2px solid #f57f17;'
                        + 'box-shadow:0 2px 6px rgba(0,0,0,.2);'
                        + 'pointer-events:none;';
                    document.body.appendChild(el);
                }
                el.textContent = '🧪 E2E: ' + text;
            }", message);
        }
        catch
        {
            // Page navigation can race the eval — non-fatal.
        }
    }

    /// <summary>
    /// Waits for a locator to be visible, ticking every 5s with elapsed time so the
    /// user can see progress during long LLM-driven waits. Throws TimeoutException on timeout.
    /// </summary>
    protected async Task WaitForWithTicksAsync(ILocator locator, int timeoutMs, string what)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            var tick = Math.Min(5_000, Math.Max(500, remaining));
            try
            {
                await locator.First.WaitForAsync(new LocatorWaitForOptions { Timeout = tick });
                var elapsed = (int)(DateTime.UtcNow - start).TotalSeconds;
                await LogStepAsync($"✅ {what} appeared after {elapsed}s");
                return;
            }
            catch (TimeoutException)
            {
                var elapsed = (int)(DateTime.UtcNow - start).TotalSeconds;
                await LogStepAsync($"⏳ Still waiting for {what}... {elapsed}s elapsed");
            }
        }
        throw new TimeoutException($"Timeout {timeoutMs}ms exceeded waiting for {what}");
    }
}
