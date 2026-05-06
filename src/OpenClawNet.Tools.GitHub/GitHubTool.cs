using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.GitHub;

public sealed class GitHubTool : ITool
{
    public const string TokenSecretName = "GITHUB_TOKEN";
    private const string ProductName = "OpenClawNet";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GitHubTool> _logger;
    private readonly IGitHubClientFactory _clientFactory;

    public GitHubTool(IServiceScopeFactory scopeFactory, ILogger<GitHubTool> logger, IGitHubClientFactory clientFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clientFactory = clientFactory;
    }

    internal GitHubTool(IServiceScopeFactory scopeFactory, ILogger<GitHubTool> logger, Func<IGitHubClient> clientFactoryFunc)
        : this(scopeFactory, logger, new FuncBasedClientFactory(clientFactoryFunc))
    {
    }

    private sealed class FuncBasedClientFactory : IGitHubClientFactory
    {
        private readonly Func<IGitHubClient> _factory;
        public FuncBasedClientFactory(Func<IGitHubClient> factory) => _factory = factory;
        public IGitHubClient CreateClient() => _factory();
    }

    public string Name => "github";

    public string Description =>
        "Read-only GitHub access (Octokit). Actions: summary, list_issues, list_pulls, list_commits, get_repo, get_file. " +
        $"Authenticate by setting the secret '{TokenSecretName}' (or environment variable of the same name) — anonymous calls work but are rate-limited.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "action": { "type": "string", "enum": ["summary", "list_issues", "list_pulls", "list_commits", "get_repo", "get_file"], "description": "GitHub operation to perform. Use 'summary' for repo issue/PR/star counts." },
                "owner": { "type": "string", "description": "Repository owner (user or org)." },
                "repo": { "type": "string", "description": "Repository name." },
                "path": { "type": "string", "description": "Path inside the repo (only for action='get_file')." },
                "perPage": { "type": "integer", "description": "Page size (default 10, max 50)." },
                "state": { "type": "string", "enum": ["open", "closed", "all"], "description": "For list_issues/list_pulls (default 'open')." }
            },
            "required": ["action", "owner", "repo"]
        }
        """),
        RequiresApproval = false,
        Category = "integration",
        Tags = ["github", "git", "issues", "pulls", "commits", "summary"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var action = (input.GetStringArgument("action") ?? "").ToLowerInvariant();
            var owner = input.GetStringArgument("owner");
            var repo = input.GetStringArgument("repo");
            if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
                return ToolResult.Fail(Name, "'action', 'owner', and 'repo' are required", sw.Elapsed);

            var perPage = Math.Clamp(input.GetArgument<int?>("perPage") ?? 10, 1, 50);

            var client = _clientFactory.CreateClient();
            string? token;
            using (var scope = _scopeFactory.CreateScope())
            {
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
                token = await secrets.GetAsync(TokenSecretName, cancellationToken);
            }
            token ??= Environment.GetEnvironmentVariable(TokenSecretName);
            if (!string.IsNullOrWhiteSpace(token) && client is GitHubClient gitHubClient)
                gitHubClient.Credentials = new Credentials(token);

            return action switch
            {
                "summary" => await GetSummaryAsync(client, owner!, repo!, sw),
                "list_issues" => await ListIssuesAsync(client, owner!, repo!, input, perPage, sw),
                "list_pulls" => await ListPullsAsync(client, owner!, repo!, input, perPage, sw),
                "list_commits" => await ListCommitsAsync(client, owner!, repo!, perPage, sw),
                "get_repo" => await GetRepoAsync(client, owner!, repo!, sw),
                "get_file" => await GetFileAsync(client, owner!, repo!, input.GetStringArgument("path"), sw),
                _ => ToolResult.Fail(Name, $"Unknown action '{action}'", sw.Elapsed)
            };
        }
        catch (RateLimitExceededException ex)
        {
            return ToolResult.Fail(Name, $"GitHub rate limit exceeded. Set the {TokenSecretName} secret to authenticate. ({ex.Message}){FormatRateLimitInfo(ex.HttpResponse?.ApiInfo)}", sw.Elapsed);
        }
        catch (NotFoundException ex)
        {
            return ToolResult.Fail(Name, $"Resource not found (or not accessible without authentication).{FormatRateLimitInfo(ex.HttpResponse?.ApiInfo)}", sw.Elapsed);
        }
        catch (ApiException ex)
        {
            return ToolResult.Fail(Name, $"GitHub API error: {ex.Message}{FormatRateLimitInfo(ex.HttpResponse?.ApiInfo)}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub tool error");
            return ToolResult.Fail(Name, ex.Message, sw.Elapsed);
        }
    }

    private async Task<ToolResult> GetSummaryAsync(IGitHubClient c, string owner, string repo, Stopwatch sw)
    {
        var repository = await c.Repository.Get(owner, repo);

        // GitHub's Repository.OpenIssuesCount includes pull requests, so use two Search API count queries
        // for accurate issue vs PR totals while avoiding paged list downloads.
        var issueCountTask = c.Search.SearchIssues(new SearchIssuesRequest($"repo:{owner}/{repo} is:issue is:open") { PerPage = 1 });
        var pullCountTask = c.Search.SearchIssues(new SearchIssuesRequest($"repo:{owner}/{repo} is:pr is:open") { PerPage = 1 });
        await Task.WhenAll(issueCountTask, pullCountTask);

        sw.Stop();
        return ToolResult.Ok(Name, FormatSummaryMarkdown(owner, repo, repository, issueCountTask.Result.TotalCount, pullCountTask.Result.TotalCount), sw.Elapsed);
    }

    internal static string FormatSummaryMarkdown(string owner, string repo, Repository repository, int openIssues, int openPulls)
    {
        var sb = new StringBuilder()
            .AppendLine($"**{owner}/{repo}:** {openIssues} open issues, {openPulls} open PRs · ⭐ {repository.StargazersCount}");

        if (!string.IsNullOrWhiteSpace(repository.Description))
            sb.AppendLine(repository.Description);

        sb.AppendLine($"Updated: {repository.UpdatedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
        if (repository.PushedAt is { } pushedAt)
            sb.AppendLine($"Last push: {pushedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC");

        return sb.ToString();
    }

    private static string FormatRateLimitInfo(ApiInfo? apiInfo)
    {
        var limit = apiInfo?.RateLimit;
        return limit is null
            ? string.Empty
            : $" Rate limit: {limit.Remaining}/{limit.Limit} remaining until {limit.Reset.UtcDateTime:yyyy-MM-dd HH:mm} UTC.";
    }

    private async Task<ToolResult> ListIssuesAsync(IGitHubClient c, string owner, string repo, ToolInput input, int perPage, Stopwatch sw)
    {
        var stateRaw = (input.GetStringArgument("state") ?? "open").ToLowerInvariant();
        var state = stateRaw switch { "closed" => ItemStateFilter.Closed, "all" => ItemStateFilter.All, _ => ItemStateFilter.Open };
        var issues = await c.Issue.GetAllForRepository(owner, repo,
            new RepositoryIssueRequest { State = state },
            new ApiOptions { PageSize = perPage, PageCount = 1 });
        var sb = new StringBuilder().AppendLine($"# Issues in {owner}/{repo} (state={stateRaw})");
        foreach (var i in issues.Where(i => i.PullRequest is null))
            sb.AppendLine($"- #{i.Number} [{i.State.StringValue}] {i.Title} — by @{i.User.Login}");
        sw.Stop();
        return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
    }

    private async Task<ToolResult> ListPullsAsync(IGitHubClient c, string owner, string repo, ToolInput input, int perPage, Stopwatch sw)
    {
        var stateRaw = (input.GetStringArgument("state") ?? "open").ToLowerInvariant();
        var state = stateRaw switch { "closed" => ItemStateFilter.Closed, "all" => ItemStateFilter.All, _ => ItemStateFilter.Open };
        var prs = await c.PullRequest.GetAllForRepository(owner, repo,
            new PullRequestRequest { State = state },
            new ApiOptions { PageSize = perPage, PageCount = 1 });
        var sb = new StringBuilder().AppendLine($"# Pull Requests in {owner}/{repo} (state={stateRaw})");
        foreach (var p in prs)
            sb.AppendLine($"- #{p.Number} [{p.State.StringValue}] {p.Title} — by @{p.User.Login}");
        sw.Stop();
        return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
    }

    private async Task<ToolResult> ListCommitsAsync(IGitHubClient c, string owner, string repo, int perPage, Stopwatch sw)
    {
        var commits = await c.Repository.Commit.GetAll(owner, repo,
            new ApiOptions { PageSize = perPage, PageCount = 1 });
        var sb = new StringBuilder().AppendLine($"# Recent commits in {owner}/{repo}");
        foreach (var c2 in commits)
        {
            var msg = c2.Commit.Message.Split('\n')[0];
            sb.AppendLine($"- {c2.Sha[..8]}  {msg}  — {c2.Commit.Author.Name}");
        }
        sw.Stop();
        return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
    }

    private async Task<ToolResult> GetRepoAsync(IGitHubClient c, string owner, string repo, Stopwatch sw)
    {
        var r = await c.Repository.Get(owner, repo);
        sw.Stop();
        var sb = new StringBuilder()
            .AppendLine($"# {r.FullName}")
            .AppendLine(r.Description ?? "(no description)")
            .AppendLine($"Stars: {r.StargazersCount}  Forks: {r.ForksCount}  Issues: {r.OpenIssuesCount}")
            .AppendLine($"Default branch: {r.DefaultBranch}  Language: {r.Language ?? "n/a"}")
            .AppendLine($"URL: {r.HtmlUrl}");
        return ToolResult.Ok(Name, sb.ToString(), sw.Elapsed);
    }

    private async Task<ToolResult> GetFileAsync(IGitHubClient c, string owner, string repo, string? path, Stopwatch sw)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ToolResult.Fail(Name, "'path' is required for action='get_file'", sw.Elapsed);
        var contents = await c.Repository.Content.GetAllContents(owner, repo, path);
        var first = contents.FirstOrDefault();
        sw.Stop();
        if (first is null) return ToolResult.Fail(Name, "File not found", sw.Elapsed);
        return ToolResult.Ok(Name, $"# {first.Path} ({first.Size} bytes)\n\n{first.Content}", sw.Elapsed);
    }
}
