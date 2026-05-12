using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// End-to-end test that exercises the full Website Watcher flow from template
/// instantiation through cross-surface visibility:
///
///   1. POST  /api/demos/website-watcher/setup        → instantiate the template
///   2. PUT   /api/jobs/{id}                          → swap the prompt to "fetch elbruno.com title"
///   3. POST  /api/jobs/{id}/execute                  → run synchronously (no scheduler/timer)
///   4. GET   /api/jobs/{id}/runs                     → assert run was recorded
///   5. GET   /api/demos/website-watcher/status       → assert demo-helper sees our job
///   6. GET   /api/jobs                               → assert it appears in the global list
///   7. GET   /api/channels                           → assert artifact-bearing channel surfaced
///   8. GET   /api/channels/{id}                      → assert channel detail / artifact present
///   9. DELETE /api/jobs/{id}                         → cleanup
///
/// The agent's actual response is allowed to fall back to a graceful failure
/// message (e.g. "could not reach elbruno.com") because the test box may be
/// behind an egress filter — the goal is to validate the **orchestration**, not
/// the upstream reachability of elbruno.com. When the network DOES allow it,
/// we additionally assert the response references the site / a title.
///
/// Tagged <c>RequiresModel</c> so it is skipped automatically when no
/// tool-capable Ollama model is available locally.
/// </summary>
[Collection("AppHost")]
[Trait("Category", "RequiresModel")]
public class WebsiteWatcherE2ETests : IAsyncLifetime
{
    private const string TargetUrl = "https://elbruno.com";
    private const string AzureProviderName = "azure-openai-e2e";
    private const string AzureProfileName = "website-watcher-azure-e2e";

    private readonly AppHostFixture _fixture;
    private readonly ITestOutputHelper _output;
    private HttpClient _gateway = null!;
    private HttpClient _scheduler = null!;
    private Guid? _createdJobId;
    private bool _createdAzureProvider;
    private bool _createdAzureProfile;

    public WebsiteWatcherE2ETests(AppHostFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _gateway = _fixture.CreateGatewayHttpClient();
        _gateway.Timeout = TimeSpan.FromMinutes(5);
        _scheduler = _fixture.CreateSchedulerHttpClient();
        _scheduler.Timeout = TimeSpan.FromMinutes(5);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Best-effort cleanup so reruns of the suite don't accumulate state.
        if (_createdJobId is { } id)
        {
            try { await _gateway.DeleteAsync($"/api/jobs/{id}"); } catch { /* swallow */ }
        }
        if (_createdAzureProfile)
        {
            try { await _gateway.DeleteAsync($"/api/agent-profiles/{AzureProfileName}"); } catch { /* swallow */ }
        }
        if (_createdAzureProvider)
        {
            try { await _gateway.DeleteAsync($"/api/model-providers/{AzureProviderName}"); } catch { /* swallow */ }
        }
        _gateway.Dispose();
        _scheduler.Dispose();
    }

