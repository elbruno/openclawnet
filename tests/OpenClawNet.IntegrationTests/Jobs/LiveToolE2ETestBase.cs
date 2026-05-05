using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Storage;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// Base class for per-tool job end-to-end live tests.
///
/// Provides:
///  - In-memory Gateway via <see cref="GatewayWebAppFactory"/> (Program-targeted minimal hosting).
///  - <see cref="HttpClient"/> wired to the in-memory server.
///  - <see cref="IDbContextFactory{OpenClawDbContext}"/> for assertions on persisted state.
///  - Helpers to create a job, execute it, wait for completion, and assert success.
///
/// Notes / discoveries:
///  - The Gateway's <c>POST /api/jobs/{id}/execute</c> endpoint (see JobEndpoints.cs)
///    runs the job synchronously and returns <see cref="JobExecutionResponse"/> with
///    the resulting RunId + Output. WaitForJobAsync is therefore mostly a sanity poll;
///    if the response already has a RunId we read the run row directly.
///  - <see cref="CreateJobRequest"/> currently has no per-job tool/provider/model fields
///    (provider/model come from RuntimeModelSettings; tools are selected by the LLM
///    from prompt text + registered tool manifest). The <c>toolName</c>/<c>provider</c>/
///    <c>model</c> arguments on <see cref="CreateJobAsync"/> are advisory: they are
///    appended to the prompt as a hint so the LLM picks the requested tool. If the
///    job-shape grows real fields later, update CreateJobAsync to forward them.
///  - All tests inheriting this base inherit <c>Category=Live</c> via class-level trait.
///
/// Live test behaviour: subclasses should call <c>SkipIfOllamaUnavailable()</c> at the
/// top of each [SkippableFact] when their flow depends on a real provider. The default
/// <see cref="GatewayWebAppFactory"/> registers a fake model client; subclasses that
/// need a real LLM should override the factory or swap <c>IModelClient</c> in their own
/// fixture (see <see cref="LiveFactory"/> hook).
/// </summary>
[Trait("Category", "Live")]
public abstract class LiveToolE2ETestBase : IClassFixture<GatewayWebAppFactory>, IDisposable
{
    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected GatewayWebAppFactory Factory { get; }
    protected HttpClient Client { get; }
    protected IDbContextFactory<OpenClawDbContext> DbFactory { get; }

    protected LiveToolE2ETestBase(GatewayWebAppFactory factory)
    {
        Factory = LiveFactory(factory);
        Client = Factory.CreateClient();
        // /execute is synchronous and blocks until the LLM finishes. With qwen2.5:3b
        // cold-start + tool loops this can exceed the 100s HttpClient default. Allow 5 min.
        Client.Timeout = TimeSpan.FromMinutes(5);
        DbFactory = Factory.Services.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
    }

