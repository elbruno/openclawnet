[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$Headed,
    [switch]$RecordRuns,
    [string]$ResultsDirectory = "TestResults",
    [string]$DashboardDirectory = "docs\test-dashboard",
    [string]$CatalogPath = "tests\catalog.yaml",
    [string]$RunsPath = "tests\runs.jsonl",
    [string]$RunsIndexPath = "tests\runs-index.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$resultsDir = Join-Path $repoRoot $ResultsDirectory
$dashboardDir = Join-Path $repoRoot $DashboardDirectory
$dashboardIndex = Join-Path $dashboardDir "index.html"
$reportPath = Join-Path $dashboardDir "summary.json"

function Invoke-TestSuite {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$TrxName,
        [string]$HtmlName,
        [string]$Filter,
        [switch]$ContinueOnFailure
    )

    $arguments = @(
        "test",
        $ProjectPath,
        "--nologo",
        "--tl:off",
        "--logger", "console;verbosity=detailed",
        "--logger", "trx;LogFileName=$TrxName",
        "--results-directory", $resultsDir
    )

    if ($HtmlName) {
        $arguments += @("--logger", "html;LogFileName=$HtmlName")
    }

    if ($Filter) {
        $arguments += @("--filter", $Filter)
    }

    Write-Host "`nRunning $Name..." -ForegroundColor Cyan
    & dotnet @arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Warning "$Name exited with code $exitCode."
        if (-not $ContinueOnFailure) {
            throw "$Name failed."
        }
    }
}

function Resolve-RepoPath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function ConvertTo-HashtableCompat {
    param([Parameter(ValueFromPipeline)] $InputObject)

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $table = @{}
        foreach ($key in $InputObject.Keys) {
            $table[$key] = ConvertTo-HashtableCompat $InputObject[$key]
        }

        return $table
    }

    if (($InputObject -is [System.Collections.IEnumerable]) -and -not ($InputObject -is [string])) {
        $items = @()
        foreach ($item in $InputObject) {
            $items += @(ConvertTo-HashtableCompat $item)
        }

        return $items
    }

    if ($InputObject -is [psobject]) {
        $properties = @($InputObject.PSObject.Properties)
        if ($properties.Count -gt 0) {
            $table = @{}
            foreach ($property in $properties) {
                $table[$property.Name] = ConvertTo-HashtableCompat $property.Value
            }

            return $table
        }
    }

    return $InputObject
}

function ConvertFrom-JsonCompat {
    param(
        [Parameter(Mandatory)] [string]$InputObject,
        [switch]$AsHashtable
    )

    $convertFromJson = Get-Command ConvertFrom-Json
    if ($convertFromJson.Parameters.ContainsKey('AsHashtable')) {
        if ($AsHashtable) {
            return $InputObject | ConvertFrom-Json -AsHashtable
        }

        return $InputObject | ConvertFrom-Json
    }

    $result = $InputObject | ConvertFrom-Json
    if ($AsHashtable) {
        return ConvertTo-HashtableCompat $result
    }

    return $result
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

function Get-RunsIndexData {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return @{}
    }

    return ConvertFrom-JsonCompat -InputObject (Get-Content -LiteralPath $Path -Raw) -AsHashtable
}

function Get-RunEntries {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return @()
    }

    return @(
        Get-Content -LiteralPath $Path |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { [pscustomobject](ConvertFrom-JsonCompat -InputObject $_ -AsHashtable) }
    )
}

function Get-SuiteArtifactMap {
    return @{
        'playwright'  = [ordered]@{ Trx = 'playwright-test-results.trx'; Html = 'dotnet-report.html' }
        'unit'        = [ordered]@{ Trx = 'unit-test-results.trx'; Html = $null }
        'integration' = [ordered]@{ Trx = 'integration-test-results.trx'; Html = $null }
        'gateway-e2e' = [ordered]@{ Trx = 'live-test-results.trx'; Html = $null }
        'azure-unit'  = [ordered]@{ Trx = $null; Html = $null }
    }
}

