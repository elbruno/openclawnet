using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenClawNet.Gateway.Endpoints;
using Xunit;

namespace OpenClawNet.IntegrationTests.Jobs.Aspire;

/// <summary>
/// Aspire-orchestrated live e2e: prove the <c>shell</c> tool works end-to-end
/// when the Gateway proxies to an Aspire-orchestrated shell-service.
///
/// Prerequisites:
///  - AppHost must build and start (Docker, Ollama, shell-service project).
///  - Shell-service must be reachable via Aspire service discovery.
///  - Ollama must be running locally (Gateway falls back to local Ollama).
///
/// Test approach:
///  - Create a Job that prompts the agent to use the shell tool to run a safe
///    command (e.g., `echo aspire-harness-ok` or `dir` / `ls` depending on OS).
///  - Execute the job via Gateway (POST /api/jobs/{id}/execute).
///  - Assert the job completes successfully and the output contains the expected
///    command output.
///
/// Notes on shell-service safety policy:
///  - The shell-service blocks dangerous commands (rm, del, shutdown, etc.) via
///    a blocklist (see src\OpenClawNet.Services.Shell\Endpoints\ShellEndpoints.cs).
///  - Safe commands like `echo`, `dir` (Windows), `ls` (Linux), `pwd` are allowed.
///  - This test uses `echo aspire-harness-ok` which works on both Windows and Linux.
///
/// Local-only by design: These tests are **never** run in GitHub Actions or any
/// hosted CI environment. See .squad/decisions.md "Live tests are local-only".
/// </summary>
public sealed class LiveShellToolE2ETests : AspireLiveTestBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Job_UsesShellTool_RunsCommand_AndReturnsOutput()
    {
        SkipIfAspireUnavailable();

        using var client = CreateGatewayClient();

        // Create a job that prompts the agent to use the shell tool to run a
        // simple echo command. This is safe (not on the blocklist) and works
        // on both Windows and Linux.
        var createRequest = new CreateJobRequest
        {
            Name = $"aspire-shell-e2e-{Guid.NewGuid():N}",
            Prompt = "Use the shell tool to run the command `echo aspire-harness-ok` and report the output you see."
        };

        var createResponse = await client.PostAsJsonAsync("/api/jobs", createRequest, JsonOpts);
        createResponse.EnsureSuccessStatusCode();
        var job = await createResponse.Content.ReadFromJsonAsync<JobDto>(JsonOpts);
        job.Should().NotBeNull();

        // Execute the job (synchronous call — blocks until LLM finishes).
        var executeResponse = await client.PostAsync($"/api/jobs/{job!.Id}/execute", null);
        executeResponse.EnsureSuccessStatusCode();
        var executeResult = await executeResponse.Content.ReadFromJsonAsync<JobExecutionResponse>(JsonOpts);
        executeResult.Should().NotBeNull();
        executeResult!.RunId.Should().NotBeEmpty();

        // Poll for job completion (should already be done since /execute is synchronous,
        // but we verify the JobRun status anyway).
        var runId = executeResult.RunId;
        var pollAttempts = 0;
        var maxAttempts = 60; // 60 * 5s = 5 minutes max
        JobRunDto? run = null;

        while (pollAttempts < maxAttempts)
        {
            var runResponse = await client.GetAsync($"/api/jobs/{job.Id}/runs/{runId}");
            runResponse.EnsureSuccessStatusCode();
            run = await runResponse.Content.ReadFromJsonAsync<JobRunDto>(JsonOpts);

            if (run?.Status == "completed" || run?.Status == "failed")
                break;

            await Task.Delay(TimeSpan.FromSeconds(5));
            pollAttempts++;
        }

        // Assert the job completed successfully.
        run.Should().NotBeNull();
        run!.Status.Should().Be("completed", "the job should complete successfully");
        run.Result.Should().NotBeNullOrWhiteSpace("the job should produce output");

        // Assert the output contains "aspire-harness-ok" (the expected output from the echo command).
        var output = run.Result!;
        output.Should().Contain("aspire-harness-ok",
            $"output should contain the echo result. Actual: {output}");
    }
}
