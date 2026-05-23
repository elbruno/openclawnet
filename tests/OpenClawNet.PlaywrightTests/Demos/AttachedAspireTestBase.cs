using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace OpenClawNet.PlaywrightTests.Demos;

/// <summary>
/// ⚠️ DEMO-ONLY BASE CLASS — NOT FOR CI OR REGRESSION TESTING ⚠️
///
/// This base class is for E2E tests that ATTACH to an already-running Aspire instance
/// (`aspire start src\OpenClawNet.AppHost`) instead of booting Aspire in-process via
/// `DistributedApplicationTestingBuilder`.
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                              WHY THIS EXISTS                                  │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ 1. Demo speed        — Aspire already running; test attaches in 2–3s vs 30–60s│
/// │ 2. Demo visibility   — Aspire dashboard stays visible to the audience         │
/// │ 3. Voice-over friendly — Combined with PLAYWRIGHT_SLOWMO, smooth presenter loop│
/// │ 4. NOT for CI        — These assume live Aspire + LLM; excluded from CI runs  │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                           WHEN TO USE THIS                                    │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ ✅ Live conference demos / voice-over recording                              │
/// │ ✅ Fast iteration during presenter rehearsal                                 │
/// │ ✅ Any scenario where the dashboard must stay visible                        │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                         WHEN NOT TO USE THIS                                  │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ ❌ CI/CD pipelines       — use AppHostFixture-based tests instead            │
/// │ ❌ Regression testing    — use AppHostFixture-based tests instead            │
/// │ ❌ Automated validation  — use AppHostFixture-based tests instead            │
/// │                                                                                │
/// │ For standard in-process E2E tests, see:                                       │
/// │   tests\OpenClawNet.PlaywrightTests\*JourneyE2ETests.cs                      │
/// │   tests\OpenClawNet.PlaywrightTests\AppHostFixture.cs                        │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                            USAGE PATTERN                                      │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ 1. Terminal 1: aspire describe --format Json                                  │
/// │ 2. If resources missing: aspire start src\OpenClawNet.AppHost                 │
/// │ 2. Wait for green health checks + dashboard (http://localhost:15178)         │
/// │ 3. Terminal 2: Set env vars and run test:                                    │
/// │                                                                                │
/// │    $env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"                │
/// │    $env:PLAYWRIGHT_HEADED = "true"                                           │
/// │    $env:PLAYWRIGHT_SLOWMO = "1500"  # 800=fast, 1500=default, 2500=slow     │
/// │                                                                                │
/// │    # Optional: override URLs if your ports differ                             │
/// │    $env:OPENCLAW_WEB_URL = "https://localhost:7294"                          │
/// │    $env:OPENCLAW_GATEWAY_URL = "https://localhost:7067"                      │
/// │                                                                                │
/// │    dotnet test tests\OpenClawNet.PlaywrightTests `                           │
/// │      --filter "Category=DemoLive&FullyQualifiedName~PirateJourneyAttachedTests" │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                         ENVIRONMENT VARIABLES                                 │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ OPENCLAW_WEB_URL (optional)                                                  │
/// │   Default: https://localhost:7294                                             │
/// │   The Blazor frontend URL (aspire describe --format Json to find actual URL) │
/// │                                                                                │
/// │ OPENCLAW_GATEWAY_URL (optional)                                              │
/// │   Default: https://localhost:7067                                             │
/// │   The Gateway API URL (for HttpClient calls)                                 │
/// │                                                                                │
/// │ PLAYWRIGHT_HEADED (read by this base)                                        │
/// │   ALWAYS "true" — these tests run headed by design                           │
/// │                                                                                │
/// │ PLAYWRIGHT_SLOWMO (read by this base)                                        │
/// │   Default: 1500ms                                                             │
/// │   Inter-step delay for voice-over pacing                                     │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │                             TRAIT CONVENTION                                  │
/// ├──────────────────────────────────────────────────────────────────────────────┤
/// │ All test classes that inherit this base MUST be marked:                      │
/// │   [Trait("Category", "DemoLive")]                                            │
/// │                                                                                │
/// │ This trait excludes them from default CI runs:                               │
/// │   dotnet test --filter "Category!=Live"                                      │
/// │                                                                                │
/// │ Demo runs explicitly opt-in:                                                 │
/// │   dotnet test --filter "Category=DemoLive"                                   │
/// └──────────────────────────────────────────────────────────────────────────────┘
///
/// <seealso cref="AppHostFixture"/> — In-process Aspire test infrastructure for CI
/// </summary>
public abstract class AttachedAspireTestBase : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private bool _startedAspireForRun;
    private bool _isReady;
    private string? _startupSkipReason;

    /// <summary>
    /// The Blazor Web frontend URL (e.g., https://localhost:7294).
/// Read from OPENCLAW_WEB_URL or from `aspire describe --format Json`.
    /// </summary>
    protected string WebBaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// The Gateway API URL (e.g., https://localhost:7067).
