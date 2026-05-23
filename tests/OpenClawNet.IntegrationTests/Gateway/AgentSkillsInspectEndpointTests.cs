using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenClawNet.Storage;

namespace OpenClawNet.IntegrationTests.Gateway;

public sealed class AgentSkillsInspectEndpointTests : IClassFixture<AgentSkillsInspectEndpointTests.Fixture>
{
    private readonly Fixture _fixture;
    private readonly HttpClient _client;

    public AgentSkillsInspectEndpointTests(Fixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task GetAgentSkills_ReturnsEnabledStateForAgent()
    {
        var skillName = "agent-inspect-enabled";
        var agentName = "agent-alice";
        await SeedInstalledSkillAsync(skillName, "Enabled state check");
        await ReloadSkillsAsync();

        var enableResponse = await _client.PutAsJsonAsync(
            $"/api/skills/{skillName}/enabled-for/{agentName}",
            new { enabled = true });
        enableResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync($"/api/skills/agents/{agentName}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("agentName").GetString().Should().Be(agentName);
        payload.RootElement.GetProperty("enabledSkills").GetInt32().Should().BeGreaterThan(0);

        var targetSkill = payload.RootElement
            .GetProperty("skills")
            .EnumerateArray()
            .FirstOrDefault(x => string.Equals(x.GetProperty("name").GetString(), skillName, StringComparison.Ordinal));

        targetSkill.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        targetSkill.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetAgentSkills_EnabledOnlyFilter_ReturnsOnlyEnabledRows()
    {
        var enabledSkill = "agent-inspect-filter-on";
        var disabledSkill = "agent-inspect-filter-off";
        var agentName = "agent-bob";

        await SeedInstalledSkillAsync(enabledSkill, "Enabled filter include");
        await SeedInstalledSkillAsync(disabledSkill, "Enabled filter exclude");
        await ReloadSkillsAsync();

        var enableResponse = await _client.PutAsJsonAsync(
            $"/api/skills/{enabledSkill}/enabled-for/{agentName}",
            new { enabled = true });
        enableResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync($"/api/skills/agents/{agentName}?enabledOnly=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var skills = payload.RootElement.GetProperty("skills").EnumerateArray().ToList();
        skills.Should().NotBeEmpty();
        skills.Should().OnlyContain(s => s.GetProperty("enabled").GetBoolean());
        skills.Should().Contain(s => string.Equals(s.GetProperty("name").GetString(), enabledSkill, StringComparison.Ordinal));
        skills.Should().NotContain(s => string.Equals(s.GetProperty("name").GetString(), disabledSkill, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAgentSkills_EmptyAgentName_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/skills/agents/%20");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedInstalledSkillAsync(string name, string body)
    {
        var folder = Path.Combine(_fixture.TestStorageRoot, "skills", "installed", name);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "SKILL.md"), $$"""
---
name: {{name}}
description: {{name}} description
source: manual
---
{{body}}
""");
    }

    private async Task ReloadSkillsAsync()
    {
        var response = await _client.PostAsync("/api/skills/reload", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public sealed class Fixture : GatewayWebAppFactory
    {
        public string TestStorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"oc-agent-skills-{Guid.NewGuid():N}");

        public Fixture()
        {
            var fullRoot = Path.GetFullPath(TestStorageRoot);
            Directory.CreateDirectory(fullRoot);
            Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, fullRoot);
            StorageRoot = fullRoot;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                if (Directory.Exists(TestStorageRoot))
                {
                    Directory.Delete(TestStorageRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }
}
