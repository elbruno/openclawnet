using FluentAssertions;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// Per-tool live e2e: prove the agent loop drives the <c>calculator</c>
/// tool through a real Ollama LLM and the numeric result flows back into
/// the final assistant message.
/// </summary>
public sealed class LiveCalculatorToolE2ETests : LiveToolE2ETestBase
{
    public LiveCalculatorToolE2ETests(GatewayWebAppFactory factory) : base(factory) { }

    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
        => new LiveOllamaWebAppFactory(model: "qwen2.5:3b");

    [SkippableFact]
    public async Task Job_UsesCalculatorTool_ReturnsCorrectAnswer()
    {
        await SkipIfOllamaUnavailableAsync();

        var job = await CreateJobAsync(
            name: $"e2e-calc-{Guid.NewGuid():N}",
            prompt: "What is 17 * 23? Use the calculator tool and include the numeric result in your final answer.",
            toolName: "calculator");

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(2));

        // 17 * 23 = 391. The calculator tool itself emits "17 * 23 = 391"
        // and we expect the LLM to echo the number in the final answer.
        AssertJobRunSucceeded(run, expectedOutputContains: "391");
    }
}
