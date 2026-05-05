using System.Net.Http.Json;
using FluentAssertions;
using OpenClawNet.Gateway.Endpoints;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// Per-tool live e2e for the <c>html_query</c> tool (see
/// <c>src/OpenClawNet.Tools.HtmlQuery/HtmlQueryTool.cs</c> — registered tool name
/// is <c>html_query</c>).
///
/// Flow under test:
///   POST /api/jobs            → create job whose prompt asks the agent to extract
///                               the page <c>h1</c> from https://example.com using
///                               the <c>html_query</c> tool
///   POST /api/jobs/{id}/execute → JobExecutor → live Ollama (qwen2.5:3b) → tool call
///   GET  /api/jobs/{id}/runs/{runId} → JobRun.Status == Completed, output contains
///                                       the literal h1 text "Example Domain"
///
/// Notes:
///  - Skips if Ollama is not reachable on localhost:11434.
///  - Uses <see cref="LiveOllamaWebAppFactory"/> to swap the fake IModelClient for
///    a real <see cref="OllamaModelClient"/> (same pattern as Calculator/FileSystem/
///    MarkItDown e2e tests).
/// </summary>
public sealed class LiveHtmlQueryToolE2ETests : LiveToolE2ETestBase
{
    private const string OllamaEndpoint = "http://localhost:11434";
    private const string OllamaModel = "qwen2.5:3b";
    private const string ToolName = "html_query";
    private const string TargetUrl = "https://example.com";

    public LiveHtmlQueryToolE2ETests(GatewayWebAppFactory factory) : base(factory) { }

    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
    {
        return CreatePreferredLiveFactory(factory, ollamaModel: OllamaModel, ollamaEndpoint: OllamaEndpoint);
    }

    [SkippableFact]
    public async Task Job_UsesHtmlQueryTool_ExtractsExpectedNode()
    {
        await SkipIfPreferredProviderUnavailableAsync(OllamaEndpoint);

        var job = await CreateJobAsync(
            name: "live-html-query-h1",
            prompt: $"Fetch {TargetUrl} and use the `{ToolName}` tool with selector " +
                    "\"h1\" to extract the page's top-level heading. Reply with the " +
                    "extracted h1 text verbatim.",
            toolName: ToolName);

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(3));

        run.Should().NotBeNull();
        run.Status.Should().BeOneOf(new[] { "Completed", "completed" }, because: $"job run failed: {run.Error}");
        run.Error.Should().BeNullOrWhiteSpace();

        // The h1 on https://example.com has been "Example Domain" since 2013;
        // an honest extraction must surface that exact phrase.
        (run.Result ?? string.Empty).Should().Contain(
            "Example Domain",
            because: "html_query on selector 'h1' against example.com must return the page heading");
    }
}