function Get-TestDisplayName {
    param([Parameter(Mandatory)] $Test)

    $methodName = Get-OptionalPropertyValue -Object $Test -Name 'methodName'
    if ([string]::IsNullOrWhiteSpace($methodName)) {
        return $Test.className
    }

    return "$($Test.className) :: $methodName"
}

function Get-RunSortKey {
    param([Parameter(Mandatory)] $Entry)

    $datePart = if ([string]::IsNullOrWhiteSpace([string]$Entry.runDate)) { '0000-00-00' } else { [string]$Entry.runDate }
    $runIdPart = if ([string]::IsNullOrWhiteSpace([string]$Entry.runId)) { '' } else { [string]$Entry.runId }
    return "$datePart|$runIdPart"
}

function Get-SparklineGlyph {
    param([int]$Percent)

    if ($Percent -lt 20) { return '.' }
    if ($Percent -lt 40) { return ':' }
    if ($Percent -lt 60) { return '=' }
    if ($Percent -lt 80) { return '+' }
    if ($Percent -lt 100) { return '*' }
    return '#'
}

function Format-HistorySummary {
    param([object[]]$RecentRuns)

    if ($RecentRuns.Count -eq 0) {
        return [ordered]@{
            Sparkline = 'n/a'
            Summary = 'No recorded runs yet.'
        }
    }

    $glyphs = foreach ($run in ($RecentRuns | Sort-Object RunDate, RunId)) {
        Get-SparklineGlyph -Percent $run.PassRatePercent
    }

    $latest = $RecentRuns | Sort-Object RunDate, RunId -Descending | Select-Object -First 1
    $summary = "{0} slice(s); latest {1} -> {2}% pass ({3}/{4})" -f `
        $RecentRuns.Count,
        $latest.RunDate,
        $latest.PassRatePercent,
        $latest.Passed,
        $latest.TotalRecorded

    return [ordered]@{
        Sparkline = -join $glyphs
        Summary = $summary
    }
}

function Format-Count {
    param([int]$Value)

    return "{0:N0}" -f $Value
}

function ConvertTo-HtmlEncoded {
    param([AllowNull()] [string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return [System.Net.WebUtility]::HtmlEncode($Value)
}

function New-SuiteSummary {
    param(
        [Parameter(Mandatory)] $Suite,
        [Parameter(Mandatory)] [object[]]$Tests,
        [Parameter(Mandatory)] [hashtable]$RunsIndex,
        [Parameter(Mandatory)] [object[]]$RunEntries,
        [Parameter(Mandatory)] [hashtable]$ArtifactMap
    )

    $latestResults = foreach ($test in $Tests) {
        $run = $null
        if ($RunsIndex.ContainsKey($test.id)) {
            $run = $RunsIndex[$test.id]
        }

        [pscustomobject]@{
            Test = $test
            Run = $run
        }
    }

    $passed = @($latestResults | Where-Object { $_.Run -and [string]$_.Run.outcome -eq 'pass' }).Count
    $failed = @($latestResults | Where-Object { $_.Run -and [string]$_.Run.outcome -eq 'fail' }).Count
    $skipped = @($latestResults | Where-Object { $_.Run -and [string]$_.Run.outcome -eq 'skip' }).Count
    $notRecorded = @($latestResults | Where-Object { -not $_.Run -or [string]$_.Run.outcome -eq 'notrun' }).Count
    $recorded = $Tests.Count - $notRecorded

    $latestRunDates = @(
        $latestResults |
            Where-Object { $_.Run -and -not [string]::IsNullOrWhiteSpace([string]$_.Run.lastDate) } |
            ForEach-Object { [string]$_.Run.lastDate }
    )
    $latestRunDate = if ($latestRunDates.Count -gt 0) {
        ($latestRunDates | Sort-Object -Descending | Select-Object -First 1)
    }
    else {
        $null
    }

    $suiteHistory = @(
        $RunEntries |
            Where-Object { [string]$_.suite -eq $Suite.id } |
            Group-Object runId |
            ForEach-Object {
                $entries = @($_.Group)
                $passedCount = @($entries | Where-Object { [string]$_.outcome -eq 'pass' }).Count
                $failedCount = @($entries | Where-Object { [string]$_.outcome -eq 'fail' }).Count
                $skippedCount = @($entries | Where-Object { [string]$_.outcome -eq 'skip' }).Count
                $totalRecorded = $entries.Count
                $passRatePercent = if ($totalRecorded -gt 0) {
                    [int][Math]::Round(($passedCount / $totalRecorded) * 100, 0)
                }
                else {
                    0
                }

                [pscustomobject]@{
                    RunId = [string]$_.Name
                    RunDate = [string]$entries[0].runDate
                    TotalRecorded = $totalRecorded
                    Passed = $passedCount
                    Failed = $failedCount
                    Skipped = $skippedCount
                    PassRatePercent = $passRatePercent
                }
            } |
            Sort-Object RunDate, RunId -Descending |
            Select-Object -First 10
    )

    $historySummary = Format-HistorySummary -RecentRuns $suiteHistory

    $failedTests = @(
        $latestResults |
            Where-Object { $_.Run -and [string]$_.Run.outcome -eq 'fail' } |
            Select-Object -First 8 |
            ForEach-Object {
                $test = $_.Test
                $run = $_.Run
                $notes = if ($null -ne $run.notes) { [string]$run.notes } else { '' }
                [pscustomobject]@{
                    Name = Get-TestDisplayName -Test $test
                    File = [string]$test.file
                    LastRunDate = [string]$run.lastDate
                    Notes = $notes
                }
            }
    )

    $artifacts = $ArtifactMap[$Suite.id]
    $trxFileName = if ($null -ne $artifacts) { [string]$artifacts.Trx } else { $null }
    $htmlFileName = if ($null -ne $artifacts) { [string]$artifacts.Html } else { $null }

    return [pscustomobject]@{
        Id = [string]$Suite.id
        Label = [string]$Suite.label
        Description = [string]$Suite.description
        Project = [string]$Suite.project
        Total = [int]$Tests.Count
        Recorded = [int]$recorded
        Passed = [int]$passed
        Failed = [int]$failed
        Skipped = [int]$skipped
        NotRecorded = [int]$notRecorded
        LatestRunDate = $latestRunDate
        HistorySparkline = [string]$historySummary.Sparkline
        HistorySummary = [string]$historySummary.Summary
        RecentRuns = $suiteHistory
        FailedTests = $failedTests
        TrxFileName = $trxFileName
        HtmlFileName = $htmlFileName
    }
}

function New-DashboardHtml {
    param([Parameter(Mandatory)] [object[]]$Suites)

    $totalTests = (($Suites | Measure-Object -Property Total -Sum).Sum)
    $totalPassed = (($Suites | Measure-Object -Property Passed -Sum).Sum)
    $totalFailed = (($Suites | Measure-Object -Property Failed -Sum).Sum)
    $totalSkipped = (($Suites | Measure-Object -Property Skipped -Sum).Sum)
    $totalNotRecorded = (($Suites | Measure-Object -Property NotRecorded -Sum).Sum)
    $generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")

    $suiteCards = foreach ($suite in $Suites) {
        $failedMarkup = if ($suite.FailedTests.Count -gt 0) {
            $items = foreach ($test in $suite.FailedTests) {
                @"
<li>
  <strong>$(ConvertTo-HtmlEncoded $test.Name)</strong>
  <span class="run-date">$(ConvertTo-HtmlEncoded $test.LastRunDate)</span>
  <div class="path"><a href="../$(ConvertTo-HtmlEncoded ($test.File -replace '\\', '/'))">$(ConvertTo-HtmlEncoded $test.File)</a></div>
  <div class="error">$(ConvertTo-HtmlEncoded $test.Notes)</div>
</li>
"@
            }

@"
<div class="failures">
  <h3>Latest failing tests</h3>
  <ul>
$(($items -join "`n"))
  </ul>
</div>
"@
        }
        else {
            '<div class="ok">No failing tests in the latest recorded status.</div>'
        }

        $artifactLinks = New-Object System.Collections.Generic.List[string]
        if (-not [string]::IsNullOrWhiteSpace($suite.TrxFileName)) {
            $artifactLinks.Add("<a href=""./$(ConvertTo-HtmlEncoded $suite.TrxFileName)"">TRX</a>")
        }
        if (-not [string]::IsNullOrWhiteSpace($suite.HtmlFileName)) {
            $artifactLinks.Add("<a href=""./$(ConvertTo-HtmlEncoded $suite.HtmlFileName)"">HTML report</a>")
        }
        if ($artifactLinks.Count -eq 0) {
            $artifactLinks.Add('<span class="muted">No artifact</span>')
        }

        $latestRunText = if ([string]::IsNullOrWhiteSpace($suite.LatestRunDate)) { 'Not recorded' } else { $suite.LatestRunDate }

