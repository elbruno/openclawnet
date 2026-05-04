namespace OpenClawNet.Storage;

/// <summary>
/// Concept-review §5 (UX) — best-effort hook fired by <see cref="ArtifactStorageService"/>
/// each time a new <c>JobRunArtifact</c> is persisted. The Gateway wires this to the
/// HTTP NDJSON channel-event stream (<c>/api/channels/{jobId}/stream</c>); other hosts
/// (tests, in-memory factories) get the no-op default.
/// </summary>
/// <remarks>
/// Storage cannot reference Gateway, so the publisher contract lives here as a tiny
/// abstraction. Implementations must NEVER throw — a failure here must not roll back
/// the artifact insert. The repo deliberately uses HTTP NDJSON instead of SignalR for
/// real-time fan-out (see <c>/api/chat/stream</c>).
/// </remarks>
public interface IArtifactCreatedNotifier
{
    void NotifyArtifactCreated(Guid jobId, Guid runId, Guid artifactId);
}

/// <summary>Default no-op publisher used when no real publisher is registered.</summary>
public sealed class NullArtifactCreatedNotifier : IArtifactCreatedNotifier
{
    public void NotifyArtifactCreated(Guid jobId, Guid runId, Guid artifactId) { /* no-op */ }
}
