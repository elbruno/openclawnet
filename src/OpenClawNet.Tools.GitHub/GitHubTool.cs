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

    public GitHubTool(IServiceScopeFactory scopeFactory, ILogger<GitHubTool> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string Name => "github";

    public string Description =>
        "Read-only GitHub access (Octokit). Actions: list_issues, list_pulls, list_commits, get_repo, get_file. " +
        $"Authenticate by setting the secret '{TokenSecretName}' (or environment variable of the same name) — anonymous calls work but are rate-limited.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "action": { "type": "string", "enum": ["list_issues", "list_pulls", "list_commits", "get_repo", "get_file"], "description": "GitHub operation to perform." },
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
        Tags = ["github", "git", "issues", "pulls", "commits"]
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

            var client = new GitHubClient(new ProductHeaderValue(ProductName));
            string? token;
            using (var scope = _scopeFactory.CreateScope())
            {
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
                token = await secrets.GetAsync(TokenSecretName, cancellationToken);
            }
            token ??= Environment.GetEnvironmentVariable(TokenSecretName);
            if (!string.IsNullOrWhiteSpace(token))
                client.Credentials = new Credentials(token);

            return action switch
            {
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
            return ToolResult.Fail(Name, $"GitHub rate limit exceeded. Set the {TokenSecretName} secret to authenticate. ({ex.Message})", sw.Elapsed);
        }
        catch (NotFoundException)
        {
            return ToolResult.Fail(Name, "Resource not found (or not accessible without authentication).", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub tool error");
            return ToolResult.Fail(Name, ex.Message, sw.Elapsed);
        }
    }

    private async Task<ToolResult> ListIssuesAsync(GitHubClient c, string owner, string repo, ToolInput input, int perPage, Stopwatch sw)
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

    private async Task<ToolResult> ListPullsAsync(GitHubClient c, string owner, string repo, ToolInput input, int perPage, Stopwatch sw)
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

    private async Task<ToolResult> ListCommitsAsync(GitHubClient c, string owner, string repo, int perPage, Stopwatch sw)
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

    private async Task<ToolResult> GetRepoAsync(GitHubClient c, string owner, string repo, Stopwatch sw)
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

    private async Task<ToolResult> GetFileAsync(GitHubClient c, string owner, string repo, string? path, Stopwatch sw)
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
