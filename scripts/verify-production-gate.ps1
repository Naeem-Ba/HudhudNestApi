[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path ".").Path
)

$ErrorActionPreference = "Stop"

$failures = New-Object System.Collections.Generic.List[string]
$artifactDirectory = Join-Path $RepositoryRoot "artifacts/production-gate"
$reportPath = Join-Path $artifactDirectory "static-production-gate.md"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

function Read-RepositoryFile {
    param([string[]] $RelativePath)

    $path = $RepositoryRoot
    foreach ($part in $RelativePath) {
        $path = Join-Path $path $part
    }

    if (-not (Test-Path $path)) {
        $failures.Add("Missing required file: $($RelativePath -join '/')")
        return ""
    }

    return Get-Content -Path $path -Raw
}

function Assert-Contains {
    param(
        [string] $Source,
        [string] $Needle,
        [string] $Description
    )

    if ($Source -notlike "*$Needle*") {
        $failures.Add($Description)
    }
}

function Assert-NotContains {
    param(
        [string] $Source,
        [string] $Needle,
        [string] $Description
    )

    if ($Source -like "*$Needle*") {
        $failures.Add($Description)
    }
}

$program = Read-RepositoryFile @("PropertyApi", "Program.cs")
$apiDockerfile = Read-RepositoryFile @("PropertyApi", "Dockerfile")
$migratorDockerfile = Read-RepositoryFile @("ci", "Dockerfile.migrator")
$compose = Read-RepositoryFile @("ci", "docker-compose.production-gate.yml")
$dependencyInjection = Read-RepositoryFile @("PropertyApi.Infrastructure", "DependencyInjection.cs")
$workflow = Read-RepositoryFile @(".github", "workflows", "production-gate.yml")
$stagingSmoke = Read-RepositoryFile @("scripts", "smoke-staging.sh")
$performanceWorkflow = Read-RepositoryFile @(".github", "workflows", "performance-validation.yml")
$performanceRunner = Read-RepositoryFile @("scripts", "run-performance-tests.sh")

Assert-Contains $program "UseForwardedHeaders" "Program.cs must apply forwarded headers before security middleware."
Assert-Contains $program "UseHsts" "Program.cs must enable HSTS for production."
Assert-Contains $program "MapOperationalHealthEndpoints" "Program.cs must map operational health endpoints."
Assert-Contains $program "UsePropertyApiSecurityHeaders" "Program.cs must apply security headers middleware."

Assert-Contains $dependencyInjection "ConfigureDataProtection" "Infrastructure composition must configure Data Protection."

Assert-Contains $apiDockerfile 'USER $APP_UID' "API Dockerfile must run as a non-root user."
Assert-Contains $migratorDockerfile "USER app" "Migrator Dockerfile must run as a non-root user."

Assert-Contains $compose "DataProtection__PersistKeysToDatabase" "Production gate compose must persist Data Protection keys."
Assert-Contains $compose 'Redis__Required: "true"' "Production gate compose must require Redis for the API."
Assert-Contains $compose "Cloudinary__CloudName" "Production gate compose must provide Cloudinary options for startup validation."
Assert-NotContains $compose "Trust Server Certificate=true" "Production gate compose must not trust invalid database certificates."

Assert-Contains $workflow "needs.staging-smoke.result == 'success'" "Production deployment must require a successful Staging job."
Assert-Contains $workflow "needs.performance-validation.result == 'success'" "Production deployment must require successful concurrent performance validation."
Assert-Contains $workflow "/api/operational/build-info" "Staging deployment must verify sanitized build metadata."
Assert-Contains $workflow "STAGING_DEPLOY_HOOK_URL" "Staging deployment hook must be mandatory."
Assert-NotContains $workflow "configured=false" "Staging configuration must not succeed through an optional configured=false path."
Assert-NotContains $workflow "continue-on-error: true" "Mandatory release jobs must not continue on error."

Assert-Contains $stagingSmoke "set -Eeuo pipefail" "Staging smoke script must use strict Bash error handling."
Assert-Contains $stagingSmoke "PropertyApi.StagingSmokeTests.csproj" "Staging smoke script must run the complete .NET journey suite."
Assert-Contains $stagingSmoke "staging-smoke-report.json" "Staging smoke script must require a JSON report."
Assert-Contains $stagingSmoke "staging-smoke-junit.xml" "Staging smoke script must require a JUnit report."

Assert-Contains $performanceWorkflow "run-performance-tests.sh" "Performance workflow must run the mandatory concurrent suite."
Assert-Contains $performanceWorkflow "if-no-files-found: error" "Performance evidence must be mandatory."
Assert-NotContains $performanceWorkflow "continue-on-error" "Mandatory performance validation must not continue on error."
Assert-Contains $performanceRunner "docker compose" "Performance runner must create the isolated multi-service environment."
Assert-Contains $performanceRunner "PERF_REQUIRE_APPROVED_BUDGETS" "Release performance budgets must fail closed until approved."
Assert-Contains $performanceRunner "capture-pg-stat-statements.sh" "Performance runner must capture pg_stat_statements."
Assert-Contains $performanceRunner "capture-query-plans.sh" "Performance runner must capture EXPLAIN ANALYZE plans."

$status = if ($failures.Count -eq 0) { "passed" } else { "failed" }

$report = @(
    "# Static Production Gate",
    "",
    "Status: $status",
    ""
)

if ($failures.Count -eq 0) {
    $report += "All static production checks passed."
}
else {
    $report += "Failures:"
    $report += ""
    foreach ($failure in $failures) {
        $report += "- $failure"
    }
}

$report | Set-Content -Path $reportPath -Encoding utf8
Get-Content $reportPath

if ($failures.Count -gt 0) {
    exit 1
}
