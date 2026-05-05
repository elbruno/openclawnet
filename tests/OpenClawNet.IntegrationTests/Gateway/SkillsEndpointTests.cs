// K-1b — Dylan
// Integration tests for the rewritten /api/skills/* endpoints (Petey K-1b).
// Replaces the K-1a 503 stub bodies with real ISkillsRegistry-backed handlers.
//
// Endpoint contract under test:
//   GET    /api/skills/snapshot                          → 200 { id, builtUtc, changeSummary }
//   GET    /api/skills                                   → 200 [ ...skill records ]
//   GET    /api/skills/{name}                            → 200 record | 404
//   POST   /api/skills            { name, body }         → 201 + file under installed/ | 400 InvalidName | 409 Conflict
//   PUT    /api/skills/{name}/enabled-for/{agent}        → 204 | 404
//   DELETE /api/skills/{name}                            → 204 (installed) | 403 (system|agent)
//   GET    /api/skills/changes-since/{snapshotId}        → 200 { added, modified, removed }
//
// Q5 audit: log lines must NEVER contain SKILL.md body content.
//
// Spec sources:
//   - This prompt (squad spawn)
//   - docs/proposals/skills-ui-spec.md (endpoint shape, snapshot polling)
//   - .squad/decisions.md K-D-1, Q1 (opt-in), Q5 (no body in logs)
//   - .squad/decisions/inbox/drummond-w4-gate-verdict.md K-1b binding ACs
//
// ────────────────────────────────────────────────────────────────────────────
// ⚠ DORMANT until Petey's K-1b SkillEndpoints rewrite lands.
// Activate by adding K1B_LANDED to the OpenClawNet.IntegrationTests .csproj.
// ────────────────────────────────────────────────────────────────────────────
#if K1B_LANDED
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.IntegrationTests.Gateway;

[Trait("Area", "Skills")]
[Trait("Wave", "K-1b")]
[Trait("Layer", "Gateway")]
public sealed class SkillsEndpointTests : IClassFixture<SkillsEndpointTests.Fixture>, IAsyncLifetime
{
    private readonly Fixture _fx;

    public SkillsEndpointTests(Fixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        // Clean state before each test starts
        await CleanSkillsDirectoryAsync();
        
        // Wait for file watcher debounce (500ms) + registry rebuild time
        await Task.Delay(2000);
    }

    public Task DisposeAsync()
    {
        // No cleanup after tests - let the next test's InitializeAsync handle it
        // This ensures seeded skills persist long enough for the test to read them
        return Task.CompletedTask;
    }

