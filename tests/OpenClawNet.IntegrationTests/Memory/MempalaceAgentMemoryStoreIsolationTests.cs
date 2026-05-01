using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Memory;
using OpenClawNet.Storage;

namespace OpenClawNet.IntegrationTests.Memory;

/// <summary>
/// Per-agent isolation tests for <see cref="MempalaceAgentMemoryStore"/> (issue #98, Phase 1).
///
/// Runs the real <c>MemPalace.Backends.Sqlite</c> engine against a temp directory and uses a
/// deterministic fake <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> so the test stays
/// CI-friendly (no ONNX model download). The isolation contract is enforced architecturally —
/// each agent gets its own SQLite palace file under <c>StorageOptions.AgentFolderForName</c>.
/// </summary>
public sealed class MempalaceAgentMemoryStoreIsolationTests : IDisposable
{
    private readonly string _root;
    private readonly StorageOptions _storageOptions;

    public MempalaceAgentMemoryStoreIsolationTests()
    {
        _root = Path.Combine(
            Path.GetDirectoryName(typeof(MempalaceAgentMemoryStoreIsolationTests).Assembly.Location)!,
            "mempalace-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _storageOptions = new StorageOptions { RootPath = _root };
        _storageOptions.EnsureDirectories();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task TwoAgents_StoreAndSearch_AreFullyIsolated()
    {
        // Arrange — single store instance, two distinct agentIds.
        await using var store = new MempalaceAgentMemoryStore(
            _storageOptions,
            new DeterministicEmbeddingGenerator(),
            NullLogger<MempalaceAgentMemoryStore>.Instance);

        const string agentA = "alice";
        const string agentB = "bob";

        // Act — each agent stores three distinct memories.
        var aliceIds = new List<string>
        {
            await store.StoreAsync(agentA, new MemoryEntry("Alice loves orange cats")),
            await store.StoreAsync(agentA, new MemoryEntry("Alice prefers tea over coffee")),
            await store.StoreAsync(agentA, new MemoryEntry("Alice lives in Madrid")),
        };

        var bobIds = new List<string>
        {
            await store.StoreAsync(agentB, new MemoryEntry("Bob plays bass guitar")),
            await store.StoreAsync(agentB, new MemoryEntry("Bob runs marathons on weekends")),
            await store.StoreAsync(agentB, new MemoryEntry("Bob lives in Toronto")),
        };

        // Assert 1 — palace files live in distinct, agent-scoped folders on disk.
        var aliceDir = _storageOptions.AgentFolderForName(agentA);
        var bobDir = _storageOptions.AgentFolderForName(agentB);
        File.Exists(Path.Combine(aliceDir, "palace.db")).Should().BeTrue("Alice's palace must be isolated to her agent folder");
        File.Exists(Path.Combine(bobDir, "palace.db")).Should().BeTrue("Bob's palace must be isolated to his agent folder");
        Path.GetFullPath(aliceDir).Should().NotBe(Path.GetFullPath(bobDir));

        // Assert 2 — searching either agent only ever returns that agent's memory IDs.
        var aliceHits = await store.SearchAsync(agentA, "what does Alice like to drink?", topK: 10);
        aliceHits.Should().NotBeEmpty();
        aliceHits.Select(h => h.Id).Should().BeSubsetOf(aliceIds);
        aliceHits.Select(h => h.Id).Should().NotIntersectWith(bobIds);
        aliceHits.Select(h => h.Content).Should().OnlyContain(c => c.StartsWith("Alice"));

        var bobHits = await store.SearchAsync(agentB, "tell me about Bob's hobbies", topK: 10);
        bobHits.Should().NotBeEmpty();
        bobHits.Select(h => h.Id).Should().BeSubsetOf(bobIds);
        bobHits.Select(h => h.Id).Should().NotIntersectWith(aliceIds);
        bobHits.Select(h => h.Content).Should().OnlyContain(c => c.StartsWith("Bob"));

        // Assert 3 — using one agent's memoryId against the other agent is a no-op (no cross-agent delete).
        await store.DeleteAsync(agentB, aliceIds[0]); // should silently succeed (Bob has no such id)
        var aliceHitsAfter = await store.SearchAsync(agentA, "Alice tea", topK: 10);
        aliceHitsAfter.Select(h => h.Id).Should().Contain(aliceIds[0], "Bob's delete must not touch Alice's data");

        // Assert 4 — a third unknown agent sees an empty result set (no cross-leak via untouched palace).
        var strangerHits = await store.SearchAsync("carol", "Alice tea Bob bass", topK: 10);
        strangerHits.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_BeforeAnyStore_ReturnsEmpty()
    {
        await using var store = new MempalaceAgentMemoryStore(
            _storageOptions,
            new DeterministicEmbeddingGenerator(),
            NullLogger<MempalaceAgentMemoryStore>.Instance);

        var hits = await store.SearchAsync("ghost-agent", "anything", topK: 5);
        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheTargetMemoryForTheAgent()
    {
        await using var store = new MempalaceAgentMemoryStore(
            _storageOptions,
            new DeterministicEmbeddingGenerator(),
            NullLogger<MempalaceAgentMemoryStore>.Instance);

        const string agent = "delete-me";
        var keepId = await store.StoreAsync(agent, new MemoryEntry("keep this memory please"));
        var dropId = await store.StoreAsync(agent, new MemoryEntry("delete this memory now"));

        await store.DeleteAsync(agent, dropId);

        var hits = await store.SearchAsync(agent, "memory", topK: 10);
        hits.Select(h => h.Id).Should().Contain(keepId);
        hits.Select(h => h.Id).Should().NotContain(dropId);
    }

    /// <summary>
    /// Deterministic 16-dimensional embedding generator: hashes the input string into a
    /// reproducible unit-norm vector. Sufficient to exercise the storage + isolation paths
    /// without an ONNX model download.
    /// </summary>
    private sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private const int Dim = 16;

        public EmbeddingGeneratorMetadata Metadata { get; } = new("deterministic-test", null, "deterministic-16");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = new List<Embedding<float>>();
            foreach (var value in values)
            {
                list.Add(new Embedding<float>(Hash(value)) { CreatedAt = DateTimeOffset.UtcNow });
            }
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType?.IsInstanceOfType(this) == true ? this : null;

        public void Dispose() { }

        private static float[] Hash(string text)
        {
            var vec = new float[Dim];
            unchecked
            {
                var h = 2166136261u;
                foreach (var c in text)
                {
                    h = (h ^ c) * 16777619u;
                }
                var rng = new Random((int)h);
                double sumSq = 0;
                for (var i = 0; i < Dim; i++)
                {
                    var v = rng.NextDouble() * 2 - 1;
                    vec[i] = (float)v;
                    sumSq += v * v;
                }
                var norm = (float)Math.Sqrt(sumSq);
                if (norm > 0)
                {
                    for (var i = 0; i < Dim; i++)
                    {
                        vec[i] /= norm;
                    }
                }
            }
            return vec;
        }
    }
}
