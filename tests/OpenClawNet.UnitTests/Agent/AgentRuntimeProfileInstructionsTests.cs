using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Regression tests for issue #245: Agent Profile instructions must reach final
/// prompt composition in both the non-streaming (<see cref="DefaultAgentRuntime.ExecuteAsync"/>)
/// and streaming (<see cref="DefaultAgentRuntime.ExecuteStreamAsync"/>) paths.
/// </summary>
public sealed class AgentRuntimeProfileInstructionsTests
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public AgentRuntimeProfileInstructionsTests()
    {
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbFactory = new TestDbContextFactory(options);
    }

    // ── Non-streaming path ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ProfileInstructions_ForwardedToPromptContext()
    {
        // Arrange: capture the PromptContext the runtime passes to IPromptComposer
        PromptContext? capturedContext = null;
        var composer = BuildCapturingComposer(ctx => capturedContext = ctx);

        var store = new ConversationStore(_dbFactory);
        var runtime = BuildRuntime(store, new FakeTextModelClient(), promptComposer: composer);

        var agentCtx = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello",
            AgentProfileInstructions = "You are a pirate. Always speak like a pirate."
        };

        // Act
        await runtime.ExecuteAsync(agentCtx);

        // Assert: profile instructions must be present in the PromptContext
        capturedContext.Should().NotBeNull();
        capturedContext!.ProfileInstructions.Should().Be("You are a pirate. Always speak like a pirate.",
            "ExecuteAsync must forward AgentContext.AgentProfileInstructions to PromptContext.ProfileInstructions");
    }

    [Fact]
    public async Task ExecuteAsync_NullProfileInstructions_ForwardedAsNullToPromptContext()
    {
        PromptContext? capturedContext = null;
        var composer = BuildCapturingComposer(ctx => capturedContext = ctx);

        var store = new ConversationStore(_dbFactory);
        var runtime = BuildRuntime(store, new FakeTextModelClient(), promptComposer: composer);

        await runtime.ExecuteAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello",
            AgentProfileInstructions = null
        });

        capturedContext!.ProfileInstructions.Should().BeNull(
            "null AgentProfileInstructions must propagate as null ProfileInstructions");
    }

    // ── Streaming path ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteStreamAsync_ProfileInstructions_ForwardedToPromptContext()
    {
        PromptContext? capturedContext = null;
        var composer = BuildCapturingComposer(ctx => capturedContext = ctx);

        var store = new ConversationStore(_dbFactory);
        var runtime = BuildRuntime(store, new FakeStreamingModelClient(), promptComposer: composer);

        var agentCtx = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello",
            AgentProfileInstructions = "Always respond in haiku."
        };

        // Drain the stream
        await foreach (var _ in runtime.ExecuteStreamAsync(agentCtx)) { }

        capturedContext.Should().NotBeNull();
        capturedContext!.ProfileInstructions.Should().Be("Always respond in haiku.",
            "ExecuteStreamAsync must forward AgentContext.AgentProfileInstructions to PromptContext.ProfileInstructions");
    }

    [Fact]
    public async Task ExecuteStreamAsync_NullProfileInstructions_ForwardedAsNullToPromptContext()
    {
        PromptContext? capturedContext = null;
        var composer = BuildCapturingComposer(ctx => capturedContext = ctx);

        var store = new ConversationStore(_dbFactory);
        var runtime = BuildRuntime(store, new FakeStreamingModelClient(), promptComposer: composer);

        await foreach (var _ in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello",
            AgentProfileInstructions = null
        })) { }

        capturedContext!.ProfileInstructions.Should().BeNull(
            "null AgentProfileInstructions must propagate as null ProfileInstructions in the streaming path");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a mock <see cref="IPromptComposer"/> that invokes <paramref name="capture"/>
    /// with the <see cref="PromptContext"/> it receives, then returns a minimal valid message list.
    /// </summary>
    private static IPromptComposer BuildCapturingComposer(Action<PromptContext> capture)
    {
        var composer = new Mock<IPromptComposer>();
        composer
            .Setup(c => c.ComposeAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .Callback<PromptContext, CancellationToken>((ctx, _) => capture(ctx))
            .ReturnsAsync(new List<ChatMessage>
            {
                new() { Role = ChatMessageRole.System, Content = "system" },
                new() { Role = ChatMessageRole.User, Content = "user" }
            });
        return composer.Object;
    }

    private DefaultAgentRuntime BuildRuntime(
        IConversationStore store,
        IModelClient modelClient,
        IPromptComposer? promptComposer = null)
    {
        promptComposer ??= BuildDefaultPromptComposer();
        var toolRegistry = BuildEmptyRegistry();
        var summaryService = BuildNoOpSummary();
        var approvalCoordinator = new ToolApprovalCoordinator(
            NullLogger<ToolApprovalCoordinator>.Instance);

        return new DefaultAgentRuntime(
            modelClient,
            promptComposer,
            new Mock<IToolExecutor>().Object,
            toolRegistry,
            store,
            summaryService,
            new OpenClawNet.Memory.StubAgentMemoryStore(),
            approvalCoordinator,
            NullLoggerFactory.Instance,
            NullLogger<DefaultAgentRuntime>.Instance);
    }

    private static IPromptComposer BuildDefaultPromptComposer()
    {
        var workspaceLoader = new Mock<IWorkspaceLoader>();
        workspaceLoader.Setup(w => w.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapContext(null, null, null));
        var skillService = new Mock<ISkillService>();
        skillService.Setup(s => s.FindRelevantSkillsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SkillSummary>());
        return new DefaultPromptComposer(
            workspaceLoader.Object,
            skillService.Object,
            NullLogger<DefaultPromptComposer>.Instance,
            Options.Create(new WorkspaceOptions()));
    }

    private static IToolRegistry BuildEmptyRegistry()
    {
        var r = new Mock<IToolRegistry>();
        r.Setup(x => x.GetToolManifest()).Returns([]);
        r.Setup(x => x.GetAllTools()).Returns([]);
        return r.Object;
    }

    private static ISummaryService BuildNoOpSummary()
    {
        var s = new Mock<ISummaryService>();
        s.Setup(x => x.SummarizeIfNeededAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        return s.Object;
    }

    // ── Minimal fake model clients ────────────────────────────────────────────

    private sealed class FakeTextModelClient : IModelClient
    {
        public string ProviderName => "fake-text";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse
            {
                Content = "Hello!",
                Role = ChatMessageRole.Assistant,
                Model = "test"
            });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = "Hello!", FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeStreamingModelClient : IModelClient
    {
        public string ProviderName => "fake-stream";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse
            {
                Content = "Stream done.",
                Role = ChatMessageRole.Assistant,
                Model = "test"
            });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = "Stream done.", FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }
}
