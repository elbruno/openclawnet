using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using OpenClawNet.Mcp.Core;
using OpenClawNet.Mcp.Web;
using OpenClawNet.Tools.Web;

namespace OpenClawNet.UnitTests.Mcp;

/// <summary>
/// Deterministic, CI-eligible round-trip test of the MCP <c>tools/call</c> request/response
/// path (ModelContextProtocol SDK 2.1.0) through the real in-memory transport used in
/// production by <see cref="InProcessMcpHost"/>.
/// </summary>
/// <remarks>
/// Exercises the same real <see cref="WebMcpTools"/> wrapper and stub-backed
/// <see cref="WebTool"/> already used by <c>InProcessMcpHostE2ETests</c>'s ListTools-only
/// coverage, but drives the request through to <c>McpClient.CallToolAsync</c> and asserts on
/// the returned content. This closes the CallTool gap flagged in review without any
/// retry/sleep/suppression workaround — a genuine hang would surface as a real test timeout
/// via the <c>Task.WhenAny</c> guards below, not a silent pass.
/// </remarks>
public sealed class InProcessMcpCallToolRoundTripTests
{
    [Fact]
    public async Task CallToolAsync_ThroughInMemoryTransport_ReturnsRealToolContent()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        await using var host = new InProcessMcpHost(loggerFactory, NullLogger<InProcessMcpHost>.Instance);

        var reg = new WebBundledMcp();

        // Build the tools by resolving the wrapper through a minimal DI container so the
        // WebTool dependency is satisfied — exactly how BundledMcpStartupService does it.
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(new StubFetchHandler()));
        services.AddSingleton(Options.Create(new WebToolOptions()));
        services.AddSingleton<WebTool>();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var tools = reg.CreateTools(provider);
        host.RegisterTools(reg.Definition.Id, tools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var startTask = host.StartAsync(reg.Definition, cts.Token);
        (await Task.WhenAny(startTask, Task.Delay(10_000, cts.Token))).Should().Be(startTask,
            "InProcessMcpHost.StartAsync must not hang");
        await startTask;

        var client = host.GetClient(reg.Definition.Id);
        client.Should().NotBeNull("the in-process server must publish a connected client after StartAsync");

        // 1) List tools through the real transport, then resolve the concrete tool to call —
        //    proves discovery works before proving invocation works.
        var listTask = client!.ListToolsAsync(cancellationToken: cts.Token).AsTask();
        (await Task.WhenAny(listTask, Task.Delay(10_000, cts.Token))).Should().Be(listTask,
            "McpClient.ListToolsAsync must not hang");
        var tool = (await listTask).SingleOrDefault(t => t.Name == "fetch");
        tool.Should().NotBeNull("the web server must expose the 'fetch' tool declared by WebMcpTools");

        // 2) Call the resolved tool through the real request/response path — the production-
        //    critical step review flagged as missing CI coverage.
        var callTask = client.CallToolAsync(
            tool!.Name,
            new Dictionary<string, object?> { ["url"] = "https://example.invalid/stub" },
            cancellationToken: cts.Token).AsTask();
        (await Task.WhenAny(callTask, Task.Delay(10_000, cts.Token))).Should().Be(callTask,
            "McpClient.CallToolAsync must not hang against the in-memory transport");
        var result = await callTask;

        // Assert the actual returned value, not merely that invocation completed.
        result.Should().NotBeNull();
        result.IsError.Should().NotBe(true, "the stub-backed fetch call must succeed, not error");
        result.Content.Should().NotBeEmpty("CallToolAsync must return content from the real tool implementation");
        var text = result.Content.OfType<TextContentBlock>().Select(b => b.Text).FirstOrDefault();
        text.Should().NotBeNull("the fetch tool returns its HTTP response body as text content");
        text.Should().Contain("hello-from-stub", "the response must carry the real (stubbed) HTTP body through the full MCP round-trip");
        text.Should().Contain("HTTP 200", "WebTool.FetchAsync formats the status line into its text output");
    }

    private sealed class StubFetchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello-from-stub"),
            });
    }
}
