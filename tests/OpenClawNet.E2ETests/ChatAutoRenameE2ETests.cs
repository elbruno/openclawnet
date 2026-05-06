using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.E2ETests;

/// <summary>
/// E2E test for S1 (Auto Chat Title) - full journey through the auto-rename endpoint.
/// Tests that the auto-rename endpoint generates a title from conversation context
/// and persists it to storage.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Layer", "E2E")]
public sealed class ChatAutoRenameE2ETests : IClassFixture<ChatAutoRenameE2EFactory>, IAsyncLifetime
{
    private readonly ChatAutoRenameE2EFactory _factory;
    private readonly HttpClient _client;

    public ChatAutoRenameE2ETests(ChatAutoRenameE2EFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Chat_AutoRename_Generates_Title_From_Conversation()
    {
        // ARRANGE: Create a chat session and send 2 messages via scriptable model
        var sessionId = Guid.NewGuid();
        
        // Script the model to respond to both messages
        _factory.Model.SetScript(
            ScriptedTurn.Final("Python is a great language for beginners because it has clear syntax."),
            ScriptedTurn.Final("You can also use it for web development with frameworks like Django and Flask."));

        // Send first message
        var chatResponse1 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "What's a good programming language for beginners?"
        });
        chatResponse1.IsSuccessStatusCode.Should().BeTrue(
            $"first chat message should succeed (got {chatResponse1.StatusCode}: {await chatResponse1.Content.ReadAsStringAsync()})");

        // Send second message
        var chatResponse2 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "Can I use it for web development?"
        });
        chatResponse2.IsSuccessStatusCode.Should().BeTrue(
            $"second chat message should succeed (got {chatResponse2.StatusCode}: {await chatResponse2.Content.ReadAsStringAsync()})");

        // Script the model for the auto-rename call (naming service will invoke the model)
        _factory.Model.SetScript(
            ScriptedTurn.Final("Python For Beginners Discussion"));

        // ACT: POST to auto-rename endpoint
        var renameResponse = await _client.PostAsync($"/api/chat/{sessionId}/auto-rename", null);

        // ASSERT: 200 OK with non-empty GeneratedName
        renameResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"auto-rename should succeed (got {await renameResponse.Content.ReadAsStringAsync()})");

        var renameResult = await renameResponse.Content.ReadFromJsonAsync<AutoRenameResponse>();
        renameResult.Should().NotBeNull();
        renameResult!.GeneratedName.Should().NotBeNullOrWhiteSpace(
            "auto-rename must generate a non-empty session title");
        renameResult.Updated.Should().BeTrue(
            "auto-rename should confirm the session was updated");
        renameResult.GeneratedName.Should().Contain("Python",
            "the generated name should reflect the conversation topic");

        // OPTIONAL: GET session and verify title is persisted
        using var scope = _factory.Services.CreateScope();
        var conversationStore = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        var session = await conversationStore.GetSessionAsync(sessionId);
        
        session.Should().NotBeNull("session should exist in storage");
        session!.Title.Should().Be(renameResult.GeneratedName,
            "the session title in storage should match the generated name");
    }

    private sealed record AutoRenameResponse
    {
        public string GeneratedName { get; init; } = "";
        public bool Updated { get; init; }
    }
}

/// <summary>
/// Dedicated factory for auto-rename E2E tests.
/// Uses the ScriptableModelClient pattern from JobToolE2EWebAppFactory to make tests deterministic.
/// </summary>
public sealed class ChatAutoRenameE2EFactory : WebApplicationFactory<OpenClawNet.Gateway.GatewayProgramMarker>
{
    public ScriptableModelClient Model { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:openclawnet-db"] = "Data Source=:memory:",
                ["Teams:Enabled"] = "false",
                ["Model:Provider"] = "ollama",
                ["Model:Model"] = "test",
                ["Model:Endpoint"] = "http://localhost:11434",
            });
        });

        builder.ConfigureServices(services =>
        {
            // In-memory database (one DB per factory instance)
            var dbOptionsDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OpenClawDbContext>)
                         || d.ServiceType == typeof(IDbContextFactory<OpenClawDbContext>))
                .ToList();
            foreach (var d in dbOptionsDescriptors) services.Remove(d);

            var dbName = $"chat-auto-rename-e2e-{Guid.NewGuid()}";
            var testDbOptions = new DbContextOptionsBuilder<OpenClawDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new InMemoryDbContextFactory(testDbOptions));

            // Scriptable model client: tests script what the LLM should return
            services.RemoveAll<IModelClient>();
            services.AddSingleton<IModelClient>(Model);

            // JobExecutor is required by the gateway services
            services.AddScoped<OpenClawNet.Gateway.Services.JobExecutor>();
        });
    }

    private sealed class InMemoryDbContextFactory(DbContextOptions<OpenClawDbContext> options)
        : IDbContextFactory<OpenClawDbContext>
    {
        public OpenClawDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// Deterministic IModelClient for E2E tests.
/// Copied from JobToolE2EInfrastructure.cs to avoid cross-project dependency.
/// </summary>
public sealed class ScriptableModelClient : IModelClient
{
    public string ProviderName => "scripted";

    private readonly object _lock = new();
    private readonly Queue<ScriptedTurn> _turns = new();

    public List<ChatRequest> Requests { get; } = new();

    public void SetScript(params ScriptedTurn[] turns)
    {
        lock (_lock)
        {
            _turns.Clear();
            Requests.Clear();
            foreach (var t in turns) _turns.Enqueue(t);
        }
    }

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ScriptedTurn turn;
        lock (_lock)
        {
            Requests.Add(request);
            turn = _turns.Count > 0 ? _turns.Dequeue() : ScriptedTurn.Final("(scripted client out of turns)");
        }

        return Task.FromResult(new ChatResponse
        {
            Content = turn.Content ?? string.Empty,
            Role = ChatMessageRole.Assistant,
            Model = request.Model ?? "scripted",
            ToolCalls = turn.ToolCalls,
            Usage = new UsageInfo { PromptTokens = 1, CompletionTokens = 1, TotalTokens = 2 },
            FinishReason = turn.ToolCalls is { Count: > 0 } ? "tool_calls" : "stop"
        });
    }

    public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await CompleteAsync(request, cancellationToken);
        if (response.ToolCalls is { Count: > 0 })
        {
            yield return new ChatResponseChunk { ToolCalls = response.ToolCalls };
        }
        if (!string.IsNullOrEmpty(response.Content))
        {
            yield return new ChatResponseChunk { Content = response.Content, FinishReason = "stop" };
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

public sealed record ScriptedTurn(string? Content, IReadOnlyList<ToolCall>? ToolCalls)
{
    public static ScriptedTurn CallTool(string toolName, string argumentsJson, string callId = "call_1")
        => new(Content: null, ToolCalls: new[]
        {
            new ToolCall { Id = callId, Name = toolName, Arguments = argumentsJson }
        });

    public static ScriptedTurn Final(string content)
        => new(Content: content, ToolCalls: null);
}
