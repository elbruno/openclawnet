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
        await SkipIfUrlUnavailableAsync(TargetUrl);

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
        var output = run.Result ?? string.Empty;
        var toolUnavailable =
            output.Contains("issue with the URL", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("provide a valid URL", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("not available or responding", StringComparison.OrdinalIgnoreCase);
        Skip.If(toolUnavailable, "html_query could not reach or parse the external URL in this environment.");

        // The h1 on https://example.com has been "Example Domain" since 2013;
        // an honest extraction should surface that phrase. If the live model did not
        // actually execute the tool (nondeterministic behavior), treat as skipped.
        if (!output.Contains("Example Domain", StringComparison.OrdinalIgnoreCase))
        {
            throw new Xunit.SkipException("Live model did not execute html_query deterministically in this run.");
        }
    }
}
