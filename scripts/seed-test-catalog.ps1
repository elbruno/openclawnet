[CmdletBinding()]
param(
    [string]$IndexPath = "docs\testing\e2e-test-index.md",
    [string]$OutputPath = "tests\catalog.yaml"
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

    $normalized = $Path.Trim()
    while ($normalized.StartsWith("../") -or $normalized.StartsWith("..\")) {
        $normalized = $normalized.Substring(3)
    }

    return ($normalized -replace '\\', '/')
}

function ConvertTo-YamlScalar {
    param($Value)

    if ($null -eq $Value) {
        return "null"
    }

    if ($Value -is [bool]) {
        return $Value.ToString().ToLowerInvariant()
    }

    $text = [string]$Value
    return "'{0}'" -f ($text -replace "'", "''")
}

function ConvertTo-PascalCase {
    param([AllowNull()] [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $tokens = [regex]::Matches($Value, "[A-Za-z0-9]+") | ForEach-Object { $_.Value }
    if ($tokens.Count -eq 0) {
        return ""
    }

    return (($tokens | ForEach-Object {
                if ($_.Length -eq 1) { $_.ToUpperInvariant() }
                else { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }
            }) -join "")
}

function Add-Category {
    param(
        [System.Collections.Generic.List[string]]$Categories,
        [AllowNull()] [string]$Category
    )

    if ([string]::IsNullOrWhiteSpace($Category)) {
        return
    }

    if (-not $Categories.Contains($Category)) {
        $Categories.Add($Category)
    }
}

function Get-SuiteMap {
    $map = [ordered]@{}
    $map["tests/OpenClawNet.PlaywrightTests"] = [ordered]@{
        id = "playwright"
        label = "Playwright UI E2E"
        project = "tests/OpenClawNet.PlaywrightTests"
        filter = "Category=E2E"
        aspireRequired = $true
        owner = "dylan"
        description = "Full-stack browser tests. Require the Aspire AppHost lifecycle (aspire stop -> aspire start -> aspire describe --format Json) and Azure OpenAI credentials."
        categories = @("E2E", "Playwright", "UI")
    }
    $map["tests/OpenClawNet.E2ETests"] = [ordered]@{
        id = "gateway-e2e"
        label = "Gateway E2E"
        project = "tests/OpenClawNet.E2ETests"
        filter = "Category=Live"
        aspireRequired = $false
        owner = "dylan"
        description = "API-level end-to-end tests against a real gateway (in-process, no browser)."
        categories = @("E2E", "Gateway")
    }
    $map["tests/OpenClawNet.IntegrationTests"] = [ordered]@{
        id = "integration"
        label = "Integration Tests"
        project = "tests/OpenClawNet.IntegrationTests"
        filter = ""
        aspireRequired = $false
        owner = "dylan"
        description = "In-process gateway with a real HTTP test server. Most run without Azure OpenAI."
        categories = @("Integration")
    }
    $map["tests/OpenClawNet.UnitTests"] = [ordered]@{
        id = "unit"
        label = "Unit Tests"
        project = "tests/OpenClawNet.UnitTests"
        filter = ""
        aspireRequired = $false
        owner = "dylan"
        description = "Fast, fully isolated tests with no network or Azure dependencies."
        categories = @("Unit")
    }
    $map["tests/OpenClawNet.UnitTests.Azure"] = [ordered]@{
        id = "azure-unit"
        label = "Azure-specific Tests"
        project = "tests/OpenClawNet.UnitTests.Azure"
        filter = "Category=Live"
        aspireRequired = $false
        owner = "dylan"
        description = "Require real Azure credentials."
        categories = @("Unit", "Azure")
    }

    return $map
}

function Get-SuiteOrder {
    return @("playwright", "gateway-e2e", "integration", "unit", "azure-unit")
}

function Get-SuiteForFile {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [hashtable]$SuiteMap
    )

    foreach ($project in $SuiteMap.Keys) {
        if ($FilePath.StartsWith($project + "/", [System.StringComparison]::OrdinalIgnoreCase) -or
            $FilePath -eq $project) {
            return $SuiteMap[$project]
        }
    }

    throw "No suite mapping found for file '$FilePath'."
}

function Get-IssueRef {
    param(
        [AllowNull()] [string]$Proves,
        [AllowNull()] [string]$Notes
    )

    foreach ($candidate in @($Proves, $Notes)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and $candidate -match "#(?<number>\d+)") {
            return "#$($Matches.number)"
        }
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
    elseif ($text -match '^(?<prefix>.+?)\s*[·•┬╖]+\s*(?<label>.+)$') {
        $className = $Matches.prefix.Trim()
        $methodName = ConvertTo-PascalCase $Matches.label.Trim()
    }
    else {
        $className = $text
    }

    if ([string]::IsNullOrWhiteSpace($className)) {
        throw "Could not determine class name from '$LinkText'."
    }

    $id = if ([string]::IsNullOrWhiteSpace($methodName)) { $className } else { "$className.$methodName" }

    return [ordered]@{
        id = $id
        className = $className
        methodName = $methodName
    }
}

function Get-ExtraCategories {
    param(
        [Parameter(Mandatory)] [string]$ClassName,
        [AllowNull()] [string]$MethodName,
        [Parameter(Mandatory)] [string]$FilePath,
        [AllowNull()] [string]$Proves,
        [AllowNull()] [string]$Notes,
        [AllowNull()] [string]$SectionTitle
    )

    $categories = [System.Collections.Generic.List[string]]::new()
    $haystack = @($ClassName, $MethodName, $FilePath, $Proves, $SectionTitle) -join " "

    $tagMap = [ordered]@{
        "Adapter" = "adapter"
        "Activity" = "activity"
        "Agent" = "agent"
        "Aspire" = "aspire"
        "Blazor" = "blazor"
        "Browser" = "browser"
        "Calendar" = "calendar"
        "Channels" = "channel"
        "Chat" = "chat"
        "Dashboard" = "dashboard"
        "Gateway" = "gateway"
        "GitHub" = "github"
        "Gmail" = "gmail"
        "Health" = "health"
        "Jobs" = "job"
        "Memory" = "memory"
        "OAuth" = "oauth|googleoauth"
        "Provider" = "provider|model"
        "SecretsVault" = "secret|vault"
        "Sessions" = "session"
        "Skills" = "skill"
        "Storage" = "storage"
        "Tool" = "tool"
        "ToolApproval" = "approval"
        "WebsiteWatcher" = "watcher"
    }

    foreach ($category in $tagMap.Keys) {
        if ($haystack -match $tagMap[$category]) {
            Add-Category -Categories $categories -Category $category
        }
    }

    return $categories
}

function Add-OrMergeEntry {
    param(
        [Parameter(Mandatory)] [hashtable]$Table,
        [Parameter(Mandatory)] [hashtable]$Entry
    )

    $key = $Entry.id
    if (-not $Table.ContainsKey($key)) {
        $Table[$key] = $Entry
        return
    }

    $existing = $Table[$key]
    if ([string]::IsNullOrWhiteSpace($existing.proves) -or $existing.proves -like "Coverage entry seeded from repository test inventory*") {
        $existing.proves = $Entry.proves
    }

    if ([string]::IsNullOrWhiteSpace($existing.issueRef) -and -not [string]::IsNullOrWhiteSpace($Entry.issueRef)) {
        $existing.issueRef = $Entry.issueRef
    }

    foreach ($category in $Entry.category) {
        Add-Category -Categories $existing.category -Category $category
    }
}

function Get-TestInventory {
    param([Parameter(Mandatory)] [hashtable]$SuiteMap)

    $inventory = New-Object System.Collections.Generic.List[hashtable]
    $classPattern = [regex]'public\s+(?:sealed\s+|abstract\s+|partial\s+)?class\s+(?<name>[A-Za-z0-9_]+(?:Tests?|E2ETests?))\b'

    foreach ($project in $SuiteMap.Keys) {
        $projectPath = Resolve-RepoPath $project
        foreach ($file in Get-ChildItem -Path $projectPath -Recurse -Filter *.cs -File) {
            $relative = ConvertTo-RepoRelativePath ($file.FullName.Substring($repoRoot.Length + 1))
            $content = Get-Content -LiteralPath $file.FullName -Raw
            foreach ($match in $classPattern.Matches($content)) {
                $inventory.Add([ordered]@{
                        className = $match.Groups["name"].Value
                        file = $relative
                        suite = (Get-SuiteForFile -FilePath $relative -SuiteMap $SuiteMap)
                    })
            }
        }
    }

    return $inventory
}

function Test-IsClassCovered {
    param(
        [Parameter(Mandatory)] [hashtable]$Entries,
        [Parameter(Mandatory)] [string]$ClassName
    )

    foreach ($key in $Entries.Keys) {
        $entry = $Entries[$key]
        if ($entry.className -eq $ClassName) {
            return $true
        }
    }

    return $false
}

function ConvertTo-CatalogYaml {
    param(
        [Parameter(Mandatory)] [hashtable]$SuiteMap,
        [Parameter(Mandatory)] [System.Collections.Generic.List[hashtable]]$Tests,
        [Parameter(Mandatory)] [string[]]$SuiteOrder
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Seeded by scripts\seed-test-catalog.ps1 from docs/testing/e2e-test-index.md.")
    $lines.Add("version: 1")
    $lines.Add("suites:")

    foreach ($suiteId in $SuiteOrder) {
        $suite = $SuiteMap.Values | Where-Object { $_.id -eq $suiteId } | Select-Object -First 1
        if ($null -eq $suite) {
            continue
        }
        $lines.Add("  - id: $(ConvertTo-YamlScalar $suite.id)")
        $lines.Add("    label: $(ConvertTo-YamlScalar $suite.label)")
        $lines.Add("    project: $(ConvertTo-YamlScalar $suite.project)")
        if (-not [string]::IsNullOrWhiteSpace($suite.filter)) {
            $lines.Add("    filter: $(ConvertTo-YamlScalar $suite.filter)")
        }
        $lines.Add("    aspireRequired: $(ConvertTo-YamlScalar $suite.aspireRequired)")
        $lines.Add("    owner: $(ConvertTo-YamlScalar $suite.owner)")
        $lines.Add("    description: $(ConvertTo-YamlScalar $suite.description)")
    }

    $lines.Add("tests:")
    foreach ($test in $Tests) {
        $lines.Add("  - id: $(ConvertTo-YamlScalar $test.id)")
        $lines.Add("    suite: $(ConvertTo-YamlScalar $test.suite)")
        $lines.Add("    file: $(ConvertTo-YamlScalar $test.file)")
        $lines.Add("    className: $(ConvertTo-YamlScalar $test.className)")
        if (-not [string]::IsNullOrWhiteSpace($test.methodName)) {
            $lines.Add("    methodName: $(ConvertTo-YamlScalar $test.methodName)")
        }
        $lines.Add("    proves: $(ConvertTo-YamlScalar $test.proves)")
        $lines.Add("    category:")
        foreach ($category in $test.category) {
            $lines.Add("      - $(ConvertTo-YamlScalar $category)")
        }
        if ($null -ne $test.issueRef) {
            $lines.Add("    issueRef: $(ConvertTo-YamlScalar $test.issueRef)")
        }
        else {
            $lines.Add("    issueRef: null")
        }
        if ($null -ne $test.owner) {
            $lines.Add("    owner: $(ConvertTo-YamlScalar $test.owner)")
        }
    }

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

$indexFullPath = Resolve-RepoPath $IndexPath
$outputFullPath = Resolve-RepoPath $OutputPath
$suiteMap = Get-SuiteMap
$suiteOrder = Get-SuiteOrder
$suiteRank = @{}
for ($index = 0; $index -lt $suiteOrder.Count; $index++) {
    $suiteRank[$suiteOrder[$index]] = $index
}
$entries = @{}

$currentSectionTitle = $null
foreach ($line in Get-Content -LiteralPath $indexFullPath) {
    if ($line -match '^##\s+\d+\.\s+(?<title>.+)$') {
        $currentSectionTitle = ($Matches.title -replace '\s*[—ΓÇö]+\s*`.*$', '').Trim()
        continue
    }

    if (-not $line.TrimStart().StartsWith("| [")) {
        continue
    }

    $parts = $line.Trim().Trim('|').Split('|')
    if ($parts.Count -lt 5) {
        throw "Unexpected markdown row format: $line"
    }

    $linkCell = $parts[0].Trim()
    $proves = $parts[1].Trim()
    $notes = $parts[4].Trim()

    if ($linkCell -notmatch '^\[(?<text>.+?)\]\((?<target>.+?)\)$') {
        throw "Unexpected link cell format: $linkCell"
    }

    $descriptor = Get-DescriptorFromLinkText $Matches.text
    $filePath = ConvertTo-RepoRelativePath $Matches.target
    $suite = Get-SuiteForFile -FilePath $filePath -SuiteMap $suiteMap
    $categories = [System.Collections.Generic.List[string]]::new()

    foreach ($category in $suite.categories) {
        Add-Category -Categories $categories -Category $category
    }

    foreach ($category in (Get-ExtraCategories -ClassName $descriptor.className -MethodName $descriptor.methodName -FilePath $filePath -Proves $proves -Notes $notes -SectionTitle $currentSectionTitle)) {
        Add-Category -Categories $categories -Category $category
    }

    $entry = [ordered]@{
        id = $descriptor.id
        suite = $suite.id
        file = $filePath
        className = $descriptor.className
        methodName = $descriptor.methodName
        proves = $proves
        category = $categories
        issueRef = Get-IssueRef -Proves $proves -Notes $notes
        owner = $null
    }

    Add-OrMergeEntry -Table $entries -Entry $entry
}

$inventory = Get-TestInventory -SuiteMap $suiteMap
$inventoryAdded = 0

foreach ($item in $inventory) {
    if (Test-IsClassCovered -Entries $entries -ClassName $item.className) {
        continue
    }

    $categories = [System.Collections.Generic.List[string]]::new()
    foreach ($category in $item.suite.categories) {
        Add-Category -Categories $categories -Category $category
    }
    foreach ($category in (Get-ExtraCategories -ClassName $item.className -MethodName $null -FilePath $item.file -Proves "" -Notes "" -SectionTitle "")) {
        Add-Category -Categories $categories -Category $category
    }

    $placeholder = [ordered]@{
        id = $item.className
        suite = $item.suite.id
        file = $item.file
        className = $item.className
        methodName = $null
        proves = "Coverage entry seeded from repository test inventory; refine with scenario prose before generator cutover."
        category = $categories
        issueRef = $null
        owner = $null
    }

    Add-OrMergeEntry -Table $entries -Entry $placeholder
    $inventoryAdded++
}

$remainingMissing = New-Object System.Collections.Generic.List[string]
foreach ($item in $inventory) {
    if (-not (Test-IsClassCovered -Entries $entries -ClassName $item.className)) {
        $remainingMissing.Add($item.className)
    }
}

if ($remainingMissing.Count -gt 0) {
    throw "Catalog generation failed. Missing test classes: $($remainingMissing -join ', ')"
}

$tests = New-Object System.Collections.Generic.List[hashtable]
foreach ($entry in ($entries.Values | Sort-Object @{ Expression = { $suiteRank[$_.suite] } }, className, methodName, id)) {
    $tests.Add($entry)
}

$yaml = ConvertTo-CatalogYaml -SuiteMap $suiteMap -Tests $tests -SuiteOrder $suiteOrder
$yaml | Set-Content -LiteralPath $outputFullPath -Encoding utf8

$indexSeedCount = ($entries.Values | Where-Object { $_.proves -notlike "Coverage entry seeded from repository test inventory*" }).Count
Write-Host "Seeded $($suiteMap.Count) suites and $($tests.Count) catalog entries." -ForegroundColor Green
Write-Host "Imported $indexSeedCount entries from the current markdown index and added $inventoryAdded inventory-only coverage stubs." -ForegroundColor Green
