[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$Headed,
    [string]$ResultsDirectory = "TestResults",
    [string]$DashboardDirectory = "docs\test-dashboard",
    [string]$CatalogPath = "tests\catalog.yaml",
    [string]$RunsPath = "tests\runs.jsonl",
    [string]$RunsIndexPath = "tests\runs-index.json",
    [string]$PreamblePath = "tests\index.preamble.md",
    [string]$IndexOutputPath = "docs\testing\e2e-test-index.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$publishArgs = @{
    ResultsDirectory = $ResultsDirectory
    DashboardDirectory = $DashboardDirectory
    CatalogPath = $CatalogPath
    RunsPath = $RunsPath
    RunsIndexPath = $RunsIndexPath
    RecordRuns = $true
}

if ($SkipTests) {
    $publishArgs.SkipTests = $true
}

if ($Headed) {
    $publishArgs.Headed = $true
}

& (Join-Path $PSScriptRoot "publish-test-dashboard.ps1") @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "publish-test-dashboard.ps1 failed."
}

& (Join-Path $PSScriptRoot "render-test-index.ps1") `
    -CatalogPath $CatalogPath `
    -RunsIndexPath $RunsIndexPath `
    -PreamblePath $PreamblePath `
    -OutputPath $IndexOutputPath

if ($LASTEXITCODE -ne 0) {
    throw "render-test-index.ps1 failed."
}

& (Join-Path $PSScriptRoot "guard-test-index.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "guard-test-index.ps1 failed."
}

Write-Host "`nTest and publish pipeline complete." -ForegroundColor Green
