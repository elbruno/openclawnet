using OpenClawNet.Tools.Abstractions;
using Xunit;

namespace OpenClawNet.UnitTests.MemoryTools;

public sealed class AsyncLocalAgentContextAccessorTests
{
    [Fact]
    public void Current_Is_Null_When_Nothing_Pushed()
    {
        var accessor = new AsyncLocalAgentContextAccessor();
        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Push_Sets_And_Restores_Previous()
    {
        var accessor = new AsyncLocalAgentContextAccessor();
        using (accessor.Push(new AgentExecutionContext("a")))
        {
            Assert.Equal("a", accessor.Current?.AgentId);
            using (accessor.Push(new AgentExecutionContext("b")))
            {
                Assert.Equal("b", accessor.Current?.AgentId);
            }
            Assert.Equal("a", accessor.Current?.AgentId);
        }
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Push_Flows_Across_Async_Boundaries()
    {
        var accessor = new AsyncLocalAgentContextAccessor();
        using var _ = accessor.Push(new AgentExecutionContext("a"));

        var observed = await Task.Run(async () =>
        {
            await Task.Yield();
            return accessor.Current?.AgentId;
        });

        Assert.Equal("a", observed);
    }
}
