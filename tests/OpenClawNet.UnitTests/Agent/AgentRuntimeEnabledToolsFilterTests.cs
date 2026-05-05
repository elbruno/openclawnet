using System.Reflection;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
/// PR-D: <see cref="DefaultAgentRuntime"/> filters its tool catalog through the
/// active <c>AgentProfile.EnabledTools</c> allow-list. Tests cover the storage-name
/// helper plus the filter's empty / subset / unknown-name branches.
/// </summary>
public sealed class AgentRuntimeEnabledToolsFilterTests
{
    [Fact]
    public void GetStorageName_PreservesMcpStorageForm()
    {
        var mcpTool = new FakeMcpTool("web_fetch", "web.fetch", Guid.NewGuid(), "web");
        DefaultAgentRuntime.GetStorageName(mcpTool).Should().Be("web.fetch");
    }

    [Fact]
    public void GetStorageName_NamespacesLegacyToolUnderScheduler()
    {
        var legacy = new NamedAIFunction("schedule", "legacy");
        DefaultAgentRuntime.GetStorageName(legacy).Should().Be("scheduler.schedule");
    }

    [Fact]
    public void EmptyEnabledTools_ReturnsEntireCatalog()
    {
        var (runtime, all, _) = BuildRuntime();
        var filtered = InvokeFilter(runtime, NewContext(null));
        filtered.Should().BeEquivalentTo(all);
    }

    [Fact]
    public void SubsetEnabledTools_RetainsOnlyAllowed()
    {
        var (runtime, _, _) = BuildRuntime();
        var filtered = InvokeFilter(runtime, NewContext(new[] { "web.fetch", "scheduler.schedule" }));

        filtered.OfType<AIFunction>().Select(f => f.Name).Should()
            .BeEquivalentTo(new[] { "web_fetch", "schedule" });
    }

    [Fact]
    public void UnknownEnabledToolName_LoggedAsWarningAndSkipped()
    {
        var (runtime, _, logger) = BuildRuntime(useListLogger: true);
        var filtered = InvokeFilter(runtime, NewContext(new[] { "web.fetch", "missing.tool" }));

        filtered.OfType<AIFunction>().Select(f => f.Name).Should()
            .ContainSingle().Which.Should().Be("web_fetch");

        logger!.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("missing.tool"));
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("restricts tools"));
    }

    [Fact]
    public void EnabledToolsContainingOnlyLegacyName_HidesMcpTools()
    {
        var (runtime, _, _) = BuildRuntime();
        var filtered = InvokeFilter(runtime, NewContext(new[] { "scheduler.schedule" }));

        filtered.OfType<AIFunction>().Select(f => f.Name).Should()
            .ContainSingle().Which.Should().Be("schedule");
    }

    // ── Plumbing ────────────────────────────────────────────────────────────

    private static AgentContext NewContext(IReadOnlyList<string>? enabledTools) => new()
    {
        SessionId = Guid.NewGuid(),
        UserMessage = "hi",
        EnabledTools = enabledTools,
        AgentProfileName = "test"
    };

    private static List<AITool> InvokeFilter(DefaultAgentRuntime runtime, AgentContext ctx)
    {
        var method = typeof(DefaultAgentRuntime).GetMethod(
            "FilterToolsForProfile", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (List<AITool>)method.Invoke(runtime, new object[] { ctx })!;
    }

    private static (DefaultAgentRuntime runtime, IReadOnlyList<AITool> all, ListLogger<DefaultAgentRuntime>? logger)
        BuildRuntime(bool useListLogger = false)
    {
        var legacyScheduler = new SimpleStubTool("schedule");
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetAllTools()).Returns([legacyScheduler]);
        registry.Setup(r => r.GetToolManifest()).Returns([legacyScheduler.Metadata]);

        var serverId = Guid.NewGuid();
        var mcpTools = new AITool[]
        {
            new FakeMcpTool("web_fetch",     "web.fetch",     serverId, "web"),
            new FakeMcpTool("web_search",    "web.search",    serverId, "web"),
            new FakeMcpTool("filesystem_read","filesystem.read", Guid.NewGuid(), "filesystem"),
        };

        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetAllToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(mcpTools);

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
        ListLogger<DefaultAgentRuntime>? listLogger = useListLogger ? new() : null;
        ILogger<DefaultAgentRuntime> logger = listLogger ?? (ILogger<DefaultAgentRuntime>)NullLogger<DefaultAgentRuntime>.Instance;

        var runtime = new DefaultAgentRuntime(
            new InertModelClient(),
            promptComposer,
            new Mock<IToolExecutor>().Object,
            registry.Object,
            store,
            summary.Object,
            new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
                NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance),
            loggerFactory,
            logger,
            agentProviders: null,
            mcpToolProvider: mcpProvider.Object);

        var allTools = (List<AITool>)typeof(DefaultAgentRuntime)
            .GetField("_toolAIFunctions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime)!;

        return (runtime, allTools, listLogger);
    }

    // ── Test doubles ────────────────────────────────────────────────────────

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

    private class NamedAIFunction : AIFunction
    {
        private readonly string _name;
        private readonly string _description;
        public NamedAIFunction(string name, string description)
        {
            _name = name;
            _description = description;
        }
        public override string Name => _name;
        public override string Description => _description;
        public override System.Text.Json.JsonElement JsonSchema =>
            System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>("invoked");
    }

    private sealed class FakeMcpTool : NamedAIFunction, IMcpAITool
    {
        public FakeMcpTool(string wireName, string storageName, Guid serverId, string serverName)
            : base(wireName, $"mcp:{storageName}")
        {
            StorageName = storageName;
            ServerId = serverId;
            ServerName = serverName;
        }
        public string StorageName { get; }
        public Guid ServerId { get; }
        public string ServerName { get; }
    }

    private sealed class InMemoryFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public InMemoryFactory(DbContextOptions<OpenClawDbContext> o) => _options = o;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
