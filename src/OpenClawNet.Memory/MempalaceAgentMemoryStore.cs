using System.Collections.Concurrent;
using System.Text.Json;
using MemPalace.Core.Backends;
using MemPalace.Core.Model;
using MemPalace.Backends.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Memory;

/// <summary>
/// MempalaceNet-backed implementation of <see cref="IAgentMemoryStore"/> (issue #98, Phase 1).
/// </summary>
/// <remarks>
/// <para>
/// Per-agent isolation strategy: each <c>agentId</c> maps to its own MempalaceNet
/// <see cref="PalaceRef"/> rooted under <see cref="StorageOptions.AgentFolderForName(string)"/>.
/// MempalaceNet stores each palace in a separate SQLite file (<c>palace.db</c>), so two agents
/// can never read each other's vectors — isolation is at the file boundary, not a query filter.
/// </para>
/// <para>
/// API delta (vs. memory-service-proposal.md §6/§14): MemPalace 0.14 exposes a flat
/// <c>palace + collection</c> hierarchy (<see cref="IBackend"/> / <see cref="ICollection"/>);
/// the original "Wings/Rooms/Drawers" naming surfaces only in the CLI/mining layer. We collapse
/// the hierarchy to "one palace per agent, one collection per agent" — a stronger isolation
/// model than the originally proposed shared-collection-with-filter approach. Recorded in
/// <c>.squad/decisions.md</c>.
/// </para>
/// </remarks>
public sealed class MempalaceAgentMemoryStore : IAgentMemoryStore, IAsyncDisposable
{
    private const string CollectionName = "memories";

    private readonly StorageOptions _storageOptions;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly string _embedderModelName;
    private readonly ILogger<MempalaceAgentMemoryStore> _logger;
    private readonly SqliteBackend _backend;
    private readonly ConcurrentDictionary<string, Lazy<Task<ICollection>>> _collections = new(StringComparer.Ordinal);
    private readonly MeaiEmbedderAdapter _embedderAdapter;
    private bool _disposed;

    public MempalaceAgentMemoryStore(
        StorageOptions storageOptions,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<MempalaceAgentMemoryStore>? logger = null,
        string embedderModelName = "sentence-transformers/all-MiniLM-L6-v2")
    {
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _embedderModelName = embedderModelName;
        _logger = logger ?? NullLogger<MempalaceAgentMemoryStore>.Instance;

        // SqliteBackend requires a baseDirectory, but we override per-palace via PalaceRef.LocalPath
        // so each agent's db lives under StorageOptions.AgentFolderForName(agentId).
        _backend = new SqliteBackend(_storageOptions.AgentsPath);
        _embedderAdapter = new MeaiEmbedderAdapter(_embeddingGenerator, _embedderModelName);
    }

    public async Task<string> StoreAsync(string agentId, MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Content);
        ThrowIfDisposed();

        var collection = await GetCollectionAsync(agentId, cancellationToken).ConfigureAwait(false);

        var embeddings = await _embedderAdapter.EmbedAsync(new[] { entry.Content }, cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        var metadata = BuildMetadata(entry);

        var record = new EmbeddedRecord(
            Id: id,
            Document: entry.Content,
            Metadata: metadata,
            Embedding: embeddings[0]);

        await collection.AddAsync(new[] { record }, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Stored memory {MemoryId} for agent {AgentId}", id, agentId);
        return id;
    }

    public async Task<IReadOnlyList<MemoryHit>> SearchAsync(string agentId, string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        ThrowIfDisposed();

        ICollection collection;
        try
        {
            collection = await GetCollectionAsync(agentId, cancellationToken).ConfigureAwait(false);
        }
        catch (MemPalace.Core.Errors.PalaceNotFoundException)
        {
            // Agent has never stored a memory — return empty rather than throwing.
            return Array.Empty<MemoryHit>();
        }

        var queryEmbeddings = await _embedderAdapter.EmbedAsync(new[] { query }, cancellationToken).ConfigureAwait(false);

        var result = await collection.QueryAsync(
            queryEmbeddings: queryEmbeddings,
            nResults: topK,
            include: IncludeFields.Documents | IncludeFields.Metadatas | IncludeFields.Distances,
            ct: cancellationToken).ConfigureAwait(false);

        if (result.Ids.Count == 0)
        {
            return Array.Empty<MemoryHit>();
        }

        var ids = result.Ids[0];
        var docs = result.Documents[0];
        var metas = result.Metadatas[0];
        var dists = result.Distances[0];

        var hits = new List<MemoryHit>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            // MemPalace returns cosine distance (lower = better). Convert to similarity score.
            var distance = i < dists.Count ? dists[i] : 0f;
            var score = 1.0 - distance;
            var metadata = i < metas.Count ? FlattenMetadata(metas[i]) : null;
            var content = i < docs.Count ? docs[i] : string.Empty;
            hits.Add(new MemoryHit(ids[i], content, score, metadata));
        }
        return hits;
    }

