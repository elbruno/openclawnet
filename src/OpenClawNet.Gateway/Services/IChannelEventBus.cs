using System.Threading.Channels;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Concept-review §5 (UX) — real-time channel events delivered via HTTP NDJSON
/// (matching the chat streaming pattern at <c>/api/chat/stream</c>). The project
/// deliberately moved chat off SignalR to NDJSON for reliability; channels follow
/// the same convention.
/// </summary>
/// <remarks>
/// Producers call <see cref="Publish"/> (e.g. when a job-run artifact is appended);
/// consumers (the streaming endpoint) call <see cref="Subscribe"/> to obtain an
/// <see cref="IAsyncEnumerable{T}"/> of events filtered to one job id. The bus is
/// in-memory and best-effort: late subscribers do not see prior events. Polling
/// remains the durable fallback.
/// </remarks>
public interface IChannelEventBus
{
    void Publish(ChannelEvent evt);
    IAsyncEnumerable<ChannelEvent> Subscribe(Guid jobId, CancellationToken ct);
}

/// <summary>One event broadcast on the channel bus.</summary>
public sealed record ChannelEvent(
    string Type,
    Guid JobId,
    Guid? RunId,
    Guid? ArtifactId,
    DateTime At);

/// <summary>
/// In-memory, per-process broadcaster. Each subscription owns its own bounded
/// <see cref="Channel{T}"/>; a slow consumer just drops the oldest events
/// (we prefer freshness over completeness for a UX nice-to-have).
/// </summary>
public sealed class InMemoryChannelEventBus : IChannelEventBus
{
    private readonly List<Subscriber> _subs = new();
    private readonly Lock _gate = new();

    public void Publish(ChannelEvent evt)
    {
        Subscriber[] snapshot;
        lock (_gate)
        {
            snapshot = _subs.ToArray();
        }

        foreach (var s in snapshot)
        {
            if (s.JobId != evt.JobId) continue;
            // TryWrite — bounded, drop-oldest. Never block the producer.
            if (!s.Channel.Writer.TryWrite(evt))
            {
                _ = s.Channel.Reader.TryRead(out _);
                s.Channel.Writer.TryWrite(evt);
            }
        }
    }

    public async IAsyncEnumerable<ChannelEvent> Subscribe(
        Guid jobId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var sub = new Subscriber(jobId, Channel.CreateBounded<ChannelEvent>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest }));
        lock (_gate) _subs.Add(sub);

        try
        {
            await foreach (var evt in sub.Channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_gate) _subs.Remove(sub);
            sub.Channel.Writer.TryComplete();
        }
    }

    private sealed record Subscriber(Guid JobId, Channel<ChannelEvent> Channel);
}
