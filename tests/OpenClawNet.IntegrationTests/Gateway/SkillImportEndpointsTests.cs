// K-4 — Irving
// Integration tests for the /api/skills/import/* endpoints.
//
// HttpClient injection: tests override the named "github-raw" client with a
// stub HttpMessageHandler so no real GitHub traffic is generated.
//
// References: squad spawn brief (K-4 Wave 6), drummond-k1b-verdict.md AC-K2-4.

#if K1B_LANDED
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.IntegrationTests.Gateway;

[Trait("Area", "Skills")]
[Trait("Wave", "K-4")]
[Trait("Layer", "Gateway")]
public sealed class SkillImportEndpointsTests : IDisposable
{
    private const string ValidSha = "0123456789abcdef0123456789abcdef01234567";
    private const string AllowedRepo = "github/awesome-copilot";

    private readonly string _root;
    private readonly Factory _factory;

    public SkillImportEndpointsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"oc-k4-ep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _root);
        _factory = new Factory();
    }

    public void Dispose()
    {
        _factory.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, null);
    }

    private static string ValidSkill(string name, string body = "Body content.") => $"""
        ---
        name: {name}
        description: Test skill {name}
        ---
        {body}
        """;

    // ====================================================================
    // Test factory: replaces "github-raw" handler with a stub.
    // ====================================================================
    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Responder(request));
    }

    private sealed class Factory : GatewayWebAppFactory
    {
        public StubHandler Handler { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.Configure<HttpClientFactoryOptions>("github-raw", o =>
                {
                    o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Handler);
                });
            });
        }
    }

    // ====================================================================
    // Tests
    // ====================================================================

    [Fact]
    public async Task PostPreview_RepoNotAllowed_Returns403()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills/import/preview", new
        {
            repo = "evil/repo",
            sha = ValidSha,
            path = "skills/x.md"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("RepoNotAllowed");
    }

    [Fact]
    public async Task PostPreview_NonMdPath_Returns400_UnsupportedExtension()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills/import/preview", new
        {
            repo = AllowedRepo,
            sha = ValidSha,
            path = "skills/foo/run.ps1"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("UnsupportedExtension");
    }

    [Fact]
    public async Task PostPreview_BranchTipAsSha_Returns400_InvalidSha()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills/import/preview", new
        {
            repo = AllowedRepo,
            sha = "main",
            path = "skills/foo.md"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("InvalidSha");
    }

    [Fact]
    public async Task PostPreview_GitHubReturns404_Returns404_NotFound()
    {
        _factory.Handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills/import/preview", new
        {
            repo = AllowedRepo,
            sha = ValidSha,
            path = "skills/missing.md"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostPreview_HappyPath_Returns200_WithMetadata()
    {
        _factory.Handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidSkill("widget", "Hello."), Encoding.UTF8, "text/plain")
        };
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills/import/preview", new
        {
            repo = AllowedRepo,
            sha = ValidSha,
            path = "skills/widget.md"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("widget");
        body.Should().Contain("previewToken");
        // Q5: body content (sentinel "Hello.") may appear in passing detail fields,
        // but the response shape must not echo the markdown body field.
        body.Should().NotContain("\"body\"");
    }

    [Fact]
    public async Task PostConfirm_WithFreshToken_Returns201_AndWritesFiles()
    {
        _factory.Handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidSkill("widget"), Encoding.UTF8, "text/plain")
        };
        var client = _factory.CreateClient();

        var preview = await client.PostAsJsonAsync("/api/skills/import/preview", new
        {
            repo = AllowedRepo,
            sha = ValidSha,
            path = "skills/widget.md"
        });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await preview.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var token = previewJson.GetProperty("previewToken").GetString();

        var confirm = await client.PostAsJsonAsync("/api/skills/import/confirm", new { previewToken = token });
        confirm.StatusCode.Should().Be(HttpStatusCode.Created);

        File.Exists(Path.Combine(_root, "skills", "installed", "widget", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "skills", "installed", "widget", ".import.json")).Should().BeTrue();
    }

    [Fact]
    public async Task PostConfirm_UnknownToken_Returns404()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills/import/confirm", new { previewToken = "bogus" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostPreview_SkillAlreadyExists_Returns409()
    {
        var existing = Path.Combine(_root, "skills", "installed", "widget");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "SKILL.md"), ValidSkill("widget"));

        _factory.Handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidSkill("widget"), Encoding.UTF8, "text/plain")
        };
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills/import/preview", new
        {
            repo = AllowedRepo,
            sha = ValidSha,
            path = "skills/widget.md"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
#endif
