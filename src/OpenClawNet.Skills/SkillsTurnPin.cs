namespace OpenClawNet.Skills;

/// <summary>
/// K-1b #3 — Per-chat-turn snapshot pin. Captured once at the start of a
/// chat-stream call, held for the duration of the turn, and disposed when
/// the turn ends. Implements Q2: hot-reload happens immediately on FS
/// change, but the agent's view of the skill set is stable for the turn.
/// </summary>
/// <remarks>
/// <para>The K-1b #4 <c>OpenClawNetSkillsProvider</c> resolves a
/// <see cref="SkillsTurnPin"/> from the request scope and uses
/// <see cref="Snapshot"/> for the entire turn — even if the watcher fires
/// mid-turn and rebuilds <see cref="ISkillsRegistry"/>'s current snapshot.</para>
/// <para>This type is a thin holder; it does not subscribe to the change
/// notifier. The intentional behavior is "pin then ignore"; observing
/// hot-reloads happens via the K-3 polling endpoint, never inside an
/// active turn.</para>
/// </remarks>
public sealed class SkillsTurnPin
{
    private ISkillsSnapshot? _pinned;

    /// <summary>
    /// Pins <paramref name="snapshot"/> for this turn. Idempotent — the
    /// first call wins; subsequent calls are no-ops so pin order doesn't
    /// matter when multiple AIContextProviders fire in the same turn.
    /// </summary>
    public ISkillsSnapshot Pin(ISkillsSnapshot snapshot)
    {
        _pinned ??= snapshot;
        return _pinned;
    }

    /// <summary>
    /// Returns the pinned snapshot for this turn. If <see cref="Pin"/>
    /// hasn't been called, falls back to <paramref name="fallback"/> (the
    /// registry's current live snapshot) and pins it.
    /// </summary>
    public ISkillsSnapshot GetOrPin(ISkillsSnapshot fallback) => Pin(fallback);

    /// <summary>The pinned snapshot, or <c>null</c> if nothing has been pinned yet.</summary>
    public ISkillsSnapshot? Snapshot => _pinned;
}
