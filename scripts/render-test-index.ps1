[CmdletBinding()]
param(
    [string]$CatalogPath = "tests\catalog.yaml",
    [string]$RunsIndexPath = "tests\runs-index.json",
    [string]$PreamblePath = "tests\index.preamble.md",
    [string]$OutputPath = "docs\testing\e2e-test-index.md",
    [string]$PostambleStartHeading = "## Running the full E2E sweep"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent

function Resolve-RepoPath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function ConvertFrom-YamlScalar {
    param([AllowNull()] [string]$Value)

    if ($null -eq $Value) {
        return $null
    }

    $trimmed = $Value.Trim()
    if ($trimmed -eq '') {
        return ''
    }

    if ($trimmed -eq 'null') {
        return $null
    }

    if ($trimmed -eq 'true') {
        return $true
    }

    if ($trimmed -eq 'false') {
        return $false
    }

    if (($trimmed.StartsWith("'") -and $trimmed.EndsWith("'")) -or
        ($trimmed.StartsWith('"') -and $trimmed.EndsWith('"'))) {
        $unwrapped = $trimmed.Substring(1, $trimmed.Length - 2)
        if ($trimmed.StartsWith("'")) {
            return $unwrapped -replace "''", "'"
        }

        return $unwrapped
    }

    return $trimmed
}

function Import-Catalog {
    param([Parameter(Mandatory)] [string]$Path)

    if (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue) {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Yaml
    }

    $catalog = [ordered]@{
        suites = @()
        tests = @()
    }
    $section = $null
    $current = $null
    $currentListProperty = $null

    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $line = $rawLine.TrimEnd()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) {
            continue
        }

        if ($line -match '^(?<section>suites|tests):\s*$') {
            if ($null -ne $current -and $section) {
                $catalog[$section] += ,([pscustomobject]$current)
            }

            $section = $Matches.section
            $current = $null
            $currentListProperty = $null
            continue
        }

        if (-not $section) {
            continue
        }

        if ($line -match '^  -\s*(?<content>.*)$') {
            if ($null -ne $current) {
                $catalog[$section] += ,([pscustomobject]$current)
            }

            $current = [ordered]@{}
            $currentListProperty = $null
            $content = $Matches.content.Trim()
            if ($content -match '^(?<key>[A-Za-z][A-Za-z0-9_]*)\:\s*(?<value>.*)$') {
                $current[$Matches.key] = ConvertFrom-YamlScalar $Matches.value
            }
            continue
        }

        if ($line -match '^    (?<key>[A-Za-z][A-Za-z0-9_]*)\:\s*(?<value>.*)$') {
            $value = $Matches.value
            if ($value -eq '') {
                $current[$Matches.key] = @()
                $currentListProperty = $Matches.key
            }
            else {
                $current[$Matches.key] = ConvertFrom-YamlScalar $value
                $currentListProperty = $null
            }
            continue
        }

        if ($line -match '^      -\s*(?<value>.*)$' -and $currentListProperty) {
            $current[$currentListProperty] += ,(ConvertFrom-YamlScalar $Matches.value)
        }
    }

    if ($null -ne $current -and $section) {
        $catalog[$section] += ,([pscustomobject]$current)
    }

    return [pscustomobject]$catalog
}

