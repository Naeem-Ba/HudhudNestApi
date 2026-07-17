[CmdletBinding()]
param(
    [string] $CoverageRoot = ".",
    [string] $BaselinePath = "ci/coverage-baseline.json",
    [string] $OutputDirectory = "artifacts/coverage"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

if (-not (Test-Path $BaselinePath)) {
    throw "Coverage baseline file was not found: $BaselinePath"
}

$baseline = Get-Content -Path $BaselinePath -Raw | ConvertFrom-Json

$coverageFiles = Get-ChildItem -Path $CoverageRoot -Recurse -Filter "coverage.cobertura.xml" |
    Where-Object { $_.FullName -notmatch "[\\/](artifacts)[\\/](coverage|report)[\\/]" }

if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml files were found under: $CoverageRoot"
}

$totalLinesCovered = 0
$totalLinesValid = 0
$totalBranchesCovered = 0
$totalBranchesValid = 0
$authLinesCovered = 0
$authLinesValid = 0
$authGeneratedClassesExcluded = 0

function Test-IsGeneratedCoverageClass {
    param(
        [string] $ClassName,
        [string] $FileName
    )

    return (
        $ClassName -match '(^|\.)Migrations(\.|$)' -or
        $FileName -match '[\\/](Migrations)[\\/]' -or
        $FileName -match '\.Designer\.cs$'
    )
}

foreach ($file in $coverageFiles) {
    [xml] $document = Get-Content -Path $file.FullName -Raw
    $coverage = $document.coverage

    $totalLinesCovered += [int] $coverage.GetAttribute("lines-covered")
    $totalLinesValid += [int] $coverage.GetAttribute("lines-valid")
    $totalBranchesCovered += [int] $coverage.GetAttribute("branches-covered")
    $totalBranchesValid += [int] $coverage.GetAttribute("branches-valid")

    $classes = @($coverage.packages.package.classes.class)

    foreach ($class in $classes) {
        $className = [string] $class.name
        $fileName = [string] $class.filename

        $isAuthSensitive =
            $className -match "(Auth|Identity|Security|Otp|Token|Login|Register|Password)" -or
            $fileName -match "(Auth|Identity|Security|Otp|Token|Login|Register|Password)"

        if (-not $isAuthSensitive) {
            continue
        }

        if (Test-IsGeneratedCoverageClass -ClassName $className -FileName $fileName) {
            $authGeneratedClassesExcluded++
            continue
        }

        $lines = @($class.lines.line)
        $authLinesValid += $lines.Count
        $authLinesCovered += @($lines | Where-Object { [int] $_.hits -gt 0 }).Count
    }
}

function Get-Percent([int] $covered, [int] $valid) {
    if ($valid -le 0) {
        return 100.0
    }

    return [Math]::Round(($covered / $valid) * 100.0, 2)
}

$lineCoverage = Get-Percent $totalLinesCovered $totalLinesValid
$branchCoverage = Get-Percent $totalBranchesCovered $totalBranchesValid
$authLineCoverage = Get-Percent $authLinesCovered $authLinesValid

$minimumLineCoverage = [double] $baseline.minimumLineCoveragePercent
$minimumBranchCoverage = [double] $baseline.minimumBranchCoveragePercent
$minimumAuthLineCoverage = [double] $baseline.minimumAuthLineCoveragePercent

$summary = @(
    "# Coverage Gate",
    "",
    "Coverage files: $($coverageFiles.Count)",
    "",
    "| Metric | Actual | Minimum |",
    "| --- | ---: | ---: |",
    "| Line coverage | $lineCoverage% | $minimumLineCoverage% |",
    "| Branch coverage | $branchCoverage% | $minimumBranchCoverage% |",
    "| Auth-sensitive line coverage | $authLineCoverage% | $minimumAuthLineCoverage% |",
    "",
    "Auth-sensitive generated classes excluded: $authGeneratedClassesExcluded"
)

$summaryPath = Join-Path $OutputDirectory "coverage-gate-summary.md"
$jsonPath = Join-Path $OutputDirectory "coverage-gate-summary.json"

$summary | Set-Content -Path $summaryPath -Encoding utf8

[pscustomobject]@{
    coverageFiles = $coverageFiles.Count
    lineCoveragePercent = $lineCoverage
    branchCoveragePercent = $branchCoverage
    authLineCoveragePercent = $authLineCoverage
    authGeneratedClassesExcluded = $authGeneratedClassesExcluded
    minimumLineCoveragePercent = $minimumLineCoverage
    minimumBranchCoveragePercent = $minimumBranchCoverage
    minimumAuthLineCoveragePercent = $minimumAuthLineCoverage
} | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonPath -Encoding utf8

Get-Content $summaryPath

$failureMessages = New-Object System.Collections.Generic.List[string]

if ($lineCoverage -lt $minimumLineCoverage) {
    $failureMessages.Add("Line coverage $lineCoverage% is below baseline $minimumLineCoverage%.")
}

if ($branchCoverage -lt $minimumBranchCoverage) {
    $failureMessages.Add("Branch coverage $branchCoverage% is below baseline $minimumBranchCoverage%.")
}

if ($authLineCoverage -lt $minimumAuthLineCoverage) {
    $failureMessages.Add("Auth-sensitive line coverage $authLineCoverage% is below baseline $minimumAuthLineCoverage%.")
}

if ($failureMessages.Count -gt 0) {
    foreach ($message in $failureMessages) {
        Write-Host "::error::$message"
    }

    exit 1
}
