using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenClawNet.Gateway.Endpoints;
using Xunit;

namespace OpenClawNet.IntegrationTests.Jobs.Aspire;

/// <summary>
/// Aspire-orchestrated live e2e: prove the <c>browser</c> tool works end-to-end
/// when the Gateway proxies to an Aspire-orchestrated browser-service (Playwright).
///
/// Prerequisites:
///  - AppHost must build and start (Docker, Ollama, browser-service container/project).
///  - Browser-service must be reachable via Aspire service discovery.
///  - Ollama must be running locally (Gateway falls back to local Ollama).
///
/// Test approach:
///  - Create a Job that prompts the agent to use the browser tool.
///  - Execute the job via Gateway (POST /api/jobs/{id}/execute).
///  - Assert the job completes successfully and the output mentions expected content
///    from the target webpage (e.g., "Example Domain" from https://example.com).
///
/// Local-only by design: These tests are **never** run in GitHub Actions or any
/// hosted CI environment. See .squad/decisions.md "Live tests are local-only".
/// </summary>
public sealed class LiveBrowserToolE2ETests : AspireLiveTestBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Job_UsesBrowserTool_FetchesPage_AndReturnsContent()
    {
        SkipIfAspireUnavailable();

        using var client = CreateGatewayClient();

        // Create a job that prompts the agent to use the browser tool to navigate
        // to https://example.com and report the page title.
        var createRequest = new CreateJobRequest
        {
            Name = $"aspire-browser-e2e-{Guid.NewGuid():N}",
            Prompt = "Use the browser tool to navigate to https://example.com and report the page title or main heading you see on the page."
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

        // Assert the output mentions "Example Domain" (the known title/heading of example.com).
        var output = run.Result!;
        var containsExpectedText = output.Contains("Example Domain", StringComparison.OrdinalIgnoreCase)
                                   || output.Contains("example", StringComparison.OrdinalIgnoreCase);
        containsExpectedText.Should().BeTrue(
            $"output should mention 'Example Domain' or 'example' from the page. Actual: {output}");
    }
}
