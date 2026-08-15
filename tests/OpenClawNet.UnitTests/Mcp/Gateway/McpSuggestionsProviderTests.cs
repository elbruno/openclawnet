using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using OpenClawNet.Gateway.Services.Mcp;

namespace OpenClawNet.UnitTests.Mcp.Gateway;

public class McpSuggestionsProviderTests
{
    // ── Schema round-trip ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_RoundTripsAllFields()
    {
        // Uses the official GitHub MCP Docker entry as a representative fixture.
        // Image is pinned to a specific version tag (v1.9.0) as required for
        // credential-bearing images; :latest is not used here or in production.
        const string yaml = """
        version: 1
        suggestions:
          - id: github-mcp
            name: GitHub MCP Server (official)
            description: "GitHub API access via Docker"
            transport: stdio
            command: docker
            args: ["run", "-i", "--rm", "-e", "GITHUB_PERSONAL_ACCESS_TOKEN", "ghcr.io/github/github-mcp-server:v1.9.0"]
            category: development
            requires_env:
              - GITHUB_PERSONAL_ACCESS_TOKEN
            homepage: https://github.com/github/github-mcp-server
            verified_on: "2026-08-15"
            source_registry: ghcr.io
        """;

        var result = McpSuggestionsProvider.Parse(yaml);

        result.Should().HaveCount(1);
        var s = result[0];
        s.Id.Should().Be("github-mcp");
        s.Name.Should().Be("GitHub MCP Server (official)");
        s.Transport.Should().Be("stdio");
        s.Command.Should().Be("docker");
        s.Args.Should().Equal("run", "-i", "--rm", "-e", "GITHUB_PERSONAL_ACCESS_TOKEN", "ghcr.io/github/github-mcp-server:v1.9.0");
        s.Category.Should().Be("development");
        s.RequiresEnv.Should().Equal("GITHUB_PERSONAL_ACCESS_TOKEN");
        s.Homepage.Should().Be("https://github.com/github/github-mcp-server");
        s.VerifiedOn.Should().Be("2026-08-15");
        s.SourceRegistry.Should().Be("ghcr.io");
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

    // ── Production catalog quality gates (B4 invariant-based offline tests) ──

    [Fact]
    public void Parse_RealRepoFile_HasAtLeastOneEntry()
    {
        // Minimum viable catalog — does not over-fit to an exact count.
        var result = LoadRealCatalog();
        result.Should().NotBeEmpty("catalog must contain at least one verified entry");
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

            if (s.Transport == "stdio")
                s.Command.Should().NotBeNullOrWhiteSpace($"stdio entry {s.Id} must specify a command");
            else
                s.Url.Should().NotBeNullOrWhiteSpace($"http entry {s.Id} must specify a url");
        }
    }

    [Fact]
    public void Parse_RealRepoFile_AllEntriesHaveProvenanceFields()
    {
        // B3 offline enforcement: every catalog entry must ship verified_on and source_registry.
        var result = LoadRealCatalog();

        foreach (var s in result)
        {
            s.VerifiedOn.Should().NotBeNullOrWhiteSpace(
                $"entry {s.Id} must have a verified_on date (YYYY-MM-DD) for supply-chain auditability");
            s.SourceRegistry.Should().NotBeNullOrWhiteSpace(
                $"entry {s.Id} must have a source_registry (e.g. npm, pypi, ghcr.io)");
        }
    }

    [Fact]
    public void Parse_RealRepoFile_CommandsAreFromApprovedSet()
    {
        // B4 invariant: only approved launchers are used; arbitrary shell strings are blocked.
        var approvedCommands = new[] { "npx", "docker", "uvx" };
        var result = LoadRealCatalog();

        foreach (var s in result.Where(e => e.Transport == "stdio"))
            s.Command.Should().BeOneOf(approvedCommands,
                $"entry {s.Id} uses unapproved command '{s.Command}'; allowed: {string.Join(", ", approvedCommands)}");
    }

    [Fact]
    public void Parse_RealRepoFile_ScopedNpmPackagesAreWellFormed()
    {
        // B4 invariant: npx args that look like scoped packages must start with @<scope>/<name>.
        var result = LoadRealCatalog();
        var npmEntries = result.Where(s => s.Command == "npx");

        foreach (var s in npmEntries)
        {
            // The package name is the last non-flag argument (after -y etc.)
            var pkg = s.Args.FirstOrDefault(a => !a.StartsWith('-'));
            if (pkg is not null && pkg.StartsWith('@'))
                pkg.Should().MatchRegex(@"^@[a-z0-9\-]+/[a-z0-9\-@./]+$",
                    $"entry {s.Id} npm package '{pkg}' must be a well-formed scoped name");
        }
    }

