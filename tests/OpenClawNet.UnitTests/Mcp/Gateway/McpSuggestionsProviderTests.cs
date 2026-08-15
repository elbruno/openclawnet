using FluentAssertions;
using OpenClawNet.Gateway.Services.Mcp;

namespace OpenClawNet.UnitTests.Mcp.Gateway;

public class McpSuggestionsProviderTests
{
    // ── Schema round-trip ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_RoundTripsAllFields()
    {
        // Uses the official GitHub MCP Docker entry as a representative fixture —
        // NOT the deprecated @modelcontextprotocol/server-github npm package.
        const string yaml = """
        version: 1
        suggestions:
          - id: github-mcp
            name: GitHub MCP Server (official)
            description: "GitHub API access via Docker"
            transport: stdio
            command: docker
            args: ["run", "-i", "--rm", "-e", "GITHUB_PERSONAL_ACCESS_TOKEN", "ghcr.io/github/github-mcp-server:latest"]
            category: development
            requires_env:
              - GITHUB_PERSONAL_ACCESS_TOKEN
            homepage: https://github.com/github/github-mcp-server
        """;

        var result = McpSuggestionsProvider.Parse(yaml);

        result.Should().HaveCount(1);
        var s = result[0];
        s.Id.Should().Be("github-mcp");
        s.Name.Should().Be("GitHub MCP Server (official)");
        s.Transport.Should().Be("stdio");
        s.Command.Should().Be("docker");
        s.Args.Should().Equal("run", "-i", "--rm", "-e", "GITHUB_PERSONAL_ACCESS_TOKEN", "ghcr.io/github/github-mcp-server:latest");
        s.Category.Should().Be("development");
        s.RequiresEnv.Should().Equal("GITHUB_PERSONAL_ACCESS_TOKEN");
        s.Homepage.Should().Be("https://github.com/github/github-mcp-server");
    }

    [Fact]
    public void Parse_EmptyYaml_ReturnsEmpty()
    {
        var result = McpSuggestionsProvider.Parse("version: 1\n");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedYaml_Throws()
    {
        var act = () => McpSuggestionsProvider.Parse("not: : : valid");
        act.Should().Throw<Exception>();
    }

    // ── Production catalog quality gates ─────────────────────────────────────

    [Fact]
    public void Parse_RealRepoFile_HasFiveCuratedEntries()
    {
        var result = LoadRealCatalog();

        result.Should().HaveCount(5, "catalog has 5 verified entries after removing the nonexistent " +
            "shell-alt-mkusaka (@mkusaka/mcp-shell does not exist on npm) and replacing " +
            "the deprecated @modelcontextprotocol/server-github with the official Docker image");

        result.Select(s => s.Id).Should().BeEquivalentTo(new[]
        {
            "github-mcp", "desktop-commander", "memory-mcp", "playwright-mcp", "fetch-mcp",
        }, "IDs must match the verified production catalog");
    }

    [Fact]
    public void Parse_RealRepoFile_AllEntriesHaveValidSchema()
    {
        var result = LoadRealCatalog();

        foreach (var s in result)
        {
            s.Id.Should().NotBeNullOrWhiteSpace($"entry must have an id (name={s.Name})");
            s.Name.Should().NotBeNullOrWhiteSpace($"entry {s.Id} must have a name");
            s.Transport.Should().BeOneOf("stdio", "http", $"entry {s.Id} must use a supported transport");
            s.Category.Should().NotBeNullOrWhiteSpace($"entry {s.Id} must have a category");
            s.Homepage.Should().NotBeNullOrWhiteSpace($"entry {s.Id} must document a homepage/source URL");

            // Every stdio entry must have an executable command; http entries must have a url.
            if (s.Transport == "stdio")
                s.Command.Should().NotBeNullOrWhiteSpace($"stdio entry {s.Id} must specify a command");
            else
                s.Url.Should().NotBeNullOrWhiteSpace($"http entry {s.Id} must specify a url");
        }
    }

    [Fact]
    public void Parse_RealRepoFile_NoDeprecatedOrNonexistentPackages()
    {
        var result = LoadRealCatalog();
        var allArgs = result.SelectMany(s => s.Args).Concat(result.Select(s => s.Command ?? "")).ToList();

        // The deprecated npm package was archived April 2025; must not appear.
        allArgs.Should().NotContain("@modelcontextprotocol/server-github",
            "@modelcontextprotocol/server-github was archived April 2025; use ghcr.io/github/github-mcp-server");

        // The nonexistent package must not appear (wrong name — does not exist on npm).
        allArgs.Should().NotContain("@mkusaka/mcp-shell",
            "@mkusaka/mcp-shell does not exist on npm; package was never published under this name");
    }

    [Fact]
    public void Parse_RealRepoFile_GitHubMcpUsesDockerNotNpx()
    {
        var result = LoadRealCatalog();
        var ghEntry = result.Should().ContainSingle(s => s.Id == "github-mcp").Subject;

        ghEntry.Command.Should().Be("docker",
            "official GitHub MCP server ships as a Docker image (ghcr.io/github/github-mcp-server), " +
            "not an npm package — the npx entry was deprecated April 2025");
        ghEntry.Args.Should().Contain("ghcr.io/github/github-mcp-server:latest");
        ghEntry.RequiresEnv.Should().Contain("GITHUB_PERSONAL_ACCESS_TOKEN");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<McpSuggestion> LoadRealCatalog()
    {
        var dir = AppContext.BaseDirectory;
        string? repoRoot = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "OpenClawNet.slnx")))
            {
                repoRoot = d.FullName;
                break;
            }
        }
        repoRoot.Should().NotBeNull("test must run from inside the repo");

        var yamlPath = Path.Combine(repoRoot!, "docs", "mcp-suggestions.yaml");
        File.Exists(yamlPath).Should().BeTrue("docs/mcp-suggestions.yaml must ship with the repo");

        return McpSuggestionsProvider.Parse(File.ReadAllText(yamlPath));
    }
}
