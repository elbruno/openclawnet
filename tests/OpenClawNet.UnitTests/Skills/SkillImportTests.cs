// K-4 — Irving
// Unit tests for SkillImportService (preview + confirm two-step flow).
//
// HttpClient is mocked via a custom HttpMessageHandler. The registry is a
// real OpenClawNetSkillsRegistry pointed at a per-test temp StorageRoot.
//
// References:
//   - Squad spawn brief (K-4 Wave 6)
//   - .squad/decisions/inbox/drummond-k1b-verdict.md AC-K2-4 (256 KB cap)
//   - .squad/decisions.md L-4 (md only), Q1 (opt-in default), Q5 (no body in logs/DTOs)

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Skills;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Skills;

[Trait("Area", "Skills")]
[Trait("Wave", "K-4")]
[Collection("StorageEnvVar")]
public sealed class SkillImportTests : IDisposable
{
    private readonly string _root;
    private readonly string? _originalEnv;

    public SkillImportTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName);
        _root = Path.Combine(Path.GetTempPath(), $"oc-k4-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _originalEnv);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    // ====================================================================
    // Test infrastructure
    // ====================================================================

    private const string ValidSha = "0123456789abcdef0123456789abcdef01234567";
    private const string AllowedRepo = "github/awesome-copilot";

    private static string ValidSkill(string name, string body = "Body content.") => $"""
        ---
        name: {name}
        description: Test skill {name}
        ---
        {body}
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public List<Uri> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(request.RequestUri!);
            return Task.FromResult(Responder(request));
        }
    }

    private sealed class StubFactory(StubHandler handler, Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
    }

    private sealed class CapturingAudit : ISkillImportLogger
    {
        public List<string> Events { get; } = new();
        public void ImportRequested(string repo, string sha, string sourcePath, string skillName, string bodySha256, int bodyBytes)
            => Events.Add($"requested:{repo}:{skillName}");
        public void ImportApproved(string previewToken, string repo, string sha, string skillName)
            => Events.Add($"approved:{repo}:{skillName}");
        public void ImportCompleted(string repo, string sha, string skillName, string installedPath, string bodySha256, int bodyBytes)
            => Events.Add($"completed:{repo}:{skillName}");
    }

    private (SkillImportService Svc, StubHandler Handler, CapturingAudit Audit, OpenClawNetSkillsRegistry Registry)
        MakeService(SkillsImportOptions? opts = null, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler();
        if (respond is not null) handler.Responder = respond;
        var factory = new StubFactory(handler, new Uri("https://raw.githubusercontent.com/"));
        var registry = new OpenClawNetSkillsRegistry(NullLogger<OpenClawNetSkillsRegistry>.Instance);
        var resolver = new SafePathResolver();
        var audit = new CapturingAudit();
        var monitor = new StaticOptionsMonitor<SkillsImportOptions>(opts ?? new SkillsImportOptions());
        var svc = new SkillImportService(
            factory, registry, resolver, monitor, audit, TimeProvider.System,
            NullLogger<SkillImportService>.Instance);
        return (svc, handler, audit, registry);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static HttpResponseMessage TextResponse(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    // ====================================================================
    // Allowlist
    // ====================================================================

    [Fact]
    public async Task Preview_RejectsRepoNotOnAllowlist_With_RepoNotAllowed()
    {
        var (svc, _, _, _) = MakeService();
        var result = await svc.PreviewAsync(new SkillImportRequest("evil/repo", ValidSha, "skills/x.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.RepoNotAllowed);
    }

    [Fact]
    public async Task Preview_AcceptsAllowedRepo_AfterValidation()
    {
        var (svc, _, _, _) = MakeService(respond: _ => TextResponse(ValidSkill("widget")));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/widget/SKILL.md"));
        result.Success.Should().BeTrue();
        result.Value!.SkillName.Should().Be("widget");
    }

    [Fact]
    public async Task Preview_RejectsBranchTipAsSha_With_InvalidSha()
    {
        var (svc, _, _, _) = MakeService();
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, "main", "skills/x.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.InvalidSha);
    }

    [Fact]
    public async Task Preview_RejectsNonMdPath_With_UnsupportedExtension()
    {
        var (svc, _, _, _) = MakeService();
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/foo/script.ps1"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.UnsupportedExtension);
    }

    [Fact]
    public async Task Preview_RejectsTraversalPath_With_InvalidPath()
    {
        var (svc, _, _, _) = MakeService();
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "../etc/passwd.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.InvalidPath);
    }

    // ====================================================================
    // Fetch
    // ====================================================================

    [Fact]
    public async Task Preview_GitHubReturns404_PropagatesAs_NotFound()
    {
        var (svc, _, _, _) = MakeService(respond: _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/missing.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.NotFound);
    }

    [Fact]
    public async Task Preview_GitHubReturns500_PropagatesAs_FetchFailed()
    {
        var (svc, _, _, _) = MakeService(respond: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/foo.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.FetchFailed);
    }

    [Fact]
    public async Task Preview_BodyOver256KB_RejectedAs_BodyTooLarge()
    {
        var huge = new string('a', SkillImportService.MaxBodyBytes + 1024);
        var (svc, _, _, _) = MakeService(respond: _ => TextResponse(ValidSkill("big", huge)));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/big.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.BodyTooLarge);
    }

    [Fact]
    public async Task Preview_HitsExpectedRawGitHubUrl()
    {
        var (svc, handler, _, _) = MakeService(respond: _ => TextResponse(ValidSkill("widget")));
        var sha = ValidSha;
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, sha, "skills/widget/SKILL.md"));
        result.Success.Should().BeTrue();
        var url = handler.Calls.Single().ToString();
        url.Should().StartWith("https://raw.githubusercontent.com/");
        url.Should().Contain($"{AllowedRepo}/{sha}/skills/widget/SKILL.md");
    }

    // ====================================================================
    // Parse + name validation
    // ====================================================================

    [Fact]
    public async Task Preview_MalformedFrontmatter_RejectedAs_MalformedSkill()
    {
        var bad = "no frontmatter here";
        var (svc, _, _, _) = MakeService(respond: _ => TextResponse(bad));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/x.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.MalformedSkill);
    }

    [Fact]
    public async Task Preview_NameViolatesH5Allowlist_RejectedAs_InvalidName()
    {
        var bad = ValidSkill("UPPER_BAD"); // uppercase + underscore both fail H-5 allowlist
        var (svc, _, _, _) = MakeService(respond: _ => TextResponse(bad));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/foo.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.InvalidName);
    }

    [Fact]
    public async Task Preview_ReservedName_RejectedAs_InvalidName()
    {
        var (svc, _, _, _) = MakeService(respond: _ => TextResponse(ValidSkill("memory")));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/memory.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.InvalidName);
    }

    [Fact]
    public async Task Preview_DuplicateInstalledSkill_RejectedAs_SkillAlreadyExists()
    {
        // Pre-create an installed skill.
        var existing = Path.Combine(_root, "skills", "installed", "widget");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "SKILL.md"), ValidSkill("widget"));

        var (svc, _, _, _) = MakeService(respond: _ => TextResponse(ValidSkill("widget")));
        var result = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/widget.md"));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.SkillAlreadyExists);
    }

    // ====================================================================
    // Confirm
    // ====================================================================

    [Fact]
    public async Task Confirm_WithFreshToken_WritesFiles_AndReturnsSkillName()
    {
        var (svc, _, audit, registry) = MakeService(respond: _ => TextResponse(ValidSkill("widget", "Fresh body.")));
        var preview = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/widget.md"));
        preview.Success.Should().BeTrue();

        var confirm = await svc.ConfirmAsync(preview.Value!.PreviewToken);
        confirm.Success.Should().BeTrue();
        confirm.Value!.SkillName.Should().Be("widget");

        File.Exists(Path.Combine(_root, "skills", "installed", "widget", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "skills", "installed", "widget", ".import.json")).Should().BeTrue();

        audit.Events.Should().Contain(e => e.StartsWith("requested:"));
        audit.Events.Should().Contain(e => e.StartsWith("approved:"));
        audit.Events.Should().Contain(e => e.StartsWith("completed:"));

        registry.Dispose();
    }

    [Fact]
    public async Task Confirm_TokenIsSingleUse()
    {
        var (svc, _, _, registry) = MakeService(respond: _ => TextResponse(ValidSkill("widget")));
        var preview = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/widget.md"));
        var first = await svc.ConfirmAsync(preview.Value!.PreviewToken);
        first.Success.Should().BeTrue();

        var second = await svc.ConfirmAsync(preview.Value!.PreviewToken);
        second.Success.Should().BeFalse();
        second.Reason.Should().Be(SkillImportReasons.PreviewNotFound);

        registry.Dispose();
    }

    [Fact]
    public async Task Confirm_UnknownToken_ReturnsPreviewNotFound()
    {
        var (svc, _, _, registry) = MakeService();
        var result = await svc.ConfirmAsync("bogus-token");
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(SkillImportReasons.PreviewNotFound);
        registry.Dispose();
    }

    [Fact]
    public async Task Confirm_LandsDisabled_NoEnabledJsonTouched()
    {
        // Q1 — imports default to disabled for all agents.
        var (svc, _, _, registry) = MakeService(respond: _ => TextResponse(ValidSkill("widget")));
        var preview = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/widget.md"));
        await svc.ConfirmAsync(preview.Value!.PreviewToken);

        // No agent overlays were written.
        var agentsRoot = Path.Combine(_root, "skills", "agents");
        if (Directory.Exists(agentsRoot))
        {
            Directory.EnumerateFiles(agentsRoot, "enabled.json", SearchOption.AllDirectories)
                .Should().BeEmpty("Q1: imported skills land disabled; no enabled.json mutated.");
        }
        registry.Dispose();
    }

    [Fact]
    public async Task Preview_Result_NeverContainsBody_Q5()
    {
        // Q5 — preview surface returns metadata only, never the body content.
        var sentinel = "K4-Q5-SENTINEL-MUST-NEVER-LEAK";
        var (svc, _, _, registry) = MakeService(respond: _ => TextResponse(ValidSkill("widget", sentinel)));
        var preview = await svc.PreviewAsync(new SkillImportRequest(AllowedRepo, ValidSha, "skills/widget.md"));
        preview.Success.Should().BeTrue();

        var json = System.Text.Json.JsonSerializer.Serialize(preview.Value);
        json.Should().NotContain(sentinel, "Q5: preview DTO must not include SKILL.md body");
        registry.Dispose();
    }
}
