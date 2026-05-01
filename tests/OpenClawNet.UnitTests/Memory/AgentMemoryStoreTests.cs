using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Memory;
using Xunit;

namespace OpenClawNet.UnitTests.Memory;

/// <summary>
/// Tests for <see cref="IAgentMemoryStore"/> DI registration and stub implementation.
/// Issue #99: Verify abstraction is properly registered and resolvable.
/// </summary>
public sealed class AgentMemoryStoreTests
{
    [Fact]
    public void AddMemory_RegistersIAgentMemoryStore()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMemory();
        var provider = services.BuildServiceProvider();

        // Assert — issue #98 replaced the stub with MempalaceAgentMemoryStore.
        var store = provider.GetRequiredService<IAgentMemoryStore>();
        Assert.NotNull(store);
        Assert.IsType<MempalaceAgentMemoryStore>(store);
    }

    [Fact]
    public async Task StubAgentMemoryStore_StoreAsync_ReturnsStubId()
    {
        // Arrange
        var store = new StubAgentMemoryStore();
        var entry = new MemoryEntry("Test content", new Dictionary<string, string> { ["key"] = "value" });

        // Act
        var memoryId = await store.StoreAsync("agent-1", entry);

        // Assert
        Assert.NotNull(memoryId);
        Assert.StartsWith("stub-", memoryId);
    }

    [Fact]
    public async Task StubAgentMemoryStore_SearchAsync_ReturnsEmptyResults()
    {
        // Arrange
        var store = new StubAgentMemoryStore();

        // Act
        var results = await store.SearchAsync("agent-1", "test query", topK: 5);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task StubAgentMemoryStore_DeleteAsync_CompletesWithoutError()
    {
        // Arrange
        var store = new StubAgentMemoryStore();

        // Act & Assert (should not throw)
        await store.DeleteAsync("agent-1", "memory-123");
    }

    [Fact]
    public async Task StubAgentMemoryStore_StoreAsync_ValidatesAgentId()
    {
        // Arrange
        var store = new StubAgentMemoryStore();
        var entry = new MemoryEntry("Test content");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("", entry));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("   ", entry));
    }

    [Fact]
    public async Task StubAgentMemoryStore_StoreAsync_ValidatesEntry()
    {
        // Arrange
        var store = new StubAgentMemoryStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.StoreAsync("agent-1", null!));
    }

    [Fact]
    public async Task StubAgentMemoryStore_SearchAsync_ValidatesParameters()
    {
        // Arrange
        var store = new StubAgentMemoryStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync("", "query"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync("agent-1", ""));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SearchAsync("agent-1", "query", topK: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SearchAsync("agent-1", "query", topK: -1));
    }

    [Fact]
    public async Task StubAgentMemoryStore_DeleteAsync_ValidatesParameters()
    {
        // Arrange
        var store = new StubAgentMemoryStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync("", "memory-id"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync("agent-1", ""));
    }

    [Fact]
    public void MemoryEntry_CanBeCreatedWithMinimalData()
    {
        // Act
        var entry = new MemoryEntry("Test content");

        // Assert
        Assert.Equal("Test content", entry.Content);
        Assert.Null(entry.Metadata);
        Assert.Null(entry.Timestamp);
    }

    [Fact]
    public void MemoryEntry_CanBeCreatedWithFullData()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        // Act
        var entry = new MemoryEntry("Test content", metadata) { Timestamp = timestamp };

        // Assert
        Assert.Equal("Test content", entry.Content);
        Assert.Equal(metadata, entry.Metadata);
        Assert.Equal(timestamp, entry.Timestamp);
    }

    [Fact]
    public void MemoryHit_ContainsAllRequiredFields()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { ["source"] = "test" };

        // Act
        var hit = new MemoryHit("id-123", "content", 0.95, metadata);

        // Assert
        Assert.Equal("id-123", hit.Id);
        Assert.Equal("content", hit.Content);
        Assert.Equal(0.95, hit.Score);
        Assert.Equal(metadata, hit.Metadata);
    }
}
