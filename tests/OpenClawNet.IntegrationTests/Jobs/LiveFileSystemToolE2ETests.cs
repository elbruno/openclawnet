using FluentAssertions;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// Per-tool live e2e: prove the agent loop drives the <c>file_system</c>
/// tool through a real Ollama LLM end-to-end via the public
/// <c>POST /api/jobs/{id}/execute</c> surface.
/// </summary>
public sealed class LiveFileSystemToolE2ETests : LiveToolE2ETestBase
{
    public LiveFileSystemToolE2ETests(GatewayWebAppFactory factory) : base(factory) { }

    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
        => CreatePreferredLiveFactory(factory, ollamaModel: "qwen2.5:3b");

    [SkippableFact]
    public async Task Job_UsesFileSystemTool_ListsExpectedFiles()
    {
        await SkipIfPreferredProviderUnavailableAsync();

        // Arrange — drop a known marker file in a temp directory the agent
        // must list. The marker name is deliberately unusual so a casual
        // hallucination cannot accidentally produce it.
        var sandbox = Path.Combine(Path.GetTempPath(),
            "openclawnet-fslive-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(sandbox);
        var marker = $"openclawnet-marker-{Guid.NewGuid():N}.txt";
        await File.WriteAllTextAsync(Path.Combine(sandbox, marker), "marker file contents");

        try
        {
            var prompt =
                $"List the files in the directory '{sandbox}'. " +
                "Respond with the raw tool output verbatim so the user can see every file name.";

            var job = await CreateJobAsync(
                name: $"e2e-fs-{Guid.NewGuid():N}",
                prompt: prompt,
                toolName: "file_system");

            // Act
            var runId = await ExecuteJobAsync(job.Id);
            var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(2));

            // Assert
            AssertJobRunSucceeded(run, expectedOutputContains: marker);
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { /* best effort */ }
        }
    }
}