@"
<section class="suite-card">
  <div class="suite-header">
    <div>
      <h2>$(ConvertTo-HtmlEncoded $suite.Label)</h2>
      <p>$(ConvertTo-HtmlEncoded $suite.Description)</p>
    </div>
    <div class="artifact-links">$(($artifactLinks -join ''))</div>
  </div>
  <div class="suite-stats">
    <div><span>Total</span><strong>$(Format-Count $suite.Total)</strong></div>
    <div><span>Recorded</span><strong>$(Format-Count $suite.Recorded)</strong></div>
    <div><span>Passed</span><strong class="passed">$(Format-Count $suite.Passed)</strong></div>
    <div><span>Failed</span><strong class="failed">$(Format-Count $suite.Failed)</strong></div>
    <div><span>Skipped</span><strong class="skipped">$(Format-Count $suite.Skipped)</strong></div>
    <div><span>Not recorded</span><strong class="muted-value">$(Format-Count $suite.NotRecorded)</strong></div>
    <div><span>Latest run</span><strong>$latestRunText</strong></div>
    <div><span>Recent slices</span><strong class="sparkline">$(ConvertTo-HtmlEncoded $suite.HistorySparkline)</strong></div>
  </div>
  <div class="history">
    <span class="history-label">Recent history</span>
    <span class="history-text">$(ConvertTo-HtmlEncoded $suite.HistorySummary)</span>
  </div>
  $failedMarkup
