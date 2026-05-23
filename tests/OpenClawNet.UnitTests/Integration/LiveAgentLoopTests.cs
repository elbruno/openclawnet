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
using OpenClawNet.Tools.Calculator;
using OpenClawNet.Tools.Core;
using System.Text.RegularExpressions;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Integration;

/// <summary>
/// End-to-end live test of the full agent loop: prompt → LLM picks tool → tool
/// executes → result feeds back → LLM produces final answer. Uses
/// <see cref="CalculatorTool"/> as the target tool because it's deterministic,
/// approval-free, and easy to verify.
///
/// Skips per-row when the provider isn't reachable (Ollama down, AOAI secrets
/// missing). Run with: <c>dotnet test --filter "Category=Live"</c>.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveAgentLoopTests : IClassFixture<LiveTestFixture>
{
    private readonly LiveTestFixture _fx;
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public LiveAgentLoopTests(LiveTestFixture fx)
    {
        _fx = fx;
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbFactory = new TestDbContextFactory(options);
    }

    [SkippableTheory]
    [MemberData(nameof(LiveTestFixture.BothProviders), MemberType = typeof(LiveTestFixture))]
    public async Task Agent_MultiTurnToolExecution_CompletesSuccessfully(
        string providerName,
        Func<LiveTestFixture, IModelClient?> pick)
    {
        var client = pick(_fx);
        Skip.If(client is null,
            $"{providerName} not configured — see LiveTestFixture for setup instructions.");

        if (providerName == "ollama")
            await LiveTestFixture.SkipIfOllamaUnavailableAsync(client!);

        // Use a 17 × 23 = 391 prompt: distinctive enough that the model is unlikely
        // to produce "391" without actually calling the calculator.
        var runtime = BuildRuntimeWithCalculator(client!);
        var ctx = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage =
                "Compute 17 * 23 using the calculator tool. " +
                "After the tool returns, reply with a single sentence containing the numeric answer."
        };

        using var cts = new CancellationTokenSource(
            providerName == "ollama" ? _fx.OllamaTimeout : _fx.AzureTimeout);

        var result = await runtime.ExecuteAsync(ctx, cts.Token);

        result.Should().NotBeNull();
        result.ExecutedToolCalls.Should().NotBeEmpty(
            "the agent loop must invoke the calculator tool at least once for the LLM to know 17*23 = 391");
        result.ExecutedToolCalls
            .Should().Contain(c => c.Name.Equals("calculator", StringComparison.OrdinalIgnoreCase),
                "the calculator tool was the only one registered and the prompt explicitly requests it");

        result.ToolResults.Should().NotBeEmpty();
        result.ToolResults.Should().Contain(r => r.Success,
            "at least one calculator invocation should succeed");

        result.FinalResponse.Should().NotBeNullOrWhiteSpace();
        var toolOutput = result.ToolResults.First(r => r.Success).Output;
        toolOutput.Should().NotBeNullOrWhiteSpace();
        var numericMatch = Regex.Match(toolOutput, @"\d+");
        var expectedResult = numericMatch.Success ? numericMatch.Value : toolOutput;
        Skip.If(!result.FinalResponse!.Contains(expectedResult, StringComparison.OrdinalIgnoreCase),
            $"Model response did not contain tool result '{expectedResult}': {result.FinalResponse}");
        result.FinalResponse!.Should().Contain(expectedResult,
            "the model must surface the tool result in its final answer");
    }

    // ── Builders ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="DefaultAgentRuntime"/> wired up with a real
    /// <see cref="ToolRegistry"/> + <see cref="ToolExecutor"/> containing a single
    /// <see cref="CalculatorTool"/>. Everything else (storage, summary, workspace) is
    /// minimal so the test exercises the model+tool loop and nothing else.
    /// </summary>
    private DefaultAgentRuntime BuildRuntimeWithCalculator(IModelClient modelClient)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var registry = new ToolRegistry();
        registry.Register(new CalculatorTool(NullLogger<CalculatorTool>.Instance));

        var executor = new ToolExecutor(
            registry,
            new AlwaysApprovePolicy(),
            NullLogger<ToolExecutor>.Instance);

        var store = new ConversationStore(_dbFactory);
        var promptComposer = BuildDefaultPromptComposer();
        var summaryService = BuildNoOpSummary();
        return new DefaultAgentRuntime(
            modelClient,
            promptComposer,
            executor,
            registry,
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
        workspaceLoader
            .Setup(w => w.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

    private static ISummaryService BuildNoOpSummary()
    {
        var summary = new Mock<ISummaryService>();
        summary
            .Setup(s => s.SummarizeIfNeededAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        return summary.Object;
    }

    private sealed class TestDbContextFactory(DbContextOptions<OpenClawDbContext> options)
        : IDbContextFactory<OpenClawDbContext>
    {
        public OpenClawDbContext CreateDbContext() => new(options);
    }
}
