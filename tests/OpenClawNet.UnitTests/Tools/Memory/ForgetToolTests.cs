using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Memory;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Memory;
using Xunit;

namespace OpenClawNet.UnitTests.MemoryTools;

/// <summary>
/// Unit tests for <see cref="ForgetTool"/> (issue #113). Mirrors the
/// <see cref="MemoryToolsTests"/> style: in-memory fake store + AsyncLocal accessor,
/// plus a DI wiring smoke test.
/// </summary>
public sealed class ForgetToolTests
{
    private static ToolInput Args(object payload) => new()
    {
        ToolName = ForgetTool.ToolName,
        RawArguments = JsonSerializer.Serialize(payload)
    };

    private static (RememberTool remember, RecallTool recall, ForgetTool forget, InMemoryAgentMemoryStore store, AsyncLocalAgentContextAccessor accessor)
        BuildTools()
    {
        var store = new InMemoryAgentMemoryStore();
        var accessor = new AsyncLocalAgentContextAccessor();
        var remember = new RememberTool(store, accessor, NullLogger<RememberTool>.Instance);
        var recall = new RecallTool(store, accessor, NullLogger<RecallTool>.Instance);
        var forget = new ForgetTool(store, accessor, NullLogger<ForgetTool>.Instance);
        return (remember, recall, forget, store, accessor);
    }

    [Fact]
    public async Task ForgetTool_Deletes_Entry_Written_By_RememberTool()
    {
        var (remember, recall, forget, _, accessor) = BuildTools();
        using var _ = accessor.Push(new AgentExecutionContext("agent-A"));

        var write = await remember.ExecuteAsync(new ToolInput
        {
            ToolName = RememberTool.ToolName,
            RawArguments = JsonSerializer.Serialize(new { content = "ephemeral fact" })
        });
        Assert.True(write.Success, write.Error);
        var id = JsonDocument.Parse(write.Output).RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        var result = await forget.ExecuteAsync(Args(new { id }));

        Assert.True(result.Success, result.Error);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.True(doc.RootElement.GetProperty("deleted").GetBoolean());
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("agent-A", doc.RootElement.GetProperty("agentId").GetString());

        // recall should no longer find it
        var read = await recall.ExecuteAsync(new ToolInput
        {
            ToolName = RecallTool.ToolName,
            RawArguments = JsonSerializer.Serialize(new { query = "ephemeral fact" })
        });
        Assert.True(read.Success, read.Error);
        Assert.Equal(0, JsonDocument.Parse(read.Output).RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ForgetTool_Succeeds_When_Id_Does_Not_Exist()
    {
        // DeleteAsync is idempotent in the contract — a missing id is not an error.
        var (_, _, forget, _, accessor) = BuildTools();
        using var _scope = accessor.Push(new AgentExecutionContext("agent-A"));

        var result = await forget.ExecuteAsync(Args(new { id = "mem-does-not-exist" }));

        Assert.True(result.Success, result.Error);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.True(doc.RootElement.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task ForgetTool_Fails_When_Id_Missing()
    {
        var (_, _, forget, _, accessor) = BuildTools();
        using var _scope = accessor.Push(new AgentExecutionContext("agent-A"));

        var result = await forget.ExecuteAsync(Args(new { other = "x" }));

        Assert.False(result.Success);
        Assert.Contains("id", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForgetTool_Fails_When_Id_Blank()
    {
        var (_, _, forget, _, accessor) = BuildTools();
        using var _scope = accessor.Push(new AgentExecutionContext("agent-A"));

        var result = await forget.ExecuteAsync(Args(new { id = "   " }));

        Assert.False(result.Success);
        Assert.Contains("id", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForgetTool_Fails_When_NoAgentScope()
    {
        var (_, _, forget, _, _) = BuildTools();

        var result = await forget.ExecuteAsync(Args(new { id = "mem-anything" }));

        Assert.False(result.Success);
        Assert.Contains("agent context", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForgetTool_Honours_AgentId_Scoping_Across_Agents()
    {
        var (remember, recall, forget, _, accessor) = BuildTools();

        string? id;
        using (accessor.Push(new AgentExecutionContext("agent-A")))
        {
            var w = await remember.ExecuteAsync(new ToolInput
            {
                ToolName = RememberTool.ToolName,
                RawArguments = JsonSerializer.Serialize(new { content = "agent-A-only secret" })
            });
            Assert.True(w.Success);
            id = JsonDocument.Parse(w.Output).RootElement.GetProperty("id").GetString();
        }

        // agent-B attempting to forget agent-A's id should not delete from agent-A's bucket.
        using (accessor.Push(new AgentExecutionContext("agent-B")))
        {
            var del = await forget.ExecuteAsync(Args(new { id = id! }));
            Assert.True(del.Success, del.Error);
        }

        using (accessor.Push(new AgentExecutionContext("agent-A")))
        {
            var r = await recall.ExecuteAsync(new ToolInput
            {
                ToolName = RecallTool.ToolName,
                RawArguments = JsonSerializer.Serialize(new { query = "agent-A-only secret" })
            });
            Assert.True(r.Success);
            using var doc = JsonDocument.Parse(r.Output);
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        }
    }

    [Fact]
    public async Task ForgetTool_Resolves_Through_DI()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemory();
        services.AddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
        services.AddMemoryTools();
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IAgentContextAccessor>();
        using var _ = accessor.Push(new AgentExecutionContext("agent-A"));

        var forget = scope.ServiceProvider.GetRequiredService<ForgetTool>();
        var result = await forget.ExecuteAsync(Args(new { id = "stub-anything" }));

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void ForgetTool_Advertises_Memory_Category_And_DoesNotRequireApproval()
    {
        var (_, _, forget, _, _) = BuildTools();

        Assert.Equal("memory", forget.Metadata.Category);
        Assert.False(forget.Metadata.RequiresApproval);
        Assert.Equal(ForgetTool.ToolName, forget.Name);
    }
}
