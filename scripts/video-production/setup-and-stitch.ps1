<#
.SYNOPSIS
    Validate video production environment and guide setup if needed.
    
.DESCRIPTION
    Checks for ffmpeg availability and provides guidance on installation.
    If ffmpeg is found, runs the stitching script. Otherwise, displays
    setup instructions.
    
.PARAMETER SkipSetupCheck
    If true, assume ffmpeg is already set up and skip validation.
#>

param(
    [switch]$SkipSetupCheck
)

$ErrorActionPreference = "Stop"

function Test-Ffmpeg {
    if ($env:FFMPEG_PATH -and (Test-Path $env:FFMPEG_PATH)) {
        return $true
    }
    
    $result = Get-Command ffmpeg -ErrorAction SilentlyContinue
    return $null -ne $result
}

Write-Host "OpenClawNet Video Production Setup" -ForegroundColor Cyan
Write-Host ""

if (-not $SkipSetupCheck) {
    if (Test-Ffmpeg) {
        Write-Host "✓ ffmpeg found" -ForegroundColor Green
        Write-Host ""
        Write-Host "Proceeding with video stitching..."
        Write-Host ""
    }
    else {
        Write-Host "⚠ ffmpeg not found in PATH" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To set up ffmpeg, see: scripts\video-production\FFMPEG_SETUP.md"
        Write-Host ""
        Write-Host "Quick start options:"
        Write-Host "  1. Download portable ffmpeg from https://www.gyan.dev/ffmpeg/builds/"
        Write-Host "  2. Set: `$env:FFMPEG_PATH = 'C:\path\to\ffmpeg.exe'"
        Write-Host "  3. Or install via Scoop: scoop install ffmpeg"
        Write-Host "  4. Or install via winget: winget install ffmpeg"
        Write-Host ""
        Write-Host "After installation, re-run this script."
        exit 1
    }
}

# Run the stitching script
$scriptDir = Split-Path $PSCommandPath -Parent
$stitchScript = Join-Path $scriptDir "stitch-video-1-skill-journey.ps1"

if (-not (Test-Path $stitchScript)) {
    Write-Error "Stitching script not found: $stitchScript"
    exit 1
}

# Change to scenario directory and run
$scenarioDir = "..\..\..\docs\testing\video-production\scenarios\video-1-skill-journey"
Push-Location $scenarioDir
try {
    & $stitchScript
}
finally {
    Pop-Location
}
