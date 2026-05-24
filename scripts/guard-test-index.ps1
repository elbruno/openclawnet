[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$trackedSources = @(
    'tests/catalog.yaml',
    'tests/runs.jsonl',
    'tests/runs-index.json',
    'tests/index.preamble.md',
    'scripts/render-test-index.ps1',
    'scripts/record-test-run.ps1'
)

function Get-ChangedFiles {
    $working = @()
    $staged = @()

    try {
        $working = @(& git diff --name-only 2>$null | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $staged = @(& git diff --cached --name-only 2>$null | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    catch {
    }

    $combined = @($working + $staged | Sort-Object -Unique)
    if ($combined.Count -gt 0) {
        return $combined
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_BASE_REF)) {
        & git fetch --no-tags --depth=1 origin $env:GITHUB_BASE_REF *> $null
        $baseRef = "origin/$($env:GITHUB_BASE_REF)"
        return @(& git diff --name-only "$baseRef...HEAD" 2>$null | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    return @(& git diff-tree --no-commit-id --name-only -r HEAD 2>$null | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$changedFiles = Get-ChangedFiles
if ($changedFiles.Count -eq 0) {
    Write-Host "No changed files detected for test index guard." -ForegroundColor Green
    exit 0
}

$indexPath = 'docs/testing/e2e-test-index.md'
if ($changedFiles -notcontains $indexPath) {
    Write-Host "e2e-test-index.md not changed; guard passed." -ForegroundColor Green
    exit 0
}

$hasCanonicalSourceChange = $false
foreach ($path in $trackedSources) {
    if ($changedFiles -contains $path) {
        $hasCanonicalSourceChange = $true
        break
    }
}

if (-not $hasCanonicalSourceChange) {
    throw "docs/testing/e2e-test-index.md changed without a canonical source update. Update tests/catalog.yaml or tests/runs.jsonl, then regenerate with scripts/test-and-publish.ps1."
}

Write-Host "Test index guard passed." -ForegroundColor Green
