using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Memory;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Memory;
using Xunit;

namespace OpenClawNet.UnitTests.MemoryTools;

/// <summary>
/// Unit tests for the issue #100 memory tools (RememberTool / RecallTool).
/// Validated against an in-memory <see cref="IAgentMemoryStore"/> fake so the
/// suite doesn't depend on the parallel MempalaceNet work in #98. A separate
/// test ("Works_With_StubAgentMemoryStore_Via_DI") proves the production wiring
/// resolves through <c>AddMemory()</c> too.
/// </summary>
public sealed class MemoryToolsTests
{
    private static ToolInput Args(string toolName, object payload) => new()
    {
        ToolName = toolName,
        RawArguments = JsonSerializer.Serialize(payload)
    };

    private static (RememberTool remember, RecallTool recall, InMemoryAgentMemoryStore store, AsyncLocalAgentContextAccessor accessor)
        BuildTools()
    {
        var store = new InMemoryAgentMemoryStore();
        var accessor = new AsyncLocalAgentContextAccessor();
        var remember = new RememberTool(store, accessor, NullLogger<RememberTool>.Instance);
        var recall = new RecallTool(store, accessor, NullLogger<RecallTool>.Instance);
        return (remember, recall, store, accessor);
    }

    [Fact]
    public async Task RememberTool_Returns_MemoryId_When_AgentScopeActive()
    {
        var (remember, _, _, accessor) = BuildTools();
        using var _ = accessor.Push(new AgentExecutionContext("agent-A"));

        var result = await remember.ExecuteAsync(Args(RememberTool.ToolName, new { content = "user prefers dark mode" }));

        Assert.True(result.Success, result.Error);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.True(doc.RootElement.GetProperty("stored").GetBoolean());
        var id = doc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal("agent-A", doc.RootElement.GetProperty("agentId").GetString());
    }

    [Fact]
    public async Task RememberTool_Fails_When_NoAgentScope()
    {
        var (remember, _, _, _) = BuildTools();

        var result = await remember.ExecuteAsync(Args(RememberTool.ToolName, new { content = "anything" }));

        Assert.False(result.Success);
        Assert.Contains("agent context", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RememberTool_Fails_When_Content_Missing()
    {
        var (remember, _, _, accessor) = BuildTools();
        using var _ = accessor.Push(new AgentExecutionContext("agent-A"));

        var result = await remember.ExecuteAsync(Args(RememberTool.ToolName, new { content = "   " }));

        Assert.False(result.Success);
        Assert.Contains("content", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecallTool_Returns_What_RememberTool_Wrote()
    {
        var (remember, recall, _, accessor) = BuildTools();
        using var _ = accessor.Push(new AgentExecutionContext("agent-A"));

        var writeResult = await remember.ExecuteAsync(Args(RememberTool.ToolName, new
        {
            content = "Bruno's favourite IDE is Visual Studio",
            kind = "fact",
            importance = 0.8
        }));
        Assert.True(writeResult.Success, writeResult.Error);

        var readResult = await recall.ExecuteAsync(Args(RecallTool.ToolName, new { query = "favourite IDE", topK = 3 }));

        Assert.True(readResult.Success, readResult.Error);
        using var doc = JsonDocument.Parse(readResult.Output);
        Assert.Equal("agent-A", doc.RootElement.GetProperty("agentId").GetString());
        Assert.True(doc.RootElement.GetProperty("count").GetInt32() >= 1);
        var topHit = doc.RootElement.GetProperty("hits").EnumerateArray().First();
        Assert.Contains("Visual Studio", topHit.GetProperty("content").GetString());
    }

    [Fact]
    public async Task RecallTool_Honours_AgentId_Scoping_Across_Agents()
    {
        var (remember, recall, _, accessor) = BuildTools();

        using (accessor.Push(new AgentExecutionContext("agent-A")))
        {
            var w = await remember.ExecuteAsync(Args(RememberTool.ToolName, new { content = "secret-A: alpha keys" }));
            Assert.True(w.Success);
        }

        using (accessor.Push(new AgentExecutionContext("agent-B")))
        {
            var r = await recall.ExecuteAsync(Args(RecallTool.ToolName, new { query = "secret-A" }));
            Assert.True(r.Success, r.Error);
            using var doc = JsonDocument.Parse(r.Output);
            Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        }

        using (accessor.Push(new AgentExecutionContext("agent-A")))
        {
            var r = await recall.ExecuteAsync(Args(RecallTool.ToolName, new { query = "secret-A" }));
            Assert.True(r.Success);
            using var doc = JsonDocument.Parse(r.Output);
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        }
    }

    [Fact]
    public async Task RecallTool_Fails_When_Query_Missing()
    {
        var (_, recall, _, accessor) = BuildTools();
        using var _scope = accessor.Push(new AgentExecutionContext("agent-A"));

        var result = await recall.ExecuteAsync(Args(RecallTool.ToolName, new { topK = 3 }));

        Assert.False(result.Success);
        Assert.Contains("query", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecallTool_Caps_TopK_At_MaxTopK()
    {
        var (_, recall, store, accessor) = BuildTools();
        using var _scope = accessor.Push(new AgentExecutionContext("agent-A"));
        for (var i = 0; i < RecallTool.MaxTopK + 5; i++)
            await store.StoreAsync("agent-A", new MemoryEntry($"item {i}"));

        var result = await recall.ExecuteAsync(Args(RecallTool.ToolName, new { query = "item", topK = 100 }));

        Assert.True(result.Success, result.Error);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(RecallTool.MaxTopK, doc.RootElement.GetProperty("topK").GetInt32());
        Assert.True(doc.RootElement.GetProperty("count").GetInt32() <= RecallTool.MaxTopK);
    }

    [Fact]
    public async Task RememberTool_Works_With_StubAgentMemoryStore_Via_DI()
    {
        // Proves the wiring story: AddMemory() + AddMemoryTools() + accessor singleton
        // resolves a working RememberTool whose StoreAsync returns a stub-prefixed id.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemory();
        services.AddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
        services.AddMemoryTools();
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IAgentContextAccessor>();
        using var _ = accessor.Push(new AgentExecutionContext("agent-A"));

        var remember = scope.ServiceProvider.GetRequiredService<RememberTool>();
        var result = await remember.ExecuteAsync(Args(RememberTool.ToolName, new { content = "hello" }));

        Assert.True(result.Success, result.Error);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.StartsWith("stub-", doc.RootElement.GetProperty("id").GetString());

        // RecallTool against the stub returns no hits (stub stores nothing).
        var recall = scope.ServiceProvider.GetRequiredService<RecallTool>();
        var read = await recall.ExecuteAsync(Args(RecallTool.ToolName, new { query = "hello" }));
        Assert.True(read.Success, read.Error);
        using var readDoc = JsonDocument.Parse(read.Output);
        Assert.Equal(0, readDoc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Tools_Advertise_Memory_Category_And_DoNotRequireApproval()
    {
        var (remember, recall, _, _) = BuildTools();

        Assert.Equal("memory", remember.Metadata.Category);
        Assert.Equal("memory", recall.Metadata.Category);
        Assert.False(remember.Metadata.RequiresApproval);
        Assert.False(recall.Metadata.RequiresApproval);
        Assert.Equal(RememberTool.ToolName, remember.Name);
        Assert.Equal(RecallTool.ToolName, recall.Name);
    }
}