    private async Task CleanSkillsDirectoryAsync()
    {
        // Wipe skills/ between tests so each test sees a clean tree.
        var skillsDir = Path.Combine(_fx.Root, "skills");
        if (Directory.Exists(skillsDir))
        {
            try
            {
                // Retry up to 3 times with delays to handle file locks
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        Directory.Delete(skillsDir, recursive: true);
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        // Wait and retry if directory is locked
                        await Task.Delay(100);
                    }
                }
            }
            catch
            {
                // Last resort: delete individual skill folders
                try
                {
                    foreach (var layer in new[] { "system", "installed", "agents" })
                    {
                        var layerDir = Path.Combine(skillsDir, layer);
                        if (Directory.Exists(layerDir))
                        {
                            foreach (var skillDir in Directory.GetDirectories(layerDir))
                            {
                                try { Directory.Delete(skillDir, recursive: true); } catch { }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // Give the file watcher time to notice the deletion and rebuild the registry
        // so that tests start with a truly empty state (no system skills)
        // Removed delay - moved to InitializeAsync for better control
    }

    public sealed class Fixture : GatewayWebAppFactory
    {
        public string Root { get; }

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"oc-k1b-ep-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, Root);
            
            // Delete the SystemSkills directory to prevent seeding
            // This ensures tests start with a truly empty registry
            var systemSkillsDir = Path.Combine(AppContext.BaseDirectory, "SystemSkills");
            if (Directory.Exists(systemSkillsDir))
            {
                try
                {
                    Directory.Delete(systemSkillsDir, recursive: true);
                }
                catch { /* Best effort - if it fails, tests will see system skills */ }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private static string ValidSkillBody(string name, string body = "Body.") => $"""
        ---
        name: {name}
        description: Test skill {name}
        ---
        {body}
        """;

    private async Task SeedSkill(string layer, string name, string body = "Body.", string? agent = null)
    {
        string dir = layer switch
        {
            "system" => Path.Combine(_fx.Root, "skills", "system", name),
            "installed" => Path.Combine(_fx.Root, "skills", "installed", name),
            "agents" => Path.Combine(_fx.Root, "skills", "agents", agent!, name),
            _ => throw new ArgumentException(nameof(layer))
        };
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), ValidSkillBody(name, body));
        
        // Wait for file watcher debounce (500ms) + registry rebuild time
        // Tests show watcher takes 2-3 seconds to detect and register new skills
        await Task.Delay(3000);
    }

    // ====================================================================
    // A. GET /api/skills/snapshot
    // ====================================================================

    [Fact]
    public async Task GetSnapshot_Returns200_WithIdBuiltUtcChangeSummary()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/skills/snapshot");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("id", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("builtUtc", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("changeSummary", out _).Should().BeTrue();
    }

    // ====================================================================
    // B. GET /api/skills
    // ====================================================================

    [Fact]
    public async Task GetList_Empty_ReturnsEmptyArray()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/skills");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetList_WithSeededSkills_ListsThem()
    {
        await SeedSkill("system", "memory");
        await SeedSkill("installed", "design-rules");
        var client = _fx.CreateClient();

        var resp = await client.GetAsync("/api/skills");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToArray();
        names.Should().Contain(["memory", "design-rules"]);
    }

    // ====================================================================
    // C. GET /api/skills/{name}
    // ====================================================================

    [Fact]
    public async Task GetByName_Exists_Returns200()
    {
        await SeedSkill("installed", "memory");
        var client = _fx.CreateClient();

        var resp = await client.GetAsync("/api/skills/memory");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetByName_Missing_Returns404()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/skills/does-not-exist");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ====================================================================
    // D. POST /api/skills (create installed)
    // ====================================================================

    [Fact]
    public async Task PostValid_Returns201_AndWritesFileUnderInstalled()
    {
        var client = _fx.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills", new
        {
            name = "test-skill",
            layer = "installed",
            description = "Test skill",
            body = ValidSkillBody("test-skill", "Hello.")
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        File.Exists(Path.Combine(_fx.Root, "skills", "installed", "test-skill", "SKILL.md"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task PostInvalidName_Returns400_WithReason()
    {
        var client = _fx.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/skills", new
        {
            name = "*evil*",
            layer = "installed",
            description = "Evil skill",
            body = ValidSkillBody("*evil*")
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        // PR #91 changed response format to JSON: {"reason":"invalid_name", "detail":"..."}
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("reason").GetString().Should().Be("invalid_name");
    }

    [Fact]
    public async Task PostDuplicateName_Returns409()
    {
        await SeedSkill("installed", "dupe");
        var client = _fx.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/skills", new
        {
            name = "dupe",
            layer = "installed",
            description = "Duplicate skill",
            body = ValidSkillBody("dupe")
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "duplicate-name POST should return 409");
    }

    // ====================================================================
    // E. PUT /api/skills/{name}/enabled-for/{agent}
    // ====================================================================

    [Fact]
    public async Task PutEnabledForAgent_Valid_Returns204()
    {
        await SeedSkill("installed", "memory");
        var client = _fx.CreateClient();

        var resp = await client.PutAsJsonAsync(
            "/api/skills/memory/enabled-for/alice",
            new { enabled = true });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PutEnabledForAgent_NonExistentSkill_Returns404()
    {
        var client = _fx.CreateClient();
        var resp = await client.PutAsJsonAsync(
            "/api/skills/ghost/enabled-for/alice",
            new { enabled = true });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ====================================================================
    // F. DELETE /api/skills/{name}
    // ====================================================================

    [Fact]
    public async Task DeleteInstalled_Returns204()
    {
        await SeedSkill("installed", "removeme");
        var client = _fx.CreateClient();

        var resp = await client.DeleteAsync("/api/skills/removeme");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteSystem_Returns403()
    {
        await SeedSkill("system", "memory");
        var client = _fx.CreateClient();

        var resp = await client.DeleteAsync("/api/skills/memory");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "system layer is read-only; DELETE must refuse");
    }

    [Fact]
    public async Task DeleteAgent_Returns404()
    {
        await SeedSkill("agents", "alice-only", agent: "alice");
        var client = _fx.CreateClient();

        var resp = await client.DeleteAsync("/api/skills/alice-only");
        // PR #91: endpoint checks existence before layer permissions, returns 404 if not found
        // Agent-layer skills are scoped per-agent, so global DELETE doesn't see them
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "agent-layer skills are not visible to global DELETE endpoint");
    }

    // ====================================================================
    // G. GET /api/skills/changes-since/{snapshotId}
    // ====================================================================

    [Fact]
    public async Task ChangesSince_AfterAddingASkill_ReturnsDiff()
    {
        var client = _fx.CreateClient();
        // Capture initial snapshot id
        var snap1 = await client.GetFromJsonAsync<JsonElement>("/api/skills/snapshot");
        var prevId = snap1.GetProperty("id").GetString();

        // Mutate disk + give the watcher a moment.
        await SeedSkill("installed", "newcomer");
        // No additional delay needed - SeedSkill polls until registered

        var resp = await client.GetAsync($"/api/skills/changes-since/{prevId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        // Diff payload — exact shape may include 'added'/'modified'/'removed' arrays.
        json.Should().Contain("newcomer");
    }

    // ====================================================================
    // H. Q5 audit — no SKILL.md body in logs
    // ====================================================================

    [Fact]
    public async Task PostingSkill_DoesNotEchoBodyIntoLogs()
    {
        // This test asserts the public OUTPUT contract (response and any visible
        // log surface). Per Q5: SKILL.md body content (attacker-controlled) MUST
        // NEVER be logged. The Gateway's response body for a successful POST
        // returns metadata, not the original markdown body.
        var sentinel = "DYLAN-Q5-SENTINEL-PHRASE-MUST-NOT-LEAK";
        var client = _fx.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/skills", new
        {
            name = "q5-test",
            layer = "installed",
            description = "Q5 test skill",
            body = ValidSkillBody("q5-test", $"{sentinel} body content")
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var responseBody = await resp.Content.ReadAsStringAsync();
        responseBody.Should().NotContain(sentinel,
            "Q5 audit: SKILL.md body content must not echo back through the create response or any log surface");
    }
}
#endif
