using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Memory;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Memory;

namespace OpenClawNet.UnitTests.Tools;

public class MemoryToolsTests
{
    [Fact]
    public async Task RememberTool_Stores_DefaultImportance_AndTimestamp()
    {
        var store = new RecordingMemoryStore();
        var ctx = new TestAgentContextAccessor(new AgentExecutionContext("agent-a"));
        var tool = new RememberTool(store, ctx, NullLogger<RememberTool>.Instance);

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = RememberTool.ToolName,
            RawArguments = """{"content":"remember this","kind":"preference"}"""
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal("agent-a", store.LastStoreAgentId);
        Assert.NotNull(store.LastStoredEntry);
        Assert.Equal("remember this", store.LastStoredEntry!.Content);
        Assert.Equal("preference", store.LastStoredEntry.Kind);
        Assert.Equal(0.5, store.LastStoredEntry.Importance);
        Assert.NotNull(store.LastStoredEntry.Timestamp);
    }

    [Fact]
    public async Task RememberTool_Fails_WithoutAgentContext()
    {
        var store = new RecordingMemoryStore();
        var ctx = new TestAgentContextAccessor(null);
        var tool = new RememberTool(store, ctx, NullLogger<RememberTool>.Instance);

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = RememberTool.ToolName,
            RawArguments = """{"content":"x"}"""
        });

        Assert.False(result.Success);
        Assert.Contains("agent context", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecallTool_ClampsTopK_AndReturnsHits()
    {
        var store = new RecordingMemoryStore
        {
            SearchResults = new List<MemoryHit>
            {
                new("m1", "stored", 0.99, new Dictionary<string, string> { ["kind"] = "fact" })
            }
        };
        var ctx = new TestAgentContextAccessor(new AgentExecutionContext("agent-r"));
        var tool = new RecallTool(store, ctx, NullLogger<RecallTool>.Instance);

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = RecallTool.ToolName,
            RawArguments = """{"query":"what do you remember?","topK":999}"""
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal("agent-r", store.LastSearchAgentId);
        Assert.Equal(RecallTool.MaxTopK, store.LastSearchTopK);

        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("m1", doc.RootElement.GetProperty("hits")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ForgetTool_DeletesMemoryById()
    {
        var store = new RecordingMemoryStore();
        var ctx = new TestAgentContextAccessor(new AgentExecutionContext("agent-f"));
        var tool = new ForgetTool(store, ctx, NullLogger<ForgetTool>.Instance);

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = ForgetTool.ToolName,
            RawArguments = """{"id":"memory-123"}"""
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal("agent-f", store.LastDeleteAgentId);
        Assert.Equal("memory-123", store.LastDeleteMemoryId);
    }

    private sealed class RecordingMemoryStore : IAgentMemoryStore
    {
        public string? LastStoreAgentId { get; private set; }
        public MemoryEntry? LastStoredEntry { get; private set; }
        public string? LastSearchAgentId { get; private set; }
        public int LastSearchTopK { get; private set; }
        public string? LastDeleteAgentId { get; private set; }
        public string? LastDeleteMemoryId { get; private set; }
        public IReadOnlyList<MemoryHit> SearchResults { get; set; } = Array.Empty<MemoryHit>();

        public Task<string> StoreAsync(string agentId, MemoryEntry entry, CancellationToken cancellationToken = default)
        {
            LastStoreAgentId = agentId;
            LastStoredEntry = entry;
            return Task.FromResult("memory-id");
        }

        public Task<IReadOnlyList<MemoryHit>> SearchAsync(string agentId, string query, int topK = 5, CancellationToken cancellationToken = default)
        {
            LastSearchAgentId = agentId;
            LastSearchTopK = topK;
            return Task.FromResult(SearchResults);
        }

        public Task DeleteAsync(string agentId, string memoryId, CancellationToken cancellationToken = default)
        {
            LastDeleteAgentId = agentId;
            LastDeleteMemoryId = memoryId;
            return Task.CompletedTask;
        }
    }

    private sealed class TestAgentContextAccessor : IAgentContextAccessor
    {
        public TestAgentContextAccessor(AgentExecutionContext? current) => Current = current;

        public AgentExecutionContext? Current { get; private set; }

        public IDisposable Push(AgentExecutionContext context)
        {
            var previous = Current;
            Current = context;
            return new Revert(() => Current = previous);
        }

        private sealed class Revert(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
