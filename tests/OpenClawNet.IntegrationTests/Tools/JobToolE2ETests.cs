using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests.Tools;

/// <summary>
/// End-to-end coverage that proves a scheduled <b>Job</b> actually invokes the
/// tool the prompt expects and that <c>JobExecutor</c> faithfully reflects the
/// outcome (success/failure/diagnostics) into the persisted <c>JobRun</c> +
/// <c>JobRunEvent</c> rows.
///
/// These were written in response to a regression where a job built around the
/// "URL Markdown Summary" demo template completed without ever returning the
/// converted Markdown — the same tool worked on the <c>/tools</c> Direct Invoke
/// page (which bypasses the LLM) but failed when wired into a job (which goes
/// through the LLM ⇒ agent runtime ⇒ tool executor path).
///
/// The test infrastructure (<see cref="JobToolE2EWebAppFactory"/>) uses a
/// scriptable model client and an in-memory HTTP handler so each test is fully
/// deterministic and never touches the network or a real LLM. The same factory
/// is reusable for any future tool — see <see cref="ToolE2EHelpers"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class JobToolE2ETests : IClassFixture<JobToolE2EWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly JobToolE2EWebAppFactory _factory;

    public JobToolE2ETests(JobToolE2EWebAppFactory factory)
    {
        _factory = factory;
    }

    // ── markdown_convert (the tool that triggered this test suite) ───────────

    [Fact]
    public async Task Job_UsingMarkdownConvert_InvokesTool_AndCompletesSuccessfully()
    {
        const string url = "https://example.test/article";
        _factory.MarkItDownHttp.RespondWithHtml(url,
            "<html><head><title>Hello</title></head><body><h1>Hi from the test</h1><p>One paragraph.</p></body></html>");

        _factory.Model.SetScript(
            ScriptedTurn.CallTool("markdown_convert", $"{{\"url\":\"{url}\"}}"),
            ScriptedTurn.Final("## Summary\n- The page is a test page titled 'Hello'."));

        var (runId, _) = await ToolE2EHelpers.CreateAndExecuteJobAsync(
            _factory,
            jobName: $"e2e-markdown-{Guid.NewGuid():N}",
            prompt: $"Convert {url} to Markdown using markdown_convert and summarize it.");

        var (run, events) = await ToolE2EHelpers.GetRunAndEventsAsync(_factory, runId);
        run.Status.Should().Be("completed",
            "the agent ran the tool successfully and returned final text — the run must be marked completed");
        run.Result.Should().Contain("Summary",
            "the model's final assistant text is captured in JobRun.Result");

        var toolEvents = events.Where(e => e.Kind == JobRunEventKind.ToolCall).ToList();
        toolEvents.Should().ContainSingle(e => e.ToolName == "markdown_convert",
            "the agent must have invoked the tool the prompt asked for — exactly once");
        toolEvents[0].ResultJson.Should().Contain("Hi from the test",
            "the converted Markdown produced by MarkItDownTool must flow back into the timeline");
    }

    [Fact]
    public async Task Job_UsingMarkdownConvert_HttpError_FlipsRunToFailed_WithDiagnostics()
    {
        const string url = "https://example.test/missing";
        _factory.MarkItDownHttp.RespondWithStatus(url, HttpStatusCode.InternalServerError, "boom");

        _factory.Model.SetScript(
            ScriptedTurn.CallTool("markdown_convert", $"{{\"url\":\"{url}\"}}"),
            ScriptedTurn.Final("Sorry — the tool failed."));

        var (runId, _) = await ToolE2EHelpers.CreateAndExecuteJobAsync(
            _factory,
            jobName: $"e2e-markdown-fail-{Guid.NewGuid():N}",
            prompt: $"Convert {url} to Markdown using markdown_convert.");

        var (run, events) = await ToolE2EHelpers.GetRunAndEventsAsync(_factory, runId);

        // Validates the JobExecutor "promote tool failure to run failure" rule
        // (Helly, 2026-04-25): a tool returning Success=false flips the run
        // even when the model itself produced a final text.
        run.Status.Should().Be("failed",
            "a tool that returned Success=false must flip the run to Failed regardless of model text");
        run.Error.Should().NotBeNullOrWhiteSpace("the tool diagnostics must land on JobRun.Error");
        run.Error.Should().Contain("markdown_convert", "the failing tool name should be in the diagnostics");
        run.Error.Should().Contain("HTTP 500",
            "the underlying HTTP failure must surface — that's what makes the failure card actionable");

        events.Should().Contain(e => e.Kind == JobRunEventKind.ToolCall && e.ToolName == "markdown_convert");
        events.Should().Contain(e => e.Kind == JobRunEventKind.AgentFailed);
    }

    [Fact]
    public async Task Job_UsingMarkdownConvert_LocalUrl_IsRefused_ByTool_AndRunFails()
    {
        // MarkItDownTool refuses localhost / private addresses as a safety check.
        // The agent loop must surface that refusal as a tool failure, and
        // JobExecutor must promote it to a run failure with the refusal message.
        const string url = "http://127.0.0.1:7000/secret";

        _factory.Model.SetScript(
            ScriptedTurn.CallTool("markdown_convert", $"{{\"url\":\"{url}\"}}"),
            ScriptedTurn.Final("Tool refused — done."));

        var (runId, _) = await ToolE2EHelpers.CreateAndExecuteJobAsync(
            _factory,
            jobName: $"e2e-markdown-local-{Guid.NewGuid():N}",
            prompt: $"Convert {url} to Markdown.");

        var (run, _) = await ToolE2EHelpers.GetRunAndEventsAsync(_factory, runId);
        run.Status.Should().Be("failed");
        run.Error.Should().Contain("refused",
            "the tool's safety check must produce an actionable error that lands on the run");
    }

    // ── calculator (proves the helper generalises beyond markdown_convert) ───

    [Fact]
    public async Task Job_UsingCalculator_InvokesTool_AndCompletesSuccessfully()
    {
        _factory.Model.SetScript(
            ScriptedTurn.CallTool("calculator", "{\"expression\":\"2+2\"}"),
            ScriptedTurn.Final("The answer is 4."));

        var (runId, _) = await ToolE2EHelpers.CreateAndExecuteJobAsync(
            _factory,
            jobName: $"e2e-calc-{Guid.NewGuid():N}",
            prompt: "Compute 2+2 using the calculator tool.");

        var (run, events) = await ToolE2EHelpers.GetRunAndEventsAsync(_factory, runId);

        run.Status.Should().Be("completed");
        var toolEvent = events.Single(e => e.Kind == JobRunEventKind.ToolCall);
        toolEvent.ToolName.Should().Be("calculator");
        toolEvent.ResultJson.Should().Contain("= 4",
            "the calculator's evaluation result must be persisted in the timeline");
    }

    // ── negative paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task Job_WhenLLMNeverCallsTool_RunCompletesWithoutToolEvents()
    {
        _factory.Model.SetScript(ScriptedTurn.Final("I considered it and decided no tool was needed."));

        var (runId, _) = await ToolE2EHelpers.CreateAndExecuteJobAsync(
            _factory,
            jobName: $"e2e-no-tool-{Guid.NewGuid():N}",
            prompt: "Tell me a one-line joke.");

        var (run, events) = await ToolE2EHelpers.GetRunAndEventsAsync(_factory, runId);
        run.Status.Should().Be("completed");
        events.Should().NotContain(e => e.Kind == JobRunEventKind.ToolCall,
            "no tool was called, so no ToolCall events should be emitted");
        events.Should().Contain(e => e.Kind == JobRunEventKind.AgentStarted);
        events.Should().Contain(e => e.Kind == JobRunEventKind.AgentCompleted);
    }

    [Fact]
    public async Task Job_WhenLLMCallsUnknownTool_FailureIsRecordedOnRun()
    {
        _factory.Model.SetScript(
            ScriptedTurn.CallTool("nonexistent_tool", "{}"),
            ScriptedTurn.Final("Tried and failed."));

        var (runId, _) = await ToolE2EHelpers.CreateAndExecuteJobAsync(
            _factory,
            jobName: $"e2e-unknown-{Guid.NewGuid():N}",
            prompt: "Trigger a missing tool.");

        var (run, _) = await ToolE2EHelpers.GetRunAndEventsAsync(_factory, runId);
        run.Status.Should().Be("failed",
            "an LLM-requested tool that doesn't exist must surface as a run failure, not a silent completion");
    }
}

