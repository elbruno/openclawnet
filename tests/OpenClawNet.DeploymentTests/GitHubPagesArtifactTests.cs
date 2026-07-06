using System.Diagnostics;
using FluentAssertions;

namespace OpenClawNet.DeploymentTests;

public sealed class GitHubPagesArtifactTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "openclawnet-pages-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PrepareGitHubPagesScript_StagesDashboardAtPublishedRoot()
    {
        Directory.CreateDirectory(_tempRoot);

        var repoRoot = Path.Combine(_tempRoot, "repo");
        var outputRoot = Path.Combine(_tempRoot, "site");
        var landingRoot = Path.Combine(repoRoot, "docs", "landing");
        var dashboardRoot = Path.Combine(repoRoot, "docs", "test-dashboard");
        var nestedDashboardRoot = Path.Combine(dashboardRoot, "assets");

        Directory.CreateDirectory(landingRoot);
        Directory.CreateDirectory(nestedDashboardRoot);

        await File.WriteAllTextAsync(Path.Combine(landingRoot, "index.html"), "<html><body>landing</body></html>");
        await File.WriteAllTextAsync(Path.Combine(dashboardRoot, "index.html"), "<html><body>dashboard</body></html>");
        await File.WriteAllTextAsync(Path.Combine(dashboardRoot, "summary.json"), """{"generatedAt":"2026-07-06T00:00:00Z"}""");
        await File.WriteAllTextAsync(Path.Combine(nestedDashboardRoot, "app.js"), "console.log('dashboard');");

        var result = await RunPrepareScriptAsync(repoRoot, outputRoot);

        result.ExitCode.Should().Be(0, result.StandardError);
        File.ReadAllText(Path.Combine(outputRoot, "index.html")).Should().Contain("landing");
        File.ReadAllText(Path.Combine(outputRoot, "test-dashboard", "index.html")).Should().Contain("dashboard");
        File.ReadAllText(Path.Combine(outputRoot, "test-dashboard", "summary.json")).Should().Contain("generatedAt");
        File.ReadAllText(Path.Combine(outputRoot, "test-dashboard", "assets", "app.js")).Should().Contain("dashboard");
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunPrepareScriptAsync(string repoRoot, string outputRoot)
    {
        var scriptPath = Path.Combine(GetProjectRoot(), "scripts", "prepare-github-pages.ps1");
        var shell = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        var arguments = OperatingSystem.IsWindows()
            ? $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -RepositoryRoot \"{repoRoot}\" -OutputDirectory \"{outputRoot}\""
            : $"-NoLogo -NoProfile -File \"{scriptPath}\" -RepositoryRoot \"{repoRoot}\" -OutputDirectory \"{outputRoot}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();

        var standardOutput = await process!.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, standardOutput, standardError);
    }

    private static string GetProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenClawNet.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
