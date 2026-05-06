using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Octokit;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GitHub;
using Xunit;

namespace OpenClawNet.UnitTests.Tools;

public class GitHubToolTests
{
    private static ToolInput Args(string json) => new()
    {
        ToolName = "github",
        RawArguments = json
    };

    [Fact]
    public async Task Summary_Returns_Expected_Markdown_Shape()
    {
        var repository = new Repository();
        SetOctokitProperty(repository, nameof(Repository.FullName), "elbruno/openclawnet");
        SetOctokitProperty(repository, nameof(Repository.Description), "OpenClawNet test repository");
        SetOctokitProperty(repository, nameof(Repository.StargazersCount), 42);
        SetOctokitProperty(repository, nameof(Repository.UpdatedAt), new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.Zero));
        SetOctokitProperty(repository, nameof(Repository.PushedAt), new DateTimeOffset(2026, 1, 3, 4, 5, 0, TimeSpan.Zero));

        var repositories = new Mock<IRepositoriesClient>(MockBehavior.Strict);
        repositories.Setup(r => r.Get("elbruno", "openclawnet")).ReturnsAsync(repository);

        var search = new Mock<ISearchClient>(MockBehavior.Strict);
        search.Setup(s => s.SearchIssues(It.Is<SearchIssuesRequest>(r => r.Term.Contains("is:issue", StringComparison.Ordinal))))
            .ReturnsAsync(new SearchIssuesResult(12, false, []));
        search.Setup(s => s.SearchIssues(It.Is<SearchIssuesRequest>(r => r.Term.Contains("is:pr", StringComparison.Ordinal))))
            .ReturnsAsync(new SearchIssuesResult(3, false, []));

        var client = new Mock<IGitHubClient>(MockBehavior.Strict);
        client.SetupGet(c => c.Repository).Returns(repositories.Object);
        client.SetupGet(c => c.Search).Returns(search.Object);

        var result = await CreateTool(client.Object).ExecuteAsync(Args("""
        { "action": "summary", "owner": "elbruno", "repo": "openclawnet" }
        """));

        Assert.True(result.Success, result.Error);
        Assert.StartsWith("**elbruno/openclawnet:** 12 open issues, 3 open PRs · ⭐ 42", result.Output);
        Assert.Contains("OpenClawNet test repository", result.Output);
        Assert.Contains("Updated: 2026-01-02 03:04 UTC", result.Output);
        Assert.Contains("Last push: 2026-01-03 04:05 UTC", result.Output);
    }

    [Theory]
    [InlineData("{ \"action\": \"summary\", \"repo\": \"openclawnet\" }")]
    [InlineData("{ \"action\": \"summary\", \"owner\": \"\", \"repo\": \"openclawnet\" }")]
    [InlineData("{ \"action\": \"summary\", \"owner\": \"elbruno\", \"repo\": \" \" }")]
    public async Task Summary_Missing_Or_Invalid_Owner_Repo_Returns_Clean_Error(string json)
    {
        var client = new Mock<IGitHubClient>(MockBehavior.Strict);

        var result = await CreateTool(client.Object).ExecuteAsync(Args(json));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("owner", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void Metadata_Advertises_Summary_Action()
    {
        var root = CreateTool(Mock.Of<IGitHubClient>()).Metadata.ParameterSchema.RootElement;
        var enumValues = root.GetProperty("properties").GetProperty("action").GetProperty("enum")
            .EnumerateArray()
            .Select(v => v.GetString())
            .ToArray();

        Assert.Contains("summary", enumValues);
    }

    [Fact]
    public void Factory_Honors_Custom_BaseUrl_From_Configuration()
    {
        var customBaseUrl = "http://wiremock.local:8080/api";
        var configData = new Dictionary<string, string?>
        {
            ["GitHub:ApiBaseUrl"] = customBaseUrl
        };
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var factory = new GitHubClientFactory(config);
        var client = factory.CreateClient();

        // Verify the client uses the custom base address
        Assert.NotNull(client.Connection);
        Assert.Equal(new Uri(customBaseUrl), client.Connection.BaseAddress);
    }

    private static void SetOctokitProperty<T>(object target, string name, T value)
    {
        target.GetType().GetProperty(name)!.SetValue(target, value);
    }

    private static GitHubTool CreateTool(IGitHubClient client)
    {
        var secrets = new Mock<ISecretsStore>(MockBehavior.Strict);
        secrets.Setup(s => s.GetAsync(GitHubTool.TokenSecretName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var services = new ServiceCollection()
            .AddSingleton(secrets.Object)
            .BuildServiceProvider();

        return new GitHubTool(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GitHubTool>.Instance,
            () => client);
    }
}
