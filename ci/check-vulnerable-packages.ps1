[CmdletBinding()]
param(
    [string] $SolutionPath = "PropertyApi.sln",
    [string] $BaselinePath = "ci/vulnerability-baseline.json",
    [string] $OutputDirectory = "artifacts/vulnerability"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$resolvedSolutionPath = (Resolve-Path $SolutionPath).Path

$reportPath = Join-Path $OutputDirectory "vulnerable-packages.txt"
$summaryPath = Join-Path $OutputDirectory "vulnerability-summary.md"

$output = & dotnet list $resolvedSolutionPath package --vulnerable --include-transitive 2>&1
$exitCode = $LASTEXITCODE

$output | Set-Content -Path $reportPath -Encoding utf8
$outputText = $output -join [Environment]::NewLine

if ($exitCode -ne 0) {
    @(
        "# Vulnerability Gate"
        ""
        "Status: failed"
        ""
        "The dotnet vulnerable-package scan command failed."
    ) | Set-Content -Path $summaryPath -Encoding utf8

    Get-Content $reportPath
    exit $exitCode
}

$hasVulnerabilities =
    ($output | Select-String -Pattern "has the following vulnerable packages" -Quiet) -or
    ($output | Select-String -Pattern "^\s*>\s" -Quiet)

if ($hasVulnerabilities) {
    $detectedAdvisories = [regex]::Matches(
        $outputText,
        "GHSA-[0-9a-z]{4}-[0-9a-z]{4}-[0-9a-z]{4}",
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) |
        ForEach-Object { $_.Value.ToLowerInvariant() } |
        Sort-Object -Unique

    $acceptedAdvisories = @()

    if (Test-Path $BaselinePath) {
        $baseline = Get-Content -Path $BaselinePath -Raw | ConvertFrom-Json
        $acceptedAdvisories = @($baseline.acceptedAdvisories) |
            ForEach-Object { [string] $_.id } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.ToLowerInvariant() } |
            Sort-Object -Unique
    }

    $newAdvisories = @($detectedAdvisories | Where-Object { $_ -notin $acceptedAdvisories })

    if ($newAdvisories.Count -eq 0) {
        @(
            "# Vulnerability Gate"
            ""
            "Status: passed with baseline"
            ""
            "Detected vulnerable package advisories are already recorded in the baseline."
            ""
            "Baseline: $BaselinePath"
            ""
            "Accepted advisories:"
        ) + ($detectedAdvisories | ForEach-Object { "- $_" }) |
            Set-Content -Path $summaryPath -Encoding utf8

        Get-Content $summaryPath
        exit 0
    }

    @(
        "# Vulnerability Gate"
        ""
        "Status: failed"
        ""
        "One or more new vulnerable NuGet advisories were detected."
        ""
        "New advisories:"
    ) + ($newAdvisories | ForEach-Object { "- $_" }) + @(
        ""
        "See vulnerable-packages.txt for the full report."
        "Update dependencies or explicitly update ci/vulnerability-baseline.json with a tracked rationale."
    ) | Set-Content -Path $summaryPath -Encoding utf8

    Get-Content $reportPath
    exit 1
}

@(
    "# Vulnerability Gate"
    ""
    "Status: passed"
    ""
    "No vulnerable NuGet packages were reported by dotnet list package."
) | Set-Content -Path $summaryPath -Encoding utf8

Get-Content $summaryPath
