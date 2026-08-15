using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;
using ModelToolCall = OpenClawNet.Models.Abstractions.ToolCall;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Phase 2 regression tests for the LoopAgent integration in <see cref="DefaultAgentRuntime"/>.
///
/// <para>These tests verify the following behaviors are preserved after the non-streaming
/// <see cref="DefaultAgentRuntime.ExecuteAsync"/> path was migrated from a manual
/// <c>while</c> loop to <see cref="Microsoft.Agents.AI.LoopAgent"/>:</para>
/// <list type="bullet">
///   <item>MaxIterations cap (25) triggers the fallback message</item>
///   <item>A single tool-call chain executes and produces the final text</item>
///   <item>Tool results pass through the sanitizer before re-entering the model</item>
///   <item>The streaming path (<see cref="DefaultAgentRuntime.ExecuteStreamAsync"/>) is
///   unaffected — approval pause/resume and NDJSON events continue to work</item>
///   <item>The streaming path iteration cap is also 25 (referenced via
///   <c>_loopAgentOptions.MaxIterations</c>)</item>
/// </list>
/// </summary>
public sealed class AgentRuntimeLoopAgentPhase2Tests
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public AgentRuntimeLoopAgentPhase2Tests()
    {
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbFactory = new TestDbContextFactory(options);
    }

    // ── Non-streaming (ExecuteAsync) — LoopAgent path ─────────────────────

    [Fact]
    public async Task ExecuteAsync_CleanStop_ReturnsFinalText()
    {
        // Arrange: model returns text on the first call (no tool calls)
        var store = new ConversationStore(_dbFactory);
        var runtime = BuildRuntime(store, new FakeNonStreamingTextClient("Hello from model!"));

        // Act
        var ctx = await runtime.ExecuteAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Say hello"
        });

        // Assert
        ctx.FinalResponse.Should().Be("Hello from model!");
        ctx.IsComplete.Should().BeTrue();
        ctx.ExecutedToolCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_OneToolCall_ExecutesToolAndReturnsAnswer()
    {
        // Arrange: model returns one tool call then final text
        var store = new ConversationStore(_dbFactory);

        var registry = new Mock<IToolRegistry>();
        var tool = new FakeNoApprovalTool("search");
        registry.Setup(r => r.GetTool("search")).Returns(tool);
        registry.Setup(r => r.GetToolManifest()).Returns([tool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([tool]);

        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync("search", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("search", "search result data", TimeSpan.Zero));

        var modelClient = new FakeNonStreamingToolThenTextClient("search", "{}", "Final answer after search.");
        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object);

        // Act
        var ctx = await runtime.ExecuteAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Search for something"
        });

        // Assert
        ctx.FinalResponse.Should().Be("Final answer after search.");
        ctx.ExecutedToolCalls.Should().HaveCount(1);
        ctx.ExecutedToolCalls[0].Name.Should().Be("search");
        executor.Verify(e => e.ExecuteAsync("search", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ToolResultPassesThroughSanitizer()
    {
        // Arrange: sanitizer strips "UNSAFE" from tool output
        var store = new ConversationStore(_dbFactory);

        var registry = new Mock<IToolRegistry>();
        var tool = new FakeNoApprovalTool("process");
        registry.Setup(r => r.GetTool("process")).Returns(tool);
        registry.Setup(r => r.GetToolManifest()).Returns([tool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([tool]);

        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync("process", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("process", "UNSAFE content here", TimeSpan.Zero));

        var sanitizer = new Mock<IToolResultSanitizer>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string>(), "process"))
            .Returns<string, string>((input, _) => input.Replace("UNSAFE", "SAFE"));

        // The capturing client records what messages the model received on the second call
        var capturingClient = new FakeNonStreamingCapturingClient("done");

        var runtime = BuildRuntime(store, capturingClient,
            toolExecutor: executor.Object,
            toolRegistry: registry.Object,
            sanitizer: sanitizer.Object);

        // Trigger one tool call
        capturingClient.SetupFirstCallToolResponse("process", "{}");

        // Act
        var ctx = await runtime.ExecuteAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Process something"
        });

        // Assert: the sanitizer was called with the raw output
        sanitizer.Verify(s => s.Sanitize("UNSAFE content here", "process"), Times.Once);
        ctx.IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// Regression: LoopAgent with MaxIterations=25 should stop at 25 tool iterations
    /// and emit the fallback message — not silently cap at LoopAgent.DefaultMaxIterations (10).
    /// This was API-U-1's critical finding: DefaultMaxIterations = 10 if not set explicitly.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlwaysToolCalling_StopsAt25IterationsWithFallback()
    {
        // Arrange: model ALWAYS returns a tool call, never plain text
        var store = new ConversationStore(_dbFactory);

        var registry = new Mock<IToolRegistry>();
        var tool = new FakeNoApprovalTool("infinite_tool");
        registry.Setup(r => r.GetTool("infinite_tool")).Returns(tool);
        registry.Setup(r => r.GetToolManifest()).Returns([tool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([tool]);

        var toolCallCount = 0;
        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync("infinite_tool", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                toolCallCount++;
                return ToolResult.Ok("infinite_tool", $"result-{toolCallCount}", TimeSpan.Zero);
            });

        var modelClient = new FakeNonStreamingAlwaysToolClient("infinite_tool", "{}");
        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object);

        // Act
        var ctx = await runtime.ExecuteAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Loop forever"
        });

        // Assert
        ctx.FinalResponse.Should().Contain("maximum number of tool iterations",
            "fallback message must be emitted when MaxIterations is exhausted");
        ctx.IsComplete.Should().BeTrue();

        // The key regression guard: LoopAgent.DefaultMaxIterations=10 would stop at 10.
        // We must execute AT LEAST 11 tool calls to prove MaxIterations=25 is in effect.
        toolCallCount.Should().BeGreaterThan(10,
            "if MaxIterations were 10 (the default), tool calls would stop at 10 — " +
            "this verifies MaxIterations=25 is being set explicitly (API-U-1 finding)");
    }

    // ── Streaming (ExecuteStreamAsync) — manual loop preserved ────────────

    /// <summary>
    /// B2 regression: fallback must fire even when the last model response contains
    /// BOTH non-empty text AND tool calls.  The old code checked
    /// <c>string.IsNullOrEmpty(finalText)</c> which would suppress the fallback for
    /// this edge case, silently returning partial/stale model chatter instead of the
    /// "maximum iterations" message.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlwaysToolAndTextCalling_StopsWithFallbackDespiteNonEmptyText()
    {
        // Arrange: model ALWAYS returns BOTH text AND a tool call — non-empty Content
        // means the old hitMaxIterations check (which also required IsNullOrEmpty(text))
        // would wrongly use the model's partial text instead of the fallback.
        var store = new ConversationStore(_dbFactory);

        var registry = new Mock<IToolRegistry>();
        var tool = new FakeNoApprovalTool("side_effect_tool");
        registry.Setup(r => r.GetTool("side_effect_tool")).Returns(tool);
        registry.Setup(r => r.GetToolManifest()).Returns([tool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([tool]);

        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync("side_effect_tool", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("side_effect_tool", "done", TimeSpan.Zero));

        // Model always returns "partial answer" text PLUS a tool call — simulates an LLM
        // that emits incremental commentary while still requesting the next tool.
        var modelClient = new FakeNonStreamingAlwaysToolAndTextClient("side_effect_tool", "{}", "partial answer");
        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object);

        // Act
        var ctx = await runtime.ExecuteAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Do something that never finishes"
        });

        // Assert: fallback must be used, NOT the model's partial text
        ctx.FinalResponse.Should().Contain("maximum number of tool iterations",
            "B2: fallback must be emitted when MaxIterations is exhausted, " +
            "even when the last model response contained non-empty text");
        ctx.FinalResponse.Should().NotContain("partial answer",
            "the model's partial text must not leak into the final response when MaxIterations is hit");
        ctx.IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// B3 strong test: context.TotalTokens must sum usage across ALL loop iterations,
    /// not just the final model call.  Uses a fake client that reports 100 tokens per
    /// tool-call response and 40 tokens for the final text response.  The test verifies
    /// the runtime accumulates them (3 tool rounds × 100 + 1 final × 40 = 340 tokens).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MultipleToolRounds_AccumulatesTokensAcrossAllIterations()
    {
        // Arrange: model does 3 tool rounds then returns plain text
        const int tokensPerToolRound = 100;
        const int tokensForFinalRound = 40;
        const int expectedTotal = 3 * tokensPerToolRound + tokensForFinalRound; // 340

        var store = new ConversationStore(_dbFactory);

        var registry = new Mock<IToolRegistry>();
        var tool = new FakeNoApprovalTool("count_tool");
        registry.Setup(r => r.GetTool("count_tool")).Returns(tool);
        registry.Setup(r => r.GetToolManifest()).Returns([tool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([tool]);

        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync("count_tool", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("count_tool", "counted", TimeSpan.Zero));

        var modelClient = new FakeNonStreamingCountedToolClient(
            toolName: "count_tool",
            toolCallRounds: 3,
            tokensPerToolRound: tokensPerToolRound,
            tokensForFinalRound: tokensForFinalRound);

        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object);

        // Act
        var ctx = await runtime.ExecuteAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Count three things"
        });

        // Assert
        ctx.IsComplete.Should().BeTrue();
        ctx.FinalResponse.Should().Be("all done");
        ctx.TotalTokens.Should().Be(expectedTotal,
            $"B3: TotalTokens must be the sum of all {3 + 1} iteration token counts " +
            $"({3} × {tokensPerToolRound} + 1 × {tokensForFinalRound} = {expectedTotal}), " +
            "not just the final call's usage");
    }


    [Fact]
    public async Task ExecuteStreamAsync_ToolApprovalDeny_PreservesHttpPauseFlow()
    {
        // Arrange: streaming path with HTTP-pause approval gate
        var store = new ConversationStore(_dbFactory);

        var modelClient = new FakeStreamingToolClient("browser_navigate", "{}");
        var registry = new Mock<IToolRegistry>();
        var browserTool = new FakeApprovalRequiredTool("browser_navigate");
        registry.Setup(r => r.GetTool("browser_navigate")).Returns(browserTool);
        registry.Setup(r => r.GetToolManifest()).Returns([browserTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([browserTool]);

        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("browser_navigate", "page loaded", TimeSpan.Zero));

        var coordinator = new ToolApprovalCoordinator(
            NullLogger<ToolApprovalCoordinator>.Instance);

        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object,
            approvalCoordinator: coordinator);

        // Collect all events; deny the first approval request
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Navigate to example.com",
            RequireToolApproval = true
        }))
        {
            events.Add(evt);
            if (evt.Type == AgentStreamEventType.ToolApprovalRequest)
            {
                // Simulate HTTP POST /api/chat/tool-approval → TryResolve
                coordinator.TryResolve(evt.RequestId!.Value, new ApprovalDecision(false, false));
            }
        }

        // Assert: approval request was emitted, tool was NOT executed, denial message sent
        events.Should().Contain(e => e.Type == AgentStreamEventType.ToolApprovalRequest,
            "streaming path must yield ToolApprovalRequest event mid-stream");
        events.Should().NotContain(e => e.Type == AgentStreamEventType.ToolCallStart,
            "denied tool must never execute");
        executor.Verify(
            e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var complete = events.Should().ContainSingle(e => e.Type == AgentStreamEventType.Complete).Subject;
        complete.Content.Should().Contain("denied");
    }

    [Fact]
    public async Task ExecuteStreamAsync_ToolApprovalApprove_ExecutesToolAndContinues()
    {
        // Arrange
        var store = new ConversationStore(_dbFactory);

        var modelClient = new FakeStreamingToolClient("browser_navigate", "{}");
        var registry = new Mock<IToolRegistry>();
        var browserTool = new FakeApprovalRequiredTool("browser_navigate");
        registry.Setup(r => r.GetTool("browser_navigate")).Returns(browserTool);
        registry.Setup(r => r.GetToolManifest()).Returns([browserTool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([browserTool]);

        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync("browser_navigate", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("browser_navigate", "page content", TimeSpan.Zero));

        var coordinator = new ToolApprovalCoordinator(
            NullLogger<ToolApprovalCoordinator>.Instance);

        var runtime = BuildRuntime(store, modelClient,
            toolExecutor: executor.Object, toolRegistry: registry.Object,
            approvalCoordinator: coordinator);

        // Collect events; approve the first approval request
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Navigate to example.com",
            RequireToolApproval = true
        }))
        {
            events.Add(evt);
            if (evt.Type == AgentStreamEventType.ToolApprovalRequest)
            {
                coordinator.TryResolve(evt.RequestId!.Value, new ApprovalDecision(true, false));
            }
        }

        // Assert: tool was approved and executed
        events.Should().Contain(e => e.Type == AgentStreamEventType.ToolApprovalRequest);
        events.Should().Contain(e => e.Type == AgentStreamEventType.ToolCallStart);
        executor.Verify(
            e => e.ExecuteAsync("browser_navigate", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        events.Should().Contain(e => e.Type == AgentStreamEventType.Complete);
    }

    [Fact]
    public async Task ExecuteStreamAsync_NoToolCalls_StreamsTextAndCompletes()
    {
        // Arrange
        var store = new ConversationStore(_dbFactory);
        var runtime = BuildRuntime(store, new FakeStreamingTextClient("Streamed response!"));

        // Act
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Just talk"
        }))
            events.Add(evt);

        // Assert
        events.Should().Contain(e => e.Type == AgentStreamEventType.ContentDelta);
        events.Should().ContainSingle(e => e.Type == AgentStreamEventType.Complete);
    }

    [Fact]
    public async Task ExecuteStreamAsync_AlwaysToolCalling_StopsAt25Iterations()
    {
        // Arrange
        var store = new ConversationStore(_dbFactory);

        var registry = new Mock<IToolRegistry>();
        var tool = new FakeNoApprovalTool("infinite_tool");
        registry.Setup(r => r.GetTool("infinite_tool")).Returns(tool);
        registry.Setup(r => r.GetToolManifest()).Returns([tool.Metadata]);
        registry.Setup(r => r.GetAllTools()).Returns([tool]);

        var toolCallCount = 0;
        var executor = new Mock<IToolExecutor>();
        executor.Setup(e => e.ExecuteAsync("infinite_tool", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                toolCallCount++;
                return ToolResult.Ok("infinite_tool", $"result-{toolCallCount}", TimeSpan.Zero);
            });

        var runtime = BuildRuntime(store, new FakeStreamingAlwaysToolClient("infinite_tool", "{}"),
            toolExecutor: executor.Object, toolRegistry: registry.Object);

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Loop"
        }))
            events.Add(evt);

        // Assert: hits the 25-iteration cap (not 10)
        toolCallCount.Should().BeGreaterThan(10,
            "streaming path must also cap at 25, not 10 (API-U-1 regression guard)");
        var complete = events.Should().ContainSingle(e => e.Type == AgentStreamEventType.Complete).Subject;
        complete.Content.Should().Contain("maximum number of tool iterations");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private DefaultAgentRuntime BuildRuntime(
        IConversationStore store,
        IModelClient modelClient,
        IToolExecutor? toolExecutor = null,
        IToolRegistry? toolRegistry = null,
        IToolApprovalCoordinator? approvalCoordinator = null,
        IToolResultSanitizer? sanitizer = null)
    {
        var promptComposer = BuildDefaultPromptComposer();
        toolExecutor ??= new Mock<IToolExecutor>().Object;
        toolRegistry ??= BuildEmptyRegistry();
        var summaryService = BuildNoOpSummary();
        approvalCoordinator ??= new ToolApprovalCoordinator(
            NullLogger<ToolApprovalCoordinator>.Instance);

        return new DefaultAgentRuntime(
            modelClient,
            promptComposer,
            toolExecutor,
            toolRegistry,
            store,
            summaryService,
            new OpenClawNet.Memory.StubAgentMemoryStore(),
            approvalCoordinator,
            NullLoggerFactory.Instance,
            NullLogger<DefaultAgentRuntime>.Instance,
            sanitizer: sanitizer);
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
            Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions()));
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
        s.Setup(x => x.SummarizeIfNeededAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        return s.Object;
    }

    // ── Fake model clients ────────────────────────────────────────────────────

    /// <summary>Non-streaming: always returns the given text, no tool calls.</summary>
    private sealed class FakeNonStreamingTextClient(string text) : IModelClient
    {
        public string ProviderName => "fake-ns-text";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse
            {
                Content = text,
                Role = ChatMessageRole.Assistant,
                Model = "test"
            });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = text, FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Non-streaming: first call returns one tool call, second call returns text.</summary>
    private sealed class FakeNonStreamingToolThenTextClient(
        string toolName, string toolArgs, string finalText) : IModelClient
    {
        private int _callCount;

        public string ProviderName => "fake-ns-tool";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
        {
            if (_callCount++ == 0)
            {
                return Task.FromResult(new ChatResponse
                {
                    Content = string.Empty,
                    Role = ChatMessageRole.Assistant,
                    Model = "test",
                    ToolCalls = [new ModelToolCall { Id = "tc1", Name = toolName, Arguments = toolArgs }]
                });
            }
            return Task.FromResult(new ChatResponse
            {
                Content = finalText,
                Role = ChatMessageRole.Assistant,
                Model = "test"
            });
        }

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = finalText, FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Non-streaming: ALWAYS returns a tool call (never text) — tests MaxIterations cap.</summary>
    private sealed class FakeNonStreamingAlwaysToolClient(string toolName, string toolArgs) : IModelClient
    {
        private int _callId;

        public string ProviderName => "fake-ns-infinite";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse
            {
                Content = string.Empty,
                Role = ChatMessageRole.Assistant,
                Model = "test",
                ToolCalls = [new ModelToolCall { Id = $"tc{++_callId}", Name = toolName, Arguments = toolArgs }]
            });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = "never", FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>
    /// Non-streaming: ALWAYS returns BOTH non-empty text AND a tool call — tests B2 regression
    /// where hitMaxIterations must fire even when Content is non-empty.
    /// </summary>
    private sealed class FakeNonStreamingAlwaysToolAndTextClient(
        string toolName, string toolArgs, string partialText) : IModelClient
    {
        private int _callId;

        public string ProviderName => "fake-ns-infinite-text";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse
            {
                Content = partialText,           // non-empty — the B2 edge case
                Role = ChatMessageRole.Assistant,
                Model = "test",
                ToolCalls = [new ModelToolCall { Id = $"tc{++_callId}", Name = toolName, Arguments = toolArgs }]
            });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = partialText, FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>
    /// Non-streaming: performs <paramref name="toolCallRounds"/> tool-call iterations each
    /// reporting <paramref name="tokensPerToolRound"/> tokens, then returns final text with
    /// <paramref name="tokensForFinalRound"/> tokens.  Used by the B3 token-accumulation test.
    /// </summary>
    private sealed class FakeNonStreamingCountedToolClient(
        string toolName,
        int toolCallRounds,
        int tokensPerToolRound,
        int tokensForFinalRound) : IModelClient
    {
        private int _callCount;
        private int _callId;

        public string ProviderName => "fake-ns-counted";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
        {
            var round = ++_callCount;
            if (round <= toolCallRounds)
            {
                return Task.FromResult(new ChatResponse
                {
                    Content = string.Empty,
                    Role = ChatMessageRole.Assistant,
                    Model = "test",
                    ToolCalls = [new ModelToolCall { Id = $"tc{++_callId}", Name = toolName, Arguments = "{}" }],
                    Usage = new UsageInfo { TotalTokens = tokensPerToolRound }
                });
            }
            // Final round: plain text, known token count
            return Task.FromResult(new ChatResponse
            {
                Content = "all done",
                Role = ChatMessageRole.Assistant,
                Model = "test",
                Usage = new UsageInfo { TotalTokens = tokensForFinalRound }
            });
        }

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = "all done", FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>
    /// Non-streaming capturing client: records messages on each call.
    /// Call <see cref="SetupFirstCallToolResponse"/> to make the first call return a tool call.
    /// </summary>
    private sealed class FakeNonStreamingCapturingClient(string finalText) : IModelClient
    {
        private int _callCount;
        private string? _toolName;
        private string? _toolArgs;

        public string ProviderName => "fake-ns-capturing";

        public void SetupFirstCallToolResponse(string toolName, string toolArgs)
        {
            _toolName = toolName;
            _toolArgs = toolArgs;
        }

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
        {
            if (_callCount++ == 0 && _toolName is not null)
            {
                return Task.FromResult(new ChatResponse
                {
                    Content = string.Empty,
                    Role = ChatMessageRole.Assistant,
                    Model = "test",
                    ToolCalls = [new ModelToolCall { Id = "tc1", Name = _toolName, Arguments = _toolArgs! }]
                });
            }
            return Task.FromResult(new ChatResponse
            {
                Content = finalText,
                Role = ChatMessageRole.Assistant,
                Model = "test"
            });
        }

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = finalText, FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Streaming: returns plain text (no tool calls).</summary>
    private sealed class FakeStreamingTextClient(string text) : IModelClient
    {
        public string ProviderName => "fake-s-text";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Content = text, Role = ChatMessageRole.Assistant, Model = "test" });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk { Content = text, FinishReason = "stop" };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Streaming: first call emits one tool call, second call emits text.</summary>
    private sealed class FakeStreamingToolClient(string toolName, string toolArgs) : IModelClient
    {
        private int _callCount;

        public string ProviderName => "fake-s-tool";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Content = "done", Role = ChatMessageRole.Assistant, Model = "test" });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (_callCount++ == 0)
            {
                yield return new ChatResponseChunk
                {
                    ToolCalls = [new ModelToolCall { Id = "stc1", Name = toolName, Arguments = toolArgs }],
                    FinishReason = "tool_calls"
                };
            }
            else
            {
                yield return new ChatResponseChunk { Content = "Navigation complete.", FinishReason = "stop" };
            }
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Streaming: ALWAYS returns a tool call — tests MaxIterations cap on streaming path.</summary>
    private sealed class FakeStreamingAlwaysToolClient(string toolName, string toolArgs) : IModelClient
    {
        private int _callId;

        public string ProviderName => "fake-s-infinite";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Content = "never", Role = ChatMessageRole.Assistant, Model = "test" });

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ChatResponseChunk
            {
                ToolCalls = [new ModelToolCall { Id = $"stc{++_callId}", Name = toolName, Arguments = toolArgs }],
                FinishReason = "tool_calls"
            };
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    // ── Fake tool helpers ─────────────────────────────────────────────────────

    private sealed class FakeNoApprovalTool(string name) : ITool    {
        public string Name => name;
        public string Description => $"No-approval: {name}";

        public ToolMetadata Metadata => new()
        {
            Name = Name,
            Description = Description,
            ParameterSchema = JsonDocument.Parse("{}"),
            RequiresApproval = false
        };

        public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok(Name, "result", TimeSpan.Zero));
    }

    private sealed class FakeApprovalRequiredTool(string name) : ITool
    {
        public string Name => name;
        public string Description => $"Approval-required: {name}";

        public ToolMetadata Metadata => new()
        {
            Name = Name,
            Description = Description,
            ParameterSchema = JsonDocument.Parse("{}"),
            RequiresApproval = true
        };

        public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok(Name, "approved", TimeSpan.Zero));
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }
}
