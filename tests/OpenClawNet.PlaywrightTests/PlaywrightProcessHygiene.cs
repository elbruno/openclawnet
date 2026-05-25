using System.Diagnostics;

namespace OpenClawNet.PlaywrightTests;

public static class PlaywrightProcessHygiene
{
    public static int CleanupOrphanedPlaywrightNodeProcesses(DateTime? startedAtUtc = null, Action<string>? log = null)
    {
        var cleaned = 0;
        foreach (var nodeProcess in Process.GetProcessesByName("node"))
        {
            try
            {
                if (startedAtUtc.HasValue)
                {
                    var startTimeUtc = nodeProcess.StartTime.ToUniversalTime();
                    if (startTimeUtc < startedAtUtc.Value.AddSeconds(-5))
                    {
                        continue;
                    }
                }

                var executablePath = nodeProcess.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    continue;
                }

                var normalizedPath = executablePath.Replace('/', '\\');
                if (!normalizedPath.Contains(@"\playwright-driver-cache\", StringComparison.OrdinalIgnoreCase) &&
                    !normalizedPath.Contains(@"\.playwright\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                nodeProcess.Kill(entireProcessTree: true);
                cleaned++;
            }
            catch
            {
                // Ignore processes we cannot inspect or terminate.
            }
            finally
            {
                nodeProcess.Dispose();
            }
        }

        if (cleaned > 0)
        {
            log?.Invoke($"[PlaywrightProcessHygiene] Cleaned up {cleaned} lingering Playwright node process(es).");
        }

        return cleaned;
    }
}
