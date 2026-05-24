[CmdletBinding(DefaultParameterSetName = 'Trx')]
param(
    [Parameter(ParameterSetName = 'Trx')]
    [string]$ResultsDirectory = "TestResults",

    [Parameter(ParameterSetName = 'Trx')]
    [string[]]$TrxPath,

    [Parameter(ParameterSetName = 'Trx')]
    [string]$RunId,

    [Parameter(ParameterSetName = 'Markdown', Mandatory)]
    [string]$MarkdownIndexPath,

    [Parameter(ParameterSetName = 'Markdown', Mandatory)]
    [string]$BackfillDate,

    [Parameter(ParameterSetName = 'Markdown', Mandatory)]
    [string]$BackfillRunId,

    [string]$CatalogPath = "tests\catalog.yaml",
    [string]$RunsPath = "tests\runs.jsonl",
    [string]$RunsIndexPath = "tests\runs-index.json",
    [string]$CommitSha
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

function ConvertTo-RepoRelativePath {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $repoRoot.TrimEnd('\') + '\'
    if ($fullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($rootWithSeparator.Length) -replace '\\', '/'
    }

    return $Path -replace '\\', '/'
}

function ConvertTo-SafeRunId {
    param([datetime]$Timestamp)

    return $Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ")
}

function ConvertTo-CanonicalOutcome {
    param([AllowNull()] [string]$Outcome)

    switch -Regex ($Outcome) {
        '^Passed$' { return 'pass' }
        '^Failed$' { return 'fail' }
        '^(NotExecuted|NotRunnable)$' { return 'skip' }
        '^(Pending|Inconclusive)$' { return 'notrun' }
        default {
            if ([string]::IsNullOrWhiteSpace($Outcome)) {
                return 'notrun'
            }

            return $Outcome.Trim().ToLowerInvariant()
        }
    }
}

function Get-OutcomeRank {
    param([Parameter(Mandatory)] [string]$Outcome)

    switch ($Outcome) {
        'fail' { return 3 }
        'skip' { return 2 }
        'notrun' { return 1 }
        default { return 0 }
    }
}

function Normalize-Message {
    param([AllowNull()] [string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return $null
    }

    $normalized = ($Message -replace '\s+', ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $null
    }

    return $normalized
}

function Get-ShortNote {
    param([AllowNull()] [string]$Message)

    $normalized = Normalize-Message $Message
    if ($null -eq $normalized) {
        return $null
    }

    if ($normalized.Length -gt 240) {
        return $normalized.Substring(0, 240) + "..."
    }

    return $normalized
}

function Get-ErrorExcerpt {
    param([AllowNull()] [string]$Message)

    $normalized = Normalize-Message $Message
    if ($null -eq $normalized) {
        return $null
    }

    if ($normalized.Length -gt 500) {
        return $normalized.Substring(0, 500) + "..."
    }

    return $normalized
}

function Get-DurationMs {
    param($Duration)

    if ($null -eq $Duration) {
        return 0
    }

    $parsed = [TimeSpan]::Zero
    if ($Duration -is [TimeSpan]) {
        $parsed = $Duration
    }
    elseif ([TimeSpan]::TryParse([string]$Duration, [ref]$parsed)) {
        $null = $parsed
    }

    return [int][Math]::Round($parsed.TotalMilliseconds, 0, [MidpointRounding]::AwayFromZero)
}

function Get-GitCommitSha {
    Push-Location $repoRoot
    try {
        $sha = (& git rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -eq 0) {
            return ($sha | Select-Object -First 1).Trim()
        }
    }
    catch {
    }
    finally {
        Pop-Location
    }

    return $null
}

function Get-DescriptorFromLinkText {
    param([Parameter(Mandatory)] [string]$LinkText)

    $text = $LinkText.Trim()
    $methodName = $null

    if ($text -match '^(?<prefix>.+?)\s*[·•┬╖]+\s*`(?<method>[^`]+)`$') {
        $className = $Matches.prefix.Trim()
        $methodName = $Matches.method.Trim()
    }
    elseif ($text -match '^(?<prefix>.+?)\s*[·•┬╖]+\s*(?<method>.+)$') {
        $className = $Matches.prefix.Trim()
        $methodName = $Matches.method.Trim()
    }
    else {
        $className = $text
    }

    return [ordered]@{
        className = $className
        methodName = $methodName
        id = if ([string]::IsNullOrWhiteSpace($methodName)) { $className } else { "$className.$methodName" }
    }
}

function Get-MarkdownOutcome {
    param([Parameter(Mandatory)] [string]$Value)

    $upper = $Value.ToUpperInvariant()
    if ($upper.Contains('FAIL')) {
        return 'fail'
    }
    if ($upper.Contains('SKIP')) {
        return 'skip'
    }
    if ($upper.Contains('PASS')) {
        return 'pass'
    }
    return 'notrun'
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

function Get-CatalogMaps {
    param([Parameter(Mandatory)] $Catalog)

    $byId = @{}
    $byClass = @{}
    $byClassMethod = @{}

    foreach ($test in @($Catalog.tests)) {
        $byId[$test.id] = $test

        if (-not $byClass.ContainsKey($test.className)) {
            $byClass[$test.className] = @()
        }
        $byClass[$test.className] += ,$test

        $methodName = Get-OptionalPropertyValue -Object $test -Name 'methodName'
        if (-not [string]::IsNullOrWhiteSpace($methodName)) {
            $byClassMethod["$($test.className).$methodName"] = $test
        }
    }

    return [ordered]@{
        ById = $byId
        ByClass = $byClass
        ByClassMethod = $byClassMethod
    }
}

function Get-SuiteMatchers {
    param([Parameter(Mandatory)] $Catalog)

    $matchers = foreach ($suite in @($Catalog.suites)) {
        [pscustomobject]@{
            SuiteId = $suite.id
            NamespacePrefix = (Split-Path $suite.project -Leaf)
        }
    }

    return @($matchers | Sort-Object { $_.NamespacePrefix.Length } -Descending)
}

function Resolve-SuiteId {
    param(
        [Parameter(Mandatory)] [string]$TestName,
        [Parameter(Mandatory)] [object[]]$SuiteMatchers
    )

    foreach ($matcher in $SuiteMatchers) {
        if ($TestName.StartsWith($matcher.NamespacePrefix + ".", [System.StringComparison]::OrdinalIgnoreCase) -or
            $TestName -eq $matcher.NamespacePrefix) {
            return $matcher.SuiteId
        }
    }

    return 'unknown'
}

function Parse-TrxTestName {
    param(
        [Parameter(Mandatory)] [string]$TestName,
        [Parameter(Mandatory)] [object[]]$SuiteMatchers
    )

    $suiteId = Resolve-SuiteId -TestName $TestName -SuiteMatchers $SuiteMatchers
    $methodSignature = ($TestName -split '\(')[0]

    $namespacePrefix = $null
    foreach ($matcher in $SuiteMatchers) {
        if ($matcher.SuiteId -eq $suiteId) {
            $namespacePrefix = $matcher.NamespacePrefix
            break
        }
    }

    $remainder = $methodSignature
    if ($namespacePrefix -and $methodSignature.StartsWith($namespacePrefix + ".", [System.StringComparison]::OrdinalIgnoreCase)) {
        $remainder = $methodSignature.Substring($namespacePrefix.Length + 1)
    }

    $segments = @($remainder -split '\.')
    if ($segments.Count -lt 2) {
        return [ordered]@{
            suiteId = $suiteId
            className = $remainder
            methodName = $null
            rawName = $TestName
        }
    }

    return [ordered]@{
        suiteId = $suiteId
        className = $segments[$segments.Count - 2]
        methodName = $segments[$segments.Count - 1]
        rawName = $TestName
    }
}

function Resolve-CatalogEntry {
    param(
        [Parameter(Mandatory)] $Descriptor,
        [Parameter(Mandatory)] $CatalogMaps
    )

    $methodKey = if ($Descriptor.methodName) { "$($Descriptor.className).$($Descriptor.methodName)" } else { $null }
    if ($methodKey -and $CatalogMaps.ByClassMethod.ContainsKey($methodKey)) {
        return $CatalogMaps.ByClassMethod[$methodKey]
    }

    if ($CatalogMaps.ByClass.ContainsKey($Descriptor.className)) {
        $matches = @($CatalogMaps.ByClass[$Descriptor.className])
        $suiteMatches = @($matches | Where-Object { $_.suite -eq $Descriptor.suiteId })
        if ($suiteMatches.Count -gt 0) {
            $classLevel = @($suiteMatches | Where-Object { [string]::IsNullOrWhiteSpace((Get-OptionalPropertyValue -Object $_ -Name 'methodName')) })
            if ($classLevel.Count -gt 0) {
                return $classLevel[0]
            }

            if ($suiteMatches.Count -eq 1) {
                return $suiteMatches[0]
            }
        }

        $classLevelAny = @($matches | Where-Object { [string]::IsNullOrWhiteSpace((Get-OptionalPropertyValue -Object $_ -Name 'methodName')) })
        if ($classLevelAny.Count -gt 0) {
            return $classLevelAny[0]
        }
    }

    return $null
}

function Add-OrUpdateAggregate {
    param(
        [Parameter(Mandatory)] [hashtable]$Aggregates,
        [Parameter(Mandatory)] [hashtable]$Entry
    )

    $key = $Entry.testId
    if (-not $Aggregates.ContainsKey($key)) {
        $Aggregates[$key] = [ordered]@{
            testId = $Entry.testId
            suite = $Entry.suite
            outcome = $Entry.outcome
            durationMs = $Entry.durationMs
            notes = $Entry.notes
            errorExcerpt = $Entry.errorExcerpt
            sourceTestNames = New-Object System.Collections.Generic.List[string]
        }
        if ($Entry.sourceTestName) {
            $Aggregates[$key].sourceTestNames.Add($Entry.sourceTestName)
        }
        return
    }

    $aggregate = $Aggregates[$key]
    $aggregate.durationMs += $Entry.durationMs

    if ((Get-OutcomeRank $Entry.outcome) -gt (Get-OutcomeRank $aggregate.outcome)) {
        $aggregate.outcome = $Entry.outcome
        $aggregate.notes = $Entry.notes
        $aggregate.errorExcerpt = $Entry.errorExcerpt
    }
    elseif ($null -eq $aggregate.notes -and $null -ne $Entry.notes) {
        $aggregate.notes = $Entry.notes
        $aggregate.errorExcerpt = $Entry.errorExcerpt
    }

    if ($Entry.sourceTestName -and -not $aggregate.sourceTestNames.Contains($Entry.sourceTestName)) {
        $aggregate.sourceTestNames.Add($Entry.sourceTestName)
    }
}

function Get-RunEntriesFromTrx {
    param(
        [Parameter(Mandatory)] [string[]]$TrxFiles,
        [Parameter(Mandatory)] $CatalogMaps,
        [Parameter(Mandatory)] [object[]]$SuiteMatchers,
        [AllowNull()] [string]$ResolvedCommitSha,
        [AllowNull()] [string]$ExplicitRunId
    )

    $entries = New-Object System.Collections.Generic.List[hashtable]

    foreach ($trxFile in $TrxFiles) {
        [xml]$trx = Get-Content -LiteralPath $trxFile -Raw
        $results = @($trx.TestRun.Results.UnitTestResult)
        if ($results.Count -eq 0) {
            continue
        }

        $runFinish = [datetime]$trx.TestRun.Times.finish
        $runStart = [datetime]$trx.TestRun.Times.start
        $resolvedRunId = if ($ExplicitRunId) { $ExplicitRunId } else { ConvertTo-SafeRunId $runFinish }
        $runDate = $runFinish.ToUniversalTime().ToString("yyyy-MM-dd")
        $aggregates = @{}

        foreach ($result in $results) {
            $descriptor = Parse-TrxTestName -TestName $result.testName -SuiteMatchers $SuiteMatchers
            $catalogEntry = Resolve-CatalogEntry -Descriptor $descriptor -CatalogMaps $CatalogMaps
            $canonicalTestId = if ($null -ne $catalogEntry) { $catalogEntry.id } elseif ($descriptor.methodName) { "$($descriptor.className).$($descriptor.methodName)" } else { $descriptor.className }
            $suiteId = if ($null -ne $catalogEntry) { $catalogEntry.suite } else { $descriptor.suiteId }
            $message = $null
            $outputNode = Get-OptionalPropertyValue -Object $result -Name 'Output'
            $errorInfoNode = if ($null -ne $outputNode) { Get-OptionalPropertyValue -Object $outputNode -Name 'ErrorInfo' } else { $null }
            if ($null -ne $errorInfoNode) {
                $message = [string]$errorInfoNode.Message
            }

            Add-OrUpdateAggregate -Aggregates $aggregates -Entry ([ordered]@{
                    testId = $canonicalTestId
                    suite = $suiteId
                    outcome = ConvertTo-CanonicalOutcome $result.outcome
                    durationMs = Get-DurationMs $result.duration
                    notes = Get-ShortNote $message
                    errorExcerpt = Get-ErrorExcerpt $message
                    sourceTestName = [string]$result.testName
                })
        }

        foreach ($aggregate in $aggregates.Values) {
            $entry = [ordered]@{
                runId = $resolvedRunId
                runDate = $runDate
                suite = $aggregate.suite
                testId = $aggregate.testId
                outcome = $aggregate.outcome
                durationMs = $aggregate.durationMs
                trx = [System.IO.Path]::GetFileName($trxFile)
                notes = $aggregate.notes
                commitSha = $ResolvedCommitSha
                source = 'trx'
                runStartedAt = $runStart.ToUniversalTime().ToString("o")
                runFinishedAt = $runFinish.ToUniversalTime().ToString("o")
            }

            if ($aggregate.errorExcerpt) {
                $entry.errorExcerpt = $aggregate.errorExcerpt
            }

            $entries.Add($entry)
        }
    }

    return $entries
}

function Get-RunEntriesFromMarkdown {
    param(
        [Parameter(Mandatory)] [string]$IndexPath,
        [Parameter(Mandatory)] [string]$Date,
        [Parameter(Mandatory)] [string]$RunId,
        [Parameter(Mandatory)] $CatalogMaps
    )

    $entries = New-Object System.Collections.Generic.List[hashtable]

    foreach ($line in Get-Content -LiteralPath $IndexPath) {
        if (-not $line.TrimStart().StartsWith("| [")) {
            continue
        }

        $parts = $line.Trim().Trim('|').Split('|')
        if ($parts.Count -lt 5) {
            continue
        }

        $runDate = $parts[2].Trim()
        if ($runDate -ne $Date) {
            continue
        }

        $linkCell = $parts[0].Trim()
        $resultCell = $parts[3].Trim()
        $notesCell = $parts[4].Trim()

        if ($linkCell -notmatch '^\[(?<text>.+?)\]\((?<target>.+?)\)$') {
            continue
        }

        $descriptor = Get-DescriptorFromLinkText $Matches.text
        $testId = $descriptor.id
        $suite = 'unknown'
        if ($CatalogMaps.ById.ContainsKey($testId)) {
            $suite = $CatalogMaps.ById[$testId].suite
        }
        elseif ($CatalogMaps.ByClassMethod.ContainsKey($testId)) {
            $suite = $CatalogMaps.ByClassMethod[$testId].suite
            $testId = $CatalogMaps.ByClassMethod[$testId].id
        }
        elseif ($CatalogMaps.ByClass.ContainsKey($descriptor.className)) {
            $suite = $CatalogMaps.ByClass[$descriptor.className][0].suite
            if ([string]::IsNullOrWhiteSpace($descriptor.methodName)) {
                $testId = $CatalogMaps.ByClass[$descriptor.className][0].id
            }
        }

        $entries.Add([ordered]@{
                runId = $RunId
                runDate = $Date
                suite = $suite
                testId = $testId
                outcome = Get-MarkdownOutcome $resultCell
                durationMs = 0
                trx = $null
                notes = if ([string]::IsNullOrWhiteSpace($notesCell)) { $null } else { $notesCell }
                commitSha = $null
                source = 'markdown-backfill'
            })
    }

    return $entries
}

function Get-ExistingKeys {
    param([Parameter(Mandatory)] [string]$Path)

    $keys = @{}
    if (-not (Test-Path $Path)) {
        return $keys
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $record = $line | ConvertFrom-Json
        $keys["$($record.runId)|$($record.testId)"] = $true
    }

    return $keys
}

function Append-RunEntries {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [System.Collections.Generic.List[hashtable]]$Entries
    )

    $parent = Split-Path $Path -Parent
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    if (-not (Test-Path $Path)) {
        New-Item -ItemType File -Path $Path -Force | Out-Null
    }

    $existingKeys = Get-ExistingKeys -Path $Path
    $lines = New-Object System.Collections.Generic.List[string]
    $skipped = 0

    foreach ($entry in $Entries) {
        $key = "$($entry.runId)|$($entry.testId)"
        if ($existingKeys.ContainsKey($key)) {
            $skipped++
            continue
        }

        $existingKeys[$key] = $true
        $lines.Add(($entry | ConvertTo-Json -Compress -Depth 6))
    }

    if ($lines.Count -gt 0) {
        Add-Content -LiteralPath $Path -Value $lines -Encoding utf8
    }

    return [ordered]@{
        appended = $lines.Count
        skipped = $skipped
    }
}

function Write-RunsIndex {
    param(
        [Parameter(Mandatory)] [string]$RunsFile,
        [Parameter(Mandatory)] [string]$IndexFile
    )

    $index = [ordered]@{}
    if (Test-Path $RunsFile) {
        foreach ($line in Get-Content -LiteralPath $RunsFile) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $record = $line | ConvertFrom-Json
            $index[$record.testId] = [ordered]@{
                lastRunId = $record.runId
                lastDate = $record.runDate
                outcome = $record.outcome
                notes = $record.notes
                suite = $record.suite
                durationMs = [int]$record.durationMs
            }
        }
    }

    $parent = Split-Path $IndexFile -Parent
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    ($index | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $IndexFile -Encoding utf8
    return $index.Count
}

$catalog = Import-Catalog -Path (Resolve-RepoPath $CatalogPath)
$catalogMaps = Get-CatalogMaps -Catalog $catalog
$suiteMatchers = Get-SuiteMatchers -Catalog $catalog
$runsFile = Resolve-RepoPath $RunsPath
$runsIndexFile = Resolve-RepoPath $RunsIndexPath
$resolvedCommitSha = if ($CommitSha) { $CommitSha } else { Get-GitCommitSha }

if ($PSCmdlet.ParameterSetName -eq 'Markdown') {
    $entries = Get-RunEntriesFromMarkdown -IndexPath (Resolve-RepoPath $MarkdownIndexPath) -Date $BackfillDate -RunId $BackfillRunId -CatalogMaps $catalogMaps
}
else {
    $resolvedTrxFiles = @(
        if ($TrxPath -and @($TrxPath).Count -gt 0) {
        @($TrxPath | ForEach-Object { Resolve-RepoPath $_ } | Where-Object { Test-Path $_ })
        }
        else {
            $resultsDir = Resolve-RepoPath $ResultsDirectory
            @(Get-ChildItem -Path $resultsDir -Filter *.trx -File | ForEach-Object { $_.FullName })
        }
    )

    if ($resolvedTrxFiles.Count -eq 0) {
        throw "No TRX files found to record."
    }

    $entries = Get-RunEntriesFromTrx -TrxFiles $resolvedTrxFiles -CatalogMaps $catalogMaps -SuiteMatchers $suiteMatchers -ResolvedCommitSha $resolvedCommitSha -ExplicitRunId $RunId
}

$appendResult = Append-RunEntries -Path $runsFile -Entries $entries
$indexCount = Write-RunsIndex -RunsFile $runsFile -IndexFile $runsIndexFile

Write-Host "Processed $($entries.Count) run entries." -ForegroundColor Green
Write-Host "Appended $($appendResult.appended) new lines to $(ConvertTo-RepoRelativePath $runsFile); skipped $($appendResult.skipped) existing keys." -ForegroundColor Green
Write-Host "Rebuilt $(ConvertTo-RepoRelativePath $runsIndexFile) with $indexCount rollup rows." -ForegroundColor Green