/// <summary>
/// Reusable helpers for job-tool E2E tests. New tool tests should look like
/// "configure the canned response, set the script, call CreateAndExecuteJobAsync,
/// assert on GetRunAndEventsAsync".
/// </summary>
public static class ToolE2EHelpers
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Create a Job via the gateway HTTP API and synchronously invoke its
    /// <c>POST /api/jobs/{id}/execute</c>. Returns the resulting JobRun id and
    /// the raw HTTP execute response (useful for asserting status codes).
    /// </summary>
    public static async Task<(Guid runId, HttpResponseMessage executeResponse)> CreateAndExecuteJobAsync(
        JobToolE2EWebAppFactory factory, string jobName, string prompt)
    {
        var client = factory.CreateClient();

        var createPayload = new
        {
            name = jobName,
            prompt
        };
        var createResponse = await client.PostAsJsonAsync("/api/jobs", createPayload, JsonOpts);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            $"the job '{jobName}' must be accepted by the gateway");
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var jobId = created.GetProperty("id").GetGuid();

        var executeResponse = await client.PostAsync($"/api/jobs/{jobId}/execute", content: null);

        // Most-recent run for the job
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await db.JobRuns
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync();

        run.Should().NotBeNull(
            $"executing job '{jobName}' must have created a JobRun (execute returned {(int)executeResponse.StatusCode})");

        return (run!.Id, executeResponse);
    }

    /// <summary>
    /// Load the JobRun + ordered JobRunEvents directly from the DB so tests can
    /// assert on the full persisted shape (Status, Error, Result, tool events).
    /// </summary>
    public static async Task<(JobRun run, IReadOnlyList<JobRunEvent> events)> GetRunAndEventsAsync(
        JobToolE2EWebAppFactory factory, Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var run = await db.JobRuns.SingleAsync(r => r.Id == runId);
        var events = await db.JobRunEvents
            .Where(e => e.JobRunId == runId)
            .OrderBy(e => e.Sequence)
            .ToListAsync();
        return (run, events);
    }
}
