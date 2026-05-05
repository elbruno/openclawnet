using FluentAssertions;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// Per-tool live e2e: prove the agent loop drives the <c>embeddings</c>
/// tool through a real Ollama LLM. Two scenarios: (1) <c>embed</c> action
/// to generate a vector for single text, (2) <c>search</c> action to rank
/// candidates by semantic similarity.
///
/// <para>
/// <b>First-run model download:</b> The <see cref="OpenClawNet.Tools.Embeddings.EmbeddingsTool"/>
/// uses ElBruno.LocalEmbeddings (ONNX-based, no external service dependency). On first
/// invocation the ONNX model is downloaded (~80MB-2GB depending on model) and cached to
/// <c>{StorageOptions.ModelsPath}/embeddings/</c>. Subsequent executions are fast (cached).
/// The <see cref="LiveToolE2ETestBase"/> base class already configures a 5-minute
/// <see cref="HttpClient.Timeout"/> which covers both the model download and LLM inference.
/// </para>
/// </summary>
public sealed class LiveEmbeddingsToolE2ETests : LiveToolE2ETestBase
{
    public LiveEmbeddingsToolE2ETests(GatewayWebAppFactory factory) : base(factory) { }

    // Embed test always uses Ollama: it exercises the EmbeddingsTool's `embed` action
    // (which runs LocalEmbeddings ONNX in-process); no need to burn AOAI quota for it.
    // Keeping it on Ollama also guarantees the per-class fixture isn't routed to AOAI
    // when LIVE_TEST_PREFER_AOAI=1 (the Search variant lives in its own class).
    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
        => new LiveOllamaWebAppFactory(model: "qwen2.5:3b");

    [SkippableFact]
    public async Task Job_UsesEmbeddingsTool_Embed_ReturnsDimensions()
    {
        await SkipIfOllamaUnavailableAsync();

        var job = await CreateJobAsync(
            name: $"e2e-embed-{Guid.NewGuid():N}",
            prompt: "Use the embeddings tool with action='embed' to generate an embedding for the text 'hello world'. " +
                    "Report the vector dimensions in your final answer.",
            toolName: "embeddings");

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(3));

        // The EmbeddingsTool returns "Dimensions: {N}" in its output (see EmbeddingsTool.cs line 88).
        // Typical embedding models return 384, 512, 768, or 1024 dimensions. We assert the output
        // mentions "Dimensions:" followed by a number >= 384 (sanity check, not a strict match).
        AssertJobRunSucceeded(run, expectedOutputContains: "Dimensions");

        // Further sanity: the result should contain a numeric dimension value (e.g., "384", "768")
        var output = run.Result ?? string.Empty;
        var hasDimensionNumber = System.Text.RegularExpressions.Regex.IsMatch(
            output,
            @"\b(384|512|768|1024|[3-9]\d{2,})\b", // matches typical embedding dimensions
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        hasDimensionNumber.Should().BeTrue(
            $"output should mention a typical embedding dimension (384+). Actual: {output}");
    }
}

/// <summary>
/// Sibling class for the Search scenario. Lives in its own class so it can opt
/// into <see cref="LiveToolE2ETestBase.CreatePreferredLiveFactory"/> (which honors
/// <c>LIVE_TEST_PREFER_AOAI=1</c>) without dragging the Embed scenario into AOAI.
/// </summary>
public sealed class LiveEmbeddingsToolSearchE2ETests : LiveToolE2ETestBase
{
    public LiveEmbeddingsToolSearchE2ETests(GatewayWebAppFactory factory) : base(factory) { }

    protected override GatewayWebAppFactory LiveFactory(GatewayWebAppFactory factory)
        => CreatePreferredLiveFactory(factory, ollamaModel: "qwen2.5:3b");

    [SkippableFact]
    public async Task Job_UsesEmbeddingsTool_Search_RanksCorrectCandidate()
    {
        await SkipIfPreferredProviderUnavailableAsync();

        // Arrange: clear semantic match — "fluffy pet" should rank "cat" above "car" and "rocket"
        var job = await CreateJobAsync(
            name: $"e2e-search-{Guid.NewGuid():N}",
            prompt: "Use the embeddings tool with action='search' to find which of these candidates is " +
                    "most similar to the query 'fluffy pet': ['cat', 'car', 'rocket']. " +
                    "Report the top-ranked candidate and its similarity score in your final answer.",
            toolName: "embeddings");

        var runId = await ExecuteJobAsync(job.Id);
        var run = await WaitForJobAsync(job.Id, runId, TimeSpan.FromMinutes(3));

        // The EmbeddingsTool's search action returns formatted output with:
        // "# Top N matches for: <query>" followed by lines of "- <score>  <text>".
        // The LLM is asked to echo the result, so "cat" should appear in the final output
        // with the highest score (semantic similarity is deterministic for these clear-cut cases).
        AssertJobRunSucceeded(run, expectedOutputContains: "cat");

        // Sanity: output should NOT mention "car" or "rocket" as the top match. If the LLM
        // echoes the full tool output (which it typically does), we can be more lenient and
        // just check that "cat" is present; the exact ranking is less critical for e2e validation.
        var output = run.Result ?? string.Empty;
        output.Should().Contain("cat", "cat should be the top semantic match for 'fluffy pet'");

        // Optional: if the output includes score formatting, "cat" should appear before "car"/"rocket"
        // (index-based ordering sanity). This is not strictly necessary but adds confidence.
        var catIndex = output.IndexOf("cat", StringComparison.OrdinalIgnoreCase);
        var carIndex = output.IndexOf("car", StringComparison.OrdinalIgnoreCase);
        var rocketIndex = output.IndexOf("rocket", StringComparison.OrdinalIgnoreCase);

        if (catIndex >= 0 && carIndex >= 0)
        {
            catIndex.Should().BeLessThan(carIndex,
                "'cat' should appear before 'car' in the ranked output (semantic relevance)");
        }
        if (catIndex >= 0 && rocketIndex >= 0)
        {
            catIndex.Should().BeLessThan(rocketIndex,
                "'cat' should appear before 'rocket' in the ranked output (semantic relevance)");
        }
    }
}
