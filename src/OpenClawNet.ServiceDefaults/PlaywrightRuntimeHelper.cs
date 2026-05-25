using System.ComponentModel;
using System.Diagnostics;

namespace OpenClawNet.ServiceDefaults;

public static class PlaywrightRuntimeHelper
{
    private const string RelativeNodePath = @"node\win32_x64\node.exe";
    private const string RelativePackagePath = @"package";
    private const string PlaywrightNodePathVariable = "PLAYWRIGHT_NODEJS_PATH";
    private const string PlaywrightDriverSearchPathVariable = "PLAYWRIGHT_DRIVER_SEARCH_PATH";
    private const string SystemNodePathVariable = "OPENCLAWNET_PLAYWRIGHT_SYSTEM_NODE";

    public static void PrepareForCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        PrepareForWindowsBaseDirectory(AppContext.BaseDirectory);
    }

    internal static void PrepareForWindowsBaseDirectory(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var systemNodePath = ResolveSystemNodePath();
        PrepareForWindowsBaseDirectory(baseDirectory, systemNodePath);
    }

    internal static void PrepareForWindowsBaseDirectory(string baseDirectory, string systemNodePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemNodePath);

        if (!CanExecuteNode(systemNodePath))
        {
            throw new InvalidOperationException(
                $"System node.exe at '{systemNodePath}' is not executable for Playwright runtime setup.");
        }

        ConfigureNodeLaunchPath(systemNodePath);

        var playwrightRoot = Path.Combine(baseDirectory, ".playwright");
        ConfigureDriverSearchPath(playwrightRoot);
        DeleteRepoLocalNodeRuntime(playwrightRoot);
    }

    private static void ConfigureNodeLaunchPath(string systemNodePath)
    {
        Environment.SetEnvironmentVariable(PlaywrightNodePathVariable, systemNodePath, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(SystemNodePathVariable, systemNodePath, EnvironmentVariableTarget.Process);
    }

    private static void ConfigureDriverSearchPath(string playwrightRoot)
    {
        var sourcePackageDirectory = Path.Combine(playwrightRoot, RelativePackagePath);
        var cliEntrypoint = Path.Combine(sourcePackageDirectory, "cli.js");
        if (!File.Exists(cliEntrypoint))
        {
            Environment.SetEnvironmentVariable(PlaywrightDriverSearchPathVariable, null, EnvironmentVariableTarget.Process);
            return;
        }

        var cacheRoot = Path.Combine(
            Path.GetTempPath(),
            "OpenClawNet",
            "playwright-driver-cache",
            ComputeDriverCacheKey(cliEntrypoint));
        var targetPackageDirectory = Path.Combine(cacheRoot, ".playwright", RelativePackagePath);

        CopyPackageDirectory(sourcePackageDirectory, targetPackageDirectory);
        Environment.SetEnvironmentVariable(PlaywrightDriverSearchPathVariable, cacheRoot, EnvironmentVariableTarget.Process);
    }

    private static void DeleteRepoLocalNodeRuntime(string playwrightRoot)
    {
        if (!Directory.Exists(playwrightRoot))
        {
            return;
        }

        var targetNodePath = Path.Combine(playwrightRoot, RelativeNodePath);
        if (!File.Exists(targetNodePath))
        {
            return;
        }

        try
        {
            File.Delete(targetNodePath);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Failed to delete Playwright node runtime under '{targetNodePath}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Failed to delete Playwright node runtime under '{targetNodePath}'.", ex);
        }

        DeleteDirectoryIfEmpty(Path.GetDirectoryName(targetNodePath));
        DeleteDirectoryIfEmpty(Path.Combine(playwrightRoot, "node"));
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

    private static string ComputeDriverCacheKey(string cliEntrypoint)
    {
        var cliInfo = new FileInfo(cliEntrypoint);
        var input = $"{cliInfo.FullName}|{cliInfo.Length}|{cliInfo.LastWriteTimeUtc.Ticks}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static void CopyPackageDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            var targetParent = Path.GetDirectoryName(targetFile)
                ?? throw new InvalidOperationException($"Could not determine target directory for '{targetFile}'.");
            Directory.CreateDirectory(targetParent);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    private static void DeleteDirectoryIfEmpty(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            return;
        }

        Directory.Delete(directoryPath);
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

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);
}
