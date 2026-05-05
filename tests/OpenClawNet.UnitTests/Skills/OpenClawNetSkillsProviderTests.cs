// K-1b — Dylan
// Tests for OpenClawNetSkillsProvider — scoped MAF AIContextProvider that
// builds ONE AgentSkillsProvider per request from the registry snapshot,
// filtered by per-agent enabled.json.
//
// Spec contract (K-D-1):
//   - Single AgentSkillsProvider per request, built via
//     AgentSkillsProviderBuilder.UseSkill(AgentInlineSkill.Create(...))
//   - DisableCaching = true (MAF cache defeats hot-reload)
//   - Provider includes only skills enabled for the active agent
//
// Spec sources:
//   - This prompt (squad spawn)
//   - .squad/decisions.md K-D-1
//
// ────────────────────────────────────────────────────────────────────────────
// ⚠ DORMANT until Petey's K-1b OpenClawNetSkillsProvider lands.
// ────────────────────────────────────────────────────────────────────────────
#if K1B_LANDED
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Skills;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.UnitTests.Skills;

[Trait("Area", "Skills")]
[Trait("Wave", "K-1b")]
[Collection("StorageEnvVar")]
public sealed class OpenClawNetSkillsProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string? _originalEnv;

    public OpenClawNetSkillsProviderTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName);
        _root = Path.Combine(Path.GetTempPath(), $"oc-k1b-prov-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _originalEnv);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    private static string ValidSkill(string name, string body = "Body content.") => $"""
        ---
        name: {name}
        description: Test skill {name}
        ---
        {body}
        """;

    private void WriteInstalledSkill(string name, string body = "Body content.")
    {
        var dir = Path.Combine(_root, "skills", "installed", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), ValidSkill(name, body));
    }

    private void EnableForAgent(string agent, params string[] skillNames)
    {
        var dir = Path.Combine(_root, "skills", "agents", agent);
        Directory.CreateDirectory(dir);
        var pairs = string.Join(", ", skillNames.Select(n => $"\"{n}\": true"));
        File.WriteAllText(Path.Combine(dir, "enabled.json"), $"{{ {pairs} }}");
    }

    private OpenClawNetSkillsRegistry CreateRegistry()
        => new(NullLogger<OpenClawNetSkillsRegistry>.Instance);

    private OpenClawNetSkillsProvider CreateProvider(OpenClawNetSkillsRegistry registry, string agentName)
        => new(registry, agentName, NullLogger<OpenClawNetSkillsProvider>.Instance);

    // ====================================================================

    [Fact]
    public async Task Build_ReturnsSingleAgentSkillsProviderInstance()
    {
        WriteInstalledSkill("memory");
        EnableForAgent("alice", "memory");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");

        var maf = await provider.BuildAgentSkillsProviderAsync();

        maf.Should().NotBeNull("spec K-D-1: one AgentSkillsProvider per request");
    }

    [Fact]
    public async Task Build_OnlyIncludesSkillsEnabledForThisAgent()
    {
        WriteInstalledSkill("memory");
        WriteInstalledSkill("design-rules");
        EnableForAgent("alice", "memory"); // only memory enabled

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");

        var resolved = await provider.GetEnabledSkillsAsync();

        resolved.Should().HaveCount(1);
        resolved.Single().Name.Should().Be("memory");
    }

    [Fact]
    public async Task Build_NoSkillsEnabled_ReturnsEmptyOrNoOpProvider()
    {
        WriteInstalledSkill("memory");
        // No enabled.json for "alice" → Q1 opt-in default: no skills enabled

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");

        var resolved = await provider.GetEnabledSkillsAsync();
        resolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Build_DisableCachingTrue_OnEveryBuild()
    {
        WriteInstalledSkill("memory");
        EnableForAgent("alice", "memory");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");

        var options = provider.GetMafProviderOptions();
        options.DisableCaching.Should().BeTrue(
            "K-D-1: MAF cache must be disabled so hot-reload propagates next-turn");
    }

    [Fact]
    public async Task Build_SkillBodyIsFullMarkdownContent()
    {
        WriteInstalledSkill("memory", "## Heading\n\nBody paragraph.\n");
        EnableForAgent("alice", "memory");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");
        var resolved = (await provider.GetEnabledSkillsAsync()).ToList();

        resolved.Should().HaveCount(1);
        resolved[0].Body.Should().Contain("## Heading").And.Contain("Body paragraph.");
    }

    [Fact]
    public async Task Build_AgentInlineSkillName_MatchesSkillMdName()
    {
        WriteInstalledSkill("memory");
        EnableForAgent("alice", "memory");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");
        var resolved = (await provider.GetEnabledSkillsAsync()).ToList();

        resolved.Should().HaveCount(1);
        resolved[0].Name.Should().Be("memory",
            "MAF AgentInlineSkill name must match the SKILL.md frontmatter name verbatim");
    }

    [Fact]
    public async Task Build_RespectsLayerPrecedence()
    {
        // system has "shared", installed has "shared" — installed wins
        var sysDir = Path.Combine(_root, "skills", "system", "shared");
        Directory.CreateDirectory(sysDir);
        File.WriteAllText(Path.Combine(sysDir, "SKILL.md"), ValidSkill("shared", "system body"));
        WriteInstalledSkill("shared", "installed body");
        EnableForAgent("alice", "shared");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");
        var resolved = (await provider.GetEnabledSkillsAsync()).ToList();

        resolved.Should().HaveCount(1);
        resolved[0].Body.Should().Contain("installed body",
            "precedence resolved by registry; provider sees the winning layer body");
    }

    [Fact]
    public async Task Build_PerRequestFreshness_NewProviderEachCall()
    {
        WriteInstalledSkill("memory");
        EnableForAgent("alice", "memory");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        var provider = CreateProvider(registry, "alice");

        var maf1 = await provider.BuildAgentSkillsProviderAsync();
        var maf2 = await provider.BuildAgentSkillsProviderAsync();

        ReferenceEquals(maf1, maf2).Should().BeFalse(
            "K-D-1: a fresh AgentSkillsProvider is built per request; no instance reuse");
    }
}
#endif
