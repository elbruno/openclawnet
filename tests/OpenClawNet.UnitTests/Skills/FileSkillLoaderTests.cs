using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Skills;
using OpenClawNet.UnitTests.Fixtures;

namespace OpenClawNet.UnitTests.Skills;

/// <summary>
/// Unit tests for FileSkillLoader covering install/uninstall, subdirectory SKILL.md
/// scanning (awesome-copilot format), and Source tagging for installed skills.
/// </summary>
public sealed class FileSkillLoaderTests : IDisposable
{
    private readonly PerTestTempDirectory _temp = new("ocn-skills");
    private string _tempDir => _temp.Path;
    private readonly string _installedDir;

    public FileSkillLoaderTests()
    {
        _installedDir = Path.Combine(_tempDir, "installed");
    }

    public void Dispose() => _temp.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a loader with the temp installed directory and optional extra source dirs.</summary>
    private FileSkillLoader CreateLoader(params string[] sourceDirs)
    {
        var dirs = sourceDirs.Length > 0 ? (IEnumerable<string>)sourceDirs : null;
        return new FileSkillLoader(NullLogger<FileSkillLoader>.Instance, dirs, _installedDir);
    }

    private static string MakeSkillMarkdown(string name, string description = "A skill") => $"""
        ---
        name: {name}
        description: {description}
        category: test
        tags:
          - test
        ---

        You are a {name} assistant.
        """;

    // ── ReloadAsync — flat files ──────────────────────────────────────────────

    [Fact]
    public async Task ReloadAsync_LoadsFlatMdFiles()
    {
        var dir = Path.Combine(_tempDir, "flat");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "alpha.md"), MakeSkillMarkdown("alpha"));

        var loader = CreateLoader(dir);
        await loader.ReloadAsync();

        var skills = await loader.ListSkillsAsync();
        skills.Should().ContainSingle(s => s.Name == "alpha");
    }

    // ── ReloadAsync — subdirectory SKILL.md (awesome-copilot format) ──────────

    [Fact]
    public async Task ReloadAsync_LoadsSubdirectorySkillMd()
    {
        var dir = Path.Combine(_tempDir, "marketplace");
        var skillSubDir = Path.Combine(dir, "my-skill");
        Directory.CreateDirectory(skillSubDir);
        await File.WriteAllTextAsync(
            Path.Combine(skillSubDir, "SKILL.md"),
            MakeSkillMarkdown("my-skill", "From subdirectory"));

        var loader = CreateLoader(dir);
        await loader.ReloadAsync();

        var skills = await loader.ListSkillsAsync();
        skills.Should().ContainSingle(s => s.Name == "my-skill");
        skills[0].Description.Should().Be("From subdirectory");
    }

    [Fact]
    public async Task ReloadAsync_IgnoresSubdirectoriesWithoutSkillMd()
    {
        var dir = Path.Combine(_tempDir, "nosk");
        var subDir = Path.Combine(dir, "empty-sub");
        Directory.CreateDirectory(subDir);

        var loader = CreateLoader(dir);
        await loader.ReloadAsync();

        var skills = await loader.ListSkillsAsync();
        skills.Should().BeEmpty();
    }

    // ── InstallSkillAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task InstallSkillAsync_CreatesSkillMdInInstalledDirectory()
    {
        var loader = CreateLoader();
        var content = MakeSkillMarkdown("installed-skill", "Freshly installed");

        await loader.InstallSkillAsync("installed-skill", content);

        var expectedFile = Path.Combine(_installedDir, "installed-skill", "SKILL.md");
        File.Exists(expectedFile).Should().BeTrue();
        (await File.ReadAllTextAsync(expectedFile)).Should().Be(content);
    }

    [Fact]
    public async Task InstallSkillAsync_SkillAppearsInListAfterInstall()
    {
        var loader = CreateLoader();
        await loader.InstallSkillAsync("brand-new", MakeSkillMarkdown("brand-new", "New from marketplace"));

        var skills = await loader.ListSkillsAsync();
        skills.Should().ContainSingle(s => s.Name == "brand-new");
    }

    [Fact]
    public async Task InstallSkillAsync_SetsSourceToInstalled()
    {
        var loader = CreateLoader();
        await loader.InstallSkillAsync("tagged-skill", MakeSkillMarkdown("tagged-skill"));

        var skills = await loader.ListSkillsAsync();
        skills.Should().ContainSingle(s => s.Name == "tagged-skill" && s.Source == "installed");
    }

    // ── UninstallSkillAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UninstallSkillAsync_RemovesSkillFromList()
    {
        var loader = CreateLoader();
        await loader.InstallSkillAsync("removable", MakeSkillMarkdown("removable"));

        await loader.UninstallSkillAsync("removable");

        var skills = await loader.ListSkillsAsync();
        skills.Should().NotContain(s => s.Name == "removable");
    }

    [Fact]
    public async Task UninstallSkillAsync_DeletesDirectory()
    {
        var loader = CreateLoader();
        await loader.InstallSkillAsync("to-delete", MakeSkillMarkdown("to-delete"));

        var dir = Path.Combine(_installedDir, "to-delete");
        Directory.Exists(dir).Should().BeTrue("directory should exist before uninstall");

        await loader.UninstallSkillAsync("to-delete");

        Directory.Exists(dir).Should().BeFalse("directory should be gone after uninstall");
    }

    [Fact]
    public async Task UninstallSkillAsync_DoesNotThrow_WhenSkillNotFound()
    {
        var loader = CreateLoader();

        Func<Task> act = () => loader.UninstallSkillAsync("does-not-exist");

        await act.Should().NotThrowAsync();
    }

    // ── Enable / Disable ──────────────────────────────────────────────────────

    [Fact]
    public async Task DisableSkill_ExcludesItFromActiveSkills()
    {
        var dir = Path.Combine(_tempDir, "toggle");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "toggle.md"), MakeSkillMarkdown("toggle"));

        var loader = CreateLoader(dir);
        await loader.ReloadAsync();

        loader.DisableSkill("toggle");

        var active = await loader.GetActiveSkillsAsync();
        active.Should().NotContain(s => s.Name == "toggle");
    }

    [Fact]
    public async Task EnableSkill_RestoresItToActiveSkills()
    {
        var dir = Path.Combine(_tempDir, "re-enable");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "re-enable.md"), MakeSkillMarkdown("re-enable"));

        var loader = CreateLoader(dir);
        await loader.ReloadAsync();

        loader.DisableSkill("re-enable");
        loader.EnableSkill("re-enable");

        var active = await loader.GetActiveSkillsAsync();
        active.Should().ContainSingle(s => s.Name == "re-enable");
    }

    // ── Source defaults ───────────────────────────────────────────────────────

    [Fact]
    public async Task BuiltInSkills_HaveSourceBuiltIn()
    {
        var dir = Path.Combine(_tempDir, "builtin");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "native.md"), MakeSkillMarkdown("native"));

        var loader = CreateLoader(dir);
        await loader.ReloadAsync();

        var skills = await loader.ListSkillsAsync();
        skills.Should().ContainSingle(s => s.Name == "native" && s.Source == "built-in");
    }
}