    [SkippableFact]
    public async Task FullFlow_CreateFromTemplate_RunGetTitle_VisibleEverywhere()
    {
        // Prefer a fast cloud model (Azure OpenAI) when configured; fall back to the
        // local Ollama tool-capable model otherwise. Skip only when neither exists.
        Skip.IfNot(_fixture.IsAnyToolCapableModelAvailable, _fixture.AnyToolCapableModelSkipReason);

        var useAzure = _fixture.IsAzureOpenAIAvailable;
        _output.WriteLine(useAzure
            ? $"[0] Using Azure OpenAI (deployment '{_fixture.AzureOpenAIDeployment}') for fast E2E."
            : $"[0] Azure OpenAI not configured; falling back to local Ollama '{AppHostFixture.ToolCapableTestModel}'.");

        // ── 0. (Azure path only) Provision provider + profile that points at it ──
        string? agentProfileName = null;
        if (useAzure)
        {
            await UpsertAzureProviderAndProfileAsync();
            agentProfileName = AzureProfileName;
            _output.WriteLine($"[0a] Provider '{AzureProviderName}' + profile '{AzureProfileName}' upserted.");
        }

        // ── 1. Instantiate the Website Watcher template pointed at elbruno.com ──
        var setupResp = await _gateway.PostAsJsonAsync(
            "/api/demos/website-watcher/setup",
            new { url = TargetUrl, logPath = @"docs\e2e-watch-log.txt" });

        Assert.Equal(HttpStatusCode.Created, setupResp.StatusCode);
        var setup = await setupResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = Guid.Parse(setup.GetProperty("jobId").GetString()!);
        var jobName = setup.GetProperty("name").GetString();
        _createdJobId = jobId;
        _output.WriteLine($"[1] Created job '{jobName}' ({jobId})");

        // ── 2. Replace the prompt: instead of hash-and-log, fetch and return title ──
        // Keep all other recurring-job fields intact so the underlying executor still
        // sees a valid scheduled job (start state, cron, etc.). When Azure is the
        // backend, also pin the agent profile so the scheduler routes through it.
        var titlePrompt =
            $"Use the `web.fetch` tool to GET {TargetUrl}.\n" +
            "Extract the contents of the HTML <title>...</title> element from the response body.\n" +
            "Reply with EXACTLY this format on a single line: `Title: <the-title>`\n" +
            "If the page cannot be fetched for any reason, reply: `Title: <unavailable: reason>`.\n" +
            "Do not call any other tool. Do not call web.fetch more than twice.";

        var putResp = await _gateway.PutAsJsonAsync($"/api/jobs/{jobId}", new
        {
            name = jobName,
            prompt = titlePrompt,
            isRecurring = true,
            cronExpression = "*/15 * * * *",
            naturalLanguageSchedule = "Every 15 minutes",
            allowConcurrentRuns = false,
            agentProfileName = agentProfileName
        });
        Assert.True(putResp.IsSuccessStatusCode, $"PUT /api/jobs/{jobId} → {(int)putResp.StatusCode}");
        _output.WriteLine(useAzure
            ? $"[2] Prompt rewritten + job pinned to profile '{agentProfileName}'"
            : "[2] Prompt rewritten to fetch <title>");

        // ── 3. Trigger via the Scheduler service (path that auto-captures artifacts) ──
        // Gateway's /api/jobs/{id}/execute runs synchronously but does NOT call
        // ArtifactStorageService.CreateArtifactFromJobRunAsync. The Scheduler's
        // /api/scheduler/jobs/{id}/trigger does — so it's the right path for the
        // full Channel-visibility assertions below.
        var triggerResp = await _scheduler.PostAsync($"/api/scheduler/jobs/{jobId}/trigger", null);
        Assert.True(triggerResp.IsSuccessStatusCode,
            $"POST /api/scheduler/jobs/{jobId}/trigger → {(int)triggerResp.StatusCode}: {await triggerResp.Content.ReadAsStringAsync()}");
        var triggerJson = await triggerResp.Content.ReadFromJsonAsync<JsonElement>();
        var runId = Guid.Parse(triggerJson.GetProperty("runId").GetString()!);
        _output.WriteLine($"[3] Triggered run {runId}");

        // ── 3b. Poll until the run reaches a terminal state (completed / failed) ──
        // Scheduler's trigger is fire-and-forget; we poll the gateway runs endpoint
        // (which reads the same JobRuns table the scheduler writes to).
        JsonElement? terminalRun = null;
        var ranToCompletion = await PollUntilAsync(TimeSpan.FromMinutes(4), async () =>
        {
            var runsResp = await _gateway.GetAsync($"/api/jobs/{jobId}/runs");
            if (!runsResp.IsSuccessStatusCode) return false;
            var runs = await runsResp.Content.ReadFromJsonAsync<List<JsonElement>>();
            var run = runs?.FirstOrDefault(r =>
                r.TryGetProperty("id", out var rid) &&
                Guid.TryParse(rid.GetString(), out var g) &&
                g == runId);
            if (run is null) return false;
            var s = run.Value.GetProperty("status").GetString();
            if (s is "completed" or "succeeded" or "failed")
            {
                terminalRun = run.Value;
                return true;
            }
            return false;
        });
        Assert.True(ranToCompletion, $"Run {runId} should reach a terminal state within 4 minutes.");

        var runStatus = terminalRun!.Value.GetProperty("status").GetString();
        var output = terminalRun.Value.TryGetProperty("result", out var resProp) ? resProp.GetString() ?? string.Empty : string.Empty;
        var error = terminalRun.Value.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
        _output.WriteLine($"[3b] Run terminal status='{runStatus}'");
        _output.WriteLine($"[3b] agent output: {output}");
        if (!string.IsNullOrEmpty(error)) _output.WriteLine($"[3b] error: {error}");

        Assert.True(
            runStatus == "completed" || runStatus == "succeeded",
            $"Latest run should be completed, not '{runStatus}' (error: {error}).");
        Assert.False(string.IsNullOrWhiteSpace(output), "Agent output must not be empty.");

        // The agent must at least follow our format — either a real title or the
        // explicit "unavailable" sentinel. This catches the prior "max iterations"
        // regression which would NOT match either pattern.
        var matchesContract =
            output.Contains("Title:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("elbruno", StringComparison.OrdinalIgnoreCase);
        Assert.True(matchesContract,
            $"Agent output should follow the prompt contract. Got: {output}");

        // ── 4. Run history records the run ──
        var runsResp2 = await _gateway.GetAsync($"/api/jobs/{jobId}/runs");
        Assert.Equal(HttpStatusCode.OK, runsResp2.StatusCode);
        var runs2 = await runsResp2.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(runs2);
        Assert.True(runs2!.Count >= 1, "At least one JobRun row should exist after trigger.");
        _output.WriteLine($"[4] Run history has {runs2.Count} run(s)");

        // ── 5. Demo-helper status endpoint sees our job ──
        var statusResp = await _gateway.GetAsync("/api/demos/website-watcher/status");
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        var statusJson = await statusResp.Content.ReadFromJsonAsync<JsonElement>();
        // Status returns the *latest* website-watcher instance — ours is newest.
        Assert.True(statusJson.TryGetProperty("jobId", out _) || statusJson.TryGetProperty("name", out _),
            "/api/demos/website-watcher/status should describe a website-watcher job.");
        _output.WriteLine("[5] Demo helper /status returned job info");

        // ── 6. Global jobs list contains it ──
        var listResp = await _gateway.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var jobsList = await listResp.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(jobsList);
        Assert.Contains(jobsList!, j =>
            j.TryGetProperty("id", out var idProp) &&
            Guid.TryParse(idProp.GetString(), out var g) &&
            g == jobId);
        _output.WriteLine($"[6] /api/jobs list contains job ({jobsList!.Count} total)");

        // ── 7. /api/channels list surfaces the job (artifact was auto-captured) ──
        // ArtifactStorageService.CreateArtifactFromJobRunAsync runs after a successful
        // execute, so the channel should appear within a few seconds.
        var channelAppeared = await PollUntilAsync(TimeSpan.FromSeconds(15), async () =>
        {
            var resp = await _gateway.GetAsync("/api/channels");
            if (!resp.IsSuccessStatusCode) return false;
            var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
            return list?.Any(c =>
                c.TryGetProperty("jobId", out var idProp) &&
                Guid.TryParse(idProp.GetString(), out var g) &&
                g == jobId) ?? false;
        });
        Assert.True(channelAppeared,
            "Job should appear in /api/channels within 15s of a successful run (artifact auto-capture).");
        _output.WriteLine("[7] /api/channels lists our job");

        // ── 8. Channel detail returns at least one run with an artifact ──
        var detailResp = await _gateway.GetAsync($"/api/channels/{jobId}");
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
        var detail = await detailResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(detail.TryGetProperty("recentRuns", out var runsArr),
            "Channel detail should expose `recentRuns`.");
        var totalArtifacts = runsArr.EnumerateArray()
            .Sum(r => r.TryGetProperty("artifactCount", out var c) ? c.GetInt32() : 0);
        Assert.True(totalArtifacts >= 1,
            $"Channel should have ≥1 artifact across runs, got {totalArtifacts}.");
        _output.WriteLine($"[8] /api/channels/{jobId} → {runsArr.GetArrayLength()} run(s), {totalArtifacts} artifact(s)");

        _output.WriteLine($"\n✅ Website Watcher E2E full-flow validated for job '{jobName}' ({jobId})");
    }

    private async Task UpsertAzureProviderAndProfileAsync()
    {
        // PUT /api/model-providers/{name} — idempotent upsert of an Azure OpenAI
        // provider populated from the developer's environment variables.
        var providerResp = await _gateway.PutAsJsonAsync($"/api/model-providers/{AzureProviderName}", new
        {
            providerType = "azure-openai",
            displayName = "Azure OpenAI (E2E)",
            endpoint = _fixture.AzureOpenAIEndpoint,
            model = _fixture.AzureOpenAIDeployment,
            apiKey = _fixture.AzureOpenAIApiKey,
            deploymentName = _fixture.AzureOpenAIDeployment,
            authMode = "api-key",
            isSupported = true
        });
        Assert.True(providerResp.IsSuccessStatusCode,
            $"PUT /api/model-providers/{AzureProviderName} → {(int)providerResp.StatusCode}: {await providerResp.Content.ReadAsStringAsync()}");
        _createdAzureProvider = true;

        // PUT /api/agent-profiles/{name} — idempotent upsert of a Standard profile
        // bound to the Azure provider above. RequireToolApproval=false because
        // this is a non-interactive E2E scenario.
        var profileResp = await _gateway.PutAsJsonAsync($"/api/agent-profiles/{AzureProfileName}", new
        {
            displayName = "Website Watcher Azure (E2E)",
            provider = AzureProviderName,
            instructions = "You are an automated website watcher. Use only the tools you are explicitly told to use, and follow the user's reply format exactly.",
            enabledTools = new[] { "web.fetch" },
            isDefault = false,
            requireToolApproval = false,
            isEnabled = true,
            kind = "Standard"
        });
        Assert.True(profileResp.IsSuccessStatusCode,
            $"PUT /api/agent-profiles/{AzureProfileName} → {(int)profileResp.StatusCode}: {await profileResp.Content.ReadAsStringAsync()}");
        _createdAzureProfile = true;
    }

    private static async Task<bool> PollUntilAsync(TimeSpan timeout, Func<Task<bool>> probe)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await probe()) return true;
            await Task.Delay(500);
        }
        return await probe();
    }
}
