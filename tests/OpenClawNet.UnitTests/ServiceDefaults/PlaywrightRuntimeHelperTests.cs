using FluentAssertions;
using OpenClawNet.ServiceDefaults;
using OpenClawNet.UnitTests.Fixtures;

namespace OpenClawNet.UnitTests.ServiceDefaults;

public sealed class PlaywrightRuntimeHelperTests
{
    [Fact]
    public void PrepareForWindowsBaseDirectory_UsesSystemNode_AndDeletesRepoLocalNodeRuntime()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Playwright runtime node override is Windows-specific.");

        using var temp = new PerTestTempDirectory("playwright-runtime-helper");
        var baseDirectory = temp.GetPath("base");
        Directory.CreateDirectory(baseDirectory);

        var packageDirectory = Path.Combine(baseDirectory, ".playwright", "package");
        var playwrightNodeDirectory = Path.Combine(baseDirectory, ".playwright", "node", "win32_x64");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(playwrightNodeDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "cli.js"), "console.log('playwright');");
        File.WriteAllText(Path.Combine(packageDirectory, "package.json"), "{ }");

        var repoLocalNodePath = Path.Combine(playwrightNodeDirectory, "node.exe");
        File.WriteAllText(repoLocalNodePath, "stale-runtime");

        var systemNodePath = ResolveSystemNodePath();
        var originalPlaywrightNodePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_NODEJS_PATH");
        var originalDriverSearchPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH");
        var originalSystemNodePath = Environment.GetEnvironmentVariable("OPENCLAWNET_PLAYWRIGHT_SYSTEM_NODE");

        try
        {
            PlaywrightRuntimeHelper.PrepareForWindowsBaseDirectory(baseDirectory, systemNodePath);

            var driverSearchPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH");

            File.Exists(repoLocalNodePath).Should().BeFalse();
            Environment.GetEnvironmentVariable("PLAYWRIGHT_NODEJS_PATH").Should().Be(systemNodePath);
            Environment.GetEnvironmentVariable("OPENCLAWNET_PLAYWRIGHT_SYSTEM_NODE").Should().Be(systemNodePath);
            driverSearchPath.Should().NotBeNullOrWhiteSpace();
            driverSearchPath!.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
            File.Exists(Path.Combine(driverSearchPath!, ".playwright", "package", "cli.js")).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_NODEJS_PATH", originalPlaywrightNodePath);
            Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", originalDriverSearchPath);
            Environment.SetEnvironmentVariable("OPENCLAWNET_PLAYWRIGHT_SYSTEM_NODE", originalSystemNodePath);
        }
    }

    private static string ResolveSystemNodePath()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "where.exe",
            Arguments = "node",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start where.exe.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to locate system node.exe for test setup. ExitCode={process.ExitCode}. Stdout={stdout} Stderr={stderr}");
        }

        return stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First(path => File.Exists(path));
    }
}
