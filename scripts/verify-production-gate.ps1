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

$program = Read-RepositoryFile @("HudhudNestApi", "Program.cs")
$apiDockerfile = Read-RepositoryFile @("HudhudNestApi", "Dockerfile")
$migratorDockerfile = Read-RepositoryFile @("ci", "Dockerfile.migrator")
$compose = Read-RepositoryFile @("ci", "docker-compose.production-gate.yml")
$dependencyInjection = Read-RepositoryFile @("HudhudNestApi.Infrastructure", "DependencyInjection.cs")
$workflow = Read-RepositoryFile @(".github", "workflows", "production-gate.yml")
$stagingSmoke = Read-RepositoryFile @("scripts", "smoke-staging.sh")
$performanceWorkflow = Read-RepositoryFile @(".github", "workflows", "performance-validation.yml")
$performanceRunner = Read-RepositoryFile @("scripts", "run-performance-tests.sh")

Assert-Contains $program "UseForwardedHeaders" "Program.cs must apply forwarded headers before security middleware."
Assert-Contains $program "UseHsts" "Program.cs must enable HSTS for production."
Assert-Contains $program "MapOperationalHealthEndpoints" "Program.cs must map operational health endpoints."
Assert-Contains $program "UseHudhudNestApiSecurityHeaders" "Program.cs must apply security headers middleware."

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
Assert-Contains $stagingSmoke "HudhudNestApi.StagingSmokeTests.csproj" "Staging smoke script must run the complete .NET journey suite."
Assert-Contains $stagingSmoke "staging-smoke-report.json" "Staging smoke script must require a JSON report."
Assert-Contains $stagingSmoke "staging-smoke-junit.xml" "Staging smoke script must require a JUnit report."

Assert-Contains $performanceWorkflow "run-performance-tests.sh" "Performance workflow must run the mandatory concurrent suite."
Assert-Contains $performanceWorkflow "if-no-files-found: error" "Performance evidence must be mandatory."
Assert-NotContains $performanceWorkflow "continue-on-error" "Mandatory performance validation must not continue on error."
Assert-Contains $performanceRunner "docker compose" "Performance runner must create the isolated multi-service environment."
Assert-Contains $performanceRunner "PERF_REQUIRE_APPROVED_BUDGETS" "Release performance budgets must fail closed until approved."
Assert-Contains $performanceRunner "capture-pg-stat-statements.sh" "Performance runner must capture pg_stat_statements."
Assert-Contains $performanceRunner "capture-query-plans.sh" "Performance runner must capture EXPLAIN ANALYZE plans."

# --- Release-path invariants -------------------------------------------------------------------
# The Assert-Contains checks above match anywhere in the file, so a comment (or an unrelated job)
# satisfies them. These are scoped to one job at a time, ignore comments, and use ordinal matching
# (`[` and `]` in shell tests are -like wildcards).
function Get-JobBlock {
    param([string] $WorkflowText, [string] $Job)

    $block = New-Object System.Collections.Generic.List[string]
    $inside = $false
    foreach ($line in ($WorkflowText -split "`r?`n")) {
        if ($line -match '^  ([A-Za-z0-9_-]+):\s*$') {
            $inside = ($Matches[1] -eq $Job)
            continue
        }
        if ($inside -and $line -notmatch '^\s*#') { $block.Add($line) }
    }
    if ($block.Count -eq 0) { $failures.Add("Workflow job '$Job' is missing.") }
    return ($block -join "`n")
}

function Assert-Has {
    param([string] $Source, [string] $Needle, [string] $Description)
    if (-not $Source.Contains($Needle)) { $failures.Add($Description) }
}

function Assert-Lacks {
    param([string] $Source, [string] $Needle, [string] $Description)
    if ($Source.Contains($Needle)) { $failures.Add($Description) }
}

$deploy = Get-JobBlock $workflow "deploy-production"
$releaseGate = Get-JobBlock $workflow "production-deployment-gate"
$stagingJob = Get-JobBlock $workflow "staging-smoke"

$deployCondition = ""
if ($deploy -match '(?s)\n    if: >-\n(.*?)\n    runs-on:') { $deployCondition = $Matches[1] }
if ([string]::IsNullOrWhiteSpace($deployCondition)) { $failures.Add("deploy-production must have an explicit if: condition.") }

