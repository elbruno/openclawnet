# ==============================================================================
# WARNING: This script is a LAST RESORT for cleaning up orphaned Aspire
# processes that have locked OpenClawNet.ServiceDefaults.dll.
# 
# ALWAYS use `aspire stop` to gracefully shut down Aspire (NEVER Ctrl+C).
# Only use this script if aspire stop has failed or been skipped.
# ==============================================================================

param(
    [switch]$Force
)

function Get-AspireProcesses {
    <#
    .SYNOPSIS
    Identifies dotnet/Aspire processes that are likely Aspire AppHost instances.
    
    .DESCRIPTION
    Searches for processes matching:
    - Process name: dotnet.exe or aspire*.exe
    - Command line contains: AppHost.dll or Aspire.Hosting
    
    .OUTPUTS
    System.Diagnostics.Process objects with matching criteria.
    #>
    
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $proc = $_
        
        # Check process name
        $isRelevantProcess = ($proc.ProcessName -eq 'dotnet') -or ($proc.ProcessName -match 'aspire')
        
        if (-not $isRelevantProcess) { return $false }
        
        # Check command line for Aspire signatures
        try {
            $cmdLine = (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | 
                Select-Object -ExpandProperty CommandLine -ErrorAction SilentlyContinue) -as [string]
            
            if ([string]::IsNullOrEmpty($cmdLine)) { return $false }
            
            return ($cmdLine -like '*AppHost.dll*') -or ($cmdLine -like '*Aspire.Hosting*')
        }
        catch {
            return $false
        }
    }
}

function Format-ProcessInfo {
    <#
    .SYNOPSIS
    Formats process info for display (PID, memory, command-line snippet).
    #>
    param([System.Diagnostics.Process]$Process)
    
    $cmdLine = ""
    try {
        $cmdLine = Get-Process -Id $Process.Id -ErrorAction SilentlyContinue | 
            Select-Object -ExpandProperty CommandLine -ErrorAction SilentlyContinue
    }
    catch {}
    
    # Truncate command line to 80 chars for readability
    $cmdLineShort = if ($cmdLine -and $cmdLine.Length -gt 80) {
        "$($cmdLine.Substring(0, 77))..."
    } else {
        $cmdLine
    }
    
    @{
        PID        = $Process.Id
        Memory_MB  = [math]::Round($Process.WorkingSet64 / 1MB, 2)
        CommandLine = $cmdLineShort
    }
}

# ============================================================================
# MAIN
# ============================================================================

Write-Host "🔍 Scanning for orphaned Aspire processes..." -ForegroundColor Cyan

$processes = @(Get-AspireProcesses)

if ($processes.Count -eq 0) {
    Write-Host "✅ No orphaned Aspire processes detected." -ForegroundColor Green
    exit 0
}

Write-Host "⚠️  Found $($processes.Count) candidate Aspire process(es):" -ForegroundColor Yellow
Write-Host ""

# Display table
$table = $processes | ForEach-Object { Format-ProcessInfo $_ }
$table | Format-Table -AutoSize | Out-Host

Write-Host ""

if (-not $Force) {
    Write-Host "ℹ️  To kill these processes, re-run with: .\kill-orphaned-aspire.ps1 -Force" -ForegroundColor Cyan
    exit 0
}

# Kill each process explicitly by PID
Write-Host "💀 Force-killing $($processes.Count) process(es)..." -ForegroundColor Red
Write-Host ""

foreach ($proc in $processes) {
    try {
        Write-Host "  Killing PID $($proc.Id) (WorkingSet: $([math]::Round($proc.WorkingSet64 / 1MB, 2)) MB)" -ForegroundColor Yellow
        Stop-Process -Id $proc.Id -Force -ErrorAction Stop
        Write-Host "    ✅ Killed." -ForegroundColor Green
    }
    catch {
        Write-Host "    ❌ Failed to kill: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "🔄 Next steps:" -ForegroundColor Cyan
Write-Host "  1. Retry your build: dotnet build src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj --verbosity quiet" -ForegroundColor White
Write-Host "  2. If still stuck, check Process Explorer or Resource Monitor for additional locks." -ForegroundColor White
Write-Host "  3. Remember: ALWAYS use 'aspire stop' next time (never Ctrl+C)." -ForegroundColor White