function Get-OptionalPropertyValue {
    param(
        [Parameter(Mandatory)] $Object,
        [Parameter(Mandatory)] [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-RunsIndex {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return @{}
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
}

function Get-SuiteFilterHint {
    param([Parameter(Mandatory)] $Suite)

    switch ($Suite.id) {
        'playwright' {
            return 'Filter: `dotnet test tests\OpenClawNet.PlaywrightTests --filter "Category=E2E"` (or `Category=ToolApproval`, `Category=RequiresModel`).'
        }
        'gateway-e2e' {
            return 'Filter: `dotnet test tests\OpenClawNet.E2ETests --filter "Category=Live"` for live Azure OpenAI tests; zero tests run by default.'
        }
        'integration' {
            return 'Filter: `dotnet test tests\OpenClawNet.IntegrationTests` (default, no Live filter needed for most).'
        }
        'unit' {
            return 'Filter: `dotnet test tests\OpenClawNet.UnitTests --filter "FullyQualifiedName~ModelClientChatClientAdapterTests"`.'
        }
        'azure-unit' {
            return 'Filter: `dotnet test tests\OpenClawNet.UnitTests.Azure --filter "Category=Live"`.'
        }
        default {
            if (-not [string]::IsNullOrWhiteSpace($Suite.filter)) {
                return "Filter: `dotnet test $($Suite.project) --filter ""$($Suite.filter)""`."
            }

            return "Filter: `dotnet test $($Suite.project)`."
        }
    }
}

function Get-TestDisplayName {
    param([Parameter(Mandatory)] $Test)

    $methodName = Get-OptionalPropertyValue -Object $Test -Name 'methodName'
    if ([string]::IsNullOrWhiteSpace($methodName)) {
        return $Test.className
    }

    return "$($Test.className) - ``$methodName``"
}

function Get-TestLinkTarget {
    param([Parameter(Mandatory)] $Test)

    return "../../$($Test.file -replace '\\', '/')"
}

function Get-OutcomeLabel {
    param([AllowNull()] [string]$Outcome)

    switch ($Outcome) {
        'pass' { return 'PASS' }
        'fail' { return 'FAIL' }
        'skip' { return 'SKIP' }
        'notrun' { return 'Not recorded' }
        default {
            if ([string]::IsNullOrWhiteSpace($Outcome)) {
                return 'Not recorded'
            }

            return $Outcome.ToUpperInvariant()
        }
    }
}

function Convert-ToMarkdownCell {
    param([AllowNull()] [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }

    $normalized = ($Value -replace '\r?\n', ' ') -replace '\s+', ' '
    return $normalized.Trim() -replace '\|', '\|'
}

function Get-ExistingPostamble {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$StartHeading
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $startIndex = $content.IndexOf($StartHeading, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0) {
        return $null
    }

    return $content.Substring($startIndex).Trim()
}

function Update-KeepingIndexSection {
    param([AllowNull()] [string]$Postamble)

    if ([string]::IsNullOrWhiteSpace($Postamble)) {
        return $Postamble
    }

    $heading = '## Keeping this index up to date'
    $startIndex = $Postamble.IndexOf($heading, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0) {
        return $Postamble
    }

    $nextSectionIndex = $Postamble.IndexOf([Environment]::NewLine + '### ', $startIndex, [System.StringComparison]::Ordinal)
    if ($nextSectionIndex -lt 0) {
        $nextSectionIndex = $Postamble.Length
    }

    $replacementLines = @(
        '## Keeping this index up to date',
        '',
        'After every test run, run `scripts\test-and-publish.ps1` so run recording, this generated index, and dashboard outputs stay aligned in one change.  ',
        'When adding or renaming an E2E/integration test, update `tests\catalog.yaml` so the generated suite tables stay in sync with the repository.  ',
        'The sync workflow mirrors `docs\test-dashboard\` from the plan repo to the public site after updates.'
    )

    $replacement = ($replacementLines -join [Environment]::NewLine).TrimEnd()
    $prefix = $Postamble.Substring(0, $startIndex).TrimEnd()
    $suffix = if ($nextSectionIndex -lt $Postamble.Length) { $Postamble.Substring($nextSectionIndex).TrimStart() } else { '' }

    if ([string]::IsNullOrWhiteSpace($prefix)) {
        if ([string]::IsNullOrWhiteSpace($suffix)) {
            return $replacement
        }

        return ($replacement, '', $suffix) -join [Environment]::NewLine
    }

    if ([string]::IsNullOrWhiteSpace($suffix)) {
        return ($prefix, '', $replacement) -join [Environment]::NewLine
    }

    return ($prefix, '', $replacement, '', $suffix) -join [Environment]::NewLine
}

$catalog = Import-Catalog -Path (Resolve-RepoPath $CatalogPath)
$runsIndex = Get-RunsIndex -Path (Resolve-RepoPath $RunsIndexPath)
$preamble = (Get-Content -LiteralPath (Resolve-RepoPath $PreamblePath) -Raw).Trim()
$outputFile = Resolve-RepoPath $OutputPath
$postamble = Get-ExistingPostamble -Path $outputFile -StartHeading $PostambleStartHeading
$postamble = Update-KeepingIndexSection -Postamble $postamble

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('<!-- GENERATED FILE. Run scripts\render-test-index.ps1 after updating tests\catalog.yaml or tests\runs.jsonl. Do not hand-edit the generated suite tables below. -->')
$lines.Add('')
$lines.Add($preamble)
$lines.Add('')

for ($i = 0; $i -lt @($catalog.suites).Count; $i++) {
    $suite = @($catalog.suites)[$i]
    $suiteTests = @($catalog.tests | Where-Object { $_.suite -eq $suite.id })

    $lines.Add("## $($i + 1). $($suite.label) -- ``$($suite.project)``")
    $lines.Add('')
    $lines.Add("$($suite.description)  ")
    $lines.Add((Get-SuiteFilterHint -Suite $suite))
    $lines.Add('')
    $lines.Add('| Test / Class | What it proves | Last run | Result | Notes |')
    $lines.Add('|---|---|---|---|---|')

    foreach ($test in $suiteTests) {
        $run = $null
        if ($runsIndex.ContainsKey($test.id)) {
            $run = $runsIndex[$test.id]
        }

        $lastDate = if ($null -ne $run -and -not [string]::IsNullOrWhiteSpace($run.lastDate)) { [string]$run.lastDate } else { '-' }
        $result = if ($null -ne $run) { Get-OutcomeLabel -Outcome ([string]$run.outcome) } else { 'Not recorded' }
        $defaultNotes = Get-OptionalPropertyValue -Object $test -Name 'defaultNotes'
        $notes = if ($null -ne $run -and -not [string]::IsNullOrWhiteSpace($run.notes)) { [string]$run.notes } else { [string]$defaultNotes }
        $displayName = Get-TestDisplayName -Test $test
        $link = Get-TestLinkTarget -Test $test

        $lines.Add("| [$displayName]($link) | $(Convert-ToMarkdownCell $test.proves) | $lastDate | $result | $(Convert-ToMarkdownCell $notes) |")
    }

    $lines.Add('')
    $lines.Add('---')
    $lines.Add('')
}

if (-not [string]::IsNullOrWhiteSpace($postamble)) {
    $lines.Add($postamble)
}

$parent = Split-Path $outputFile -Parent
if (-not (Test-Path $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

($lines -join [Environment]::NewLine).TrimEnd() + [Environment]::NewLine |
    Set-Content -LiteralPath $outputFile -Encoding utf8

Write-Host "Rendered $(Resolve-Path -LiteralPath $outputFile | ForEach-Object { $_.Path })" -ForegroundColor Green
