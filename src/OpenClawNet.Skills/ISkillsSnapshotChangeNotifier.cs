namespace OpenClawNet.Skills;

/// <summary>
/// K-1b #3 — Subscription point for snapshot rebuilds. K-3 endpoints poll
/// <see cref="ISkillsRegistry.GetSnapshotAsync"/> directly, but server-side
/// consumers (chat-turn pinning, future SSE) want push notifications when
/// the watcher rebuilds.
/// </summary>
/// <remarks>
/// Per Q2 (hot-reload semantics): snapshot rebuilds happen immediately on
/// FS change, but agents keep using the snapshot pinned at chat-turn start.
/// The notifier lets <see cref="SkillsTurnPin"/> capture a stable reference
/// at turn start; it does NOT itself change agent behavior mid-turn.
/// </remarks>
public interface ISkillsSnapshotChangeNotifier
{
    /// <summary>
    /// Fires after the registry has atomically swapped to the new snapshot.
    /// Handlers must be fast and non-blocking (notifier serializes them).
    /// </summary>
    event EventHandler<SkillsSnapshotChangedEventArgs>? SnapshotChanged;
}

public sealed class SkillsSnapshotChangedEventArgs : EventArgs
{
    public ISkillsSnapshot Previous { get; }
    public ISkillsSnapshot Current { get; }

    public SkillsSnapshotChangedEventArgs(ISkillsSnapshot previous, ISkillsSnapshot current)
    {
        Previous = previous;
        Current = current;
    }
}
