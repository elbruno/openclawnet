[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$Headed,
    [string]$ResultsDirectory = "TestResults",
    [string]$DashboardDirectory = "docs\test-dashboard"
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

function Get-TrxSuiteSummary {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Label,
        [Parameter(Mandatory)] [string]$Description
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $results = @($trx.TestRun.Results.UnitTestResult)

    $failedTests = @(
        $results |
            Where-Object { $_.outcome -eq "Failed" } |
            Select-Object -First 8 |
            ForEach-Object {
                [pscustomobject]@{
                    Name = $_.testName
                    Duration = $_.duration
                    Error = ($_.Output.ErrorInfo.Message | Out-String).Trim()
                }
            }
    )

    [pscustomobject]@{
        Label = $Label
        Description = $Description
        FileName = [System.IO.Path]::GetFileName($Path)
        Total = [int]$counters.total
        Passed = [int]$counters.passed
        Failed = [int]$counters.failed
        Skipped = [int]([int]$counters.notExecuted + [int]$counters.notRunnable)
        Duration = ([datetime]$trx.TestRun.Times.finish) - ([datetime]$trx.TestRun.Times.start)
        FailedTests = $failedTests
    }
}

function Format-Duration {
    param([TimeSpan]$Duration)

    if ($Duration.TotalHours -ge 1) {
        return "{0}h {1}m {2}s" -f [int]$Duration.TotalHours, $Duration.Minutes, $Duration.Seconds
    }

    if ($Duration.TotalMinutes -ge 1) {
        return "{0}m {1}s" -f [int]$Duration.TotalMinutes, $Duration.Seconds
    }

    return "{0}s" -f [Math]::Max([int][Math]::Round($Duration.TotalSeconds), 0)
}

function ConvertTo-HtmlEncoded {
    param([string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return [System.Net.WebUtility]::HtmlEncode($Value)
}

function New-DashboardHtml {
    param([Parameter(Mandatory)] [object[]]$Suites)

    $totalTests = ($Suites | Measure-Object -Property Total -Sum).Sum
    $totalPassed = ($Suites | Measure-Object -Property Passed -Sum).Sum
    $totalFailed = ($Suites | Measure-Object -Property Failed -Sum).Sum
    $totalSkipped = ($Suites | Measure-Object -Property Skipped -Sum).Sum
    $totalDuration = [TimeSpan]::FromSeconds((($Suites | ForEach-Object { $_.Duration.TotalSeconds }) | Measure-Object -Sum).Sum)
    $generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")

    $suiteCards = foreach ($suite in $Suites) {
        $failedMarkup = if ($suite.FailedTests.Count -gt 0) {
            $items = foreach ($test in $suite.FailedTests) {
                $errorSummary = $test.Error
                if ($errorSummary.Length -gt 220) {
                    $errorSummary = $errorSummary.Substring(0, 220) + "..."
                }

                @"
<li>
  <strong>$(ConvertTo-HtmlEncoded $test.Name)</strong>
  <span class="duration">$(ConvertTo-HtmlEncoded $test.Duration)</span>
  <div class="error">$(ConvertTo-HtmlEncoded $errorSummary)</div>
</li>
"@
            }

@"
<div class="failures">
  <h3>Recent failures</h3>
  <ul>
$(($items -join "`n"))
  </ul>
</div>
"@
        }
        else {
            '<div class="ok">No failed tests recorded in this TRX.</div>'
        }

@"
<section class="suite-card">
  <div class="suite-header">
    <div>
      <h2>$(ConvertTo-HtmlEncoded $suite.Label)</h2>
      <p>$(ConvertTo-HtmlEncoded $suite.Description)</p>
    </div>
    <a href="./$(ConvertTo-HtmlEncoded $suite.FileName)">TRX</a>
  </div>
  <div class="suite-stats">
    <div><span>Total</span><strong>$($suite.Total)</strong></div>
    <div><span>Passed</span><strong class="passed">$($suite.Passed)</strong></div>
    <div><span>Failed</span><strong class="failed">$($suite.Failed)</strong></div>
    <div><span>Skipped</span><strong class="skipped">$($suite.Skipped)</strong></div>
    <div><span>Duration</span><strong>$(Format-Duration $suite.Duration)</strong></div>
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
    .container { max-width: 1100px; margin: 0 auto; padding: 32px 20px 56px; }
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
    .suite-header p { margin: 6px 0 0; color: var(--muted); }
    .suite-stats {
      margin-top: 16px;
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
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
    .suite-stats strong { display: block; margin-top: 6px; font-size: 1.2rem; }
    .suite-stats .passed { color: var(--passed); }
    .suite-stats .failed { color: var(--failed); }
    .suite-stats .skipped { color: var(--skipped); }
    .failures, .ok {
      margin-top: 18px;
      padding: 14px 16px;
      border-radius: 12px;
      background: rgba(12, 10, 26, 0.7);
      border: 1px solid rgba(255,255,255,0.06);
    }
    .failures h3 { margin: 0 0 12px; font-size: 0.95rem; color: #fca5a5; }
    .failures ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .failures li { border-top: 1px solid rgba(255,255,255,0.06); padding-top: 10px; }
    .failures li:first-child { border-top: 0; padding-top: 0; }
    .duration {
      margin-left: 8px;
      color: var(--muted);
      font-size: 0.82rem;
    }
    .error {
      margin-top: 4px;
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
      <p>Private-repo generated dashboard mirrored to the public site after sync.</p>
      <div class="summary">
        <div class="stat"><span>Total tests</span><strong>$totalTests</strong></div>
        <div class="stat"><span>Passed</span><strong class="passed">$totalPassed</strong></div>
        <div class="stat"><span>Failed</span><strong class="failed">$totalFailed</strong></div>
        <div class="stat"><span>Skipped</span><strong class="skipped">$totalSkipped</strong></div>
        <div class="stat"><span>Duration</span><strong>$(Format-Duration $totalDuration)</strong></div>
      </div>
      <div class="meta">
        <span>Generated: $generatedAt</span>
        <span>Suites: $($Suites.Count)</span>
        <span><a href="./summary.json">summary.json</a></span>
        <span><a href="./dotnet-report.html">Playwright HTML report</a></span>
      </div>
    </div>
    <div class="suite-grid">
$(($suiteCards -join "`n"))
    </div>
    <div class="footer">
      Rebuild with <code>scripts\publish-test-dashboard.ps1</code>. Do not hand-edit this page.
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

$trxFiles = @(
    @{ Path = Join-Path $resultsDir "playwright-test-results.trx"; Label = "Playwright E2E"; Description = "Browser-driven UI and AppHost coverage."; },
    @{ Path = Join-Path $resultsDir "unit-test-results.trx"; Label = "Unit"; Description = "Fast repository unit coverage."; },
    @{ Path = Join-Path $resultsDir "integration-test-results.trx"; Label = "Integration"; Description = "Service and API integration coverage."; },
    @{ Path = Join-Path $resultsDir "live-test-results.trx"; Label = "Live E2E"; Description = "Live gateway and end-to-end slices."; }
)

$suiteSummaries = @()
foreach ($trxFile in $trxFiles) {
    $summary = Get-TrxSuiteSummary -Path $trxFile.Path -Label $trxFile.Label -Description $trxFile.Description
    if ($null -ne $summary) {
        Copy-Item $trxFile.Path (Join-Path $dashboardDir $summary.FileName) -Force
        $suiteSummaries += $summary
    }
}

if ($suiteSummaries.Count -eq 0) {
    throw "No TRX files were found in $resultsDir."
}

$htmlReport = Join-Path $resultsDir "dotnet-report.html"
if (Test-Path $htmlReport) {
    Copy-Item $htmlReport (Join-Path $dashboardDir "dotnet-report.html") -Force
}

$suiteSummaries |
    Select-Object Label, Description, FileName, Total, Passed, Failed, Skipped,
        @{ Name = "Duration"; Expression = { Format-Duration $_.Duration } },
        FailedTests |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $reportPath -Encoding utf8

New-DashboardHtml -Suites $suiteSummaries |
    Set-Content -LiteralPath $dashboardIndex -Encoding utf8

Write-Host "`nDashboard written to $dashboardDir" -ForegroundColor Green