    public async Task DeleteAsync(string agentId, string memoryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);
        ThrowIfDisposed();

        ICollection collection;
        try
        {
            collection = await GetCollectionAsync(agentId, cancellationToken).ConfigureAwait(false);
        }
        catch (MemPalace.Core.Errors.PalaceNotFoundException)
        {
            // Agent has no palace yet — nothing to delete.
            return;
        }

        await collection.DeleteAsync(ids: new[] { memoryId }, ct: cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Deleted memory {MemoryId} for agent {AgentId}", memoryId, agentId);
    }

    private Task<ICollection> GetCollectionAsync(string agentId, CancellationToken cancellationToken)
    {
        var lazy = _collections.GetOrAdd(agentId, id => new Lazy<Task<ICollection>>(() => OpenCollectionAsync(id, cancellationToken)));
        return lazy.Value;
    }

    private async Task<ICollection> OpenCollectionAsync(string agentId, CancellationToken cancellationToken)
    {
        var palacePath = _storageOptions.AgentFolderForName(agentId);
        var palace = new PalaceRef(Id: agentId, LocalPath: palacePath);

        // SqliteBackend reads embedder.Dimensions at collection-create time, so warm up
        // the adapter once to populate the cached dimension count.
        await _embedderAdapter.EnsureDimensionsAsync(cancellationToken).ConfigureAwait(false);

        var collection = await _backend.GetCollectionAsync(
            palace,
            CollectionName,
            create: true,
            embedder: _embedderAdapter,
            ct: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Opened MempalaceNet collection '{Collection}' for agent {AgentId} at {PalacePath}",
            CollectionName, agentId, palacePath);
        return collection;
    }

    private static IReadOnlyDictionary<string, object?> BuildMetadata(MemoryEntry entry)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (entry.Metadata is not null)
        {
            foreach (var kvp in entry.Metadata)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }
        var ts = entry.Timestamp ?? DateTime.UtcNow;
        dict["timestamp"] = ts.ToString("O");
        return dict;
    }

    private static IReadOnlyDictionary<string, string>? FlattenMetadata(IReadOnlyDictionary<string, object?>? src)
    {
        if (src is null || src.Count == 0)
        {
            return null;
        }
        var dict = new Dictionary<string, string>(src.Count, StringComparer.Ordinal);
        foreach (var kvp in src)
        {
            if (kvp.Value is null)
            {
                continue;
            }
            dict[kvp.Key] = kvp.Value as string ?? JsonSerializer.Serialize(kvp.Value);
        }
        return dict;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var entry in _collections.Values)
        {
            if (entry.IsValueCreated)
            {
                try
                {
                    var collection = await entry.Value.ConfigureAwait(false);
                    await collection.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }
        _collections.Clear();
        await _backend.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Adapts <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> (Microsoft.Extensions.AI)
    /// to MemPalace's <see cref="IEmbedder"/> contract. Mirrors MemPalace.Ai.MeaiEmbedder but
    /// avoids dragging the OpenAI/Azure-AI transitive deps from MemPalace.Ai's DI helper.
    /// </summary>
    private sealed class MeaiEmbedderAdapter : IEmbedder
    {
        private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
        private int? _dimensions;

        public MeaiEmbedderAdapter(IEmbeddingGenerator<string, Embedding<float>> generator, string modelName)
        {
            _generator = generator;
            ModelIdentity = $"local:{modelName}";
        }

        public string ModelIdentity { get; }

        public int Dimensions => _dimensions
            ?? throw new InvalidOperationException("Dimensions not yet known. Call EmbedAsync first.");

        public async ValueTask EnsureDimensionsAsync(CancellationToken ct)
        {
            if (_dimensions.HasValue)
            {
                return;
            }
            await EmbedAsync(new[] { "warmup" }, ct).ConfigureAwait(false);
        }

        public async ValueTask<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            if (texts is null || texts.Count == 0)
            {
                return Array.Empty<ReadOnlyMemory<float>>();
            }

            var embeddings = await _generator.GenerateAsync(texts, cancellationToken: ct).ConfigureAwait(false);
            var results = new List<ReadOnlyMemory<float>>(embeddings.Count);
            foreach (var embedding in embeddings)
            {
                var vector = embedding.Vector;
                _dimensions ??= vector.Length;
                results.Add(vector);
            }
            return results;
        }
    }
}
