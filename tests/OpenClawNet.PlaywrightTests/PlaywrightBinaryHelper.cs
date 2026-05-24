using System.Diagnostics;

namespace OpenClawNet.PlaywrightTests;

internal static class PlaywrightBinaryHelper
{
    internal static void UnblockPlaywrightBinaries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var playwrightRoot = Path.Combine(AppContext.BaseDirectory, ".playwright");
        if (!Directory.Exists(playwrightRoot))
        {
            return;
        }

        var command = $"Get-ChildItem -LiteralPath '{EscapeSingleQuotes(playwrightRoot)}' -Recurse -Filter '*.exe' | Unblock-File";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell for Playwright binary unblocking.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to unblock Playwright binaries under '{playwrightRoot}'. ExitCode={process.ExitCode}. Stdout={stdout} Stderr={stderr}");
        }
    }

    private static string EscapeSingleQuotes(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
