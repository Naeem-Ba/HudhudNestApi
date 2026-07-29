[CmdletBinding()]
param(
    [string] $TestResultsRoot = "artifacts/test-results",
    [string[]] $ExpectedTrxFileNames = @(
        "application-tests.trx",
        "auth-tests.trx",
        "integration-tests.trx",
        "architecture-tests.trx"
    ),
    [string] $OutputDirectory = "artifacts/supply-chain"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$reportPath = Join-Path $OutputDirectory "test-results-validation.json"
$failures = New-Object System.Collections.Generic.List[string]
$resultReports = New-Object System.Collections.Generic.List[object]

if (-not (Test-Path $TestResultsRoot)) {
    throw "Test results root was not found: $TestResultsRoot"
}

$trxFiles = Get-ChildItem -Path $TestResultsRoot -Recurse -Filter "*.trx" -File

foreach ($expectedTrxFileName in $ExpectedTrxFileNames) {
    if (-not ($trxFiles | Where-Object { $_.Name -eq $expectedTrxFileName })) {
        $failures.Add("Expected TRX result file was not found: $expectedTrxFileName")
    }
}

foreach ($trxFile in $trxFiles) {
    [xml] $trx = Get-Content -Path $trxFile.FullName -Raw
    $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")

    if ($null -eq $counters) {
        $failures.Add("$($trxFile.FullName): missing TRX Counters element.")
        continue
    }

    $total = [int] $counters.GetAttribute("total")
    $executed = [int] $counters.GetAttribute("executed")
    $passed = [int] $counters.GetAttribute("passed")
    $failed = [int] $counters.GetAttribute("failed")

    if ($total -le 0 -or $executed -le 0) {
        $failures.Add("$($trxFile.Name): reported zero executed tests.")
    }

    $testResultStatus = "failed"
    if ($total -gt 0 -and $executed -gt 0) {
        $testResultStatus = "passed"
    }

    $resultReports.Add([pscustomobject]@{
        file = $trxFile.FullName
        total = $total
        executed = $executed
        passed = $passed
        failed = $failed
        result = $testResultStatus
    })
}

$overallStatus = "failed"
if ($failures.Count -eq 0) {
    $overallStatus = "passed"
}

$discoveredTrxFiles = @($trxFiles | ForEach-Object { $_.FullName })
$testResultArray = @($resultReports.ToArray())
$failureArray = @($failures.ToArray())

$report = [pscustomobject]@{
    schemaVersion = 1
    status = $overallStatus
    expectedTrxFiles = $ExpectedTrxFileNames
    discoveredTrxFiles = $discoveredTrxFiles
    testResults = $testResultArray
    failures = $failureArray
}

$report | ConvertTo-Json -Depth 5 | Set-Content -Path $reportPath -Encoding utf8

Write-Host "Test results validation report: $reportPath"
Write-Host "Status: $($report.status)"

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "::error::$failure"
    }

    exit 1
}