/// Read from OPENCLAW_GATEWAY_URL or from `aspire describe --format Json`.
    /// </summary>
    protected string GatewayBaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// The active Playwright page. Tests can use this directly for navigation and assertions.
    /// Disposed automatically when the test class completes.
    /// </summary>
    protected IPage Page
    {
        get
        {
            EnsureReadyOrSkip();
            return _page ?? throw new InvalidOperationException("Page not initialized");
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await ResolveOrStartAspireAsync();

            // Initialize Playwright — ALWAYS headed for demo tests.
            _playwright = await Playwright.CreateAsync();

            // SlowMo: read from PLAYWRIGHT_SLOWMO, default 1500ms for voice-over comfort.
            // Match the AppHostFixture pattern (lines ~141–160) for consistency.
            var defaultSlowMo = 1500;
            var slowMo = defaultSlowMo;
            var slowMoRaw = Environment.GetEnvironmentVariable("PLAYWRIGHT_SLOWMO");
            if (!string.IsNullOrWhiteSpace(slowMoRaw)
                && int.TryParse(slowMoRaw, out var parsedSlowMo)
                && parsedSlowMo >= 0)
            {
                slowMo = parsedSlowMo;
            }

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false, // ALWAYS headed for demo visibility
                SlowMo = slowMo
            });

            // Create a new page for this test class.
            _page = await _browser.NewPageAsync();
            _isReady = true;
        }
        catch (Xunit.SkipException ex)
        {
            _isReady = false;
            _startupSkipReason = ex.Message;
        }
        catch (Exception ex)
        {
            _isReady = false;
            _startupSkipReason =
                "Attached Aspire demo prerequisites are unavailable. " +
                $"Startup error: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_page is not null)
        {
            await _page.CloseAsync();
        }
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();

        if (_startedAspireForRun)
        {
            await RunAspireCommandAsync("stop");
        }
    }

    /// <summary>
    /// Creates an HttpClient configured for the Gateway endpoint with SSL validation disabled.
    /// Matches the AppHostFixture pattern for API calls.
    /// </summary>
    protected HttpClient CreateGatewayHttpClient()
    {
        EnsureReadyOrSkip();

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri(GatewayBaseUrl) };
    }

    /// <summary>
    /// Helper to log step progress to xUnit output. Override in derived classes to capture output.
    /// </summary>
    protected virtual Task LogStepAsync(string message)
    {
        // Base implementation does nothing; derived classes can inject ITestOutputHelper.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Helper to wait for a locator with periodic "still waiting..." ticks.
    /// Matches the pattern from SkillsPirateJourneyE2ETests.
    /// </summary>
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
                return; // Success
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

        // Final attempt with full timeout error
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Captures a screenshot on assertion failure. Useful for debugging demo runs.
    /// </summary>
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
            var screenshotPath = Path.Combine(
                Path.GetTempPath(),
                $"demo-failure-{DateTime.UtcNow:yyyyMMddHHmmss}.png");
            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
            await LogStepAsync($"❌ Test failed. Screenshot saved: {screenshotPath}");
            throw new Exception($"Test failed. Screenshot: {screenshotPath}", ex);
        }
    }

    private void EnsureReadyOrSkip()
    {
        Skip.IfNot(_isReady, _startupSkipReason ?? "Attached Aspire demo prerequisites are unavailable.");
    }

    private async Task ResolveOrStartAspireAsync()
    {
        var envWeb = Environment.GetEnvironmentVariable("OPENCLAW_WEB_URL");
        var envGateway = Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_URL");
        if (!string.IsNullOrWhiteSpace(envWeb) && !string.IsNullOrWhiteSpace(envGateway))
        {
            WebBaseUrl = envWeb.TrimEnd('/');
            GatewayBaseUrl = envGateway.TrimEnd('/');
            return;
        }

        if (await TryResolveUrlsFromDescribeAsync())
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = "start",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = GetRepositoryRoot()
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to run 'aspire start'.");
        _startedAspireForRun = true;

        var timeoutAt = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (await TryResolveUrlsFromDescribeAsync())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        throw new Xunit.SkipException(
            "Aspire resources were not available within 2 minutes. " +
            "Skipping demo-attached Playwright test because live Aspire prerequisites are unavailable.");
    }

    private async Task<bool> TryResolveUrlsFromDescribeAsync()
    {
        var result = await RunAspireCommandAsync("describe --format Json");
        if (result.ExitCode != 0)
        {
            return false;
        }

        var trimmed = result.Stdout.Trim();
        var jsonStart = trimmed.IndexOf('{');
        var jsonEnd = trimmed.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return false;
        }

        var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        string? webUrl = null;
        string? gatewayUrl = null;
        foreach (var resource in resources.EnumerateArray())
        {
            var name = resource.TryGetProperty("displayName", out var nameProp)
                ? nameProp.GetString() ?? string.Empty
                : string.Empty;
            if (!resource.TryGetProperty("urls", out var urlsProp) || urlsProp.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var urls = urlsProp.EnumerateArray()
                .Select(u => u.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u!)
                .ToList();
            var selectedUrl = urls.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                ?? urls.FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(selectedUrl))
            {
                continue;
            }

            if (name.Equals("web", StringComparison.OrdinalIgnoreCase))
            {
                webUrl = selectedUrl;
            }
            else if (name.Equals("gateway", StringComparison.OrdinalIgnoreCase))
            {
                gatewayUrl = selectedUrl;
            }
        }

        if (string.IsNullOrWhiteSpace(webUrl) || string.IsNullOrWhiteSpace(gatewayUrl))
        {
            return false;
        }

        WebBaseUrl = webUrl.TrimEnd('/');
        GatewayBaseUrl = gatewayUrl.TrimEnd('/');
        return true;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAspireCommandAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = GetRepositoryRoot()
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (-1, string.Empty, "Failed to launch aspire process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitForExitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));

        if (await Task.WhenAny(waitForExitTask, timeoutTask) != waitForExitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may already be terminating.
            }

            var timedOutStdout = await stdoutTask;
            var timedOutStderr = await stderrTask;
            return (
                -1,
                timedOutStdout,
                $"{timedOutStderr}{Environment.NewLine}Aspire command timed out after 30 seconds: aspire {arguments}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        await waitForExitTask;
        return (process.ExitCode, stdout, stderr);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
    }
}
