using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Integration;

/// <summary>
/// Pipeline smoke tests that verify the chat flow from runtime through to model.
/// Uses the REAL DefaultAgentRuntime with a fake IModelClient at the boundary.
/// If these pass, the core "type message → get response" pipeline works.
/// </summary>
public sealed class ChatSmokeTests
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public ChatSmokeTests()
    {
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbFactory = new TestDbContextFactory(options);
    }

    [Fact]
    public async Task Pipeline_SendMessage_ReturnsStreamedTokens()
    {
        var fakeClient = new SmokeTestFakeModelClient();
        var runtime = BuildRuntime(fakeClient);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "HI"
        };

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(context))
            events.Add(evt);

        events.Should().NotBeEmpty("the pipeline should yield stream events");

        var contentEvents = events.Where(e => e.Type == AgentStreamEventType.ContentDelta).ToList();
        contentEvents.Should().NotBeEmpty("at least one content token should stream through");

        var allContent = string.Join("", contentEvents.Select(e => e.Content));
        allContent.Should().Contain("Hello from fake",
            "the fake model's response should flow through the entire runtime pipeline");
    }

    [Fact]
    public async Task Pipeline_WithProviderError_YieldsErrorEvent()
    {
        var fakeClient = new ErrorThrowingFakeModelClient();
        var runtime = BuildRuntime(fakeClient);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "HI"
        };

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(context))
            events.Add(evt);

        events.Should().Contain(e => e.Type == AgentStreamEventType.Error,
            "errors from the model provider must propagate to callers");
    }

    [Fact]
    public async Task Pipeline_EmptyMessage_DoesNotThrow()
    {
        var fakeClient = new SmokeTestFakeModelClient();
        var runtime = BuildRuntime(fakeClient);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = ""
        };

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(context))
            events.Add(evt);

        events.Should().NotBeEmpty("even an empty message should yield some response");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private DefaultAgentRuntime BuildRuntime(IModelClient modelClient)
    {
        var store = new ConversationStore(_dbFactory);
        var promptComposer = BuildDefaultPromptComposer();
        var toolExecutor = new Mock<IToolExecutor>().Object;
        var toolRegistry = BuildEmptyRegistry();
        var summaryService = BuildNoOpSummary();
        var loggerFactory = NullLoggerFactory.Instance;
        return new DefaultAgentRuntime(
            modelClient,
            promptComposer,
            toolExecutor,
            toolRegistry,
            store,
            summaryService,
            new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
                NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance),
            loggerFactory,
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
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetToolManifest()).Returns([]);
        registry.Setup(r => r.GetAllTools()).Returns([]);
        return registry.Object;
    }

    private static ISummaryService BuildNoOpSummary()
    {
        var summary = new Mock<ISummaryService>();
        summary.Setup(s => s.SummarizeIfNeededAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        return summary.Object;
    }

    // ── Fake Model Clients ───────────────────────────────────────────

    private sealed class SmokeTestFakeModelClient : IModelClient
    {
        public string ProviderName => "smoke-test-fake";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse
            {
                Content = "Hello from fake model",
                Role = ChatMessageRole.Assistant,
                Model = "fake-model"
            });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = "Hello from fake", FinishReason = null };
            yield return new ChatResponseChunk { Content = " model!", FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class ErrorThrowingFakeModelClient : IModelClient
    {
        public string ProviderName => "error-fake";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Simulated provider failure");

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new HttpRequestException("Simulated provider failure");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }
}