    [Fact]
    public void Parse_RealRepoFile_DockerImagesAreFullyQualified()
    {
        // B4 invariant: Docker images must include a registry host (no bare Docker Hub short names).
        var result = LoadRealCatalog();
        var dockerEntries = result.Where(s => s.Command == "docker");

        foreach (var s in dockerEntries)
        {
            // The image is the last positional arg after all flags.
            var image = s.Args.LastOrDefault(a => !a.StartsWith('-') && a != "run" && a != "-i" && a != "--rm");
            if (image is not null)
                image.Should().MatchRegex(@"^[a-z0-9\-\.]+\.[a-z]+/",
                    $"entry {s.Id} Docker image '{image}' must be fully qualified with a registry host (e.g. ghcr.io/...)");
        }
    }

    [Fact]
    public void Parse_RealRepoFile_NoLatestTagOnCredentialBearingImages()
    {
        // B4 invariant: Docker images that receive credentials must be pinned to a specific version,
        // not :latest, to prevent silent upstream updates from running with elevated access.
        var result = LoadRealCatalog();
        var credentialBearingEntries = result.Where(s => s.RequiresEnv.Count > 0 && s.Command == "docker");

        foreach (var s in credentialBearingEntries)
        {
            var image = s.Args.LastOrDefault(a => a.Contains('/'));
            if (image is not null)
                image.Should().NotEndWith(":latest",
                    $"entry {s.Id} passes credentials but uses :latest — pin to a specific version tag or digest");
        }
    }

    [Fact]
    public void Parse_RealRepoFile_NoDeprecatedOrNonexistentPackages()
    {
        var result = LoadRealCatalog();
        var allTokens = result
            .SelectMany(s => s.Args)
            .Concat(result.Select(s => s.Command ?? string.Empty))
            .ToList();

        allTokens.Should().NotContain("@modelcontextprotocol/server-github",
            "@modelcontextprotocol/server-github was archived April 2025; use ghcr.io/github/github-mcp-server:v1.9.0");

        allTokens.Should().NotContain("@mkusaka/mcp-shell",
            "@mkusaka/mcp-shell does not exist on npm; the package was never published under this name");

        allTokens.Should().NotContain("@modelcontextprotocol/server-fetch",
            "@modelcontextprotocol/server-fetch does not exist on npm (404); use uvx mcp-server-fetch from PyPI");

        allTokens.Should().NotContain("@executeautomation/playwright-mcp-server",
            "@executeautomation/playwright-mcp-server is a third-party fork; use the official @playwright/mcp (Microsoft)");
    }

    [Fact]
    public void Parse_RealRepoFile_GitHubMcpUsesDockerAndPinnedVersion()
    {
        var result = LoadRealCatalog();
        var ghEntry = result.Should().ContainSingle(s => s.Id == "github-mcp").Subject;

        ghEntry.Command.Should().Be("docker",
            "official GitHub MCP server ships as a Docker image, not an npm package");
        ghEntry.Args.Should().Contain(a => a.StartsWith("ghcr.io/github/github-mcp-server:") && !a.EndsWith(":latest"),
            "GitHub MCP image must be pinned to a specific version (not :latest) because it receives credentials");
        ghEntry.RequiresEnv.Should().Contain("GITHUB_PERSONAL_ACCESS_TOKEN");
    }

    [Fact]
    public void Parse_RealRepoFile_FetchMcpUsesUvxNotNpx()
    {
        // B1: @modelcontextprotocol/server-fetch does not exist on npm; authoritative
        // distribution is PyPI (mcp-server-fetch). The correct launcher is uvx.
        var result = LoadRealCatalog();
        var fetchEntry = result.Should().ContainSingle(s => s.Id == "fetch-mcp").Subject;

        fetchEntry.Command.Should().Be("uvx",
            "@modelcontextprotocol/server-fetch does not exist on npm; use uvx mcp-server-fetch (PyPI)");
        fetchEntry.Args.Should().Contain("mcp-server-fetch");
    }

    [Fact]
    public void Parse_RealRepoFile_PlaywrightMcpUsesOfficialMicrosoftPackage()
    {
        // B5: @playwright/mcp is the officially maintained Microsoft package (Apache-2.0, v0.0.79).
        var result = LoadRealCatalog();
        var pwEntry = result.Should().ContainSingle(s => s.Id == "playwright-mcp").Subject;

        pwEntry.Args.Should().Contain("@playwright/mcp",
            "@playwright/mcp is the official Microsoft-maintained Playwright MCP package");
        pwEntry.Args.Should().NotContain("@executeautomation/playwright-mcp-server",
            "@executeautomation/playwright-mcp-server is a third-party fork, not the official package");
    }

