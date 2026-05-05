using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Mcp.Abstractions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// PR-B: when both a legacy ITool and an MCP tool advertise the same name, the MCP
/// version must replace the legacy one. Scheduler (and any other non-overlapping
/// legacy tool) must remain.
/// </summary>
public sealed class AgentRuntimeMcpDedupTests
{
    [Fact]
    public void DefaultAgentRuntime_McpToolWinsOverLegacyOnNameCollision()
    {
        var legacyWeb = new SimpleStubTool("web_fetch");
        var legacyScheduler = new SimpleStubTool("schedule");

        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetAllTools()).Returns([legacyWeb, legacyScheduler]);
        registry.Setup(r => r.GetToolManifest()).Returns([legacyWeb.Metadata, legacyScheduler.Metadata]);

        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NamedAIFunction("web_fetch", "mcp"), new NamedAIFunction("filesystem_read", "mcp")]);

        var runtime = BuildRuntime(registry.Object, mcpProvider.Object);

        var tools = ReadToolList(runtime);
        var byName = tools.OfType<AIFunction>().ToDictionary(f => f.Name, f => f);

        // The MCP web_fetch must be present, and its description proves it's the MCP one.
        byName.Should().ContainKey("web_fetch");
        byName["web_fetch"].Description.Should().Be("mcp");

        // The MCP-only tool surfaces too.
        byName.Should().ContainKey("filesystem_read");

        // Scheduler (legacy, no MCP equivalent) is preserved.
        byName.Should().ContainKey("schedule");

        // No duplicate entries — exactly 3 tools total.
        tools.Should().HaveCount(3);
    }

    private static List<AITool> ReadToolList(DefaultAgentRuntime runtime)
    {
        var field = typeof(DefaultAgentRuntime).GetField("_toolAIFunctions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (List<AITool>)field.GetValue(runtime)!;
    }

    private static DefaultAgentRuntime BuildRuntime(IToolRegistry registry, IMcpToolProvider mcp)
    {
        var dbOptions = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var store = new ConversationStore(new InMemoryFactory(dbOptions));

        var workspaceLoader = new Mock<IWorkspaceLoader>();
        workspaceLoader.Setup(w => w.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapContext(null, null, null));
        var skillService = new Mock<ISkillService>();
        skillService.Setup(s => s.FindRelevantSkillsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SkillSummary>());
        var promptComposer = new DefaultPromptComposer(
            workspaceLoader.Object,
            skillService.Object,
            NullLogger<DefaultPromptComposer>.Instance,
            Options.Create(new WorkspaceOptions()));

        var summary = new Mock<ISummaryService>();
        summary.Setup(s => s.SummarizeIfNeededAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<OpenClawNet.Models.Abstractions.ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var loggerFactory = NullLoggerFactory.Instance;
        return new DefaultAgentRuntime(
            new InertModelClient(),
            promptComposer,
            new Mock<IToolExecutor>().Object,
            registry,
            store,
            summary.Object,
            new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
                NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance),
            loggerFactory,
            NullLogger<DefaultAgentRuntime>.Instance,
            agentProviders: null,
            mcpToolProvider: mcp);
    }

    private sealed class InertModelClient : IModelClient
    {
        public string ProviderName => "inert";
        public Task<OpenClawNet.Models.Abstractions.ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new OpenClawNet.Models.Abstractions.ChatResponse { Content = "", Role = ChatMessageRole.Assistant, Model = "test" });
        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.Yield(); yield break; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class SimpleStubTool : ITool
    {
        public SimpleStubTool(string name) { Name = name; }
        public string Name { get; }
        public string Description => $"legacy:{Name}";
        public ToolMetadata Metadata => new()
        {
            Name = Name,
            Description = Description,
            ParameterSchema = System.Text.Json.JsonDocument.Parse("{}"),
            RequiresApproval = false,
        };
        public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.Ok(Name, "ok", TimeSpan.Zero));
    }

    private sealed class NamedAIFunction : AIFunction
    {
        public NamedAIFunction(string name, string description)
        {
            _name = name;
            _description = description;
        }
        private readonly string _name;
        private readonly string _description;
        public override string Name => _name;
        public override string Description => _description;
        public override System.Text.Json.JsonElement JsonSchema =>
            System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>("invoked");
    }

    private sealed class InMemoryFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public InMemoryFactory(DbContextOptions<OpenClawDbContext> o) => _options = o;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }
}
