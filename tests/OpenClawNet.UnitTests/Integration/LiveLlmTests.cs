using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Agent;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;
using OpenClawNet.Models.Ollama;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

#pragma warning disable MAAI001

namespace OpenClawNet.UnitTests.Integration;

/// <summary>
/// Live integration tests that hit REAL LLM providers — no fakes.
/// Ollama tests require a running instance at localhost:11434 with gemma4:e2b.
/// Azure OpenAI tests require user secrets configured on the Gateway project.
/// All tests skip gracefully when their provider is unavailable (safe for CI).
/// Run with: dotnet test --filter "Category=Live"
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveLlmTests : IDisposable
{
    private const string OllamaEndpoint = "http://localhost:11434";
    private const string OllamaModel = "gemma4:e2b";
    private const string GatewayUserSecretsId = "c15754a6-dc90-4a2a-aecb-1233d1a54fe1";

    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public LiveLlmTests()
    {
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbFactory = new TestDbContextFactory(options);
    }

    // ── Ollama: Direct Client ─────────────────────────────────────────────

    [SkippableFact]
    public async Task Ollama_CompleteAsync_ReturnsResponse()
    {
        var client = BuildOllamaClient();
        Skip.IfNot(await client.IsAvailableAsync(), "Ollama is not running at localhost:11434");

        var response = await client.CompleteAsync(new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.System, Content = "You are a helpful assistant. Be brief." },
                new ChatMessage { Role = ChatMessageRole.User, Content = "Say hello in exactly one sentence." }
            ]
        });

        response.Content.Should().NotBeNullOrWhiteSpace("Ollama should return a non-empty greeting");
        response.Role.Should().Be(ChatMessageRole.Assistant);
    }

    [SkippableFact]
    public async Task Ollama_StreamAsync_YieldsTokens()
    {
        var client = BuildOllamaClient();
        Skip.IfNot(await client.IsAvailableAsync(), "Ollama is not running at localhost:11434");

        var chunks = new List<ChatResponseChunk>();
        await foreach (var chunk in client.StreamAsync(new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.System, Content = "You are a helpful assistant. Be brief." },
                new ChatMessage { Role = ChatMessageRole.User, Content = "Count from 1 to 3." }
            ]
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCountGreaterThan(1, "streaming should yield multiple incremental tokens");
        var fullContent = string.Concat(chunks.Select(c => c.Content ?? ""));
        fullContent.Should().NotBeNullOrWhiteSpace("concatenated tokens should form a non-empty response");
    }

    [SkippableFact]
    public async Task Ollama_Pipeline_SendMessage_ReturnsStreamedContent()
    {
        var client = BuildOllamaClient();
        Skip.IfNot(await client.IsAvailableAsync(), "Ollama is not running at localhost:11434");

        var runtime = BuildRealRuntime(client);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Say hello in one sentence."
        };

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(context))
            events.Add(evt);

        events.Should().NotBeEmpty("the pipeline should yield stream events from a real Ollama response");

        var contentEvents = events.Where(e => e.Type == AgentStreamEventType.ContentDelta).ToList();
        contentEvents.Should().NotBeEmpty("at least one content token should stream through the pipeline");

        var allContent = string.Join("", contentEvents.Select(e => e.Content));
        allContent.Should().NotBeNullOrWhiteSpace(
            "Ollama's real response should flow through DefaultAgentRuntime as content deltas");
    }

    // ── Azure OpenAI: Direct Client ───────────────────────────────────────

    [SkippableFact]
    public async Task AzureOpenAI_CompleteAsync_ReturnsResponse()
    {
        var (client, configured) = BuildAzureOpenAIClient();
        Skip.IfNot(configured, "Azure OpenAI credentials not configured — set user secrets to run live tests.");

        var response = await client.CompleteAsync(new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.System, Content = "You are a helpful assistant. Be brief." },
                new ChatMessage { Role = ChatMessageRole.User, Content = "Say hello in exactly one sentence." }
            ]
        });

        response.Content.Should().NotBeNullOrWhiteSpace("Azure OpenAI should return a non-empty greeting");
        response.Role.Should().Be(ChatMessageRole.Assistant);
        response.Usage.Should().NotBeNull();
        response.Usage!.TotalTokens.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task AzureOpenAI_StreamAsync_YieldsTokens()
    {
        var (client, configured) = BuildAzureOpenAIClient();
        Skip.IfNot(configured, "Azure OpenAI credentials not configured — set user secrets to run live tests.");

        var chunks = new List<ChatResponseChunk>();
        await foreach (var chunk in client.StreamAsync(new ChatRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatMessageRole.System, Content = "You are a helpful assistant. Be brief." },
                new ChatMessage { Role = ChatMessageRole.User, Content = "Count from 1 to 3." }
            ]
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCountGreaterThan(1, "streaming should yield multiple incremental tokens");
        var fullContent = string.Concat(chunks.Select(c => c.Content ?? ""));
        fullContent.Should().NotBeNullOrWhiteSpace("concatenated tokens should form a non-empty response");
    }

    [SkippableFact]
    public async Task AzureOpenAI_Pipeline_SendMessage_ReturnsStreamedContent()
    {
        var (client, configured) = BuildAzureOpenAIClient();
        Skip.IfNot(configured, "Azure OpenAI credentials not configured — set user secrets to run live tests.");

        var runtime = BuildRealRuntime(client);

        var context = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            UserMessage = "Say hello in one sentence."
        };

        var events = new List<AgentStreamEvent>();
        await foreach (var evt in runtime.ExecuteStreamAsync(context))
            events.Add(evt);

        events.Should().NotBeEmpty("the pipeline should yield stream events from a real Azure OpenAI response");

        var contentEvents = events.Where(e => e.Type == AgentStreamEventType.ContentDelta).ToList();
        contentEvents.Should().NotBeEmpty("at least one content token should stream through the pipeline");

        var allContent = string.Join("", contentEvents.Select(e => e.Content));
        allContent.Should().NotBeNullOrWhiteSpace(
            "Azure OpenAI's real response should flow through DefaultAgentRuntime as content deltas");
    }

    // ── Builders ──────────────────────────────────────────────────────────

    private static OllamaModelClient BuildOllamaClient()
    {
        var options = Options.Create(new OllamaOptions
        {
            Endpoint = OllamaEndpoint,
            Model = OllamaModel,
            Temperature = 0.0,
            MaxTokens = 256
        });
        var httpClient = new HttpClient { BaseAddress = new Uri(OllamaEndpoint) };
        return new OllamaModelClient(httpClient, options, NullLogger<OllamaModelClient>.Instance);
    }

    private static (AzureOpenAIModelClient Client, bool IsConfigured) BuildAzureOpenAIClient()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(GatewayUserSecretsId, reloadOnChange: false)
            .Build();

        var opts = new AzureOpenAIOptions();
        if (config["Model:Endpoint"] is { Length: > 0 } ep) opts.Endpoint = ep;
        if (config["Model:ApiKey"] is { Length: > 0 } key) opts.ApiKey = key;
        if (config["Model:DeploymentName"] is { Length: > 0 } dep) opts.DeploymentName = dep;
        if (config["Model:AuthMode"] is { Length: > 0 } mode) opts.AuthMode = mode;

        var isConfigured = !string.IsNullOrEmpty(opts.Endpoint)
            && (opts.AuthMode.Equals("integrated", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(opts.ApiKey));

        var client = new AzureOpenAIModelClient(
            Options.Create(opts), NullLogger<AzureOpenAIModelClient>.Instance);

        return (client, isConfigured);
    }

    /// <summary>
    /// Builds a REAL DefaultAgentRuntime pipeline with a live IModelClient.
    /// No fakes for the model — only mock the peripherals (tools, storage, summary).
    /// </summary>
    private DefaultAgentRuntime BuildRealRuntime(IModelClient realModelClient)
    {
        var store = new ConversationStore(_dbFactory);
        var promptComposer = BuildDefaultPromptComposer();
        var toolExecutor = new Mock<IToolExecutor>().Object;
        var toolRegistry = BuildEmptyRegistry();
        var summaryService = BuildNoOpSummary();
        var loggerFactory = NullLoggerFactory.Instance;
        return new DefaultAgentRuntime(
            realModelClient,
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

    public void Dispose() { }

    private sealed class TestDbContextFactory(DbContextOptions<OpenClawDbContext> options)
        : IDbContextFactory<OpenClawDbContext>
    {
        public OpenClawDbContext CreateDbContext() => new(options);
    }
}
