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
using ModelToolCall = OpenClawNet.Models.Abstractions.ToolCall;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Tests for DefaultAgentRuntime focusing on streaming error handling and edge cases
/// that don't require a real LLM.
/// </summary>
public sealed class AgentRuntimeStreamTests
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public AgentRuntimeStreamTests()
    {
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbFactory = new TestDbContextFactory(options);
    }

    [Fact]
    public async Task ExecuteStreamAsync_YieldsError_WhenStorageFailsOnUserMessage()
    {
        var failingStore = new Mock<IConversationStore>();
        failingStore
            .Setup(s => s.AddMessageAsync(It.IsAny<Guid>(), "user", It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB offline"));

        var runtime = BuildRuntime(failingStore.Object, new FakeModelClient());

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello"
        };

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(context))
            events.Add(evt);

        events.Should().HaveCount(1);
        events[0].Type.Should().Be(AgentStreamEventType.Error);
        events[0].Content.Should().Contain("Storage error");
    }

    [Fact]
    public async Task ExecuteStreamAsync_YieldsError_WhenPromptComposeThrows()
    {
        var store = new ConversationStore(_dbFactory);

        var failingComposer = new Mock<IPromptComposer>();
        failingComposer
            .Setup(c => c.ComposeAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Skill file missing"));

        var runtime = BuildRuntime(store, new FakeModelClient(), promptComposer: failingComposer.Object);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Hello"
        };

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(context))
            events.Add(evt);

        events.Should().HaveCount(1);
        events[0].Type.Should().Be(AgentStreamEventType.Error);
        events[0].Content.Should().Contain("Setup error");
    }

    // ── ToolApprovalRequest event ─────────────────────────────────────────────

    [Fact]
    public void AgentStreamEventType_ContainsToolApprovalRequest()
    {
        // Verify the enum value exists — a regression guard so it can't be accidentally removed
        var values = Enum.GetNames<AgentStreamEventType>();
        values.Should().Contain("ToolApprovalRequest",
            "ToolApprovalRequest must be a named value in AgentStreamEventType");
    }

    [Fact]
    public async Task ExecuteStreamAsync_EmitsToolApprovalRequest_BeforeToolCallStart_ForApprovalRequiredTools()
    {
        var store = new ConversationStore(_dbFactory);

        // Model client that returns one tool call then finishes
        var modelClient = new FakeModelClientWithToolCall("file_system", "{\"action\":\"list\",\"path\":\".\"}");

        // Tool registry with a tool that has RequiresApproval = true
        var registry = new Mock<IToolRegistry>();
        var approvableTool = new FakeApprovalRequiredTool("file_system");
        registry.Setup(r => r.GetTool("file_system")).Returns(approvableTool);
        registry.Setup(r => r.GetToolManifest()).Returns([approvableTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([approvableTool]);

        // Executor that returns a successful result
        var executor = new Mock<IToolExecutor>();
        executor
            .Setup(e => e.ExecuteAsync("file_system", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("file_system", "file listing", TimeSpan.Zero));

        // Real coordinator — auto-approve as soon as the request is registered
        // so the runtime doesn't deadlock waiting for a click.
        var coordinator = new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
            NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance);

        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object, approvalCoordinator: coordinator);

        var events = new List<AgentStreamEvent>();
        var sessionId = Guid.NewGuid();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = sessionId,
            UserMessage = "List files",
            RequireToolApproval = true
        }))
        {
            events.Add(evt);
            if (evt.Type == AgentStreamEventType.ToolApprovalRequest)
            {
                evt.RequestId.Should().NotBeNull();
                coordinator.TryResolve(evt.RequestId!.Value,
                    new OpenClawNet.Agent.ToolApproval.ApprovalDecision(true, false))
                    .Should().BeTrue();
            }
        }

        var approvalIdx = events.FindIndex(e => e.Type == AgentStreamEventType.ToolApprovalRequest);
        var startIdx = events.FindIndex(e => e.Type == AgentStreamEventType.ToolCallStart);

        approvalIdx.Should().BeGreaterThanOrEqualTo(0,
            "a ToolApprovalRequest event should be emitted for tools with RequiresApproval=true");
        startIdx.Should().BeGreaterThan(approvalIdx,
            "ToolCallStart must come AFTER ToolApprovalRequest");

        events[approvalIdx].ToolName.Should().Be("file_system");
        events[approvalIdx].ToolArgsJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteStreamAsync_DoesNotEmitApprovalRequest_WhenRequireToolApprovalIsFalse()
    {
        var store = new ConversationStore(_dbFactory);
        var modelClient = new FakeModelClientWithToolCall("file_system", "{}");
        var registry = new Mock<IToolRegistry>();
        var approvableTool = new FakeApprovalRequiredTool("file_system");
        registry.Setup(r => r.GetTool("file_system")).Returns(approvableTool);
        registry.Setup(r => r.GetToolManifest()).Returns([approvableTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([approvableTool]);

        var executor = new Mock<IToolExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("file_system", "done", TimeSpan.Zero));

        var runtime = BuildRuntime(store, modelClient, toolExecutor: executor.Object, toolRegistry: registry.Object);

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "List files",
            RequireToolApproval = false  // master switch off
        }))
            events.Add(evt);

        events.Should().NotContain(e => e.Type == AgentStreamEventType.ToolApprovalRequest,
            "approval-required tools must not prompt when the profile master switch is off");
    }

    [Fact]
    public async Task ExecuteStreamAsync_ScheduleTool_IsExemptFromApproval_EvenWhenRequireToolApprovalTrue()
    {
        var store = new ConversationStore(_dbFactory);
        var modelClient = new FakeModelClientWithToolCall("schedule", "{\"action\":\"list\"}");
        var registry = new Mock<IToolRegistry>();
        // schedule's metadata says RequiresApproval=false, but to prove the EXEMPTION list
        // is the source of truth, we simulate someone flipping it on.
        var scheduleTool = new FakeApprovalRequiredTool("schedule");
        registry.Setup(r => r.GetTool("schedule")).Returns(scheduleTool);
        registry.Setup(r => r.GetToolManifest()).Returns([scheduleTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([scheduleTool]);

        var executor = new Mock<IToolExecutor>();
        executor
            .Setup(e => e.ExecuteAsync("schedule", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("schedule", "[]", TimeSpan.Zero));

        var runtime = BuildRuntime(store, modelClient, toolExecutor: executor.Object, toolRegistry: registry.Object);

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "List jobs",
            RequireToolApproval = true
        }))
            events.Add(evt);

        events.Should().NotContain(e => e.Type == AgentStreamEventType.ToolApprovalRequest,
            "the `schedule` tool is on the exemption list and must never prompt for approval");
        events.Should().Contain(e => e.Type == AgentStreamEventType.ToolCallComplete && e.ToolName == "schedule");
    }

    [Fact]
    public async Task ExecuteStreamAsync_RememberForSession_SuppressesSubsequentApprovalRequests()
    {
        var store = new ConversationStore(_dbFactory);
        var sessionId = Guid.NewGuid();

        var coordinator = new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
            NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance);

        // Simulate the user previously checking "remember for session" for this tool.
        coordinator.RememberApproval(sessionId, "file_system");

        var modelClient = new FakeModelClientWithToolCall("file_system", "{}");
        var registry = new Mock<IToolRegistry>();
        var approvableTool = new FakeApprovalRequiredTool("file_system");
        registry.Setup(r => r.GetTool("file_system")).Returns(approvableTool);
        registry.Setup(r => r.GetToolManifest()).Returns([approvableTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([approvableTool]);

        var executor = new Mock<IToolExecutor>();
        executor
            .Setup(e => e.ExecuteAsync("file_system", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("file_system", "done", TimeSpan.Zero));

        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object, approvalCoordinator: coordinator);

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = sessionId,
            UserMessage = "List again",
            RequireToolApproval = true
        }))
            events.Add(evt);

        events.Should().NotContain(e => e.Type == AgentStreamEventType.ToolApprovalRequest,
            "RememberForSession should suppress subsequent approval prompts for the same tool");
        events.Should().Contain(e => e.Type == AgentStreamEventType.ToolCallComplete);
    }

    [Fact]
    public async Task ExecuteStreamAsync_DenialEndsTurnCleanly_WithSyntheticAssistantMessage()
    {
        var store = new ConversationStore(_dbFactory);
        var sessionId = Guid.NewGuid();

        var modelClient = new FakeModelClientWithToolCall("file_system", "{}");
        var registry = new Mock<IToolRegistry>();
        var approvableTool = new FakeApprovalRequiredTool("file_system");
        registry.Setup(r => r.GetTool("file_system")).Returns(approvableTool);
        registry.Setup(r => r.GetToolManifest()).Returns([approvableTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([approvableTool]);

        var executor = new Mock<IToolExecutor>();
        // The executor MUST NOT be invoked when the user denies.
        executor
            .Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("x", "should not run", TimeSpan.Zero));

        var coordinator = new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
            NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance);

        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object, approvalCoordinator: coordinator);

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = sessionId,
            UserMessage = "List files",
            RequireToolApproval = true
        }))
        {
            events.Add(evt);
            if (evt.Type == AgentStreamEventType.ToolApprovalRequest)
            {
                coordinator.TryResolve(evt.RequestId!.Value,
                    new OpenClawNet.Agent.ToolApproval.ApprovalDecision(false, false));
            }
        }

        executor.Verify(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "denied tool calls must not execute");
        events.Should().NotContain(e => e.Type == AgentStreamEventType.ToolCallStart);
        var complete = events.Single(e => e.Type == AgentStreamEventType.Complete);
        complete.Content.Should().Contain("denied");
    }

    [Fact]
    public async Task ExecuteStreamAsync_DoesNotEmitToolApprovalRequest_ForNonApprovalTools()
    {
        var store = new ConversationStore(_dbFactory);

        var modelClient = new FakeModelClientWithToolCall("safe_tool", "{}");

        var registry = new Mock<IToolRegistry>();
        var noApprovalTool = new FakeNoApprovalTool("safe_tool");
        registry.Setup(r => r.GetTool("safe_tool")).Returns(noApprovalTool);
        registry.Setup(r => r.GetToolManifest()).Returns([noApprovalTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([noApprovalTool]);

        var executor = new Mock<IToolExecutor>();
        executor
            .Setup(e => e.ExecuteAsync("safe_tool", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("safe_tool", "done", TimeSpan.Zero));

        var runtime = BuildRuntime(store, modelClient, toolExecutor: executor.Object, toolRegistry: registry.Object);

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Run safe tool"
        }))
            events.Add(evt);

        events.Should().NotContain(e => e.Type == AgentStreamEventType.ToolApprovalRequest,
            "tools without RequiresApproval should not emit an approval event");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DefaultAgentRuntime BuildRuntime(
        IConversationStore store,
        IModelClient modelClient,
        IPromptComposer? promptComposer = null,
        IToolExecutor? toolExecutor = null,
        IToolRegistry? toolRegistry = null,
        ISummaryService? summaryService = null,
        OpenClawNet.Agent.ToolApproval.IToolApprovalCoordinator? approvalCoordinator = null)
    {
        promptComposer ??= BuildDefaultPromptComposer();
        toolExecutor ??= new Mock<IToolExecutor>().Object;
        toolRegistry ??= BuildEmptyRegistry();
        summaryService ??= BuildNoOpSummary();
        approvalCoordinator ??= new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
            NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance);

        var loggerFactory = NullLoggerFactory.Instance;
        return new DefaultAgentRuntime(
            modelClient,
            promptComposer,
            toolExecutor,
            toolRegistry,
            store,
            summaryService,
            approvalCoordinator,
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
        summary.Setup(s => s.SummarizeIfNeededAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        return summary.Object;
    }

    // ── Fake helpers ──────────────────────────────────────────────────────────

    private sealed class FakeModelClient : IModelClient
    {
        public string ProviderName => "fake";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse { Content = "Hi!", Role = ChatMessageRole.Assistant, Model = "test" });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = "Hi!", FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    /// <summary>Model that emits one tool call on the first call, then plain text.</summary>
    private sealed class FakeModelClientWithToolCall(string toolName, string toolArgs) : IModelClient
    {
        private int _callCount;

        public string ProviderName => "fake-with-tool";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse { Content = "done", Role = ChatMessageRole.Assistant, Model = "test" });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            if (_callCount++ == 0)
            {
                // First call: return a tool call
                yield return new ChatResponseChunk
                {
                    ToolCalls = [new ModelToolCall { Id = "tc1", Name = toolName, Arguments = toolArgs }],
                    FinishReason = "tool_calls"
                };
            }
            else
            {
                // Second call (after tool result): return final answer
                yield return new ChatResponseChunk { Content = "Here are the results.", FinishReason = "stop" };
            }
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakeApprovalRequiredTool(string name) : ITool
    {
        public string Name => name;
        public string Description => $"Approval-required tool: {name}";

        public ToolMetadata Metadata => new()
        {
            Name = Name,
            Description = Description,
            ParameterSchema = System.Text.Json.JsonDocument.Parse("{}"),
            RequiresApproval = true
        };

        public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.Ok(Name, "approved result", TimeSpan.Zero));
    }

    private sealed class FakeNoApprovalTool(string name) : ITool
    {
        public string Name => name;
        public string Description => $"No-approval tool: {name}";

        public ToolMetadata Metadata => new()
        {
            Name = Name,
            Description = Description,
            ParameterSchema = System.Text.Json.JsonDocument.Parse("{}"),
            RequiresApproval = false
        };

        public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.Ok(Name, "safe result", TimeSpan.Zero));
    }

    [Fact]
    public async Task ExecuteStreamAsync_McpBrowserTool_RequiresApproval_WhenNotInLegacyRegistry()
    {
        // This test verifies that MCP tools (e.g., browser_navigate) correctly trigger the approval
        // flow even though they're not in the legacy IToolRegistry. The runtime should check for
        // bundled MCP server prefixes like "browser", "shell", "web", "file_system".
        var store = new ConversationStore(_dbFactory);
        var sessionId = Guid.NewGuid();

        // Model returns a tool call for "browser_navigate" (MCP wire-form name)
        var modelClient = new FakeModelClientWithToolCall("browser_navigate", """{"url":"https://example.com"}""");

        // Legacy registry does NOT have this tool (simulating the bug condition)
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetTool("browser_navigate")).Returns((ITool?)null);
        registry.Setup(r => r.GetToolManifest()).Returns([]);
        registry.Setup(r => r.GetAllTools()).Returns([]);

        var coordinator = new OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator(
            NullLogger<OpenClawNet.Agent.ToolApproval.ToolApprovalCoordinator>.Instance);

        var executor = new Mock<IToolExecutor>();
        executor
            .Setup(e => e.ExecuteAsync("browser_navigate", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("browser_navigate", "page loaded", TimeSpan.Zero));

        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object, approvalCoordinator: coordinator);

        // Run the stream in a background task, then approve the request
        var events = new List<AgentStreamEvent>();
        var streamTask = Task.Run(async () =>
        {
            await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
            {
                SessionId = sessionId,
                UserMessage = "Open example.com",
                RequireToolApproval = true
            }))
                events.Add(evt);
        });

        // Wait for the approval request to appear
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !events.Any(e => e.Type == AgentStreamEventType.ToolApprovalRequest))
        {
            await Task.Delay(50);
        }

        var approvalEvent = events.FirstOrDefault(e => e.Type == AgentStreamEventType.ToolApprovalRequest);
        approvalEvent.Should().NotBeNull("MCP tool browser_navigate should trigger approval request");
        approvalEvent!.ToolName.Should().Be("browser_navigate");
        approvalEvent.RequestId.Should().NotBeEmpty();

        // Approve the request
        coordinator.TryResolve(approvalEvent.RequestId!.Value, new OpenClawNet.Agent.ToolApproval.ApprovalDecision(true, false));

        // Wait for stream to complete
        await streamTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Verify tool was executed after approval
        events.Should().Contain(e => e.Type == AgentStreamEventType.ToolCallComplete && e.ToolName == "browser_navigate");
        executor.Verify(e => e.ExecuteAsync("browser_navigate", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }
}
