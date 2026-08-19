using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Mcp.Abstractions;
using OpenClawNet.Mcp.Core;
using OpenClawNet.Mcp.Web;
using OpenClawNet.Tools.Web;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Deferred from PR-A: real end-to-end exercise of <see cref="InProcessMcpHost"/> +
/// <see cref="McpToolProvider"/> using a bundled wrapper (the simplest one — Web).
/// Verifies a wrapper's tools survive the in-memory transport and surface through the
/// catalog under the expected wire-form name.
/// </summary>
/// <remarks>
/// The round-trip <c>aiFunc.InvokeAsync</c> step from the PR-B plan is intentionally
/// omitted here: this project is excluded from PR CI (Docker/Aspire), so it can't serve as
/// CI-eligible coverage for the CallTool path anyway. ListTools is the guarantee this test
/// focuses on (it proves tool discovery via the catalog), using the timeout-guarded teardown
/// introduced in PR-B for InProcessMcpHost. As of SDK 2.1.0, the CallTool round-trip through
/// the in-memory transport under the xUnit host is verified deterministic (20+ consecutive
/// runs, no hangs) by the CI-eligible
/// <c>OpenClawNet.UnitTests.Mcp.InProcessMcpCallToolRoundTripTests</c>.
/// </remarks>
public sealed class InProcessMcpHostE2ETests
{
    [Fact]
    public async Task BundledWebServer_StartsAndExposesFetchToolThroughProvider()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var host = new InProcessMcpHost(loggerFactory, NullLogger<InProcessMcpHost>.Instance);
        var stdio = new StdioMcpHost(new InMemorySecretStore(), NullLogger<StdioMcpHost>.Instance);

        var reg = new WebBundledMcp();

        // Build the tools by resolving the wrapper through a minimal DI container so the
        // WebTool dependency is satisfied — exactly how BundledMcpStartupService does it.
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(new StubFetchHandler()));
        services.AddSingleton(Options.Create(new WebToolOptions()));
        services.AddSingleton<WebTool>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var tools = reg.CreateTools(provider);
        host.RegisterTools(reg.Definition.Id, tools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var startTask = host.StartAsync(reg.Definition, cts.Token);
        (await Task.WhenAny(startTask, Task.Delay(10_000))).Should().Be(startTask, "InProcessMcpHost.StartAsync must not hang");
        await startTask;

        var catalog = new SingleServerCatalog(reg.Definition);
        var toolProvider = new McpToolProvider(catalog, host, stdio, NullLogger<McpToolProvider>.Instance);

        var listTask = toolProvider.GetAllToolsAsync(cts.Token);
        (await Task.WhenAny(listTask, Task.Delay(10_000))).Should().Be(listTask, "McpToolProvider.GetAllToolsAsync must not hang");
        var allTools = await listTask;

        // Wire-form name = <serverName>_<toolName>; the tool was declared as Name="fetch".
        allTools.Should().ContainSingle();
        var aiFunc = allTools.Single() as Microsoft.Extensions.AI.AIFunction;
        aiFunc.Should().NotBeNull();
        aiFunc!.Name.Should().Be("web_fetch");

        await host.DisposeAsync();
    }

    private sealed class StubFetchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello-from-stub"),
            });
    }

    private sealed class SingleServerCatalog : IMcpServerCatalog
    {
        private readonly McpServerDefinition _def;
        public SingleServerCatalog(McpServerDefinition def) => _def = def;
        public Task<IReadOnlyList<McpServerDefinition>> GetServersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpServerDefinition>>([_def]);
        public Task<IReadOnlyList<McpToolOverride>> GetOverridesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpToolOverride>>([]);
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        public string Protect(string plaintext) => plaintext;
        public string? Unprotect(string ciphertext) => ciphertext;
    }
}
