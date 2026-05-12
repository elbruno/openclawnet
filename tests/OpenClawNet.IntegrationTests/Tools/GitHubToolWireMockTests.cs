using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GitHub;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OpenClawNet.IntegrationTests.Tools;

/// <summary>
/// Integration test for S2 (GitHub Repo Insights) - WireMock round-trip test.
/// Tests that GitHubTool can successfully fetch repository stats through the
/// hermetic IGitHubClientFactory pattern pointing at a WireMock server.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class GitHubToolWireMockTests : IAsyncLifetime
{
    private WireMockServer? _wireMockServer;
    private IServiceProvider? _serviceProvider;
    private const string TestOwner = "testorg";
    private const string TestRepo = "testrepo";

    public Task InitializeAsync()
    {
        // Start WireMock server
        _wireMockServer = WireMockServer.Start();
        
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_wireMockServer is not null)
        {
            _wireMockServer.Stop();
            _wireMockServer.Dispose();
        }
        
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GitHubTool_Summary_RoundTrip_Returns_Repo_Stats()
    {
        // ARRANGE: Set up WireMock stubs for GitHub API endpoints
        _wireMockServer.Should().NotBeNull();
        
        // Stub GET /repos/{owner}/{repo}
        _wireMockServer!.Given(
            Request.Create()
                .WithPath($"/repos/{TestOwner}/{TestRepo}")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""
                    {
                        "id": 123456,
                        "name": "{{TestRepo}}",
                        "full_name": "{{TestOwner}}/{{TestRepo}}",
                        "owner": {
                            "login": "{{TestOwner}}"
                        },
                        "description": "A test repository for WireMock integration tests",
                        "stargazers_count": 42,
                        "open_issues_count": 15,
                        "updated_at": "2026-05-06T14:30:00Z",
                        "pushed_at": "2026-05-06T13:45:00Z"
                    }
                    """));

        // Stub GET /search/issues for issues count (repo:owner/repo is:issue is:open)
        _wireMockServer.Given(
            Request.Create()
                .WithPath("/search/issues")
                .WithParam("q", $"repo:{TestOwner}/{TestRepo} is:issue is:open")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "total_count": 8,
                        "incomplete_results": false,
                        "items": []
                    }
                    """));

        // Stub GET /search/issues for PR count (repo:owner/repo is:pr is:open)
        _wireMockServer.Given(
            Request.Create()
                .WithPath("/search/issues")
                .WithParam("q", $"repo:{TestOwner}/{TestRepo} is:pr is:open")
                .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "total_count": 3,
                        "incomplete_results": false,
                        "items": []
                    }
                    """));

        // Configure DI container with WireMock base URL
        var services = new ServiceCollection();
        
        // Add configuration pointing to WireMock
        var configValues = new Dictionary<string, string?>
        {
            ["GitHub:ApiBaseUrl"] = _wireMockServer.Urls[0]
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // Add in-memory storage for secrets (no secrets needed for this test, but tool requires ISecretsStore)
        services.AddSingleton<ISecretsStore, InMemorySecretsStore>();
        
        // Add scope factory
        services.AddScoped<IServiceScopeFactory>(sp => sp.GetRequiredService<IServiceScopeFactory>());
        
        // Add GitHubTool with factory pointing at WireMock
        services.AddGitHubTool();
        
        _serviceProvider = services.BuildServiceProvider();

        // Resolve GitHubTool from DI
        var gitHubTool = _serviceProvider.GetServices<ITool>()
            .OfType<GitHubTool>()
            .FirstOrDefault();
        
        gitHubTool.Should().NotBeNull("GitHubTool should be registered in DI");

        // ACT: Invoke summary action
        var input = new ToolInput
        {
            ToolName = "github",
            RawArguments = System.Text.Json.JsonSerializer.Serialize(new
            {
                action = "summary",
                owner = TestOwner,
                repo = TestRepo
            })
        };

        var result = await gitHubTool!.ExecuteAsync(input);

        // ASSERT: Verify result contains expected stats from WireMock stubs
        result.Should().NotBeNull();
        result.Success.Should().BeTrue($"tool execution should succeed; error: {result.Error}");
        result.Output.Should().NotBeNullOrWhiteSpace("summary should return content");
        
        // Verify the output contains the stats we stubbed
        result.Output.Should().Contain($"{TestOwner}/{TestRepo}", 
            "output should contain the repository name");
        result.Output.Should().Contain("8 open issues", 
            "output should contain the issue count from WireMock");
        result.Output.Should().Contain("3 open PRs", 
            "output should contain the PR count from WireMock");
        result.Output.Should().Contain("⭐ 42", 
            "output should contain the star count from WireMock");
        result.Output.Should().Contain("A test repository for WireMock integration tests",
            "output should contain the repository description");

        // Verify WireMock received the expected calls
        var repoRequests = _wireMockServer.LogEntries
            .Where(e => e.RequestMessage.Path == $"/repos/{TestOwner}/{TestRepo}")
            .ToList();
        repoRequests.Should().ContainSingle("WireMock should have received exactly one repo request");

        var searchRequests = _wireMockServer.LogEntries
            .Where(e => e.RequestMessage.Path == "/search/issues")
            .ToList();
        searchRequests.Should().HaveCount(2, "WireMock should have received two search requests (issues + PRs)");
    }

    /// <summary>
    /// In-memory secrets store for testing (no secrets needed, but tool requires the interface).
    /// </summary>
    private sealed class InMemorySecretsStore : ISecretsStore
    {
        private readonly Dictionary<string, (string Value, string? Description, DateTime UpdatedAt)> _secrets = new();

        public Task<string?> GetAsync(string name, CancellationToken ct = default)
            => Task.FromResult<string?>(_secrets.TryGetValue(name, out var s) ? s.Value : null);

        public Task SetAsync(string name, string value, string? description = null, CancellationToken ct = default)
        {
            _secrets[name] = (value, description, DateTime.UtcNow);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_secrets.Remove(name));

        public Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecretSummary>>(
                _secrets.Select(kv => new SecretSummary(kv.Key, kv.Value.Description, kv.Value.UpdatedAt)).ToList());
    }
}