    /// <summary>
    /// Override to substitute a factory variant that wires up a real <c>IModelClient</c>
    /// (e.g. OllamaModelClient) instead of the default fake. Default returns the supplied
    /// fixture-provided factory unchanged.
    /// </summary>
    protected virtual GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory) => factory;

    // ── Skip helpers (mirror LiveTestFixture so this base is self-contained for the
    //    IntegrationTests project, which doesn't reference UnitTests). ─────────

    /// <summary>
    /// Returns true when the <c>LIVE_TEST_PREFER_AOAI</c> environment variable
    /// is set to "1" or "true" (case-insensitive).
    /// </summary>
    protected static bool PreferAoai =>
        Environment.GetEnvironmentVariable("LIVE_TEST_PREFER_AOAI") == "1" ||
        string.Equals(Environment.GetEnvironmentVariable("LIVE_TEST_PREFER_AOAI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Factory helper that returns <see cref="LiveAoaiWebAppFactory"/> when
    /// <see cref="PreferAoai"/> is true, else <see cref="LiveOllamaWebAppFactory"/>
    /// with the specified model and endpoint.
    /// </summary>
    protected static GatewayWebAppFactory CreatePreferredLiveFactory(
        GatewayWebAppFactory baseFactory,
        string ollamaModel = "qwen2.5:3b",
        string? ollamaEndpoint = null)
    {
        return PreferAoai
            ? new LiveAoaiWebAppFactory()
            : new LiveOllamaWebAppFactory(ollamaModel, ollamaEndpoint);
    }

    /// <summary>
    /// Skip helper that checks Ollama availability when <see cref="PreferAoai"/> is false,
    /// or AOAI configuration presence when <see cref="PreferAoai"/> is true.
    /// </summary>
    protected static async Task SkipIfPreferredProviderUnavailableAsync(string ollamaEndpoint = "http://localhost:11434")
    {
        if (PreferAoai)
        {
            // Check AOAI config presence — read from user secrets + env vars
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddUserSecrets("c15754a6-dc90-4a2a-aecb-1233d1a54fe1", reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var endpoint = config["Model:Endpoint"];
            var authMode = config["Model:AuthMode"] ?? "api-key";
            var apiKey = config["Model:ApiKey"];

            var configured = !string.IsNullOrEmpty(endpoint)
                && (string.Equals(authMode, "integrated", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrEmpty(apiKey));

            Skip.IfNot(configured, "Azure OpenAI credentials not configured — set user secrets (Model:Endpoint, Model:ApiKey) or environment variables to run AOAI tests.");
        }
        else
        {
            await SkipIfOllamaUnavailableAsync(ollamaEndpoint);
        }
    }

    protected static void SkipIfPreferredProviderUnavailable(string ollamaEndpoint = "http://localhost:11434")
        => SkipIfPreferredProviderUnavailableAsync(ollamaEndpoint).GetAwaiter().GetResult();

    protected static async Task SkipIfOllamaUnavailableAsync(string endpoint = "http://localhost:11434")
    {
        bool ok = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync($"{endpoint.TrimEnd('/')}/api/tags");
            ok = resp.IsSuccessStatusCode;
        }
        catch { ok = false; }
        Skip.IfNot(ok, $"Ollama is not running at {endpoint}.");
    }

    protected static void SkipIfOllamaUnavailable(string endpoint = "http://localhost:11434")
        => SkipIfOllamaUnavailableAsync(endpoint).GetAwaiter().GetResult();

    // ── Job helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Create a job via <c>POST /api/jobs</c>. <paramref name="toolName"/>,
    /// <paramref name="provider"/>, and <paramref name="model"/> are appended to the
    /// prompt as a hint (the API doesn't currently expose per-job overrides).
    /// </summary>
    protected async Task<JobDto> CreateJobAsync(
        string name,
        string prompt,
        string toolName,
        string? provider = null,
        string? model = null)
    {
        var enriched = string.IsNullOrWhiteSpace(toolName)
            ? prompt
            : $"{prompt}\n\n(Use the `{toolName}` tool.)";

        var body = new CreateJobRequest
        {
            Name = name,
            Prompt = enriched,
        };

        var resp = await Client.PostAsJsonAsync("/api/jobs", body);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        dto.Should().NotBeNull("POST /api/jobs should return a JobDto");
        return dto!;
    }

    /// <summary>
    /// Execute a job via <c>POST /api/jobs/{id}/execute</c> (synchronous endpoint).
    /// Returns the JobRun id from the response.
    /// </summary>
    protected async Task<Guid> ExecuteJobAsync(Guid jobId)
    {
        var resp = await Client.PostAsync($"/api/jobs/{jobId}/execute", content: null);
        resp.EnsureSuccessStatusCode();
        var execResp = await resp.Content.ReadFromJsonAsync<JobExecutionResponse>(JsonOpts);
        execResp.Should().NotBeNull("execute endpoint should return JobExecutionResponse");
        execResp!.RunId.Should().NotBeNull("a successful execute returns a JobRun id");
        return execResp.RunId!.Value;
    }

    /// <summary>
    /// Poll <c>GET /api/jobs/{jobId}/runs/{runId}</c> until the run reaches a terminal
    /// state (Completed / Failed / Cancelled) or the timeout elapses.
    /// </summary>
    protected async Task<JobRunDetailDto> WaitForJobAsync(Guid jobId, Guid runId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        JobRunDetailDto? last = null;

        while (DateTime.UtcNow < deadline)
        {
            var resp = await Client.GetAsync($"/api/jobs/{jobId}/runs/{runId}");
            if (resp.IsSuccessStatusCode)
            {
                last = await resp.Content.ReadFromJsonAsync<JobRunDetailDto>(JsonOpts);
                if (last is not null && IsTerminal(last.Status)) return last;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"JobRun {runId} did not reach a terminal state within {timeout.TotalSeconds:0}s. " +
            $"Last status: {last?.Status ?? "<unknown>"}");
    }

    private static bool IsTerminal(string status) =>
        status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Failed",  StringComparison.OrdinalIgnoreCase)
        || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Canceled",  StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Asserts the run completed successfully and that its Result contains
    /// <paramref name="expectedOutputContains"/> (case-insensitive substring match).
    /// </summary>
    protected static void AssertJobRunSucceeded(JobRunDetailDto run, string expectedOutputContains)
    {
        run.Should().NotBeNull();
        run.Status.Should().BeOneOf(new[] { "Completed", "completed" }, $"job run failed: {run.Error}");
        run.Error.Should().BeNullOrWhiteSpace();
        if (!string.IsNullOrEmpty(expectedOutputContains))
        {
            (run.Result ?? "").Should().Contain(
                expectedOutputContains,
                because: $"job output should mention '{expectedOutputContains}'");
        }
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}
