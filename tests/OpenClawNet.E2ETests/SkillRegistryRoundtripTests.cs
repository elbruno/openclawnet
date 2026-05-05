using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenClawNet.Skills;
using OpenClawNet.Storage;

namespace OpenClawNet.E2ETests;

/// <summary>
/// E2E-1 — proves the K-1b registry is reachable through the gateway HTTP
/// surface and that per-agent enable persistence round-trips.
/// </summary>
/// <remarks>
/// This test does <b>not</b> require Azure OpenAI — it exercises the storage
/// + endpoints layer only. It still carries the Live trait so it runs in the
/// same Live filter as the chat tests, which keeps the runbook simple.
/// </remarks>
[Trait("Category", "Live")]
[Trait("Layer", "E2E")]
public sealed class SkillRegistryRoundtripTests : IClassFixture<GatewayE2EFactory>, IDisposable
{
    private readonly GatewayE2EFactory _factory;
    private readonly HttpClient _client;
    private readonly string _agentName = "openclawnet-agent";
    private readonly List<string> _enabledSkillsToReset = new();

    public SkillRegistryRoundtripTests(GatewayE2EFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [SkippableFact]
    public async Task Skills_Endpoints_RoundTripPerAgentEnable()
    {
        // GET /api/skills — bundled SystemSkills (memory, doc-processor) are
        // seeded into {root}/skills/system on first registry construction.
        var listResp = await _client.GetAsync("/api/skills");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the registry endpoint should be wired even with no live model.");

        using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        var skills = listDoc.RootElement.EnumerateArray().ToList();
        skills.Should().NotBeEmpty(
            "the gateway eagerly seeds at least one bundled system skill on boot.");

        // Pick the first system skill (deterministic — sorted by name in the snapshot).
        var firstSystem = skills.FirstOrDefault(s =>
            string.Equals(s.GetProperty("layer").GetString(), "system", StringComparison.OrdinalIgnoreCase));
        Skip.If(firstSystem.ValueKind != JsonValueKind.Object,
            "No system-layer skill found in snapshot; cannot validate enable round-trip.");

        var skillName = firstSystem.GetProperty("name").GetString()!;
        _enabledSkillsToReset.Add(skillName);

        // PATCH /api/skills/enabled — enable for default agent.
        var patchResp = await _client.PatchAsJsonAsync("/api/skills/enabled",
            new { agent = _agentName, skill = skillName, enabled = true });
        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // GET /api/skills/{name} — verify enabledByAgent reflects our flip.
        var getResp = await _client.GetAsync($"/api/skills/{Uri.EscapeDataString(skillName)}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detail = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
        var enabledByAgent = detail.RootElement.GetProperty("enabledByAgent");
        enabledByAgent.TryGetProperty(_agentName, out var enabledForAgent).Should().BeTrue(
            "the per-agent enabled.json should now contain a row for the default agent.");
        enabledForAgent.GetBoolean().Should().BeTrue();

        // Snapshot endpoint should also be live.
        var snapResp = await _client.GetAsync("/api/skills/snapshot");
        snapResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var snap = JsonDocument.Parse(await snapResp.Content.ReadAsStringAsync());
        snap.RootElement.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }

    public void Dispose()
    {
        // Idempotent teardown — disable anything we enabled, then drop the
        // per-agent enabled.json file so the next test starts clean.
        foreach (var skill in _enabledSkillsToReset)
        {
            try
            {
                _client.PatchAsJsonAsync("/api/skills/enabled",
                    new { agent = _agentName, skill, enabled = false })
                    .GetAwaiter().GetResult();
            }
            catch { /* best-effort */ }
        }

        try
        {
            var folder = Path.Combine(_factory.StorageRoot, "skills", "agents", _agentName);
            var path = Path.Combine(folder, "enabled.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort */ }
    }
}
