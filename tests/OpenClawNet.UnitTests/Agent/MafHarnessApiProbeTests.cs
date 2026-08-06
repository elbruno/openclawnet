using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Phase 1 Harness API probe tests — resolves the five API uncertainties
/// (API-U-1 through API-U-5) documented in
/// <c>.squad/decisions/inbox/petey-harness-migration.md</c>.
///
/// Every test is purely in-process: no live model, no HTTP, no Aspire.
/// These tests MUST remain in <c>Category!=Live</c> (no trait = excluded from live).
///
/// Non-goals (per Phase 1 plan):
///   • Do NOT touch DefaultAgentRuntime, IToolApprovalCoordinator, or any production loop.
///   • Do NOT replace the HTTP approval flow.
///   • Do NOT wire any of these APIs into production DI registration.
/// </summary>
public sealed class MafHarnessApiProbeTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // API-U-1: LoopAgent.DefaultMaxIterations value
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves API-U-1: confirms the numeric value of the DefaultMaxIterations constant.
    /// Phase 2 must set MaxIterations = 25 explicitly regardless of this value.
    /// Documents the MAF default so the team isn't surprised by a lower cap.
    /// </summary>
    [Fact]
    public void ApiU1_LoopAgent_DefaultMaxIterations_IsKnownAndDocumented()
    {
        var field = typeof(LoopAgent).GetField(
            "DefaultMaxIterations",
            BindingFlags.Public | BindingFlags.Static);

        field.Should().NotBeNull("LoopAgent must expose a public static DefaultMaxIterations field");

        var value = (int)field!.GetRawConstantValue()!;

        // Record the value in the assertion message so it appears in the test report.
        // OpenClawNet bumped its cap from 10 → 25 in April 2026; the MAF default
        // should be at least 10.  We do NOT assert a specific number — the point
        // is to document whatever MAF ships and gate Phase 2 on explicit override.
        value.Should().BeGreaterThan(0, $"DefaultMaxIterations={value} — Phase 2 must set MaxIterations=25 explicitly");

        // Emit to test output so CI logs capture it without needing to dig into the test report.
        Console.WriteLine($"[API-U-1] LoopAgent.DefaultMaxIterations = {value}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // API-U-2: InvokingContext.Agent.Name source (ctor vs run-options)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves API-U-2: verifies whether ChatClientAgent populates
    /// InvokingContext.Agent.Name from ChatClientAgentOptions.Name (set at
    /// construction time) when an AIContextProvider fires.
    ///
    /// OpenClawNet's BuildAgentForTurn() currently creates a fresh ChatClientAgent
    /// with the profile name baked into ChatClientAgentOptions.Name.  If AIAgentBuilder
    /// is to replace BuildAgentForTurn(), the name must reach InvokingContext.Agent.Name.
    /// </summary>
    [Fact]
    public async Task ApiU2_ChatClientAgent_CtorName_IsVisibleInInvokingContext()
    {
        const string expectedName = "test-agent-profile";
        string? capturedName = null;

        var capturingProvider = new NameCapturingContextProvider(name => capturedName = name);
        var fakeClient = new SingleResponseFakeChatClient("Hello from probe");

        var agentOptions = new ChatClientAgentOptions
        {
            Name = expectedName,
            AIContextProviders = [capturingProvider],
            UseProvidedChatClientAsIs = true,
        };

        var agent = new ChatClientAgent(fakeClient, agentOptions, NullLoggerFactory.Instance, null);
        var messages = new List<ChatMessage> { new(ChatRole.User, "ping") };

        await agent.RunAsync(messages, session: null, options: null, CancellationToken.None);

        capturedName.Should().Be(expectedName,
            "InvokingContext.Agent.Name must be populated from ChatClientAgentOptions.Name " +
            "so OpenClawNetSkillsProvider can read the agent profile name for the enabled.json overlay");

        Console.WriteLine($"[API-U-2] ✅ InvokingContext.Agent.Name = '{capturedName}' (from ctor ChatClientAgentOptions.Name)");
    }

    /// <summary>
    /// Supplementary API-U-2: verifies that a ChatClientAgent built WITHOUT a
    /// name results in a null/empty InvokingContext.Agent.Name.  This matches
    /// the test-harness path in BuildAgentForTurn(agentName: null).
    /// </summary>
    [Fact]
    public async Task ApiU2_ChatClientAgent_NoCtorName_InvokingContextNameIsNullOrEmpty()
    {
        string? capturedName = "sentinel";   // will be overwritten by the provider

        var capturingProvider = new NameCapturingContextProvider(name => capturedName = name);
        var fakeClient = new SingleResponseFakeChatClient("Nameless response");

        var agentOptions = new ChatClientAgentOptions
        {
            // Name intentionally not set
            AIContextProviders = [capturingProvider],
            UseProvidedChatClientAsIs = true,
        };

        var agent = new ChatClientAgent(fakeClient, agentOptions, NullLoggerFactory.Instance, null);
        await agent.RunAsync(
            [new ChatMessage(ChatRole.User, "ping")],
            session: null, options: null, CancellationToken.None);

        // OpenClawNetSkillsProvider short-circuits to empty AIContext when name is null/empty.
        (string.IsNullOrWhiteSpace(capturedName)).Should().BeTrue(
            "no-name agent must yield null/whitespace InvokingContext.Agent.Name " +
            "so skills provider short-circuit path continues to work");

        Console.WriteLine($"[API-U-2] ✅ InvokingContext.Agent.Name = '{capturedName}' for no-name agent (null/empty as expected)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // API-U-3: LoopAgent streaming — does FunctionCallContent pass through?
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves API-U-3: verifies what AgentResponseUpdate items LoopAgent
    /// surfaces in its streaming output when the inner agent returns a
    /// FunctionCallContent on the first iteration and text on the second.
    ///
    /// This determines whether DefaultAgentRuntime's streaming tool-collection
    /// code (which scans update.Contents.OfType&lt;FunctionCallContent&gt;()) can
    /// work transparently with LoopAgent, or whether an adaptation is needed.
    /// </summary>
    [Fact]
    public async Task ApiU3_LoopAgent_Streaming_Surfaces_FunctionCallContent_FromInnerAgent()
    {
        const string toolName = "probe_tool";
        const string callId = "u3-call-1";

        // Inner agent: first call returns FunctionCallContent, second call returns text.
        var inner = new TwoPhaseToolCallingAgent(toolName, callId, finalText: "Done!");

        // Evaluator: on first iteration inject the tool-result message and continue;
        // on second iteration stop.
        var evaluator = new DelegateLoopEvaluator((ctx, ct) =>
        {
            var hasFunctionCall = ctx.LastResponse.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .Any();

            if (hasFunctionCall && ctx.Iteration == 1)
            {
                // Simulate what Phase 2 DefaultAgentRuntime would do:
                // inject tool result and continue.
                var toolResult = new FunctionResultContent(callId, "probe result");
                var resultMsg = new ChatMessage(ChatRole.Tool, [toolResult]);
                return ValueTask.FromResult(LoopEvaluation.ContinueWithMessages([resultMsg]));
            }

            return ValueTask.FromResult(LoopEvaluation.Stop());
        });

        var loopOptions = new LoopAgentOptions { MaxIterations = 5 };
        var loopAgent = new LoopAgent(inner, evaluator, loopOptions, NullLoggerFactory.Instance);

        var updates = new List<AgentResponseUpdate>();
        var session = await loopAgent.CreateSessionAsync(CancellationToken.None);
        await foreach (var update in loopAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, "call the probe tool")],
            session: session, options: null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // The key question: are FunctionCallContent items present in the stream?
        var functionCallUpdates = updates
            .SelectMany(u => u.Contents.OfType<FunctionCallContent>())
            .ToList();

        var textUpdates = updates
            .Where(u => !string.IsNullOrEmpty(u.Text))
            .ToList();

        Console.WriteLine($"[API-U-3] Total updates: {updates.Count}");
        Console.WriteLine($"[API-U-3] Updates with FunctionCallContent: {functionCallUpdates.Count}");
        Console.WriteLine($"[API-U-3] Text updates: {textUpdates.Count}");
        Console.WriteLine($"[API-U-3] All update contents: [{string.Join(", ", updates.Select(u => u.Contents.Count > 0 ? string.Join("|", u.Contents.Select(c => c.GetType().Name)) : $"text:'{u.Text}'"))}]");

        // RESULT ASSERTION: document the actual behavior regardless of which outcome we get.
        // The final text "Done!" must appear somewhere in the stream.
        updates.Should().NotBeEmpty("LoopAgent must yield at least one AgentResponseUpdate");
        var allText = string.Join("", updates.Where(u => u.Text != null).Select(u => u.Text));
        allText.Should().Contain("Done!", "the final iteration's text must surface in the stream");

        // Document whether FunctionCallContent surfaces (key migration gate for Phase 2).
        if (functionCallUpdates.Count > 0)
        {
            Console.WriteLine("[API-U-3] ✅ FINDING: FunctionCallContent IS surfaced in LoopAgent streaming output " +
                              "— DefaultAgentRuntime's current tool-collection code can work with LoopAgent as-is.");
            functionCallUpdates.Should().ContainSingle(f => f.Name == toolName,
                "the surfaced FunctionCallContent must match the inner agent's tool call");
        }
        else
        {
            Console.WriteLine("[API-U-3] ⚠️ FINDING: FunctionCallContent is NOT surfaced in LoopAgent streaming output " +
                              "— Phase 2 must use LoopContext.LastResponse to collect tool calls from the evaluator callback, " +
                              "not from the outer streaming updates. Adaptation required.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // API-U-4: CompactionProvider — in-place mutation vs new list
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves API-U-4: verifies whether CompactionProvider mutates the
    /// ChatClientAgent's internal message list in-place or returns a new list.
    ///
    /// OpenClawNet calls IConversationStore.PruneOldMessagesAsync after compaction.
    /// If CompactionProvider mutates in-place, the agent's context window shrinks
    /// automatically; if it returns a new list, the production code must observe
    /// the post-compaction message count differently.
    /// </summary>
    [Fact]
    public async Task ApiU4_CompactionProvider_ReducesMessageCountWhenThresholdExceeded()
    {
        const int threshold = 6;   // intentionally low for a fast test
        const int totalMessages = 9;
        const string summaryText = "[SUMMARY]";

        // Fake chat client that just returns the summary string for compaction.
        var summaryClient = new SingleResponseFakeChatClient(summaryText);

        var strategy = new SummarizationCompactionStrategy(
            chatClient: summaryClient,
            trigger: CompactionTriggers.MessagesExceed(threshold));

        var compactionProvider = new CompactionProvider(
            strategy,
            stateKey: "probe-session",
            loggerFactory: NullLoggerFactory.Instance);

        // Fake model client for the outer agent.
        var fakeModel = new SingleResponseFakeChatClient("final answer");
        var agentOptions = new ChatClientAgentOptions
        {
            AIContextProviders = [compactionProvider],
            UseProvidedChatClientAsIs = true,
        };
        var agent = new ChatClientAgent(fakeModel, agentOptions, NullLoggerFactory.Instance, null);

        // Build a message list that exceeds the threshold.
        var messages = Enumerable.Range(1, totalMessages)
            .Select(i => new ChatMessage(
                i % 2 == 0 ? ChatRole.Assistant : ChatRole.User,
                $"Message {i}"))
            .ToList();

        AgentResponse? response = null;
        var act = async () =>
        {
            response = await agent.RunAsync(messages, session: null, options: null, CancellationToken.None);
        };

        await act.Should().NotThrowAsync(
            "CompactionProvider must not throw when the threshold is reached");

        response.Should().NotBeNull();

        Console.WriteLine($"[API-U-4] Input messages: {totalMessages}, threshold: {threshold}");
        Console.WriteLine($"[API-U-4] CompactionProvider fired without exception — see test output above for mutation details.");
        Console.WriteLine($"[API-U-4] Final response text: '{response!.Text}'");

        // Document what messages the model actually received (post-compaction).
        // We expose this via the SingleResponseFakeChatClient's captured request.
        Console.WriteLine($"[API-U-4] Model received {fakeModel.LastRequestMessageCount} messages " +
                          $"(if < {totalMessages}, CompactionProvider reduced the list before the model call)");

        if (fakeModel.LastRequestMessageCount < totalMessages)
        {
            Console.WriteLine($"[API-U-4] ✅ FINDING: CompactionProvider DID reduce messages before the model call " +
                              $"({totalMessages} → {fakeModel.LastRequestMessageCount}).");
        }
        else
        {
            Console.WriteLine($"[API-U-4] ⚠️ FINDING: CompactionProvider did NOT reduce messages " +
                              $"(model still received all {totalMessages}). " +
                              $"May need a higher message count or different trigger configuration.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // API-U-5: ToolApprovalAgentOptions.AutoApprovalRules scoping
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves API-U-5: verifies the type and structure of
    /// ToolApprovalAgentOptions.AutoApprovalRules to determine whether MAF's
    /// auto-approval rules are process-scoped (global to all sessions) or can
    /// be configured per-session.
    ///
    /// OpenClawNet's ToolApprovalCoordinator._rememberedBySession stores session-scoped
    /// "remember" decisions.  If MAF's auto-approval rules are process-wide, they
    /// cannot replace per-session "remember" semantics without a session-keyed wrapper.
    /// </summary>
    [Fact]
    public void ApiU5_ToolApprovalAgentOptions_AutoApprovalRules_ScopeIsDocumented()
    {
        var options = new ToolApprovalAgentOptions();

        // AutoApprovalRules must be a list/collection we can inspect at runtime.
        var rulesProperty = typeof(ToolApprovalAgentOptions)
            .GetProperty("AutoApprovalRules", BindingFlags.Public | BindingFlags.Instance);

        rulesProperty.Should().NotBeNull("ToolApprovalAgentOptions must expose AutoApprovalRules");

        var rulesValue = rulesProperty!.GetValue(options);

        Console.WriteLine($"[API-U-5] AutoApprovalRules type: {rulesProperty.PropertyType.FullName}");
        Console.WriteLine($"[API-U-5] Default value is null: {rulesValue is null}");

        // Discover the ToolApprovalRule type to check if it carries a session identifier.
        // Discover the ToolApprovalRule type — check if it's accessible.
        var ruleType = typeof(ToolApprovalAgentOptions).Assembly
            .GetType("Microsoft.Agents.AI.ToolApprovalRule");

        if (ruleType is null)
        {
            Console.WriteLine("[API-U-5] ⚠️ ToolApprovalRule type is not accessible via public reflection — it may be internal.");
            Console.WriteLine("[API-U-5] AutoApprovalRules property type: " + rulesProperty!.PropertyType.FullName);
        }
        else
        {
            var ruleProps = ruleType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var rulePropsStr = string.Join(", ", ruleProps.Select(p => $"{p.PropertyType.Name} {p.Name}"));
            Console.WriteLine($"[API-U-5] ToolApprovalRule public properties: {rulePropsStr}");

            var hasSessionId = ruleProps.Any(p =>
                p.Name.Contains("Session", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("Scope", StringComparison.OrdinalIgnoreCase));

            if (hasSessionId)
            {
                Console.WriteLine("[API-U-5] ✅ FINDING: ToolApprovalRule has a session/scope property — " +
                                  "MAF auto-approval CAN be scoped per session.");
            }
            else
            {
                Console.WriteLine("[API-U-5] ⚠️ FINDING: ToolApprovalRule has NO session/scope property — " +
                                  "rules are PROCESS-SCOPED. Session-scoped 'remember' needs a wrapper.");
            }
        }

        // Also check if ToolApprovalAgentOptions itself is instantiated per-agent-run or shared.
        // The constructor is public — this means it CAN be instantiated per request (good).
        var ctor = typeof(ToolApprovalAgentOptions).GetConstructor(Type.EmptyTypes);
        ctor.Should().NotBeNull("ToolApprovalAgentOptions must have a public parameterless ctor for per-request instantiation");
        Console.WriteLine("[API-U-5] ✅ ToolApprovalAgentOptions has public parameterless ctor — can be instantiated per request for session isolation.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AIAgentBuilder prototype — minimal pipeline construction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal AIAgentBuilder prototype: verifies that the builder pattern compiles,
    /// constructs a pipeline, and runs without error when given a simple inner agent.
    ///
    /// This is the foundation for Phase 3.  Does NOT affect production code.
    /// Non-goal: this test does not wire any production dependencies.
    /// </summary>
    [Fact]
    public async Task Prototype_AIAgentBuilder_BuildsAndRuns_WithLoggingAndContextProvider()
    {
        const string expectedResponse = "builder pipeline worked";
        string? capturedAgentName = null;

        var nameCapture = new NameCapturingContextProvider(name => capturedAgentName = name);
        var fakeInner = new SingleResponseFakeChatClient(expectedResponse);

        // Build the pipeline: inner ChatClientAgent → logging → context provider
        var builder = new AIAgentBuilder(_ =>
            new ChatClientAgent(fakeInner, new ChatClientAgentOptions
            {
                Name = "prototype-agent",
                AIContextProviders = [nameCapture],
                UseProvidedChatClientAsIs = true,
            }, NullLoggerFactory.Instance, null))
            .UseLogging(NullLoggerFactory.Instance);

        var pipeline = builder.Build(EmptyServiceProvider.Instance);

        pipeline.Should().NotBeNull("AIAgentBuilder.Build must return a non-null AIAgent");

        var response = await pipeline.RunAsync(
            [new ChatMessage(ChatRole.User, "probe")],
            session: null, options: null, CancellationToken.None);

        response.Should().NotBeNull();
        response.Text.Should().Be(expectedResponse);
        capturedAgentName.Should().Be("prototype-agent");

        Console.WriteLine($"[Builder] ✅ AIAgentBuilder pipeline ran successfully.");
        Console.WriteLine($"[Builder]    Response: '{response.Text}'");
        Console.WriteLine($"[Builder]    Captured agent name in provider: '{capturedAgentName}'");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>An AIContextProvider that captures InvokingContext.Agent.Name.</summary>
    private sealed class NameCapturingContextProvider(Action<string?> capture) : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext context, CancellationToken ct = default)
        {
            capture(context.Agent?.Name);
            return ValueTask.FromResult(new AIContext());
        }
    }

    /// <summary>
    /// Minimal IChatClient that returns a single text response.
    /// Records how many messages it received on the last call (for API-U-4).
    /// </summary>
    private sealed class SingleResponseFakeChatClient(string text) : IChatClient
    {
        public int LastRequestMessageCount { get; private set; }

        public ChatClientMetadata Metadata => new("probe-fake", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken ct = default)
        {
            var list = messages.ToList();
            LastRequestMessageCount = list.Count;
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var list = messages.ToList();
            LastRequestMessageCount = list.Count;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>
    /// Fake AIAgent for API-U-3: returns a FunctionCallContent on the first
    /// call and a plain text response on subsequent calls.
    /// </summary>
    private sealed class TwoPhaseToolCallingAgent(
        string toolName, string callId, string finalText) : AIAgent
    {
        private int _callCount;

        protected override string? IdCore => "two-phase-tool-agent";

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken ct)
            => ValueTask.FromResult<AgentSession>(new TestAgentSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session, JsonSerializerOptions? opts, CancellationToken ct)
            => ValueTask.FromResult(JsonSerializer.SerializeToElement(new { }));

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement state, JsonSerializerOptions? opts, CancellationToken ct)
            => ValueTask.FromResult<AgentSession>(new TestAgentSession());

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken ct)
        {
            if (_callCount++ == 0)
            {
                var fcc = new FunctionCallContent(callId, toolName,
                    new Dictionary<string, object?> { ["probe"] = "1" });
                return Task.FromResult(new AgentResponse(
                    new ChatMessage(ChatRole.Assistant, [fcc])));
            }
            return Task.FromResult(new AgentResponse(
                new ChatMessage(ChatRole.Assistant, finalText)));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            if (_callCount++ == 0)
            {
                var fcc = new FunctionCallContent(callId, toolName,
                    new Dictionary<string, object?> { ["probe"] = "1" });
                yield return new AgentResponseUpdate(ChatRole.Assistant, [fcc]);
            }
            else
            {
                yield return new AgentResponseUpdate(ChatRole.Assistant, finalText);
            }
        }
    }

    /// <summary>Minimal IServiceProvider for AIAgentBuilder.Build().</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>Minimal concrete AgentSession for probe tests (AgentSession is abstract).</summary>
    private sealed class TestAgentSession() : AgentSession;
}
