using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GitHub;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OpenClawNet.E2ETests.Scenarios;

/// <summary>
/// Scenario 2: the GitHub tool exposes a deterministic repository summary for chat-driven repo insights.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Layer", "E2E")]
public sealed class GitHubInsightsTests
{
    private const string Owner = "elbruno";
    private const string Repo = "openclawnet";
    private const string ExpectedSummary = "**elbruno/openclawnet:** 12 open issues, 3 open PRs · ⭐ 42";

    [Fact]
    public async Task GitHubSummary_WithMockedRepositoryStats_ReturnsContractMarkdown()
    {
        using var github = StartGitHubMock();
        StubSummarySuccess(github);

        using var env = new GitHubEnvironment(clearToken: false);
        using var factory = new GitHubGatewayFactory(github.Url!);

        var result = await ExecuteSummaryAsync(factory);

        result.Success.Should().BeTrue(
            $"summary should succeed against WireMock GitHub at {github.Url}; error: {result.Error}");
        result.Output.Trim().Should().Be(ExpectedSummary);
    }

    [Fact]
    public async Task GitHubSummary_WhenGitHubReturns404_ReturnsCleanErrorResult()
    {
        using var github = StartGitHubMock();
        foreach (var prefix in new[] { string.Empty, "/api/v3" })
        {
            github.Given(Request.Create().UsingGet().WithPath($"{prefix}/repos/{Owner}/{Repo}"))
                .RespondWith(Response.Create().WithStatusCode(404).WithBody("{\"message\":\"Not Found\"}"));
        }

        using var env = new GitHubEnvironment(clearToken: false);
        using var factory = new GitHubGatewayFactory(github.Url!);

        var result = await ExecuteSummaryAsync(factory);

        result.Success.Should().BeFalse("404s should be converted to ToolResult.Fail, not thrown.");
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.Error.Should().ContainEquivalentOf("not found");
    }

    [Fact]
    public async Task GitHubSummary_WithNoToken_EitherUsesAnonymousClientOrReturnsHelpfulAuthError()
    {
        using var github = StartGitHubMock();
        StubSummarySuccess(github);

        using var env = new GitHubEnvironment(clearToken: true);
        using var factory = new GitHubGatewayFactory(github.Url!);

        var result = await ExecuteSummaryAsync(factory);

        if (result.Success)
        {
            result.Output.Trim().Should().Be(ExpectedSummary,
                "anonymous GitHub access should produce the same summary when the API permits it.");
            return;
        }

        result.Error.Should().NotBeNullOrWhiteSpace();
        result.Error.Should().MatchRegex("(?i)(GITHUB_TOKEN|auth|credential|anonymous|rate limit)",
            "a no-token failure should tell the user how to proceed instead of surfacing a raw exception.");
    }

    private static async Task<ToolResult> ExecuteSummaryAsync(GatewayE2EFactory factory)
    {
        var executor = factory.Services.GetRequiredService<IToolExecutor>();
        var args = JsonSerializer.Serialize(new
        {
            action = "summary",
            owner = Owner,
            repo = Repo
        });

        return await executor.ExecuteAsync("github", args);
    }

    private static WireMockServer StartGitHubMock() =>
        WireMockServer.Start();

    private static void StubSummarySuccess(WireMockServer github)
    {
        foreach (var prefix in new[] { string.Empty, "/api/v3" })
        {
            github.Given(Request.Create().UsingGet().WithPath($"{prefix}/repos/{Owner}/{Repo}"))
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                      "id": 123,
                      "name": "openclawnet",
                      "full_name": "elbruno/openclawnet",
                      "owner": { "login": "elbruno", "id": 456, "type": "User" },
                      "private": false,
                      "html_url": "https://github.com/elbruno/openclawnet",
                      "description": "OpenClawNet test fixture",
                      "fork": false,
                      "url": "https://api.github.com/repos/elbruno/openclawnet",
                      "stargazers_count": 42,
                      "open_issues_count": 15,
                      "updated_at": "2026-01-02T03:04:00Z",
                      "pushed_at": "2026-01-03T04:05:00Z",
                      "default_branch": "main",
                      "language": "C#"
                    }
                    """));

            github.Given(Request.Create().UsingGet().WithPath($"{prefix}/search/issues").WithParam("q", $"repo:{Owner}/{Repo} is:issue is:open"))
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(BuildSearchJson(12)));

            github.Given(Request.Create().UsingGet().WithPath($"{prefix}/search/issues").WithParam("q", $"repo:{Owner}/{Repo} is:pr is:open"))
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(BuildSearchJson(3)));
        }
    }

    private static string BuildSearchJson(int totalCount) => $$"""
        {
          "total_count": {{totalCount}},
          "incomplete_results": false,
          "items": []
        }
        """;

    private sealed class GitHubGatewayFactory(string githubBaseUrl) : GatewayE2EFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                // Interim seam: Petey's implementation exposes an internal GitHubTool client factory,
                // so the E2E host replaces the registered singleton with an Octokit client pointed at WireMock.
                services.AddSingleton(sp => CreateGitHubTool(sp, githubBaseUrl));
                services.AddSingleton<ITool>(sp => sp.GetRequiredService<GitHubTool>());
            });
        }

        private static GitHubTool CreateGitHubTool(IServiceProvider sp, string baseUrl)
        {
            var ctor = typeof(GitHubTool).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(IServiceScopeFactory), typeof(ILogger<GitHubTool>), typeof(Func<IGitHubClient>)],
                modifiers: null);

            ctor.Should().NotBeNull("GitHubTool should expose the interim internal client factory seam for hermetic E2E tests.");

            return (GitHubTool)ctor!.Invoke([
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<GitHubTool>>(),
                () => new GitHubClient(new ProductHeaderValue("OpenClawNet-E2E"), new Uri(baseUrl))
            ]);
        }
    }

    private sealed class GitHubEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public GitHubEnvironment(bool clearToken)
        {
            if (clearToken)
            {
                Set("GITHUB_TOKEN", null);
            }
        }

        private void Set(string name, string? value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
