using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Embeddings;
using Xunit;

namespace OpenClawNet.UnitTests.Tools;

/// <summary>
/// Issue #105 — EmbeddingsTool must consume the DI-registered
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> instead of constructing
/// its own <c>LocalEmbeddingGenerator</c>. These tests pin the new constructor
/// shape and prove ExecuteAsync delegates to the injected generator instead of
/// reaching for a side instance.
/// </summary>
[Trait("Category", "Unit")]
public class EmbeddingsToolDiTests
{
    [Fact]
    public void Ctor_RequiresIEmbeddingGenerator()
    {
        // Pin the constructor shape — regression guard for issue #105.
        var ctors = typeof(EmbeddingsTool).GetConstructors();
        ctors.Should().HaveCount(1);
        var paramTypes = ctors[0].GetParameters().Select(p => p.ParameterType).ToArray();
        paramTypes.Should().Contain(typeof(IEmbeddingGenerator<string, Embedding<float>>),
            "the tool must take the DI-registered embedding generator instead of building one internally");
    }

    [Fact]
    public async Task ExecuteAsync_Embed_DelegatesToInjectedGenerator()
    {
        var generator = new RecordingEmbeddingGenerator(dimensions: 384);
        var tool = new EmbeddingsTool(generator, NullLogger<EmbeddingsTool>.Instance);

        var input = new ToolInput
        {
            ToolName = "embeddings",
            RawArguments = "{\"action\":\"embed\",\"text\":\"hello world\"}"
        };

        var result = await tool.ExecuteAsync(input);

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain("Dimensions: 384");
        generator.GenerateCalls.Should().BeGreaterThan(0,
            "EmbeddingsTool must use the injected IEmbeddingGenerator for the embed action");
    }

    [Fact]
    public async Task ExecuteAsync_Search_RanksCandidatesViaInjectedGenerator()
    {
        var generator = new RecordingEmbeddingGenerator(dimensions: 8);
        var tool = new EmbeddingsTool(generator, NullLogger<EmbeddingsTool>.Instance);

        var input = new ToolInput
        {
            ToolName = "embeddings",
            RawArguments = "{\"action\":\"search\",\"text\":\"query\",\"candidates\":[\"alpha\",\"beta\",\"gamma\"],\"topK\":2}"
        };

        var result = await tool.ExecuteAsync(input);

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain("matches for");
        generator.GenerateCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_Search_FailsOnMissingCandidates()
    {
        var generator = new RecordingEmbeddingGenerator(dimensions: 8);
        var tool = new EmbeddingsTool(generator, NullLogger<EmbeddingsTool>.Instance);

        var input = new ToolInput
        {
            ToolName = "embeddings",
            RawArguments = "{\"action\":\"search\",\"text\":\"q\"}"
        };

        var result = await tool.ExecuteAsync(input);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("candidates");
    }

    /// <summary>
    /// Minimal in-memory <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> that
    /// hashes the input string into a deterministic vector. Sufficient to exercise
    /// the EmbeddingsTool code paths (embed + search) without loading ONNX.
    /// </summary>
    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly int _dimensions;
        public int GenerateCalls { get; private set; }

        public RecordingEmbeddingGenerator(int dimensions) => _dimensions = dimensions;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            var list = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var v in values)
            {
                var seed = v?.GetHashCode(StringComparison.Ordinal) ?? 0;
                var rng = new Random(seed);
                var vec = new float[_dimensions];
                for (int i = 0; i < _dimensions; i++) vec[i] = (float)(rng.NextDouble() - 0.5);
                list.Add(new Embedding<float>(vec));
            }
            return Task.FromResult(list);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
