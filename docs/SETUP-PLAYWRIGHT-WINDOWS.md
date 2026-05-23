# Playwright on Windows - Setup Guide

## Issue: Playwright Binary Blocking (Windows Defender / Mark of the Web)

GitHub Issue: [#92 - Playwright binary blocking](https://github.com/elbruno/openclawnet/issues/92)

### Problem

On Windows systems, the Windows Defender SmartScreen or "Mark of the Web" security feature can block the execution of Playwright binaries, resulting in:

```
Win32Exception (5): Access Denied
```

This happens when:
- Playwright `.exe` or `.dll` files are downloaded but not properly unblocked
- The files are marked as "coming from the internet"
- Windows security policies block execution

### Symptoms

When Playwright binaries are blocked, you may see:
- Browser actions fail with `Win32Exception` (code 5)
- Error message: "Access Denied" when trying to launch Chromium
- Browser endpoints return errors mentioning blocked binaries

### Solution

#### Immediate Fix (for Local Development)

Run the following PowerShell command in the repository root to unblock all Playwright binaries:

```powershell
Get-ChildItem -Recurse '.playwright' -Filter '*.exe' | Unblock-File; Get-ChildItem -Recurse '.playwright' -Filter '*.dll' | Unblock-File
```

**Note:** You may need to run PowerShell as Administrator if you encounter "Access Denied" when unblocking files.

#### Verification

After running the unblock command:
1. Restart the application or service
2. Test browser functionality (navigate, screenshot, etc.)
3. Monitor logs for the startup health check message confirming binaries are accessible

### For CI/CD Environments

If you're running OpenClaw.NET in CI/CD pipelines on Windows:

1. Add the unblock command to your build/setup script **before** running the application
2. Example GitHub Actions workflow step:
   ```yaml
   - name: Unblock Playwright binaries
     run: Get-ChildItem -Recurse '.playwright' -Filter '*.exe' | Unblock-File; Get-ChildItem -Recurse '.playwright' -Filter '*.dll' | Unblock-File
     shell: pwsh
   ```

### Technical Details

#### Why This Happens

When files are downloaded from the internet (especially through package managers like npm or through NuGet), Windows adds a NTFS alternate data stream (ADS) called the "Zone Identifier" that marks them as potentially unsafe. The `Unblock-File` PowerShell cmdlet removes this stream.

#### What the Code Does

The `OpenClawNet.Services.Browser` service includes:

1. **Enhanced Error Handling** (`BrowserEndpoints.cs`):
   - Catches `Win32Exception` with code 5 specifically
   - Provides helpful error messages with the unblock command
   - Guides users to this documentation

2. **Startup Health Check** (`PlaywrightHealthCheck.cs`):
   - Runs at application startup
   - Tests if Playwright binaries are accessible
   - Logs warnings with unblock instructions if blocked
   - Prevents silent failures later in operation

### Prevention

To prevent this issue when setting up a new Windows development environment:

1. Clone the repository
2. Run the unblock command immediately after initial setup
3. The startup health check will verify everything is working

### Additional Resources

- [Microsoft Docs: Unblock-File](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/unblock-file)
- [Playwright Browser Binaries](https://playwright.dev/dotnet/docs/intro)
- [Windows SmartScreen Information](https://support.microsoft.com/en-us/windows/windows-defender-smartscreen-faq-36e4d4b9-e681-4e6e-8b0c-11112a7c5ee9)

---

**Last Updated**: 2025  
**Related Issue**: GitHub #92 - Playwright binary blocking
