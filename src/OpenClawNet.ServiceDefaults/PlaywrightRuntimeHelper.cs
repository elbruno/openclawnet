using System.ComponentModel;
using System.Diagnostics;

namespace OpenClawNet.ServiceDefaults;

public static class PlaywrightRuntimeHelper
{
    private const string RelativeNodePath = @"node\win32_x64\node.exe";
    private const string PlaywrightNodePathVariable = "PLAYWRIGHT_NODEJS_PATH";
    private const string SystemNodePathVariable = "OPENCLAWNET_PLAYWRIGHT_SYSTEM_NODE";

    public static void PrepareForCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var systemNodePath = ResolveSystemNodePath();
        if (!CanExecuteNode(systemNodePath))
        {
            throw new InvalidOperationException(
                $"System node.exe at '{systemNodePath}' is not executable for Playwright runtime setup.");
        }

        var playwrightRoot = Path.Combine(AppContext.BaseDirectory, ".playwright");
        if (!Directory.Exists(playwrightRoot))
        {
            ConfigureNodeLaunchPath(systemNodePath);
            return;
        }

        EnsureUsableNodeRuntime(playwrightRoot, systemNodePath);
        ConfigureNodeLaunchPath(systemNodePath);
        UnblockExecutables(playwrightRoot);
    }

    private static void ConfigureNodeLaunchPath(string systemNodePath)
    {
        var shimPath = ResolveNodeShimPath();
        if (shimPath is not null && CanExecuteNode(shimPath))
        {
            Environment.SetEnvironmentVariable(PlaywrightNodePathVariable, shimPath, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(SystemNodePathVariable, systemNodePath, EnvironmentVariableTarget.Process);
            return;
        }

        // Fall back to trusted system node if the shim is not yet available.
        Environment.SetEnvironmentVariable(PlaywrightNodePathVariable, systemNodePath, EnvironmentVariableTarget.Process);
    }

    private static void EnsureUsableNodeRuntime(string playwrightRoot, string systemNodePath)
    {
        var targetNodePath = Path.Combine(playwrightRoot, RelativeNodePath);
        if (CanExecuteNode(targetNodePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetNodePath)!);
        try
        {
            File.Copy(systemNodePath, targetNodePath, overwrite: true);
        }
        catch (IOException)
        {
            // Another process may hold node.exe; rely on PLAYWRIGHT_NODEJS_PATH fallback.
            if (CanExecuteNode(systemNodePath))
            {
                return;
            }

            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // Defender/quarantine or ACL hardening can block write; rely on fallback if usable.
            if (CanExecuteNode(systemNodePath))
            {
                return;
            }

            throw;
        }

        if (!CanExecuteNode(targetNodePath))
        {
            // Keep PLAYWRIGHT_NODEJS_PATH fallback active; fail only when neither runtime is usable.
            if (!CanExecuteNode(systemNodePath))
            {
                throw new InvalidOperationException(
                    $"Playwright node runtime under '{targetNodePath}' could not be repaired, and fallback system node '{systemNodePath}' is not executable.");
            }
        }
    }

    private static string ResolveSystemNodePath()
    {
        var probe = StartProcess("where.exe", "node");
        if (probe.ExitCode != 0 || string.IsNullOrWhiteSpace(probe.Stdout))
        {
            throw new InvalidOperationException(
                $"Unable to locate system node.exe required for Playwright runtime repair. ExitCode={probe.ExitCode}. Stdout={probe.Stdout} Stderr={probe.Stderr}");
        }

        var firstLine = probe.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine) || !File.Exists(firstLine))
        {
            throw new InvalidOperationException(
                $"System node.exe path returned by where.exe was invalid: '{firstLine ?? "<empty>"}'.");
        }

        return firstLine;
    }

    private static string? ResolveNodeShimPath()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var shimPath = Path.Combine(
            repoRoot,
            "src",
            "OpenClawNet.PlaywrightNodeShim",
            "bin",
            "Debug",
            "net10.0",
            OperatingSystem.IsWindows() ? "OpenClawNet.PlaywrightNodeShim.exe" : "OpenClawNet.PlaywrightNodeShim");

        return File.Exists(shimPath) ? shimPath : null;
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenClawNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool CanExecuteNode(string nodePath)
    {
        if (!File.Exists(nodePath))
        {
            return false;
        }

        try
        {
            var probe = StartProcess(nodePath, "--version");
            return probe.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void UnblockExecutables(string playwrightRoot)
    {
        var command = $"Get-ChildItem -LiteralPath '{EscapeSingleQuotes(playwrightRoot)}' -Recurse -Filter '*.exe' | Unblock-File";
        var probe = StartProcess(
            "powershell.exe",
            $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"");

        if (probe.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to unblock Playwright binaries under '{playwrightRoot}'. ExitCode={probe.ExitCode}. Stdout={probe.Stdout} Stderr={probe.Stderr}");
        }
    }

    private static ProcessResult StartProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new InvalidOperationException(
                $"Timed out waiting for process '{fileName} {arguments}' to exit.");
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string EscapeSingleQuotes(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);
}
