// K-1b — Dylan
// Tests for OpenClawNetSkillsRegistry : ISkillsRegistry (Petey K-1b spec).
//
// Contracts under test:
//   - 3-layer scan: system / installed / agents/{name}
//   - Layer precedence: agents > installed > system
//   - Immutable snapshot record { Skills, BuiltUtc, SnapshotId }
//   - SnapshotId = SHA-256 of (sorted names + content hashes), 16-char hex prefix
//   - Per-agent enabled.json = { "skill-name": true, ... }; default false (Q1 opt-in)
//   - IsEnabledForAgentAsync(name, agent) reads enabled.json overlay
//   - Interlocked.Exchange swap; non-blocking concurrent reads
//   - Malformed YAML frontmatter: skill skipped + WARN, not throw
//
// Spec sources:
//   - This prompt (squad spawn)
//   - docs/proposals/agent-skills.md §K-1
//   - .squad/decisions.md K-D-1, K-D-2, Q1 (opt-in)
//   - .squad/decisions/inbox/drummond-w4-gate-verdict.md K-1b binding ACs
//
// ────────────────────────────────────────────────────────────────────────────
// ⚠ DORMANT until Petey's K-1b OpenClawNetSkillsRegistry lands. Activate by
// adding K1B_LANDED to the OpenClawNet.UnitTests .csproj DefineConstants.
// ────────────────────────────────────────────────────────────────────────────
#if K1B_LANDED
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
public sealed class OpenClawNetSkillsRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly string? _originalEnv;

    public OpenClawNetSkillsRegistryTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName);
        _root = Path.Combine(Path.GetTempPath(), $"oc-k1b-reg-{Guid.NewGuid():N}");
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
    // Helpers
    // ====================================================================

    private static string ValidSkill(string name, string body = "Body content.") => $"""
        ---
        name: {name}
        description: Test skill {name}
        ---
        {body}
        """;

    private void WriteSkill(string layer, string name, string content, string? agentName = null)
    {
        string dir = layer switch
        {
            "system" => Path.Combine(_root, "skills", "system", name),
            "installed" => Path.Combine(_root, "skills", "installed", name),
            "agents" => Path.Combine(_root, "skills", "agents", agentName!, name),
            _ => throw new ArgumentException(nameof(layer))
        };
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    private static OpenClawNetSkillsRegistry CreateRegistry()
        => new(NullLogger<OpenClawNetSkillsRegistry>.Instance);

    // ====================================================================
    // A. Empty / basic discovery
    // ====================================================================

    [Fact]
    public async Task EmptyRoots_ReturnsEmptySnapshot()
    {
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        snap.Skills.Should().BeEmpty();
        snap.SnapshotId.Should().NotBeNullOrEmpty();
        snap.BuiltUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ValidSkillInSystem_ReturnsOneRecord_LayerSystem()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        snap.Skills.Should().HaveCount(1);
        snap.Skills[0].Name.Should().Be("memory");
        snap.Skills[0].Layer.Should().Be(SkillLayer.System);
    }

    [Fact]
    public async Task ValidSkillInInstalled_LayerInstalled()
    {
        WriteSkill("installed", "design-rules", ValidSkill("design-rules"));
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        snap.Skills.Should().HaveCount(1);
        snap.Skills[0].Layer.Should().Be(SkillLayer.Installed);
    }

    [Fact]
    public async Task ValidSkillInAgent_LayerAgent()
    {
        WriteSkill("agents", "alice-only", ValidSkill("alice-only"), agentName: "alice");
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        snap.Skills.Should().HaveCount(1);
        snap.Skills[0].Layer.Should().Be(SkillLayer.Agent);
    }

    // ====================================================================
    // B. Layer precedence (agents > installed > system)
    // ====================================================================

    [Fact]
    public async Task SameNameInAllLayers_AgentLayerWins()
    {
        WriteSkill("system", "shared", ValidSkill("shared", "system body"));
        WriteSkill("installed", "shared", ValidSkill("shared", "installed body"));
        WriteSkill("agents", "shared", ValidSkill("shared", "agent body"), agentName: "alice");
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        var matches = snap.Skills.Where(s => s.Name == "shared").ToList();
        matches.Should().HaveCount(1, "precedence collapses to one record per name");
        matches[0].Layer.Should().Be(SkillLayer.Agent);
        matches[0].Body.Should().Contain("agent body");
    }

    [Fact]
    public async Task SameNameInInstalledAndSystem_InstalledWins()
    {
        WriteSkill("system", "shared", ValidSkill("shared", "system body"));
        WriteSkill("installed", "shared", ValidSkill("shared", "installed body"));
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        var matches = snap.Skills.Where(s => s.Name == "shared").ToList();
        matches.Should().HaveCount(1);
        matches[0].Layer.Should().Be(SkillLayer.Installed);
        matches[0].Body.Should().Contain("installed body");
    }

    // ====================================================================
    // C. Per-agent enabled.json overlay (Q1 opt-in)
    // ====================================================================

    [Fact]
    public async Task EnabledJsonMissing_AllSkillsDisabledForAgent()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        WriteSkill("installed", "design-rules", ValidSkill("design-rules"));
        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();

        (await registry.IsEnabledForAgentAsync("memory", "alice"))
            .Should().BeFalse("Q1 opt-in: missing enabled.json means no skills enabled");
        (await registry.IsEnabledForAgentAsync("design-rules", "alice"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task EnabledJsonSetTrue_OnlyThatSkillEnabledForThatAgent()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        WriteSkill("installed", "design-rules", ValidSkill("design-rules"));

        var agentDir = Path.Combine(_root, "skills", "agents", "alice");
        Directory.CreateDirectory(agentDir);
        File.WriteAllText(
            Path.Combine(agentDir, "enabled.json"),
            """{ "memory": true }""");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();

        (await registry.IsEnabledForAgentAsync("memory", "alice")).Should().BeTrue();
        (await registry.IsEnabledForAgentAsync("design-rules", "alice")).Should().BeFalse();
    }

    [Fact]
    public async Task EnabledJsonOnlyAffectsTargetAgent()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));

        var aliceDir = Path.Combine(_root, "skills", "agents", "alice");
        Directory.CreateDirectory(aliceDir);
        File.WriteAllText(Path.Combine(aliceDir, "enabled.json"), """{ "memory": true }""");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();

        (await registry.IsEnabledForAgentAsync("memory", "alice")).Should().BeTrue();
        (await registry.IsEnabledForAgentAsync("memory", "bob")).Should().BeFalse(
            "bob has no enabled.json, so opt-in default applies");
    }

    // ====================================================================
    // D. SnapshotId — SHA-256 of (sorted names + content hashes), 16-char hex prefix
    // ====================================================================

    [Fact]
    public async Task SnapshotId_DeterministicForSameContent()
    {
        WriteSkill("system", "memory", ValidSkill("memory", "stable body"));
        using var r1 = CreateRegistry();
        var s1 = await r1.GetSnapshotAsync();

        using var r2 = CreateRegistry();
        var s2 = await r2.GetSnapshotAsync();

        s1.SnapshotId.Should().Be(s2.SnapshotId,
            "snapshot id is content-derived; same disk state must hash identically");
        s1.SnapshotId.Should().HaveLength(16, "spec: 16-char hex prefix of SHA-256");
    }

    [Fact]
    public async Task SnapshotId_ChangesWhenSkillBodyChanges()
    {
        var dir = Path.Combine(_root, "skills", "system", "memory");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), ValidSkill("memory", "body v1"));

        using var r1 = CreateRegistry();
        var s1 = await r1.GetSnapshotAsync();

        File.WriteAllText(Path.Combine(dir, "SKILL.md"), ValidSkill("memory", "body v2"));

        using var r2 = CreateRegistry();
        var s2 = await r2.GetSnapshotAsync();

        s1.SnapshotId.Should().NotBe(s2.SnapshotId);
    }

    [Fact]
    public async Task SnapshotId_ChangesWhenSkillAdded()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        using var r1 = CreateRegistry();
        var s1 = await r1.GetSnapshotAsync();

        WriteSkill("installed", "doc-processor", ValidSkill("doc-processor"));
        using var r2 = CreateRegistry();
        var s2 = await r2.GetSnapshotAsync();

        s1.SnapshotId.Should().NotBe(s2.SnapshotId);
    }

    [Fact]
    public async Task SnapshotId_ChangesWhenSkillRemoved()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        WriteSkill("installed", "doc-processor", ValidSkill("doc-processor"));

        using var r1 = CreateRegistry();
        var s1 = await r1.GetSnapshotAsync();

        Directory.Delete(Path.Combine(_root, "skills", "installed", "doc-processor"), recursive: true);

        using var r2 = CreateRegistry();
        var s2 = await r2.GetSnapshotAsync();

        s1.SnapshotId.Should().NotBe(s2.SnapshotId);
    }

    // ====================================================================
    // E. Snapshot shape
    // ====================================================================

    [Fact]
    public async Task BuiltUtc_IsUtc()
    {
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();
        snap.BuiltUtc.Offset.Should().Be(TimeSpan.Zero, "spec: BuiltUtc is UTC");
    }

    [Fact]
    public async Task SnapshotSkills_IsReadOnlyList()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        // The Skills collection is exposed as IReadOnlyList — try to cast to mutable
        // and confirm the implementation either returns Array.AsReadOnly / a frozen
        // collection (no IList<T> mutate path) or an immutable type.
        var asMutable = snap.Skills as System.Collections.Generic.IList<ISkillRecord>;
        if (asMutable is not null)
        {
            // If the cast succeeds, it must be read-only (Array, ImmutableArray.Builder.MoveToImmutable, etc.)
            asMutable.IsReadOnly.Should().BeTrue("snapshot.Skills must not allow mutation");
        }
    }

    // ====================================================================
    // F. Concurrency — non-blocking reads under rebuild
    // ====================================================================

    [Fact]
    public async Task ConcurrentReadsDuringRebuild_NoTornReads()
    {
        WriteSkill("system", "memory", ValidSkill("memory", "v1"));
        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync(); // prime

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var observed = new ConcurrentBag<string>();

        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var snap = await registry.GetSnapshotAsync();
                // Each individual snapshot must be self-consistent: SnapshotId is
                // derived from Skills, so a torn snapshot would be detectable here.
                observed.Add(snap.SnapshotId);
                snap.Skills.Should().NotBeNull();
                foreach (var s in snap.Skills)
                {
                    s.Name.Should().NotBeNullOrWhiteSpace();
                    s.Body.Should().NotBeNull();
                }
            }
        })).ToArray();

        // Concurrent rebuild trigger
        var writer = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested)
            {
                var dir = Path.Combine(_root, "skills", "system", "memory");
                File.WriteAllText(Path.Combine(dir, "SKILL.md"), ValidSkill("memory", $"v{i++}"));
                await Task.Delay(50);
            }
        });

        await Task.WhenAll(readers.Concat(new[] { writer }));

        // We saw at least one snapshot id, no exceptions thrown by readers.
        observed.Should().NotBeEmpty();
    }

    // ====================================================================
    // G. Malformed input handling
    // ====================================================================

    [Fact]
    public async Task MalformedYamlFrontmatter_SkillSkipped_NoThrow()
    {
        // Garbage YAML — unterminated frontmatter
        var dir = Path.Combine(_root, "skills", "system", "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: broken\ndescription: [\nbody");

        // Plus a valid sibling
        WriteSkill("system", "memory", ValidSkill("memory"));

        using var registry = CreateRegistry();
        var snap = await registry.GetSnapshotAsync();

        snap.Skills.Select(s => s.Name).Should().Contain("memory")
            .And.NotContain("broken", "malformed YAML must skip the skill, not crash the registry");
    }

    [Fact]
    public async Task MissingDescription_SkillSkippedOrUsesDefault()
    {
        // No description in frontmatter — agentskills.io spec requires it.
        // Petey's behavior may either skip OR substitute a default; assert the
        // registry does not THROW. This test documents the resilience contract.
        var dir = Path.Combine(_root, "skills", "system", "no-desc");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: no-desc\n---\nbody");

        using var registry = CreateRegistry();

        // Should NOT throw — either skipped or defaulted.
        var snap = await registry.GetSnapshotAsync();
        snap.Should().NotBeNull();
    }

    // ====================================================================
    // H. enabled.json file lifecycle
    // ====================================================================

    [Fact]
    public async Task EnabledJson_PreservedAcrossSnapshotRebuilds()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        var dir = Path.Combine(_root, "skills", "agents", "alice");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "enabled.json"), """{ "memory": true }""");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();

        // Force a rebuild by adding a new skill file.
        WriteSkill("installed", "design-rules", ValidSkill("design-rules"));
        await registry.GetSnapshotAsync();

        (await registry.IsEnabledForAgentAsync("memory", "alice")).Should().BeTrue(
            "enabled.json overlay must survive snapshot rebuilds");
    }
}
#endif
