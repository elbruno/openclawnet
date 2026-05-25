using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

public sealed class AspireHostFixture : IAsyncLifetime
{
    public sealed record OllamaToolCallProbeResult(
        bool IsSupported,
        string SkipReason,
        string? ObservedToolName = null,
        string? ObservedArgumentsJson = null);

    /// <summary>
    /// Ollama model used for E2E tests. Matches the AppHost default (<c>gemma4:e2b</c>).
    /// </summary>
    public const string ToolCapableTestModel = "gemma4:e2b";

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Process? _aspireProcess;
    private bool _startedByFixture;
    private DateTime _fixtureStartedAtUtc;
    private readonly ConcurrentDictionary<string, Lazy<Task<OllamaToolCallProbeResult>>> _ollamaToolCallProbeCache =
        new(StringComparer.OrdinalIgnoreCase);

    public string WebBaseUrl { get; private set; } = string.Empty;
    public string GatewayBaseUrl { get; private set; } = string.Empty;
    public string SchedulerBaseUrl { get; private set; } = string.Empty;
    public bool IsReady { get; private set; }
    public string? StartupSkipReason { get; private set; }
    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Fixture not initialized");

    public bool IsToolCapableModelAvailable { get; private set; }
    public string ToolCapableModelSkipReason { get; private set; } =
        $"Ollama model '{ToolCapableTestModel}' not available locally; pull it with `ollama pull {ToolCapableTestModel}`.";

    public bool IsAzureOpenAIAvailable { get; private set; }
    public string? AzureOpenAIEndpoint { get; private set; }
    public string? AzureOpenAIApiKey { get; private set; }
    public string? AzureOpenAIDeployment { get; private set; }

    public string AnyToolCapableModelSkipReason =>
        $"No tool-capable model available. {ToolCapableModelSkipReason} " +
        $"Alternatively set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.";

    public bool IsAnyToolCapableModelAvailable =>
        IsToolCapableModelAvailable || IsAzureOpenAIAvailable;

    public Task<OllamaToolCallProbeResult> ProbeOllamaToolCallCompatibilityAsync(string modelName)
        => _ollamaToolCallProbeCache
            .GetOrAdd(modelName, static model => new Lazy<Task<OllamaToolCallProbeResult>>(
                () => ProbeOllamaToolCallCompatibilityCoreAsync(model)))
            .Value;

    public HttpClient CreateGatewayHttpClient()
    {
        if (!IsReady)
        {
            throw new Xunit.SkipException(
                StartupSkipReason ?? "Aspire host fixture did not initialize successfully.");
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri(GatewayBaseUrl) };
    }

    public HttpClient CreateSchedulerHttpClient()
    {
        if (!IsReady)
        {
            throw new Xunit.SkipException(
                StartupSkipReason ?? "Aspire host fixture did not initialize successfully.");
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri(SchedulerBaseUrl) };
    }

    public async Task InitializeAsync()
    {
        _fixtureStartedAtUtc = DateTime.UtcNow;
        PlaywrightProcessHygiene.CleanupOrphanedPlaywrightNodeProcesses(log: Console.WriteLine);

        await ProbeOllamaModelAvailabilityAsync();
        ProbeAzureOpenAIAvailability();

        try
        {
            var resolved = await TryResolveRunningAspireAsync();
            if (resolved is null)
            {
                await StartAspireAsync();
                resolved = await WaitForAspireUrlsAfterStartAsync();
                _startedByFixture = true;
            }

            WebBaseUrl = resolved.WebBaseUrl;
            GatewayBaseUrl = resolved.GatewayBaseUrl;
            SchedulerBaseUrl = resolved.SchedulerBaseUrl;

            await WaitForEndpointReadyAsync($"{WebBaseUrl}/health");
            await WaitForEndpointReadyAsync($"{GatewayBaseUrl}/health");
            if (!string.IsNullOrWhiteSpace(SchedulerBaseUrl))
            {
                await WaitForEndpointReadyAsync($"{SchedulerBaseUrl}/health");
            }

            PlaywrightBinaryHelper.UnblockPlaywrightBinaries();
            _playwright = await Playwright.CreateAsync();

            var headed = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADED")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
            var slowMo = headed ? 1500 : 0;
            if (int.TryParse(Environment.GetEnvironmentVariable("PLAYWRIGHT_SLOWMO"), out var parsedSlowMo) &&
                parsedSlowMo >= 0)
            {
                slowMo = parsedSlowMo;
            }

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = !headed,
                SlowMo = slowMo
            });

