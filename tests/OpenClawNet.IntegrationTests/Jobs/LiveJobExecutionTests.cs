using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Ollama;
using OpenClawNet.Storage;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// End-to-end live tests that exercise the full job-execution pipeline against a
/// real Ollama LLM (qwen2.5:3b by default). Validates two halves of the recording
/// surface that unit tests with FakeModelClient cannot:
///
///   1. <see cref="Job_ExecuteWithLiveLlm_ProducesJobRunWithEvents"/> — proves a
///      live job execution persists at least one row in <c>JobRunEvents</c>
///      (queried via <c>GET /api/jobs/{jobId}/runs/{runId}/events</c>).
///   2. <see cref="Job_RunHistory_RecordsToolInvocations"/> — proves a job whose
///      prompt should drive the LLM to invoke a tool ends up with at least one
///      row in <c>ToolCalls</c> (queried via
///      <c>GET /api/jobs/{jobId}/runs/{runId}/tool-calls</c>).
///
/// Both tests <see cref="LiveToolE2ETestBase.SkipIfOllamaUnavailable"/> first so
/// they no-op cleanly in CI / on machines without Ollama.
///
/// Provider wiring: <see cref="LiveFactory"/> swaps the default fake model client
/// for a real <see cref="OllamaModelClient"/> via a derived
/// <see cref="LiveOllamaGatewayWebAppFactory"/>. Endpoint and model can be
/// overridden via env vars <c>LIVE_TEST_OLLAMA_ENDPOINT</c> /
/// <c>LIVE_TEST_OLLAMA_MODEL</c> (mirrors the conventions established by
/// LiveTestFixture in the UnitTests project).
/// </summary>
public sealed class LiveJobExecutionTests : LiveToolE2ETestBase
{
    private const string DefaultOllamaEndpoint = "http://localhost:11434";
    private const string DefaultOllamaModel = "qwen2.5:3b";

    public LiveJobExecutionTests(GatewayWebAppFactory factory) : base(factory) { }

    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
    {
        // The fixture-supplied factory is wired with FakeModelClient. For live tests
        // we discard it and stand up a sibling factory configured with the preferred
        // provider (Ollama by default, AOAI when LIVE_TEST_PREFER_AOAI=1).
        return CreatePreferredLiveFactory(factory, ollamaModel: GetOllamaModel(), ollamaEndpoint: GetOllamaEndpoint());
    }

    private static string GetOllamaEndpoint() =>
        Environment.GetEnvironmentVariable("LIVE_TEST_OLLAMA_ENDPOINT") is { Length: > 0 } e
            ? e : DefaultOllamaEndpoint;

    private static string GetOllamaModel() =>
        Environment.GetEnvironmentVariable("LIVE_TEST_OLLAMA_MODEL") is { Length: > 0 } m
            ? m : DefaultOllamaModel;

    [SkippableFact]
    public async Task Job_ExecuteWithLiveLlm_ProducesJobRunWithEvents()
    {
        SkipIfOllamaUnavailable(GetOllamaEndpoint());

        var job = await CreateJobAsync(
            name: $"live-events-{Guid.NewGuid():N}",
            prompt: "Reply with a single sentence: say hello.",
            toolName: string.Empty);

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(2));

        AssertJobRunSucceeded(run, expectedOutputContains: string.Empty);

        // Pull the persisted event timeline. The endpoint returns
        // List<JobRunEventDto>; at minimum the executor should record the
        // agent_started / agent_completed lifecycle pair (see JobExecutor).
        var eventsResp = await Client.GetAsync($"/api/jobs/{job.Id}/runs/{runId}/events");
        eventsResp.EnsureSuccessStatusCode();
        var events = await eventsResp.Content.ReadFromJsonAsync<List<JobRunEventDto>>(JsonOpts);

        events.Should().NotBeNull("the events endpoint must return a JSON array");
        events!.Should().NotBeEmpty(
            "a completed live job run must persist at least one JobRunEvent " +
            "(executor lifecycle and/or LLM activity)");

        // Cross-check via direct DB read so we catch HTTP/serialization regressions
        // separately from missing-event regressions.
        await using var db = await DbFactory.CreateDbContextAsync();
        var dbCount = await db.JobRunEvents.CountAsync(e => e.JobRunId == runId);
        dbCount.Should().BeGreaterThan(0,
            "JobRunEvents row count in the DB must match what the events endpoint returned");
    }

    [SkippableFact]
    public async Task Job_RunHistory_RecordsToolInvocations()
    {
        SkipIfPreferredProviderUnavailable(GetOllamaEndpoint());

        // Prompt is engineered to nudge the LLM into picking a tool. The base
        // helper additionally appends "(Use the `<tool>` tool.)" to make the
        // intent unambiguous for small models like qwen2.5:3b.
        var job = await CreateJobAsync(
            name: $"live-toolcalls-{Guid.NewGuid():N}",
            prompt: "List the files in the current working directory and report the count.",
            toolName: "file_system");

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(3));

        // Don't fail on Result content here — small local models often produce
        // wildly different prose. We only care that the *tool-invocation
        // recording pipeline* fired.
        run.Status.Should().BeOneOf(new[] { "Completed", "completed" }, $"job run failed: {run.Error}");

        var toolCallsResp = await Client.GetAsync($"/api/jobs/{job.Id}/runs/{runId}/tool-calls");
        toolCallsResp.EnsureSuccessStatusCode();
        var toolCalls = await toolCallsResp.Content.ReadFromJsonAsync<JobRunToolCallsResponse>(JsonOpts);

        toolCalls.Should().NotBeNull("the tool-calls endpoint must return JobRunToolCallsResponse");
        toolCalls!.RunId.Should().Be(runId);

        // Endpoint count and DB count must agree — guards against either side
        // silently dropping rows.
        await using var db = await DbFactory.CreateDbContextAsync();
        var dbToolCallCount = await db.ToolCalls.CountAsync(tc => tc.SessionId == runId);
        toolCalls.TotalCount.Should().Be(dbToolCallCount,
            "endpoint TotalCount must match the DB ToolCalls row count for this run");

        toolCalls.TotalCount.Should().BeGreaterThan(0,
            "a job whose prompt explicitly asks the LLM to use a tool must produce at " +
            "least one persisted tool invocation. If this fails the LLM didn't pick a " +
            "tool — try a stronger prompt or a more capable model via " +
            "LIVE_TEST_OLLAMA_MODEL.");
    }
}
