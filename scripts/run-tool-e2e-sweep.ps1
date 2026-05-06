#!/usr/bin/env pwsh
# Tool E2E Test Sweep Script
# Runs all Tool Matrix E2E tests with Azure OpenAI configured

param(
    [string]$LogDir = ""
)

# Generate timestamp
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
if ([string]::IsNullOrEmpty($LogDir)) {
    $LogDir = "TestResults\tool-e2e-sweep-$timestamp"
}

# Create log directory
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

Write-Host "=== Tool E2E Test Sweep ===" -ForegroundColor Green
Write-Host "Timestamp: $timestamp"
Write-Host "Log directory: $LogDir"
Write-Host ""

# Load Azure OpenAI secrets — skip if already set (CI mode)
Write-Host "Loading Azure OpenAI configuration..." -ForegroundColor Cyan
if ([string]::IsNullOrEmpty($env:AZURE_OPENAI_ENDPOINT)) {
    Write-Host "  Loading from user-secrets (local mode)..." -ForegroundColor Gray
    Push-Location "src\OpenClawNet.Gateway"
    $secrets = dotnet user-secrets list 2>$null
    $env:AZURE_OPENAI_ENDPOINT   = ($secrets | Select-String "Model:Endpoint = (.*)").Matches[0].Groups[1].Value
    $env:AZURE_OPENAI_DEPLOYMENT = ($secrets | Select-String "Model:DeploymentName = (.*)").Matches[0].Groups[1].Value
    $env:AZURE_OPENAI_API_KEY    = ($secrets | Select-String "Model:ApiKey = (.*)").Matches[0].Groups[1].Value
    Pop-Location
} else {
    Write-Host "  Using pre-populated environment variables (CI mode)..." -ForegroundColor Gray
}

# Set test configuration
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"
$env:PLAYWRIGHT_HEADED = "false"  # Headless for sweep

# Verify configuration
if ([string]::IsNullOrEmpty($env:AZURE_OPENAI_ENDPOINT)) {
    Write-Host "ERROR: Azure OpenAI endpoint not configured!" -ForegroundColor Red
    exit 1
}

Write-Host "Configuration:" -ForegroundColor Green
Write-Host "  Endpoint: $env:AZURE_OPENAI_ENDPOINT"
Write-Host "  Deployment: $env:AZURE_OPENAI_DEPLOYMENT"
Write-Host "  Headed: $env:PLAYWRIGHT_HEADED"
Write-Host ""

# Run all Tool Matrix E2E tests
Write-Host "Running all Tool Matrix E2E tests..." -ForegroundColor Cyan
$startTime = Get-Date

dotnet test tests\OpenClawNet.PlaywrightTests\OpenClawNet.PlaywrightTests.csproj `
  --filter "FullyQualifiedName~ToolMatrixE2ETests" `
  --logger "console;verbosity=detailed" --nologo *> "$LogDir\all-tests.log" 2>&1

$exitCode = $LASTEXITCODE
$duration = (Get-Date) - $startTime

# Parse results from log
$logContent = Get-Content "$LogDir\all-tests.log" -Raw
$totalTests = if ($logContent -match "Total tests:\s+(\d+)") { $Matches[1] } else { "?" }
$passed = if ($logContent -match "Passed:\s+(\d+)") { $Matches[1] } else { "0" }
$failed = if ($logContent -match "Failed:\s+(\d+)") { $Matches[1] } else { "0" }
$skipped = if ($logContent -match "Skipped:\s+(\d+)") { $Matches[1] } else { "0" }

# Output summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TEST SWEEP COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Total tests: $totalTests"
Write-Host "  Passed:  $passed" -ForegroundColor $(if ([int]$passed -gt 0) { "Green" } else { "Gray" })
Write-Host "  Failed:  $failed" -ForegroundColor $(if ([int]$failed -gt 0) { "Red" } else { "Gray" })
Write-Host "  Skipped: $skipped" -ForegroundColor $(if ([int]$skipped -gt 0) { "Yellow" } else { "Gray" })
Write-Host "Duration: $([math]::Round($duration.TotalMinutes, 1)) minutes"
Write-Host "Exit code: $exitCode"
Write-Host "========================================" -ForegroundColor Cyan

exit $exitCode