# Production is reachable only by an explicit dispatch from master...
Assert-Has $deployCondition "github.event_name == 'workflow_dispatch'" "deploy-production must run only on workflow_dispatch."
Assert-Has $deployCondition "github.ref == 'refs/heads/master'" "deploy-production must run only from refs/heads/master."
Assert-Lacks $deployCondition "push" "deploy-production must not be reachable from a push."
Assert-Lacks $deployCondition "refs/heads/main" "deploy-production must not accept any ref other than master."
Assert-Lacks $deployCondition "||" "deploy-production's condition must be a pure conjunction; an OR can bypass a gate."
Assert-Lacks $deploy "always()" "deploy-production must not use always(); a failed or skipped gate would no longer block it."
Assert-Lacks $deploy "cancelled()" "deploy-production must not use cancelled(); it removes the implicit success() check."
Assert-Lacks $deploy "continue-on-error" "deploy-production must not continue on error."
Assert-Has $deploy "environment: production" "deploy-production must use the production environment."

# ...only when every gate reported success (in `if:` and in `needs:`)...
foreach ($gate in @(
        "build-test-container", "observability-validation", "redis-sentinel-ha", "staging-smoke",
        "redis-staging-failover", "performance-validation", "production-deployment-gate")) {
    Assert-Has $deployCondition "needs.$gate.result == 'success'" "deploy-production must require needs.$gate.result == 'success'."
    Assert-Has $deploy "      - $gate" "deploy-production must list $gate in needs."
}

# ...only for the commit that was gated, re-checked before the deploy hook fires...
Assert-Has $deploy "commits/master" "deploy-production must compare the gated commit with master's current tip."
Assert-Has $deploy 'GITHUB_SHA,,' "deploy-production must compare against the gated commit (GITHUB_SHA)."
$tipCheckAt = $deploy.IndexOf("Assert the gated commit is still the tip of master")
$hookAt = $deploy.IndexOf("Trigger production deployment")
if ($tipCheckAt -lt 0 -or $hookAt -lt 0 -or $tipCheckAt -gt $hookAt) {
    $failures.Add("The tip-of-master assertion must run before the production deploy hook is triggered.")
}
Assert-Has $deploy "PRODUCTION_DEPLOY_HOOK_URL is required" "The production deploy hook must fail closed when it is not configured."
# ...and the hook itself must be pinned to that commit: without `ref` Render builds whatever the
# tracked branch points at when it handles the request, which the tip check cannot guarantee.
Assert-Has $deploy 'ref=${GITHUB_SHA}' "The production deploy hook must be pinned to the gated commit with ref=`${GITHUB_SHA}."
Assert-Has $deploy '"${pinned_hook_url}"' "deploy-production must call the commit-pinned hook URL, not the bare PRODUCTION_DEPLOY_HOOK_URL."

# The release gate must accept nothing but success from each upstream job, and must verify that
# Render cannot deploy Production around this workflow.
foreach ($result in @("BUILD_RESULT", "STAGING_RESULT", "RECOVERY_RESULT")) {
    Assert-Has $releaseGate ('[ "${' + $result + '}" = "success" ]') "production-deployment-gate must require $result to be exactly 'success'."
}
Assert-Has $releaseGate "autoDeployTrigger" "production-deployment-gate must verify the Render auto-deploy trigger."
Assert-Has $releaseGate "RENDER_SERVICE_ID is not set or is not in the expected" "production-deployment-gate must validate RENDER_SERVICE_ID."
Assert-Lacks $releaseGate "continue-on-error" "production-deployment-gate must not continue on error."

# Staging proof is only valid when it names the commit under test and never skips configuration.
Assert-Has $stagingJob "bash scripts/smoke-staging.sh --validate-only" "staging-smoke must validate its mandatory configuration first."
Assert-Has $stagingJob "STAGING_DATABASE_URL is not set" "staging-smoke must fail closed without STAGING_DATABASE_URL."
Assert-Has $stagingJob 'deployed_sha,,' "staging-smoke must compare the deployed commit with the expected one."
Assert-Lacks $stagingJob "continue-on-error" "staging-smoke must not continue on error."

# Rollback is a protected, master-only, confirmed operation.
$rollback = Read-RepositoryFile @(".github", "workflows", "rollback-production.yml")
Assert-Has $rollback "environment: production-rollback" "Rollback must run in the production-rollback environment."
Assert-Has $rollback "refs/heads/master" "Rollback must be restricted to master."
Assert-Has $rollback "confirm_autodeploy_disabled" "Rollback must require the auto-deploy confirmation."

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
