using FluentAssertions;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// Per-tool live e2e + REGRESSION coverage for the markitdown-in-jobs bug
/// Bruno reported (markdown_convert worked when invoked directly but blew
/// up inside JobExecutor). This test creates a job whose prompt asks the
/// agent to convert a stable URL to Markdown via the <c>markdown_convert</c>
/// tool and asserts the run completes without error and produces something
/// markdown-shaped.
///
/// <para>
/// We use <c>https://example.com</c> because it is hosted by IANA, has a
/// stable, tiny HTML body, and is the canonical "always-available" URL.
/// The page has the literal text <c>Example Domain</c> which we sanity-check.
/// </para>
///
/// <para>
/// Note: <see cref="OpenClawNet.Tools.MarkItDown.MarkItDownTool"/> hard-blocks
/// localhost / RFC1918 hosts (SSRF guard), so a localhost stub would be
/// rejected by the tool itself. If example.com is unreachable from the test
/// machine the run will fail and surface a clear network error rather than
/// silently passing.
/// </para>
/// </summary>
public sealed class LiveMarkItDownToolE2ETests : LiveToolE2ETestBase
{
    public LiveMarkItDownToolE2ETests(GatewayWebAppFactory factory) : base(factory) { }

    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
        => CreatePreferredLiveFactory(factory, ollamaModel: "qwen2.5:3b");

    [SkippableFact]
    public async Task Job_UsesMarkItDownTool_ConvertsUrl_DoesNotFail()
    {
        await SkipIfPreferredProviderUnavailableAsync();

        const string url = "https://example.com";

        var job = await CreateJobAsync(
            name: $"e2e-md-{Guid.NewGuid():N}",
            prompt:
                $"Convert the page at {url} to Markdown. " +
                "Return the Markdown output verbatim so the user can read the converted page.",
            toolName: "markdown_convert");

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(2));

        // First and most important assertion: the run must NOT have failed.
        // The original Bruno-reported regression manifested as a Failed
        // JobRun with a tool-pipeline exception in run.Error even though
        // the markdown_convert tool worked when invoked directly.
        AssertJobRunSucceeded(run, expectedOutputContains: string.Empty);

        // The tool prefixes its successful output with "# Source: <url>"
        // (see MarkItDownTool.ExecuteAsync). The model is asked to echo the
        // markdown verbatim, so the final job output should contain either
        // that header or the page's signature text.
        var output = run.Result ?? string.Empty;
        var looksLikeMarkdown =
            output.Contains("# Source:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Example Domain", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("# ", StringComparison.Ordinal); // any markdown heading

        looksLikeMarkdown.Should().BeTrue(
            "the markdown_convert tool should produce markdown-shaped output " +
            "when invoked through the job pipeline (regression: Bruno hit a " +
            "case where markdown_convert worked standalone but failed in jobs). " +
            $"Actual output: {output}");
    }
}
