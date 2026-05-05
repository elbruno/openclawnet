using FluentAssertions;
using OpenClawNet.Gateway.Services;
using Xunit;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// Concept-review §5: HTTP NDJSON channel-event bus (intentionally NOT SignalR —
/// the project moved chat off SignalR and channels follow the same pattern).
/// </summary>
public sealed class InMemoryChannelEventBusTests
{
    [Fact]
    public async Task Subscriber_ReceivesEvents_ForMatchingJobIdOnly()
    {
        var bus = new InMemoryChannelEventBus();
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var received = new List<ChannelEvent>();
        var task = Task.Run(async () =>
        {
            await foreach (var evt in bus.Subscribe(jobA, cts.Token))
            {
                received.Add(evt);
                if (received.Count >= 1) break;
            }
        });

        await Task.Delay(100);
        bus.Publish(new ChannelEvent("artifact_created", jobB, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow));
        bus.Publish(new ChannelEvent("artifact_created", jobA, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow));

        await task;
        received.Should().ContainSingle()
            .Which.JobId.Should().Be(jobA);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var bus = new InMemoryChannelEventBus();
        var act = () => bus.Publish(new ChannelEvent("x", Guid.NewGuid(), null, null, DateTime.UtcNow));
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Subscribe_RemovesSubscriber_WhenCancellationRequested()
    {
        var bus = new InMemoryChannelEventBus();
        var jobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in bus.Subscribe(jobId, cts.Token)) { /* drain */ }
            }
            catch (OperationCanceledException) { /* expected */ }
        });

        await Task.Delay(50);
        cts.Cancel();
        await task;

        // Publishing after cancellation must not throw and must be a no-op.
        var act = () => bus.Publish(new ChannelEvent("x", jobId, null, null, DateTime.UtcNow));
        act.Should().NotThrow();
    }
}