</section>
"@
    }

@"
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>OpenClaw .NET Test Dashboard</title>
  <style>
    :root {
      --bg: #0c0a1a;
      --surface: #1a1630;
      --surface-2: #241f3e;
      --text: #e8e6f0;
      --muted: #a29bbd;
      --primary: #7c5cfc;
      --accent: #22d3ee;
      --passed: #22c55e;
      --failed: #ef4444;
      --skipped: #f59e0b;
      --border: rgba(124, 92, 252, 0.2);
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Inter, Arial, sans-serif;
      color: var(--text);
      background:
        radial-gradient(circle at top, rgba(81, 43, 212, 0.35), transparent 45%),
        var(--bg);
    }

    a { color: var(--accent); text-decoration: none; }
    a:hover { text-decoration: underline; }
    .container { max-width: 1180px; margin: 0 auto; padding: 32px 20px 56px; }
    .hero { padding: 24px 0 12px; }
    .hero h1 { margin: 0 0 8px; font-size: 2.2rem; }
    .hero p { margin: 0; color: var(--muted); }
    .summary {
      margin-top: 24px;
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: 16px;
    }
    .stat, .suite-card {
      background: rgba(26, 22, 48, 0.88);
      border: 1px solid var(--border);
      border-radius: 16px;
      backdrop-filter: blur(10px);
    }
    .stat { padding: 18px 20px; }
    .stat span { display: block; color: var(--muted); font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.06em; }
    .stat strong { display: block; margin-top: 8px; font-size: 1.8rem; }
    .stat .passed { color: var(--passed); }
    .stat .failed { color: var(--failed); }
    .stat .skipped { color: var(--skipped); }
    .meta {
      margin-top: 16px;
      color: var(--muted);
      font-size: 0.9rem;
      display: flex;
      gap: 18px;
      flex-wrap: wrap;
    }
    .suite-grid {
      margin-top: 28px;
      display: grid;
      gap: 18px;
    }
    .suite-card { padding: 20px; }
    .suite-header {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      align-items: start;
    }
    .suite-header h2 { margin: 0; font-size: 1.25rem; }
    .suite-header p { margin: 6px 0 0; color: var(--muted); max-width: 760px; }
    .artifact-links {
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
      white-space: nowrap;
    }
    .suite-stats {
      margin-top: 16px;
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
      gap: 12px;
    }
    .suite-stats div {
      background: var(--surface-2);
      border-radius: 12px;
      padding: 12px;
    }
    .suite-stats span {
      display: block;
      color: var(--muted);
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .suite-stats strong { display: block; margin-top: 6px; font-size: 1.1rem; }
    .suite-stats .passed { color: var(--passed); }
    .suite-stats .failed { color: var(--failed); }
    .suite-stats .skipped { color: var(--skipped); }
    .suite-stats .muted-value { color: var(--muted); }
    .sparkline {
      font-family: Consolas, "Courier New", monospace;
      letter-spacing: 0.12em;
    }
    .history, .failures, .ok {
      margin-top: 18px;
      padding: 14px 16px;
      border-radius: 12px;
      background: rgba(12, 10, 26, 0.7);
      border: 1px solid rgba(255,255,255,0.06);
    }
    .history-label {
      display: block;
      color: var(--muted);
      font-size: 0.78rem;
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
    .history-text {
      display: block;
      margin-top: 6px;
      color: var(--text);
    }
    .failures h3 { margin: 0 0 12px; font-size: 0.95rem; color: #fca5a5; }
    .failures ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .failures li { border-top: 1px solid rgba(255,255,255,0.06); padding-top: 10px; }
    .failures li:first-child { border-top: 0; padding-top: 0; }
    .run-date, .path, .muted {
      display: block;
      margin-top: 4px;
      color: var(--muted);
      font-size: 0.85rem;
    }
    .error {
      margin-top: 6px;
      color: var(--muted);
      white-space: pre-wrap;
      font-size: 0.85rem;
    }
    .footer { margin-top: 28px; color: var(--muted); font-size: 0.88rem; }
  </style>
</head>
<body>
  <div class="container">
    <div class="hero">
      <a href="../">Back to OpenClaw .NET</a>
      <h1>OpenClaw .NET Test Dashboard</h1>
      <p>Generated from the canonical test catalog and recorded run history in the private repo, then mirrored to the public site.</p>
      <div class="summary">
        <div class="stat"><span>Total tests</span><strong>$(Format-Count $totalTests)</strong></div>
        <div class="stat"><span>Passed</span><strong class="passed">$(Format-Count $totalPassed)</strong></div>
        <div class="stat"><span>Failed</span><strong class="failed">$(Format-Count $totalFailed)</strong></div>
        <div class="stat"><span>Skipped</span><strong class="skipped">$(Format-Count $totalSkipped)</strong></div>
        <div class="stat"><span>Not recorded</span><strong>$(Format-Count $totalNotRecorded)</strong></div>
      </div>
      <div class="meta">
        <span>Generated: $generatedAt</span>
        <span>Suites: $($Suites.Count)</span>
        <span><a href="./summary.json">summary.json</a></span>
      </div>
    </div>
    <div class="suite-grid">
$(($suiteCards -join "`n"))
    </div>
    <div class="footer">
      Rebuild with <code>scripts\test-and-publish.ps1</code>. Do not hand-edit this page.
    </div>
  </div>
</body>
</html>
"@
}

if (-not $SkipTests) {
    $env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"
    $env:Aspire__SkipOllama = "true"
    $env:Logging__LogLevel__Default = "Debug"
    $env:Logging__LogLevel__Microsoft = "Information"
    $env:Logging__LogLevel__OpenClawNet = "Trace"

    if ($Headed) {
        $env:PLAYWRIGHT_HEADED = "true"
    }
    else {
        Remove-Item Env:PLAYWRIGHT_HEADED -ErrorAction SilentlyContinue
    }

    if (Test-Path $resultsDir) {
        Remove-Item $resultsDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

    Invoke-TestSuite -Name "Playwright tests" `
        -ProjectPath (Join-Path $repoRoot "tests\OpenClawNet.PlaywrightTests") `
        -TrxName "playwright-test-results.trx" `
        -HtmlName "dotnet-report.html" `
        -ContinueOnFailure

    Invoke-TestSuite -Name "Unit tests" `
        -ProjectPath (Join-Path $repoRoot "tests\OpenClawNet.UnitTests") `
        -TrxName "unit-test-results.trx" `
        -Filter "Category!=Live" `
        -ContinueOnFailure

    Invoke-TestSuite -Name "Integration tests" `
        -ProjectPath (Join-Path $repoRoot "tests\OpenClawNet.IntegrationTests") `
        -TrxName "integration-test-results.trx" `
        -ContinueOnFailure

    $liveProject = Join-Path $repoRoot "tests\OpenClawNet.E2ETests"
    if (Test-Path $liveProject) {
        Invoke-TestSuite -Name "Gateway live E2E tests" `
            -ProjectPath $liveProject `
            -TrxName "live-test-results.trx" `
            -Filter "Category=Live" `
            -ContinueOnFailure
    }
}

New-Item -ItemType Directory -Path $dashboardDir -Force | Out-Null

$artifactMap = Get-SuiteArtifactMap
$recordableTrxFiles = @(
    $artifactMap.Values |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.Trx) } |
        ForEach-Object { Join-Path $resultsDir $_.Trx } |
        Where-Object { Test-Path $_ } |
        Sort-Object -Unique
)

foreach ($suiteArtifacts in $artifactMap.Values) {
    if (-not [string]::IsNullOrWhiteSpace([string]$suiteArtifacts.Trx)) {
        $sourcePath = Join-Path $resultsDir $suiteArtifacts.Trx
        if (Test-Path $sourcePath) {
            Copy-Item $sourcePath (Join-Path $dashboardDir $suiteArtifacts.Trx) -Force
        }
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$suiteArtifacts.Html)) {
        $sourcePath = Join-Path $resultsDir $suiteArtifacts.Html
        if (Test-Path $sourcePath) {
            Copy-Item $sourcePath (Join-Path $dashboardDir $suiteArtifacts.Html) -Force
        }
    }
}

$shouldRecordRuns = $RecordRuns -or (-not $SkipTests)
if ($shouldRecordRuns) {
    if ($recordableTrxFiles.Count -eq 0) {
        Write-Warning "No known TRX files were available to record into tests\runs.jsonl."
    }
    else {
        & (Join-Path $PSScriptRoot "record-test-run.ps1") `
            -TrxPath $recordableTrxFiles `
            -CatalogPath $CatalogPath `
            -RunsPath $RunsPath `
            -RunsIndexPath $RunsIndexPath

        if ($LASTEXITCODE -ne 0) {
            throw "record-test-run.ps1 failed."
        }
    }
}

$catalog = Import-Catalog -Path (Resolve-RepoPath $CatalogPath)
$runsIndex = Get-RunsIndexData -Path (Resolve-RepoPath $RunsIndexPath)
$runEntries = Get-RunEntries -Path (Resolve-RepoPath $RunsPath)

$suiteSummaries = @(
    foreach ($suite in @($catalog.suites)) {
        $suiteTests = @($catalog.tests | Where-Object { $_.suite -eq $suite.id })
        New-SuiteSummary -Suite $suite -Tests $suiteTests -RunsIndex $runsIndex -RunEntries $runEntries -ArtifactMap $artifactMap
    }
)

if ($suiteSummaries.Count -eq 0) {
    throw "No suites were found in $CatalogPath."
}

[pscustomobject]@{
    GeneratedAt = (Get-Date).ToUniversalTime().ToString("o")
    Suites = $suiteSummaries
} |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $reportPath -Encoding utf8

New-DashboardHtml -Suites $suiteSummaries |
    Set-Content -LiteralPath $dashboardIndex -Encoding utf8

Write-Host "`nDashboard written to $dashboardDir" -ForegroundColor Green
