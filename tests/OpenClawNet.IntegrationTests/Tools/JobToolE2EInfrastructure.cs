using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Tools.MarkItDown;

namespace OpenClawNet.IntegrationTests.Tools;

/// <summary>
/// E2E factory for **job-tool** integration tests. Differs from
/// <see cref="GatewayWebAppFactory"/> in two important ways:
///
/// 1. <see cref="ScriptableModelClient"/> replaces <c>IModelClient</c>. Each test
///    enqueues a scripted set of "turns" — either tool calls the LLM should
///    "decide" to make, or final content. This proves the agent loop wires
///    tools correctly and that <c>JobExecutor</c> reflects tool successes /
///    failures into <c>JobRun</c>.
///
/// 2. The named <c>HttpClient</c> used by <see cref="MarkItDownTool"/> is
///    re-configured to use a swappable in-memory handler
///    (<see cref="FakeHttpHandler"/>) so the tool exercises its real fetch +
///    convert pipeline without ever touching the network. Tests configure
///    canned responses per URL.
///
/// Job execution is invoked through the public HTTP surface
/// (<c>POST /api/jobs/{id}/execute</c>) so the factory exercises the same
/// end-to-end path that the Scheduler hits in production.
/// </summary>
public sealed class JobToolE2EWebAppFactory : WebApplicationFactory<OpenClawNet.Gateway.GatewayProgramMarker>
{
    public ScriptableModelClient Model { get; } = new();
    public FakeHttpHandler MarkItDownHttp { get; } = new();

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
            // In-memory database (one DB per factory instance).
            var dbOptionsDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OpenClawDbContext>)
                         || d.ServiceType == typeof(IDbContextFactory<OpenClawDbContext>))
                .ToList();
            foreach (var d in dbOptionsDescriptors) services.Remove(d);

            var dbName = $"job-tool-e2e-{Guid.NewGuid()}";
            var testDbOptions = new DbContextOptionsBuilder<OpenClawDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new InMemoryDbContextFactory(testDbOptions));

            // Scripted model client: each test enqueues turns describing what
            // the LLM should "decide" to do.
            services.RemoveAll<IModelClient>();
            services.AddSingleton<IModelClient>(Model);

            // JobExecutor lives in the Gateway and is required by /execute.
            services.AddScoped<OpenClawNet.Gateway.Services.JobExecutor>();

            // Re-route the named HttpClient used by MarkItDownTool so requests
            // are answered from the per-test FakeHttpHandler. The named client
            // is created via IHttpClientFactory.CreateClient(nameof(MarkItDownTool)).
            services.Configure<HttpClientFactoryOptions>(nameof(MarkItDownTool), opts =>
            {
                opts.HttpMessageHandlerBuilderActions.Add(builder =>
                {
                    builder.PrimaryHandler = MarkItDownHttp;
                });
            });
        });
    }

    internal sealed class InMemoryDbContextFactory(DbContextOptions<OpenClawDbContext> options)
        : IDbContextFactory<OpenClawDbContext>
    {
        public OpenClawDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// In-memory <see cref="HttpMessageHandler"/> with per-URL canned responses.
/// Used by <see cref="JobToolE2EWebAppFactory"/> to satisfy
/// <see cref="MarkItDownTool"/>'s outbound fetches without hitting the network.
/// Falls back to <c>503 Service Unavailable</c> when a URL is unconfigured so
/// tests fail loudly if they forget to set up a stub.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Configure a successful HTML response for the given URL.</summary>
    public void RespondWithHtml(string url, string html, string contentType = "text/html; charset=utf-8")
    {
        _responders[url] = _ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
            resp.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            return resp;
        };
    }

    /// <summary>Configure a fixed status code (e.g. 500) for the given URL.</summary>
    public void RespondWithStatus(string url, HttpStatusCode status, string body = "")
    {
        _responders[url] = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        if (_responders.TryGetValue(url, out var responder))
        {
            return Task.FromResult(responder(request));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent($"FakeHttpHandler: no responder configured for {url}")
        });
    }
}

/// <summary>
/// Deterministic <see cref="IModelClient"/> stand-in for E2E tests. A test
/// composes a <see cref="ScriptedTurn"/> sequence that lists, in order, what
/// the LLM will "do" on each call: emit tool calls (the agent runtime will
/// execute them and feed the results back) or terminate with final assistant
/// text.
///
/// Critically this does **not** look at the prompt — tests prove that the
/// runtime correctly executes whatever the model asks for. Verifying that an
/// LLM picks the right tool for a prompt is the model provider's job, not
/// the agent's.
/// </summary>
public sealed class ScriptableModelClient : IModelClient
{
    public string ProviderName => "scripted";

    private readonly object _lock = new();
    private readonly Queue<ScriptedTurn> _turns = new();

    /// <summary>Records every <c>CompleteAsync</c> request the agent makes.</summary>
    public List<ChatRequest> Requests { get; } = new();

    /// <summary>Configure the script for the next agent execution.</summary>
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

/// <summary>Single turn in a <see cref="ScriptableModelClient"/> script.</summary>
public sealed record ScriptedTurn(string? Content, IReadOnlyList<ToolCall>? ToolCalls)
{
    /// <summary>Tell the model to call a single tool with the given JSON args.</summary>
    public static ScriptedTurn CallTool(string toolName, string argumentsJson, string callId = "call_1")
        => new(Content: null, ToolCalls: new[]
        {
            new ToolCall { Id = callId, Name = toolName, Arguments = argumentsJson }
        });

    /// <summary>End the agent loop with a final assistant message.</summary>
    public static ScriptedTurn Final(string content)
        => new(Content: content, ToolCalls: null);
}
