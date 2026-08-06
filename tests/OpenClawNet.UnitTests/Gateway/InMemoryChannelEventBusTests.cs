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
        // Outer guard: only fires if the test itself hangs. Normal path completes in < 500 ms.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var received = new List<ChannelEvent>();
        var task = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in bus.Subscribe(jobA, cts.Token))
                {
                    received.Add(evt);
                    if (received.Count >= 1) break;
                }
            }
            catch (OperationCanceledException) when (received.Count >= 1)
            {
                // Breaking out of an IAsyncEnumerable-backed async iterator causes DisposeAsync
                // to propagate OperationCanceledException via [EnumeratorCancellation] + ReadAllAsync.
                // This is expected after receiving the target item; suppress so the task succeeds.
            }
        });

        // Deterministic synchronisation: poll SubscriberCount until Subscribe() has added itself
        // to _subs before publishing. Eliminates the Task.Delay(100) timing race that caused
        // a flaky failure in CI run 31122017186 (windows-latest scheduler jitter — the goroutine
        // was not yet scheduled within 100 ms, the event was published to zero subscribers,
        // and the 2 s CTS fired with OperationCanceledException).
        await WaitForSubscriberAsync(bus, expected: 1, cts.Token);

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

        // Deterministic: wait for subscriber to register before cancelling
        await WaitForSubscriberAsync(bus, expected: 1, CancellationToken.None);
        cts.Cancel();
        await task;

        // Publishing after cancellation must not throw and must be a no-op.
        var act = () => bus.Publish(new ChannelEvent("x", jobId, null, null, DateTime.UtcNow));
        act.Should().NotThrow();
    }

    /// <summary>
    /// Regression guard for the CI timing race fixed in this PR.
    /// Verifies that SubscriberCount reflects registration before any publish,
    /// so that a slow-scheduled goroutine cannot cause events to be lost.
    /// </summary>
    [Fact]
    public async Task SubscriberCount_ReflectsRegistrationBeforeFirstPublish()
    {
        var bus = new InMemoryChannelEventBus();
        bus.SubscriberCount.Should().Be(0, "no subscribers before any Subscribe call");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscribeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var drainTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in bus.Subscribe(Guid.NewGuid(), cts.Token))
                {
                    break; // immediately break after first event (if any)
                }
            }
            catch (OperationCanceledException) { }
        });

        // Poll until registered — this is exactly the pattern the repaired production test uses
        await WaitForSubscriberAsync(bus, expected: 1, cts.Token);
        bus.SubscriberCount.Should().Be(1, "Subscribe must have added itself to _subs by the time WaitForSubscriber returns");

        cts.Cancel();
        await drainTask;

        bus.SubscriberCount.Should().Be(0, "Subscribe must remove itself from _subs after cancellation");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task WaitForSubscriberAsync(
        InMemoryChannelEventBus bus, int expected, CancellationToken ct)
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, guard.Token);

        while (bus.SubscriberCount < expected)
        {
            linked.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, linked.Token);
        }
    }
}
