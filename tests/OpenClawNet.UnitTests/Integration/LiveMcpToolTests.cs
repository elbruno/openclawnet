using FluentAssertions;
using ModelContextProtocol.Client;

namespace OpenClawNet.UnitTests.Integration;

/// <summary>
/// Live tests that verify the MCP protocol round-trips against two real public MCP servers:
/// <list type="bullet">
///   <item><b>Microsoft Learn MCP</b> — <c>https://learn.microsoft.com/api/mcp</c> (no auth)</item>
///   <item><b>GitHub MCP</b> — <c>https://api.githubcopilot.com/mcp/</c> (requires PAT via <c>GITHUB_TOKEN</c>)</item>
/// </list>
///
/// <para><b>Prerequisites</b> (tests skip cleanly when missing):</para>
/// <list type="bullet">
///   <item>Network access to the public endpoints.</item>
///   <item>For GitHub MCP: <c>GITHUB_TOKEN</c> environment variable with <c>read:user</c> + <c>repo</c> scopes.</item>
/// </list>
///
/// <para>Per Bruno's 2026-04-24 directive: local-only tests, no CI workflow. Run with:</para>
/// <code>dotnet test --filter "Category=Live&amp;Category=McpRequired"</code>
/// </summary>
[Trait("Category", "Live")]
[Trait("Category", "McpRequired")]
public sealed class LiveMcpToolTests
{
    private const string MsLearnMcpEndpoint = "https://learn.microsoft.com/api/mcp";
    private const string GitHubMcpEndpoint = "https://api.githubcopilot.com/mcp/";
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(60);

    [SkippableFact]
    public async Task Live_MicrosoftLearnMcp_ListsTools_AndQueriesDocs()
    {
        // MS Learn MCP is public (no auth), but requires network access.
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(MsLearnMcpEndpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = ConnectionTimeout
        });

        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Skip.IfNot(false, $"MS Learn MCP endpoint unreachable: {ex.Message}");
        }

        await using (client)
        {
            client.Should().NotBeNull("connection should succeed if endpoint is reachable");

            // 1) List tools — MS Learn exposes microsoft_docs_search, microsoft_docs_fetch, microsoft_code_sample_search.
            var tools = await client!.ListToolsAsync().ConfigureAwait(false);
            tools.Should().NotBeEmpty("MS Learn MCP should expose at least one tool");
            tools.Should().Contain(t => t.Name.Contains("search") || t.Name.Contains("docs"),
                "MS Learn MCP should expose docs search or fetch tools");

            // 2) Invoke a search tool to prove the protocol round-trips.
            var searchTool = tools.FirstOrDefault(t => t.Name.Contains("search"));
            if (searchTool is not null)
            {
                var result = await client.CallToolAsync(
                    searchTool.Name,
                    new Dictionary<string, object?> { ["query"] = "Azure Functions" },
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                result.Should().NotBeNull();
                result.Content.Should().NotBeEmpty("search should return at least one content block");
                // MS Learn returns TextContentBlock with docs snippets.
                result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                    .Should().NotBeEmpty("search result should contain text content");
            }
        }
    }

    [SkippableFact]
    public async Task Live_GitHubMcp_ListsTools_AndReadsRepoData()
    {
        // GitHub MCP requires a PAT via GITHUB_TOKEN env var.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Skip.If(string.IsNullOrWhiteSpace(token),
            "GITHUB_TOKEN not set — export a PAT with read:user + repo scopes to run this test.");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(GitHubMcpEndpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = ConnectionTimeout,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}"
            }
        });

        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Skip.IfNot(false, $"GitHub MCP endpoint unreachable or auth failed: {ex.Message}");
        }

        await using (client)
        {
            client.Should().NotBeNull("connection should succeed if endpoint is reachable and token is valid");

            // 1) List tools — GitHub MCP exposes repository read/search tools.
            var tools = await client!.ListToolsAsync().ConfigureAwait(false);
            tools.Should().NotBeEmpty("GitHub MCP should expose at least one tool");

            // 2) Invoke a simple read tool to prove round-trip (e.g., get_file_contents or search_repositories).
            //    We use elbruno/openclawnet-plan as the test target (this repo).
            var repoTool = tools.FirstOrDefault(t =>
                t.Name.Contains("file") || t.Name.Contains("repo") || t.Name.Contains("search"));

            if (repoTool is not null)
            {
                // Try to read README.md from this repo or search for repos.
                var args = repoTool.Name.Contains("file")
                    ? new Dictionary<string, object?>
                    {
                        ["owner"] = "elbruno",
                        ["repo"] = "openclawnet-plan",
                        ["path"] = "README.md"
                    }
                    : new Dictionary<string, object?> { ["query"] = "openclawnet repo:elbruno/openclawnet-plan" };

                var result = await client.CallToolAsync(
                    repoTool.Name,
                    args,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                result.Should().NotBeNull();
                result.Content.Should().NotBeEmpty("GitHub MCP tool should return content");
                result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                    .Should().NotBeEmpty("result should contain text content");
            }
        }
    }
}
