using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// Shared Aspire AppHost + Playwright fixture for all E2E tests.
/// Starts the full distributed application once, then reuses it across tests.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    /// <summary>
    /// Ollama model used by the AppHost for E2E tests. Matches the AppHost default
    /// (<c>gemma4:e2b</c>) so tests exercise the same model real users hit. Per
    /// Bruno (2026-04-25): keep the test in lockstep with the shipped default
    /// rather than pinning a different model just for tests.
    /// </summary>
    public const string ToolCapableTestModel = "gemma4:e2b";

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string WebBaseUrl { get; private set; } = string.Empty;
    public string GatewayBaseUrl { get; private set; } = string.Empty;
    public string SchedulerBaseUrl { get; private set; } = string.Empty;
    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Fixture not initialized");

    /// <summary>
    /// True when the configured tool-capable model (<see cref="ToolCapableTestModel"/>)
    /// is present in the local Ollama tag listing. Tool-approval E2E scenarios should
    /// <c>Skip.IfNot</c> on this when Ollama is unavailable or the model isn't pulled.
    /// </summary>
    public bool IsToolCapableModelAvailable { get; private set; }

    public string ToolCapableModelSkipReason { get; private set; } =
        $"Ollama model '{ToolCapableTestModel}' not available locally; pull it with `ollama pull {ToolCapableTestModel}`.";

    /// <summary>
    /// True when AZURE_OPENAI_ENDPOINT + AZURE_OPENAI_API_KEY + AZURE_OPENAI_DEPLOYMENT
    /// are set to non-placeholder values. Tests that prefer a fast cloud model for
    /// orchestration validation (vs a slow local Ollama) can branch on this.
    /// </summary>
    public bool IsAzureOpenAIAvailable { get; private set; }

    /// <summary>Azure OpenAI endpoint URL discovered from env vars (null when unavailable).</summary>
    public string? AzureOpenAIEndpoint { get; private set; }

    /// <summary>Azure OpenAI API key discovered from env vars (null when unavailable).</summary>
    public string? AzureOpenAIApiKey { get; private set; }

    /// <summary>Azure OpenAI deployment / model name discovered from env vars (null when unavailable).</summary>
    public string? AzureOpenAIDeployment { get; private set; }

    /// <summary>
    /// Combined skip reason when neither a local tool-capable Ollama model nor an
    /// Azure OpenAI deployment is available. Use this when a test can run against
    /// either backend.
    /// </summary>
    public string AnyToolCapableModelSkipReason =>
        $"No tool-capable model available. {ToolCapableModelSkipReason} " +
        $"Alternatively set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT.";

    /// <summary>True when EITHER local Ollama model OR Azure OpenAI is configured.</summary>
    public bool IsAnyToolCapableModelAvailable =>
        IsToolCapableModelAvailable || IsAzureOpenAIAvailable;

    /// <summary>
    /// Creates an HttpClient configured for the gateway endpoint with SSL validation disabled.
    /// </summary>
    public HttpClient CreateGatewayHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri(GatewayBaseUrl) };
    }

    /// <summary>
    /// Creates an HttpClient pointed at the Scheduler service. The Scheduler hosts
    /// the /api/scheduler/jobs/{id}/trigger endpoint that actually wires up the
    /// artifact-capture path (gateway /api/jobs/{id}/execute does not).
    /// </summary>
    public HttpClient CreateSchedulerHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri(SchedulerBaseUrl) };
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Wave 5 PR-D: pin a tool-capable model BEFORE the AppHost reads its config.
            // OPENCLAW_OLLAMA_MODEL is honoured first inside AppHost.cs:14-17.
            Environment.SetEnvironmentVariable("OPENCLAW_OLLAMA_MODEL", ToolCapableTestModel);

            // Sqlite Web requires Docker. Disable it for Playwright test runs so the
            // fixture can still boot the AppHost in Docker-less environments.
            Environment.SetEnvironmentVariable("OPENCLAW_ENABLE_SQLITE_WEB", "false");

            // Wave 5 fix (Petey): Wipe per-agent skill state from previous test runs to
            // prevent skill contamination. The doc-processor system skill and any test-
            // created skills (e.g., emoji-teacher-journey) persist across runs and can
            // poison tool selection (e.g., model picks `shell` instead of `browser`).
            // Safe to delete: this fixture is only used by E2E tests, not by dev users.
            CleanAgentSkillState();

            // Best-effort probe of local Ollama; results expose IsToolCapableModelAvailable
            // so live tool-approval scenarios can Skip cleanly when the model isn't pulled.
            await ProbeOllamaModelAvailabilityAsync();

            // Probe AZURE_OPENAI_* env vars so tests can prefer the (much faster) cloud
            // model when the developer has it configured. Cheap synchronous check; no I/O.
            ProbeAzureOpenAIAvailability();

            // Build and start the Aspire AppHost
            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.OpenClawNet_AppHost>();

            _app = await appHost.BuildAsync();
            await _app.StartAsync();

            // Wait for both web and gateway resources to be running
            var webTask = _app.ResourceNotifications
                .WaitForResourceAsync("web", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(5));

            var gatewayTask = _app.ResourceNotifications
                .WaitForResourceAsync("gateway", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(5));

            var schedulerTask = _app.ResourceNotifications
                .WaitForResourceAsync("scheduler", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(5));

            await Task.WhenAll(webTask, gatewayTask, schedulerTask);

            // Get endpoints
            WebBaseUrl = _app.GetEndpoint("web", "https").ToString().TrimEnd('/');
            GatewayBaseUrl = _app.GetEndpoint("gateway", "https").ToString().TrimEnd('/');
            SchedulerBaseUrl = _app.GetEndpoint("scheduler", "http").ToString().TrimEnd('/');

            // Initialize Playwright
            _playwright = await Playwright.CreateAsync();

            // Allow headed mode via environment variable: PLAYWRIGHT_HEADED=true
            var headed = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADED")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

            // Allow tuning the inter-step delay via PLAYWRIGHT_SLOWMO (milliseconds).
            // Defaults: 1500ms when headed (good for voice-over), 0 when headless.
            var defaultSlowMo = headed ? 1500 : 0;
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
                Headless = !headed,
                SlowMo = slowMo
            });
        }
        catch (Xunit.SkipException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new Xunit.SkipException(
                "Playwright AppHost fixture could not start in this environment. " +
                "Ensure Aspire prerequisites are available (Docker/host resources as needed). " +
                $"Startup error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ProbeAzureOpenAIAvailability()
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");

        // Reject obvious .env placeholders so the test doesn't try to call a fake endpoint.
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

    /// <summary>
    /// Wave 5 fix (Petey): Wipes the per-agent skill state folder so each test run
    /// starts with a clean slate. Prevents skills from previous test runs (especially
    /// doc-processor and emoji-teacher-journey) from contaminating tool selection.
    /// </summary>
    private void CleanAgentSkillState()
    {
        try
        {
            // The default storage root is C:\openclawnet on Windows. The per-agent
            // skill enabled.json files live at C:\openclawnet\skills\agents\{agentName}\enabled.json.
            var skillsAgentsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "openclawnet", "skills", "agents");

            // Also check the legacy path (might exist on dev machines)
            var legacyPath = Path.Combine("C:", "openclawnet", "skills", "agents");

            foreach (var root in new[] { skillsAgentsPath, legacyPath })
            {
                if (Directory.Exists(root))
                {
                    Console.WriteLine($"[AppHostFixture] Cleaning agent skill state: {root}");
                    Directory.Delete(root, recursive: true);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppHostFixture] Warning: Could not clean agent skill state: {ex.Message}");
            // Non-fatal — tests can still run with stale skill state; just log and continue.
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
            // Cheap substring match — avoids pulling in an Ollama client just for this.
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

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }
}
