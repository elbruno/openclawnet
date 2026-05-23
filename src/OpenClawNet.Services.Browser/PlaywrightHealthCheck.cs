using System.ComponentModel;
using Microsoft.Playwright;

namespace OpenClawNet.Services.Browser;

/// <summary>
/// Health check for Playwright binaries on application startup.
/// Detects Windows Defender/Mark of the Web blocking issues early.
/// Related to GitHub issue #92: Playwright binary blocking
/// </summary>
public static class PlaywrightHealthCheck
{
    public static async Task<bool> CheckPlaywrightBinariesAsync(ILogger logger)
    {
        try
        {
            logger.LogInformation("Running Playwright binary health check...");
            
            // Try to create Playwright instance to verify binaries are accessible
            using var playwright = await Playwright.CreateAsync();
            logger.LogInformation("Playwright binaries health check passed");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            logger.LogWarning(
                ex, 
                "⚠️ PLAYWRIGHT BINARY BLOCKED (Win32Exception code 5)\n" +
                "Windows Defender or Mark of the Web is blocking Playwright binaries.\n" +
                "Execute this PowerShell command to fix:\n" +
                "  Get-ChildItem -Recurse '.playwright' -Filter '*.exe' | Unblock-File; " +
                "Get-ChildItem -Recurse '.playwright' -Filter '*.dll' | Unblock-File\n" +
                "See: https://github.com/elbruno/openclawnet/issues/92"
            );
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Playwright binary health check failed with error: {Message}", ex.Message);
            return false;
        }
    }
}
