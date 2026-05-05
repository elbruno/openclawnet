using Microsoft.Extensions.Logging;
using OpenClawNet.Storage;

namespace OpenClawNet.Skills;

/// <summary>
/// K-1b #1 — Copies the bundled <c>SystemSkills/**/SKILL.md</c> resources
/// (shipped alongside <c>OpenClawNet.Skills.dll</c>) into the system layer
/// of the storage root (<see cref="OpenClawNetPaths.ResolveSkillsSystemRoot"/>)
/// at boot. Idempotent: per-file copy only when destination is missing or
/// older than source. Replaces the K-1a-and-earlier
/// <c>OpenClawNet.Gateway/skills/**</c> content glob.
/// </summary>
/// <remarks>
/// Per K-D-2, the v1 system layer ships exactly two skills (<c>memory</c>,
/// <c>doc-processor</c>). Per Drummond K-1b AC1 the seeder uses
/// <see cref="OpenClawNetPaths.ResolveSkillsSystemRoot"/> only — no direct
/// <c>Path.GetFullPath</c> callsite is introduced.
/// </remarks>
public static class SystemSkillsSeeder
{
    /// <summary>
    /// Subdirectory beneath <c>AppContext.BaseDirectory</c> where MSBuild copies
    /// the bundled SKILL.md folders (matches the
    /// <c>&lt;Content Include="SystemSkills\**\*.md" CopyToOutputDirectory="Always" /&gt;</c>
    /// item in <c>OpenClawNet.Skills.csproj</c>).
    /// </summary>
    public const string BundledSubdirectory = "SystemSkills";

    /// <summary>
    /// Copies every <c>SKILL.md</c> file under
    /// <c>{AppContext.BaseDirectory}/SystemSkills/</c> into
    /// <c>{StorageRoot}/skills/system/</c>, preserving the per-skill folder
    /// name. Returns the number of files written (0 if every destination was
    /// already up-to-date).
    /// </summary>
    public static int Seed(ILogger? logger = null)
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, BundledSubdirectory);
        if (!Directory.Exists(bundledRoot))
        {
            (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
                .LogInformation(
                    "SystemSkillsSeeder: no bundled SystemSkills directory at '{BundledRoot}' — skipping seed.",
                    bundledRoot);
            return 0;
        }

        var systemRoot = OpenClawNetPaths.ResolveSkillsSystemRoot(logger);
        var copied = 0;

        // Each immediate subdirectory is one skill (e.g. SystemSkills/memory/SKILL.md).
        foreach (var skillDir in Directory.EnumerateDirectories(bundledRoot))
        {
            var skillName = Path.GetFileName(skillDir);
            var destSkillDir = Path.Combine(systemRoot, skillName);
            Directory.CreateDirectory(destSkillDir);

            foreach (var sourceFile in Directory.EnumerateFiles(skillDir, "*.md", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(skillDir, sourceFile);
                var destFile = Path.Combine(destSkillDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                var srcInfo = new FileInfo(sourceFile);
                var dstInfo = new FileInfo(destFile);

                if (!dstInfo.Exists || dstInfo.LastWriteTimeUtc < srcInfo.LastWriteTimeUtc)
                {
                    File.Copy(sourceFile, destFile, overwrite: true);
                    copied++;
                }
            }
        }

        (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            .LogInformation(
                "SystemSkillsSeeder: seeded {SystemRoot} from '{BundledRoot}' ({Copied} file(s) updated).",
                systemRoot, bundledRoot, copied);

        return copied;
    }
}
