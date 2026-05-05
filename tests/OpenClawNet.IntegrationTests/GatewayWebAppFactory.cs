using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

using OpenClawNet.Gateway;
using ModelToolCall = OpenClawNet.Models.Abstractions.ToolCall;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// WebApplicationFactory that wires up the Gateway with in-memory dependencies
/// so tests run without Aspire, Ollama, or real SQLite.
/// </summary>
public class GatewayWebAppFactory : WebApplicationFactory<GatewayProgramMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Provide connection string so AddSqliteConnection doesn't fail
                ["ConnectionStrings:openclawnet-db"] = "Data Source=:memory:",
                // Disable Teams adapter
                ["Teams:Enabled"] = "false",
                // Model config
                ["Model:Provider"] = "ollama",
                ["Model:Model"] = "test",
                ["Model:Endpoint"] = "http://localhost:11434",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace EF Core factory with a direct InMemory implementation to avoid
            // "multiple database providers registered" conflict (Sqlite + InMemory)
            var dbOptionsDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OpenClawDbContext>)
                         || d.ServiceType == typeof(IDbContextFactory<OpenClawDbContext>))
                .ToList();
            foreach (var d in dbOptionsDescriptors) services.Remove(d);

            var testDbOptions = new DbContextOptionsBuilder<OpenClawDbContext>()
                .UseInMemoryDatabase($"gateway-test-{Guid.NewGuid()}")
                .Options;
            services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new DirectDbContextFactory(testDbOptions));

            // Replace IModelClient with a fake that returns predictable responses
            services.RemoveAll<IModelClient>();
            services.AddSingleton<IModelClient, FakeModelClient>();

            // Register JobExecutor for job execution endpoints
            services.AddScoped<OpenClawNet.Gateway.Services.JobExecutor>();
        });
    }
}

/// <summary>
/// WebApplicationFactory variant that registers the tool-calling fake model client
/// so integration tests can exercise the streaming tool-call pipeline.
/// </summary>
public sealed class GatewayToolCallWebAppFactory : WebApplicationFactory<GatewayProgramMarker>
{
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
            var dbOptionsDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OpenClawDbContext>)
                         || d.ServiceType == typeof(IDbContextFactory<OpenClawDbContext>))
                .ToList();
            foreach (var d in dbOptionsDescriptors) services.Remove(d);

            var testDbOptions = new DbContextOptionsBuilder<OpenClawDbContext>()
                .UseInMemoryDatabase($"gateway-toolcall-test-{Guid.NewGuid()}")
                .Options;
            services.AddSingleton<IDbContextFactory<OpenClawDbContext>>(
                new DirectDbContextFactory(testDbOptions));

            // Replace IModelClient with the tool-calling fake
            services.RemoveAll<IModelClient>();
            services.AddSingleton<IModelClient, FakeToolCallingModelClient>();

            // Register JobExecutor for job execution endpoints
            services.AddScoped<OpenClawNet.Gateway.Services.JobExecutor>();
        });
    }
}

/// <summary>
/// Fake model client that returns a predictable "Hello!" response without calling any real model.
/// </summary>
internal sealed class FakeModelClient : IModelClient
{
    public string ProviderName => "fake";

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse
        {
            Content = "Hello from fake model!",
            Role = ChatMessageRole.Assistant,
            Model = request.Model ?? "test",
            Usage = new UsageInfo { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 }
        });
    }

    public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseChunk { Content = "Hello " };
        yield return new ChatResponseChunk { Content = "from fake model!", FinishReason = "stop" };
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

/// <summary>
/// Fake model client that returns tool calls in its StreamAsync method,
/// simulating an LLM deciding to call a tool. Used to test the streaming
/// tool-call pipeline end-to-end.
/// </summary>
internal sealed class FakeToolCallingModelClient : IModelClient
{
    public string ProviderName => "fake-toolcall";

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse
        {
            Content = "",
            Role = ChatMessageRole.Assistant,
            Model = request.Model ?? "test",
            ToolCalls =
            [
                new ModelToolCall { Id = "call_test_1", Name = "list_files", Arguments = """{"path":"/src"}""" }
            ],
            Usage = new UsageInfo { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 }
        });
    }

    public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        // First chunk: tool call (simulating LLM deciding to call a tool)
        yield return new ChatResponseChunk
        {
            ToolCalls = [new ModelToolCall { Id = "call_test_1", Name = "list_files", Arguments = """{"path":"/src"}""" }]
        };

        // Second chunk: content after tool execution would happen
        yield return new ChatResponseChunk { Content = "Here are the files.", FinishReason = "stop" };
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

internal sealed class DirectDbContextFactory(DbContextOptions<OpenClawDbContext> options)
    : IDbContextFactory<OpenClawDbContext>
{
    public OpenClawDbContext CreateDbContext() => new(options);
}
