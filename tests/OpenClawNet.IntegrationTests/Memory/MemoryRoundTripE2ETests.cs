using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Memory;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Core;
using OpenClawNet.Tools.Memory;
using OCChatMessage = OpenClawNet.Models.Abstractions.ChatMessage;
using OCChatResponse = OpenClawNet.Models.Abstractions.ChatResponse;
using ModelToolCall = OpenClawNet.Models.Abstractions.ToolCall;

#pragma warning disable MAAI001

namespace OpenClawNet.IntegrationTests.Memory;

/// <summary>
/// End-to-end demo for plan-issue #103 — proves the memory loop now closes through the
/// real agent runtime + tool stack after #98 (MempalaceAgentMemoryStore) and
/// #100 (RememberTool/RecallTool wiring) merged.
/// </summary>
/// <remarks>
/// The LLM is stubbed (<see cref="ScriptedModelClient"/>) on purpose — see PR body. The
/// point of this test is the Remember→Recall loop and per-agent isolation through the
/// production tool path, not the model's reasoning. Everything else (agent runtime,
/// orchestrator, agent context accessor, real <see cref="MempalaceAgentMemoryStore"/>
/// with a deterministic embedder, tool registry, executor, conversation store) runs as
/// in production.
/// </remarks>
public sealed class MemoryRoundTripE2ETests : IDisposable
{
    private readonly string _root;
    private readonly StorageOptions _storageOptions;
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public MemoryRoundTripE2ETests()
    {
        _root = Path.Combine(
            Path.GetDirectoryName(typeof(MemoryRoundTripE2ETests).Assembly.Location)!,
            "memory-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _storageOptions = new StorageOptions { RootPath = _root };
        _storageOptions.EnsureDirectories();

        var dbOptions = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase($"memory-e2e-{Guid.NewGuid()}")
            .Options;
        _dbFactory = new InMemoryDbContextFactory(dbOptions);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    [Fact]
    public async Task Turn1_Remember_Turn2_Recall_FactSurfacesInAssistantResponse()
    {
        const string agentId = "demo-agent";
        const string fact = "my favorite color is teal";

        await using var harness = BuildHarness();

        // Turn 1 — model decides to call RememberTool with the user's fact.
        harness.Model.Enqueue(ToolCallResponse(RememberTool.ToolName, new { content = fact, kind = "preference" }));
        harness.Model.Enqueue(TextResponse("Got it — I'll remember that."));

        var sessionId = Guid.NewGuid();

        var turn1 = await harness.Orchestrator.ProcessAsync(new AgentRequest
        {
            SessionId = sessionId,
            UserMessage = "Please remember: " + fact,
            Model = "llama3.2",
            AgentProfileName = agentId
        });

        turn1.ToolCallCount.Should().Be(1, "turn 1 should invoke RememberTool exactly once");
        turn1.ToolResults.Should().ContainSingle()
            .Which.Success.Should().BeTrue("RememberTool must succeed under an active agent context");

        // Sanity: the memory really landed in the per-agent palace.
        var directHits = await harness.MemoryStore.SearchAsync(agentId, "favorite color", topK: 5);
        directHits.Select(h => h.Content).Should().Contain(fact,
            "the agent's palace must persist the fact stored on turn 1");

        // Turn 2 — model decides to call RecallTool, then in the next iteration
        // composes its answer from the recalled hit. The scripted client mirrors
        // what an LLM would do: read the tool message, quote the top hit verbatim.
        harness.Model.Enqueue(ToolCallResponse(RecallTool.ToolName, new { query = "favorite color", topK = 3 }));
        harness.Model.EnqueueRecallEcho(harness.MemoryStore, harness.Accessor, "favorite color");

        var turn2 = await harness.Orchestrator.ProcessAsync(new AgentRequest
        {
            SessionId = sessionId,
            UserMessage = "What's my favorite color?",
            Model = "llama3.2",
            AgentProfileName = agentId
        });

        turn2.ToolCallCount.Should().Be(1, "turn 2 should invoke RecallTool exactly once");
        turn2.ToolResults.Should().ContainSingle()
            .Which.Success.Should().BeTrue("RecallTool must succeed under an active agent context");

        // The RecallTool's JSON output must carry the fact end-to-end through the production
        // tool path (executor → MempalaceAgentMemoryStore → embedder → palace → back).
        using (var doc = JsonDocument.Parse(turn2.ToolResults[0].Output))
        {
            doc.RootElement.GetProperty("agentId").GetString().Should().Be(agentId);
            doc.RootElement.GetProperty("count").GetInt32().Should().BeGreaterThan(0,
                "RecallTool must surface at least one hit for an agent that called RememberTool earlier");
            var contents = doc.RootElement.GetProperty("hits").EnumerateArray()
                .Select(h => h.GetProperty("content").GetString())
                .ToList();
            contents.Should().Contain(fact, "the stored fact must round-trip through RecallTool's hit list");
        }

        turn2.Content.Should().Contain(fact,
            "the recalled fact must surface in the final assistant response — that's the demo");
    }

    [Fact]
    public async Task TwoAgents_BobCannotRecallAlicesSecret_AtToolLayer()
    {
        const string aliceId  = "alice";
        const string bobId    = "bob";
        const string aliceSecret = "Alice's secret rotation phrase is rosebud-42";

        await using var harness = BuildHarness();

        // ── Alice remembers her secret ───────────────────────────────────────
        harness.Model.Enqueue(ToolCallResponse(RememberTool.ToolName, new { content = aliceSecret }));
        harness.Model.Enqueue(TextResponse("Stored."));

        var aliceTurn = await harness.Orchestrator.ProcessAsync(new AgentRequest
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Please remember: " + aliceSecret,
            Model = "llama3.2",
            AgentProfileName = aliceId
        });
        aliceTurn.ToolResults.Should().ContainSingle().Which.Success.Should().BeTrue();

        // ── Bob asks the agent to recall a secret rotation phrase ────────────
        harness.Model.Enqueue(ToolCallResponse(RecallTool.ToolName, new { query = "secret rotation phrase", topK = 5 }));
        harness.Model.EnqueueRecallEcho(harness.MemoryStore, harness.Accessor, "secret rotation phrase",
            fallbackText: "I have no memory of that.");

        var bobTurn = await harness.Orchestrator.ProcessAsync(new AgentRequest
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "What's the secret rotation phrase?",
            Model = "llama3.2",
            AgentProfileName = bobId
        });

        bobTurn.ToolResults.Should().ContainSingle();
        var recallResult = bobTurn.ToolResults[0];
        recallResult.Success.Should().BeTrue("the tool itself runs cleanly — isolation comes from the store");

        using (var doc = JsonDocument.Parse(recallResult.Output))
        {
            doc.RootElement.GetProperty("agentId").GetString().Should().Be(bobId,
                "RecallTool must scope to the ambient agent — never to a body-supplied id");
            doc.RootElement.GetProperty("count").GetInt32().Should().Be(0,
                "Bob must not see any hits — Alice's palace is isolated from his");
        }

        bobTurn.Content.Should().NotContain("rosebud-42",
            "the leak would defeat the whole point of per-agent memory isolation");
        bobTurn.Content.Should().NotContain(aliceSecret);

        // Final cross-check at the store layer: Alice still sees her own fact.
        var aliceDirect = await harness.MemoryStore.SearchAsync(aliceId, "rotation phrase", topK: 5);
        aliceDirect.Select(h => h.Content).Should().Contain(aliceSecret);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private Harness BuildHarness()
    {
        var memoryStore = new MempalaceAgentMemoryStore(
            _storageOptions,
            new DeterministicEmbeddingGenerator(),
            NullLogger<MempalaceAgentMemoryStore>.Instance);

        var accessor = new AsyncLocalAgentContextAccessor();

        var registry = new ToolRegistry();
        registry.Register(new RememberTool(memoryStore, accessor, NullLogger<RememberTool>.Instance));
        registry.Register(new RecallTool(memoryStore, accessor, NullLogger<RecallTool>.Instance));

        var executor = new ToolExecutor(registry, new AlwaysApprovePolicy(), NullLogger<ToolExecutor>.Instance);

        var conversationStore = new ConversationStore(_dbFactory);

        var workspaceLoader = new Mock<IWorkspaceLoader>();
        workspaceLoader
            .Setup(w => w.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapContext(null, null, null));
        var workspaceOptions = Options.Create(new WorkspaceOptions { WorkspacePath = _root });
        var promptComposer = new DefaultPromptComposer(workspaceLoader.Object, workspaceOptions);

        var summaryService = new Mock<ISummaryService>();
        summaryService
            .Setup(s => s.SummarizeIfNeededAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<OCChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var loggerFactory = NullLoggerFactory.Instance;
        var skillsProvider = new AgentSkillsProvider(
            Path.Combine(_root, "skills-empty"), null, null, null, loggerFactory);

        var approvalCoordinator = new ToolApprovalCoordinator(
            NullLogger<ToolApprovalCoordinator>.Instance);

        var model = new ScriptedModelClient();

        var runtime = new DefaultAgentRuntime(
            model,
            promptComposer,
            executor,
            registry,
            conversationStore,
            summaryService.Object,
            skillsProvider,
            approvalCoordinator,
            loggerFactory,
            NullLogger<DefaultAgentRuntime>.Instance);

        var orchestrator = new AgentOrchestrator(
            runtime,
            conversationStore,
            workspaceLoader.Object,
            workspaceOptions,
            NullLogger<AgentOrchestrator>.Instance,
            accessor);

        return new Harness(memoryStore, runtime, orchestrator, accessor, model);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public MempalaceAgentMemoryStore MemoryStore { get; }
        public DefaultAgentRuntime Runtime { get; }
        public AgentOrchestrator Orchestrator { get; }
        public AsyncLocalAgentContextAccessor Accessor { get; }
        public ScriptedModelClient Model { get; }

        public Harness(
            MempalaceAgentMemoryStore memoryStore,
            DefaultAgentRuntime runtime,
            AgentOrchestrator orchestrator,
            AsyncLocalAgentContextAccessor accessor,
            ScriptedModelClient model)
        {
            MemoryStore = memoryStore;
            Runtime = runtime;
            Orchestrator = orchestrator;
            Accessor = accessor;
            Model = model;
        }

        public async ValueTask DisposeAsync() => await MemoryStore.DisposeAsync();
    }

    // ── Helpers for scripting model responses ─────────────────────────────────

    private static ScriptedResponse ToolCallResponse(string toolName, object args) =>
        new(Content: string.Empty, ToolCalls:
        [
            new ModelToolCall
            {
                Id = $"call-{Guid.NewGuid():N}",
                Name = toolName,
                Arguments = JsonSerializer.Serialize(args)
            }
        ]);

    private static ScriptedResponse TextResponse(string text) =>
        new(Content: text, ToolCalls: null);

    // ── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic LLM stub: dequeues a scripted response per call. The special
    /// <see cref="EnqueueRecallEcho"/> helper reads the most-recent tool message
    /// (a RecallTool JSON payload) and echoes its top hit's content into the assistant
    /// reply — that's how a real LLM would surface the recalled fact.
    /// </summary>
    private sealed class ScriptedModelClient : IModelClient
    {
        private readonly Queue<Func<ChatRequest, ScriptedResponse>> _queue = new();

        public string ProviderName => "scripted";

        public void Enqueue(ScriptedResponse response) => _queue.Enqueue(_ => response);

        public void EnqueueRecallEcho(IAgentMemoryStore store, IAgentContextAccessor accessor, string query, string fallbackText = "I don't have that in memory.")
        {
            _queue.Enqueue(_ =>
            {
                var agentId = accessor.Current?.AgentId;
                if (string.IsNullOrEmpty(agentId))
                    return new ScriptedResponse(fallbackText + " [no-agent-ctx]", null);

                var hits = store.SearchAsync(agentId, query, topK: 3).GetAwaiter().GetResult();
                if (hits.Count == 0)
                    return new ScriptedResponse(fallbackText, null);

                return new ScriptedResponse($"Based on what I remember: {hits[0].Content}", null);
            });
        }

        public Task<OCChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (_queue.Count == 0)
                throw new InvalidOperationException("ScriptedModelClient ran out of canned responses.");

            var producer = _queue.Dequeue();
            var scripted = producer(request);
            return Task.FromResult(new OCChatResponse
            {
                Content = scripted.Content,
                Role = ChatMessageRole.Assistant,
                Model = request.Model ?? "scripted",
                ToolCalls = scripted.ToolCalls,
                FinishReason = scripted.ToolCalls is { Count: > 0 } ? "tool_calls" : "stop",
                Usage = new UsageInfo { PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0 }
            });
        }

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var response = await CompleteAsync(request, cancellationToken);
            yield return new ChatResponseChunk
            {
                Content = response.Content,
                ToolCalls = response.ToolCalls,
                FinishReason = response.FinishReason
            };
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed record ScriptedResponse(string Content, IReadOnlyList<ModelToolCall>? ToolCalls);

    private sealed class InMemoryDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public InMemoryDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }

    /// <summary>
    /// Same deterministic 16-dim embedder used by
    /// <c>MempalaceAgentMemoryStoreIsolationTests</c> — keeps the test off any ONNX
    /// model download while exercising the real palace storage path.
    /// </summary>
    private sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private const int Dim = 16;

        public EmbeddingGeneratorMetadata Metadata { get; } = new("deterministic-test", null, "deterministic-16");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = new List<Embedding<float>>();
            foreach (var value in values)
                list.Add(new Embedding<float>(Hash(value)) { CreatedAt = DateTimeOffset.UtcNow });
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType?.IsInstanceOfType(this) == true ? this : null;

        public void Dispose() { }

        private static float[] Hash(string text)
        {
            var vec = new float[Dim];
            unchecked
            {
                var h = 2166136261u;
                foreach (var c in text)
                    h = (h ^ c) * 16777619u;
                var rng = new Random((int)h);
                double sumSq = 0;
                for (var i = 0; i < Dim; i++)
                {
                    var v = rng.NextDouble() * 2 - 1;
                    vec[i] = (float)v;
                    sumSq += v * v;
                }
                var norm = (float)Math.Sqrt(sumSq);
                if (norm > 0)
                    for (var i = 0; i < Dim; i++)
                        vec[i] /= norm;
            }
            return vec;
        }
    }
}
