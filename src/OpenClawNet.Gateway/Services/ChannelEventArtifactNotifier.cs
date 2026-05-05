using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Concept-review §5 (UX) — bridges Storage's <see cref="IArtifactCreatedNotifier"/>
/// hook into the Gateway-owned <see cref="IChannelEventBus"/>. The bus then fans
/// the event out to every subscriber on <c>/api/channels/{jobId}/stream</c>
/// (HTTP NDJSON — the project deliberately avoids SignalR for new features).
/// </summary>
public sealed class ChannelEventArtifactNotifier : IArtifactCreatedNotifier
{
    private readonly IChannelEventBus _bus;

    public ChannelEventArtifactNotifier(IChannelEventBus bus) => _bus = bus;

    public void NotifyArtifactCreated(Guid jobId, Guid runId, Guid artifactId)
    {
        _bus.Publish(new ChannelEvent(
            Type: "artifact_created",
            JobId: jobId,
            RunId: runId,
            ArtifactId: artifactId,
            At: DateTime.UtcNow));
    }
}