    [Fact]
    public void Parse_RealRepoFile_DesktopCommanderHasSecurityDisclosure()
    {
        // B2: Desktop Commander grants terminal execution + filesystem write — the description
        // must contain a visible security warning so users are not misled.
        var result = LoadRealCatalog();
        var dcEntry = result.Should().ContainSingle(s => s.Id == "desktop-commander").Subject;

        dcEntry.Description.Should().ContainAny(
            "terminal", "filesystem", "SECURITY", "shell", "write",
            "entry desktop-commander grants terminal execution and filesystem write access; " +
            "the description must contain an explicit security disclosure");
    }

    // ── Opt-in live registry tests (run with: dotnet test --filter Category=Live) ──

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Live_NpmPackages_ResolvableOnRegistry()
    {
        Skip.IfNot(await IsNetworkAvailableAsync(), "network not available");

        var result = LoadRealCatalog();
        var npmEntries = result.Where(s => s.Command == "npx").ToList();
        npmEntries.Should().NotBeEmpty("expected at least one npx entry in catalog");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenClawNetTests", "1.0"));
        http.Timeout = TimeSpan.FromSeconds(15);

        foreach (var s in npmEntries)
        {
            var pkg = s.Args.FirstOrDefault(a => !a.StartsWith('-'));
            if (pkg is null) continue;

            var encoded = pkg.Replace("/", "%2F");
            var response = await http.GetAsync($"https://registry.npmjs.org/{encoded}/latest");
            response.IsSuccessStatusCode.Should().BeTrue(
                $"entry {s.Id} npm package '{pkg}' must resolve on registry.npmjs.org (got {response.StatusCode})");
        }
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Live_PyPiPackages_ResolvableOnRegistry()
    {
        Skip.IfNot(await IsNetworkAvailableAsync(), "network not available");

        var result = LoadRealCatalog();
        var uvxEntries = result.Where(s => s.Command == "uvx").ToList();
        uvxEntries.Should().NotBeEmpty("expected at least one uvx (PyPI) entry in catalog");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenClawNetTests", "1.0"));
        http.Timeout = TimeSpan.FromSeconds(15);

        foreach (var s in uvxEntries)
        {
            var pkg = s.Args.FirstOrDefault();
            if (pkg is null) continue;

            var response = await http.GetAsync($"https://pypi.org/pypi/{pkg}/json");
            response.IsSuccessStatusCode.Should().BeTrue(
                $"entry {s.Id} PyPI package '{pkg}' must resolve on pypi.org (got {response.StatusCode})");
        }
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Live_GhcrImages_TagsResolvableOnRegistry()
    {
        Skip.IfNot(await IsNetworkAvailableAsync(), "network not available");

        var result = LoadRealCatalog();
        var dockerEntries = result.Where(s => s.Command == "docker").ToList();
        dockerEntries.Should().NotBeEmpty("expected at least one docker entry in catalog");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenClawNetTests", "1.0"));
        http.Timeout = TimeSpan.FromSeconds(20);

        foreach (var s in dockerEntries)
        {
            var image = s.Args.LastOrDefault(a => a.Contains('/'));
            if (image is null) continue;

            // Parse registry/repo:tag from image ref (supports ghcr.io/owner/name:tag)
            var atColon = image.LastIndexOf(':');
            var tag = atColon >= 0 ? image[(atColon + 1)..] : "latest";
            var repoRef = atColon >= 0 ? image[..atColon] : image;
            var slashIdx = repoRef.IndexOf('/');
            var registry = slashIdx >= 0 ? repoRef[..slashIdx] : "registry-1.docker.io";
            var repoPath = slashIdx >= 0 ? repoRef[(slashIdx + 1)..] : repoRef;

            if (!registry.Contains('.')) continue; // skip bare short names

            // ghcr.io uses the OCI token endpoint
            var tokenResp = await http.GetAsync(
                $"https://{registry}/token?scope=repository:{repoPath}:pull");
            tokenResp.IsSuccessStatusCode.Should().BeTrue(
                $"entry {s.Id}: token request for {registry}/{repoPath} failed ({tokenResp.StatusCode})");

            var tokenJson = await tokenResp.Content.ReadAsStringAsync();
            // Verify the tag manifest is accessible (just a HEAD via manifests endpoint)
            var manifestUrl = $"https://{registry}/v2/{repoPath}/manifests/{tag}";
            using var req = new HttpRequestMessage(HttpMethod.Head, manifestUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.index.v1+json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.list.v2+json"));

            // Extract token from JSON response
            var tokenMatch = System.Text.RegularExpressions.Regex.Match(tokenJson, @"""token""\s*:\s*""([^""]+)""");
            if (tokenMatch.Success)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenMatch.Groups[1].Value);

            var manifestResp = await http.SendAsync(req);
            manifestResp.IsSuccessStatusCode.Should().BeTrue(
                $"entry {s.Id} image '{image}' tag '{tag}' manifest must be accessible on {registry} (got {manifestResp.StatusCode})");
        }
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

    private static async Task<bool> IsNetworkAvailableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync("https://registry.npmjs.org/@modelcontextprotocol/server-memory/latest");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
