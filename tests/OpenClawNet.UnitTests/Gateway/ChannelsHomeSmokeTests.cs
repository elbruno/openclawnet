using FluentAssertions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Tools.Core;

namespace OpenClawNet.UnitTests.Gateway;

/// <summary>
/// 📌 Proactive — Channels home smoke. Today there is no <c>ChannelStore</c>;
/// channels are surfaced through <see cref="IChannelRegistry"/>. These tests
/// pin the contract the Channels home page (and <c>/api/channel-adapters</c>)
/// depends on:
///   * the registry honours <see cref="IChannelRegistry.Register"/>
///   * <see cref="IChannelRegistry.GetAllChannels"/> returns every registered
///     channel (so the home page is never empty when adapters are wired up).
///
/// If/when Irving introduces a real <c>ChannelStore.GetAllAsync()</c>, the
/// pending-skipped test below should be filled in to assert that
/// <c>EnsureCreatedAsync</c> + seed leaves rows discoverable through the store.
/// </summary>
public class ChannelsHomeSmokeTests
{
    [Fact]
    public void GetAllChannels_ReturnsRegisteredChannels()
    {
        var registry = new ChannelRegistry();
        registry.Register(new FakeChannel("teams", enabled: true));
        registry.Register(new FakeChannel("slack", enabled: false));

        var channels = registry.GetAllChannels();

        channels.Should().HaveCount(2);
        channels.Select(c => c.ChannelName).Should().BeEquivalentTo(new[] { "teams", "slack" });
        channels.Single(c => c.ChannelName == "teams").IsEnabled.Should().BeTrue();
        channels.Single(c => c.ChannelName == "slack").IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void GetChannel_IsCaseInsensitive()
    {
        var registry = new ChannelRegistry();
        registry.Register(new FakeChannel("Teams", enabled: true));

        registry.GetChannel("teams").Should().NotBeNull();
        registry.GetChannel("TEAMS").Should().NotBeNull();
        registry.GetChannel("missing").Should().BeNull();
    }

    [Fact(Skip = "📌 Pending: ChannelStore.GetAllAsync() does not exist yet; channels are registry-backed only. Once a ChannelStore with GetAllAsync is implemented (likely as part of Mark's ChannelDetail report work), this test can verify that EnsureCreatedAsync + seed leaves rows discoverable through the store.")]
    public void GetAllAsync_ReturnsSeededChannels()
    {
        // EXPECTED once Irving lands a ChannelStore:
        //   await using var db = NewDb();
        //   await db.Database.EnsureCreatedAsync();
        //   var store = new ChannelStore(new TestDbContextFactory(db.Options));
        //   var rows = await store.GetAllAsync();
        //   rows.Should().NotBeEmpty();
        Assert.Fail("Replace with real ChannelStore.GetAllAsync() smoke when API lands.");
    }

    private sealed class FakeChannel : IChannel
    {
        public FakeChannel(string name, bool enabled)
        {
            ChannelName = name;
            IsEnabled = enabled;
        }
        public string ChannelName { get; }
        public bool IsEnabled { get; }
        public Task SendMessageAsync(string conversationId, string message, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(IsEnabled);
    }
}