            IsReady = true;
        }
        catch (Exception ex)
        {
            await DisposePlaywrightAsync();
            if (_startedByFixture)
            {
                await StopAspireAsync();
            }

            IsReady = false;
            StartupSkipReason =
                "AspireHostFixture could not initialize in this environment. " +
                $"Startup error: {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[AspireHostFixture] {StartupSkipReason}");
        }
    }

    public async Task DisposeAsync()
    {
        await DisposePlaywrightAsync();
        PlaywrightProcessHygiene.CleanupOrphanedPlaywrightNodeProcesses(_fixtureStartedAtUtc, Console.WriteLine);

        if (_startedByFixture)
        {
            await StopAspireAsync();
        }
    }

    private async Task DisposePlaywrightAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }

    private async Task<AspireResolvedUrls?> TryResolveRunningAspireAsync()
    {
        var describe = await RunAspireCommandAsync("describe --format Json", TimeSpan.FromSeconds(30));
        if (describe.ExitCode != 0)
        {
            return null;
        }

        return AspireDescribeResolver.TryResolveResources(describe.Stdout, out var resolved)
            ? resolved
            : null;
    }

    private async Task StartAspireAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = "start src\\OpenClawNet.AppHost",
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _aspireProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Aspire host process.");

        _ = DrainOutputAsync(_aspireProcess.StandardOutput);
        _ = DrainOutputAsync(_aspireProcess.StandardError);

        Console.WriteLine("[AspireHostFixture] Started Aspire because no running resources were detected.");
    }

    private async Task<AspireResolvedUrls> WaitForAspireUrlsAfterStartAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);

        while (DateTime.UtcNow < deadline)
        {
            var resolved = await TryResolveRunningAspireAsync();
            if (resolved is not null)
            {
                return resolved;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        throw new TimeoutException("Aspire did not surface web/gateway resources within 3 minutes.");
    }

    private async Task StopAspireAsync()
    {
        var stop = await RunAspireCommandAsync("stop", TimeSpan.FromSeconds(30));
        if (stop.ExitCode != 0)
        {
            Console.WriteLine($"[AspireHostFixture] Warning: aspire stop returned {stop.ExitCode}. {stop.Stderr}");
        }

        if (_aspireProcess is null)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _aspireProcess.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                _aspireProcess.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort only.
            }
        }
        finally
        {
            _aspireProcess.Dispose();
            _aspireProcess = null;
        }
    }

    private static async Task WaitForEndpointReadyAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        Exception? lastException = null;
        HttpStatusCode? lastStatusCode = null;

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url);
                lastStatusCode = response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException(
            $"Timed out waiting for endpoint '{url}' to become ready. " +
            $"Last status: {lastStatusCode?.ToString() ?? "<none>"}. " +
            $"Last error: {lastException?.Message ?? "<none>"}",
            lastException);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAspireCommandAsync(string arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = arguments,
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (-1, string.Empty, "Failed to launch aspire process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(timeout);
        if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may already be exiting.
            }
        }

        await waitTask;
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task DrainOutputAsync(StreamReader reader)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line is null)
                {
                    break;
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private void ProbeAzureOpenAIAvailability()
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");

        static bool IsRealValue(string? v) =>
            !string.IsNullOrWhiteSpace(v) &&
            !v!.StartsWith("your-", StringComparison.OrdinalIgnoreCase) &&
            !v.Contains("your-azure-openai", StringComparison.OrdinalIgnoreCase);

        if (IsRealValue(endpoint) && IsRealValue(apiKey) && IsRealValue(deployment))
        {
            AzureOpenAIEndpoint = endpoint;
            AzureOpenAIApiKey = apiKey;
            AzureOpenAIDeployment = deployment;
            IsAzureOpenAIAvailable = true;
        }
    }

    private async Task ProbeOllamaModelAvailabilityAsync()
    {
        var ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
            ?? "http://localhost:11434";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync($"{ollamaBase.TrimEnd('/')}/api/tags");
            if (!resp.IsSuccessStatusCode)
            {
                ToolCapableModelSkipReason =
                    $"Ollama at {ollamaBase} responded {(int)resp.StatusCode} to /api/tags; cannot verify '{ToolCapableTestModel}'.";
                IsToolCapableModelAvailable = false;
                return;
            }

            var body = await resp.Content.ReadAsStringAsync();
            IsToolCapableModelAvailable = body.Contains(ToolCapableTestModel, StringComparison.OrdinalIgnoreCase);
            if (!IsToolCapableModelAvailable)
            {
                ToolCapableModelSkipReason =
                    $"Ollama at {ollamaBase} is reachable but model '{ToolCapableTestModel}' is not pulled. Run: `ollama pull {ToolCapableTestModel}`.";
            }
        }
        catch (Exception ex)
        {
            IsToolCapableModelAvailable = false;
            ToolCapableModelSkipReason =
                $"Could not reach Ollama at {ollamaBase} ({ex.GetType().Name}: {ex.Message}); cannot verify '{ToolCapableTestModel}'.";
        }
    }

    private static async Task<OllamaToolCallProbeResult> ProbeOllamaToolCallCompatibilityCoreAsync(string modelName)
    {
        var ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
            ?? "http://localhost:11434";

        var payload = new
        {
            model = modelName,
            stream = false,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Use the provided browser_navigate tool when the user asks to open example.com. Do not answer from memory."
                },
                new
                {
                    role = "user",
                    content = "Please use browser_navigate to open https://example.com and tell me the title."
                }
            },
            tools = new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "browser_navigate",
                        description = "Navigate the headless browser to a URL.",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                url = new
                                {
                                    type = "string",
                                    description = "URL to navigate to"
                                }
                            },
                            required = new[] { "url" }
                        }
                    }
                }
            }
        };

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(ollamaBase), Timeout = TimeSpan.FromSeconds(120) };
            using var response = await http.PostAsJsonAsync("/api/chat", payload);
            if (!response.IsSuccessStatusCode)
            {
                return new OllamaToolCallProbeResult(
                    IsSupported: false,
                    SkipReason: $"Ollama probe for '{modelName}' returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            if (!document.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("tool_calls", out var toolCalls) ||
                toolCalls.ValueKind != JsonValueKind.Array ||
                toolCalls.GetArrayLength() == 0)
            {
                return new OllamaToolCallProbeResult(
                    IsSupported: false,
                    SkipReason: $"Ollama model '{modelName}' did not emit any tool call during a direct browser_navigate probe.");
            }

            var firstToolCall = toolCalls[0];
            var function = firstToolCall.TryGetProperty("function", out var functionElement)
                ? functionElement
                : default;
            var observedName = function.ValueKind == JsonValueKind.Object &&
                               function.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var observedArguments = function.ValueKind == JsonValueKind.Object &&
                                    function.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetRawText()
                : null;

            if (!string.Equals(observedName, "browser_navigate", StringComparison.OrdinalIgnoreCase))
            {
                var reason = string.IsNullOrWhiteSpace(observedName)
                    ? $"Ollama model '{modelName}' emitted a malformed tool call (missing function name) during the browser_navigate probe."
                    : $"Ollama model '{modelName}' emitted '{observedName}' instead of 'browser_navigate' during the direct tool-call probe.";
                return new OllamaToolCallProbeResult(false, reason, observedName, observedArguments);
            }

            return new OllamaToolCallProbeResult(
                IsSupported: true,
                SkipReason: string.Empty,
                ObservedToolName: observedName,
                ObservedArgumentsJson: observedArguments);
        }
        catch (Exception ex)
        {
            return new OllamaToolCallProbeResult(
                IsSupported: false,
                SkipReason: $"Could not verify Ollama tool-call compatibility for '{modelName}' at {ollamaBase} ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tests", "catalog.yaml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
