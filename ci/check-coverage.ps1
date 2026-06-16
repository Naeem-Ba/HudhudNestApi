param(
    [string]$SummaryPath = "coverage-report/Summary.json",
    [int]$Threshold = 80
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SummaryPath)) {
    throw "Coverage summary file not found: $SummaryPath"
}

$summary = Get-Content $SummaryPath -Raw | ConvertFrom-Json

$coverage = $null

if ($summary.summary -and $null -ne $summary.summary.linecoverage) {
    $coverage = [double]$summary.summary.linecoverage
} elseif ($null -ne $summary.linecoverage) {
    $coverage = [double]$summary.linecoverage
} elseif ($summary.summary -and $null -ne $summary.summary.LineCoverage) {
    $coverage = [double]$summary.summary.LineCoverage
}

if ($null -eq $coverage) {
    Write-Host "Coverage summary content:"
    Get-Content $SummaryPath | Write-Host
    throw "Could not find line coverage value in ReportGenerator Summary.json."
}

Write-Host "Line coverage: $coverage%"
Write-Host "Required threshold: $Threshold%"

if ($coverage -lt $Threshold) {
    Write-Error "Coverage is below $Threshold%. Current line coverage: $coverage%."
    exit 1
}

Write-Host "Coverage threshold passed."
exit 0
