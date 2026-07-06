[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory = "_site",
    [string]$LandingDirectory = "docs\landing",
    [string]$FallbackLandingFile = "index.html",
    [string]$DashboardDirectory = "docs\test-dashboard",
    [string]$PublishedDashboardDirectory = "test-dashboard"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepoPath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $RepositoryRoot $Path
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

$resolvedOutputDirectory = Resolve-RepoPath $OutputDirectory
$resolvedLandingDirectory = Resolve-RepoPath $LandingDirectory
$resolvedFallbackLandingFile = Resolve-RepoPath $FallbackLandingFile
$resolvedDashboardDirectory = Resolve-RepoPath $DashboardDirectory

if (Test-Path $resolvedOutputDirectory) {
    Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

if (Test-Path $resolvedLandingDirectory) {
    Copy-DirectoryContents -Source $resolvedLandingDirectory -Destination $resolvedOutputDirectory
}
elseif (Test-Path $resolvedFallbackLandingFile) {
    Copy-Item -LiteralPath $resolvedFallbackLandingFile -Destination (Join-Path $resolvedOutputDirectory "index.html") -Force
}
else {
    throw "Could not find landing page content in '$LandingDirectory' or fallback file '$FallbackLandingFile'."
}

if (-not (Test-Path $resolvedDashboardDirectory)) {
    throw "Dashboard source directory not found: $resolvedDashboardDirectory"
}

$resolvedPublishedDashboardDirectory = Join-Path $resolvedOutputDirectory $PublishedDashboardDirectory
Copy-DirectoryContents -Source $resolvedDashboardDirectory -Destination $resolvedPublishedDashboardDirectory

foreach ($optionalRootFile in @(".nojekyll", "CNAME")) {
    $sourcePath = Join-Path $RepositoryRoot $optionalRootFile
    if (Test-Path $sourcePath) {
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $resolvedOutputDirectory $optionalRootFile) -Force
    }
}

Write-Host "GitHub Pages artifact staged at '$resolvedOutputDirectory'." -ForegroundColor Green
