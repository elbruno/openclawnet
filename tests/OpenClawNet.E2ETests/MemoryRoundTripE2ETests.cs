using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Memory;

namespace OpenClawNet.E2ETests;

/// <summary>
/// End-to-end validation for per-agent long-term memory behavior:
/// remember (store) -> recall (search) -> forget (delete) with agent isolation.
/// </summary>
[Trait("Category", "Live")]
[Trait("Layer", "E2E")]
public sealed class MemoryRoundTripE2ETests : IClassFixture<GatewayE2EFactory>
{
    private readonly GatewayE2EFactory _factory;

    public MemoryRoundTripE2ETests(GatewayE2EFactory factory) => _factory = factory;

    [SkippableFact]
    public async Task MemoryStore_RoundTripAndIsolation_WorksAcrossAgents()
    {
        _ = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentMemoryStore>();

        var aliceId = "e2e-memory-alice";
        var bobId = "e2e-memory-bob";

        var aliceMemoryId = await store.StoreAsync(
            aliceId,
            new MemoryEntry(
                Content: "Alice likes markdown summaries",
                Kind: "preference",
                Importance: 0.8,
                Timestamp: DateTimeOffset.UtcNow,
                SourceSessionId: "e2e-session-1"));

        var bobMemoryId = await store.StoreAsync(
            bobId,
            new MemoryEntry(
                Content: "Bob prefers terse answers",
                Kind: "preference",
                Importance: 0.7,
                Timestamp: DateTimeOffset.UtcNow,
                SourceSessionId: "e2e-session-2"));

        var aliceHits = await store.SearchAsync(aliceId, "markdown summaries", topK: 5);
        aliceHits.Should().NotBeEmpty();
        aliceHits.Select(h => h.Id).Should().Contain(aliceMemoryId);
        aliceHits.Select(h => h.Id).Should().NotContain(bobMemoryId);

        var bobHits = await store.SearchAsync(bobId, "terse answers", topK: 5);
        bobHits.Should().NotBeEmpty();
        bobHits.Select(h => h.Id).Should().Contain(bobMemoryId);
        bobHits.Select(h => h.Id).Should().NotContain(aliceMemoryId);

        await store.DeleteAsync(aliceId, aliceMemoryId);
        var aliceAfterDelete = await store.SearchAsync(aliceId, "markdown summaries", topK: 5);
        aliceAfterDelete.Select(h => h.Id).Should().NotContain(aliceMemoryId);
    }
}
