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
