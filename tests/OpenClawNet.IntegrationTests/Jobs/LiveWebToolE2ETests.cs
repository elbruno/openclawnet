using System.Net.Http.Json;
using FluentAssertions;
using OpenClawNet.Gateway.Endpoints;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// Per-tool live e2e for the <c>web_fetch</c> tool (see
/// <c>src/OpenClawNet.Tools.Web/WebTool.cs</c> — registered tool name is
/// <c>web_fetch</c>).
///
/// Flow under test:
///   POST /api/jobs            → create job whose prompt asks the agent to fetch
///                               https://example.com via the <c>web_fetch</c> tool
///   POST /api/jobs/{id}/execute → JobExecutor → live Ollama (qwen2.5:3b) → tool call
///   GET  /api/jobs/{id}/runs/{runId} → JobRun.Status == Completed, output references
///                                       the well-known "Example Domain" body text
///
/// Notes:
///  - Skips if Ollama is not reachable on localhost:11434.
///  - Uses <see cref="LiveOllamaWebAppFactory"/> to swap the fake IModelClient for
///    a real <see cref="OllamaModelClient"/> (same pattern as Calculator/FileSystem/
///    MarkItDown e2e tests).
/// </summary>
public sealed class LiveWebToolE2ETests : LiveToolE2ETestBase
{
    private const string OllamaEndpoint = "http://localhost:11434";
    private const string OllamaModel = "qwen2.5:3b";
    private const string ToolName = "web_fetch";
    private const string TargetUrl = "https://example.com";

    public LiveWebToolE2ETests(GatewayWebAppFactory factory) : base(factory) { }

    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
    {
        return CreatePreferredLiveFactory(factory, ollamaModel: OllamaModel, ollamaEndpoint: OllamaEndpoint);
    }

    [SkippableFact]
    public async Task Job_UsesWebTool_FetchesUrl_ReturnsContent()
    {
        await SkipIfPreferredProviderUnavailableAsync(OllamaEndpoint);

        var job = await CreateJobAsync(
            name: "live-web-fetch",
            prompt: $"Fetch the page at {TargetUrl} and tell me, in one sentence, " +
                    "what the page is about. Quote at least one phrase from the page body.",
            toolName: ToolName);

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(3));

        run.Should().NotBeNull();
        run.Status.Should().BeOneOf(new[] { "Completed", "completed" }, because: $"job run failed: {run.Error}");
        run.Error.Should().BeNullOrWhiteSpace();

        // example.com's body has been the literal string "Example Domain" since 2013.
        // We accept either the canonical phrase or a close paraphrase the LLM might
        // produce when summarising — but require at least one stable token from the page.
        var output = run.Result ?? string.Empty;
        output.Should().ContainAny(
            new[] { "Example Domain", "example domain", "illustrative examples" },
            because: "the LLM should have invoked web_fetch and surfaced text from example.com");
    }
}
