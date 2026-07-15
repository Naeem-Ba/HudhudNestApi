[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $OutputDirectory = "artifacts/phase-b-closeout",
    [string] $StagingBaseUrl = "",
    [string] $PerformanceBaselinePath = "",
    [string] $PerformanceCurrentPath = "",
    [switch] $SkipIntegrationTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$runId = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$results = New-Object System.Collections.Generic.List[object]
$hasFailures = $false

function Add-GateResult {
    param(
        [string] $Name,
        [string] $Status,
        [string] $Details = ""
    )

    $script:results.Add([pscustomobject]@{
        name = $Name
        status = $Status
        details = $Details
    })
}

function Invoke-Gate {
    param(
        [string] $Name,
        [scriptblock] $Command
    )

    try {
        & $Command
        Add-GateResult $Name "pass"
    }
    catch {
        $script:hasFailures = $true
        Add-GateResult $Name "fail" $_.Exception.Message
    }
}

function Assert-StagingUrl {
    param([string] $Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        throw "StagingBaseUrl is required for smoke tests."
    }

    $uri = [Uri] $Url
    $host = $uri.Host.ToLowerInvariant()

    if ($host.Contains("prod") -or $host.Contains("production")) {
        throw "Refusing to run smoke tests against a host that looks like production: $host"
    }

    return $uri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")
}

function Invoke-SmokeCheck {
    param([string] $BaseUrl)

    $base = Assert-StagingUrl $BaseUrl
    $paths = @(
        "/health/live",
        "/health/ready",
        "/api/enums/PropertyStatus"
    )

    foreach ($path in $paths) {
        $response = Invoke-WebRequest `
            -Uri "$base$path" `
            -UseBasicParsing `
            -TimeoutSec 10

        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
            throw "Smoke check failed for $path with HTTP $($response.StatusCode)."
        }
    }
}

function Compare-Performance {
    param(
        [string] $BaselinePath,
        [string] $CurrentPath
    )

    if ([string]::IsNullOrWhiteSpace($BaselinePath) -or
        [string]::IsNullOrWhiteSpace($CurrentPath)) {
        Add-GateResult "performance-compare" "skipped" "Provide PerformanceBaselinePath and PerformanceCurrentPath to compare p95/p99/throughput."
        return
    }

    $baseline = Get-Content -Path $BaselinePath -Raw | ConvertFrom-Json
    $current = Get-Content -Path $CurrentPath -Raw | ConvertFrom-Json
    $failures = New-Object System.Collections.Generic.List[string]

    foreach ($currentScenario in $current.results) {
        if ($currentScenario.skipped) {
            continue
        }

        $baselineScenario = @($baseline.results | Where-Object { $_.name -eq $currentScenario.name }) | Select-Object -First 1

        if ($null -eq $baselineScenario -or $baselineScenario.skipped) {
            continue
        }

        if ($baselineScenario.p95Ms -gt 0 -and
            $currentScenario.p95Ms -gt ($baselineScenario.p95Ms * 1.20)) {
            $failures.Add("$($currentScenario.name) p95 regressed from $($baselineScenario.p95Ms) ms to $($currentScenario.p95Ms) ms.")
        }

        if ($baselineScenario.throughputRps -gt 0 -and
            $currentScenario.throughputRps -lt ($baselineScenario.throughputRps * 0.80)) {
            $failures.Add("$($currentScenario.name) throughput regressed from $($baselineScenario.throughputRps) rps to $($currentScenario.throughputRps) rps.")
        }
    }

    if ($failures.Count -gt 0) {
        throw ($failures -join " ")
    }

    Add-GateResult "performance-compare" "pass"
}

Invoke-Gate "restore" { dotnet restore }
Invoke-Gate "format" { dotnet format PropertyApi.sln --verify-no-changes --no-restore }
Invoke-Gate "build" { dotnet build PropertyApi.sln --configuration $Configuration --no-restore }
Invoke-Gate "application-tests" { dotnet test tests/PropertyApi.Application.Tests/PropertyApi.Application.Tests.csproj --configuration $Configuration --no-build }
Invoke-Gate "auth-tests" { dotnet test tests/PropertyApi.Auth.Tests/PropertyApi.Auth.Tests.csproj --configuration $Configuration --no-build }
Invoke-Gate "architecture-tests" { dotnet test tests/PropertyApi.Architecture.Tests/PropertyApi.Architecture.Tests.csproj --configuration $Configuration --no-build }

if ($SkipIntegrationTests) {
    Add-GateResult "integration-tests" "skipped" "SkipIntegrationTests was specified."
}
else {
    Invoke-Gate "integration-tests" { dotnet test tests/PropertyApi.Integration.Tests/PropertyApi.Integration.Tests.csproj --configuration $Configuration --no-build }
}

Invoke-Gate "vulnerability-baseline" { powershell -NoProfile -ExecutionPolicy Bypass -File ci/check-vulnerable-packages.ps1 }
Invoke-Gate "security-telemetry-review" {
    if (-not (Test-Path "docs/phase-b-observability.md")) {
        throw "docs/phase-b-observability.md is missing."
    }

    if (-not (Test-Path "ci/vulnerability-baseline.json")) {
        throw "ci/vulnerability-baseline.json is missing."
    }
}

if ([string]::IsNullOrWhiteSpace($StagingBaseUrl)) {
    Add-GateResult "staging-smoke" "skipped" "Provide StagingBaseUrl to run smoke checks."
}
else {
    Invoke-Gate "staging-smoke" { Invoke-SmokeCheck $StagingBaseUrl }
}

Invoke-Gate "performance-compare" { Compare-Performance $PerformanceBaselinePath $PerformanceCurrentPath }

$decision = if ($hasFailures) { "NO-GO" } else { "GO" }
$jsonPath = Join-Path $OutputDirectory "phase-b-closeout-$runId.json"
$markdownPath = Join-Path $OutputDirectory "phase-b-closeout-$runId.md"

$report = [pscustomobject]@{
    runId = $runId
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    decision = $decision
    gates = $results
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add("# Phase B Closeout")
$markdown.Add("")
$markdown.Add("Decision: $decision")
$markdown.Add("")
$markdown.Add("| Gate | Status | Details |")
$markdown.Add("| --- | --- | --- |")

foreach ($result in $results) {
    $markdown.Add("| $($result.name) | $($result.status) | $($result.details) |")
}

$markdown | Set-Content -Path $markdownPath -Encoding utf8
Get-Content $markdownPath
Write-Host "JSON: $jsonPath"

if ($hasFailures) {
    exit 1
}
