[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Condition {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        $failures.Add($Message)
    }
}

$solution = Join-Path $RepositoryRoot 'PropertyApi.sln'
$domain = Join-Path $RepositoryRoot 'PropertyApi.Domain'
$application = Join-Path $RepositoryRoot 'PropertyApi.Application'
$program = Join-Path $RepositoryRoot 'PropertyApi/Program.cs'

$forwardedRegistration = Join-Path `
    $RepositoryRoot `
    'PropertyApi/Configuration/ForwardedHeadersRegistration.cs'

$outputCacheRegistration = Join-Path `
    $RepositoryRoot `
    'PropertyApi/Configuration/OutputCacheRegistration.cs'

$healthEndpoints = Join-Path `
    $RepositoryRoot `
    'PropertyApi/Health/HealthEndpointExtensions.cs'

Assert-Condition `
    (Test-Path $solution) `
    'PropertyApi.sln not found.'

Assert-Condition `
    (-not (Test-Path (Join-Path $domain 'Auth/DTOs/SendOtpResult.cs'))) `
    'OTP/JWT DTO file still exists inside Domain.'

Assert-Condition `
    (Test-Path (Join-Path $application 'Auth/DTOs/SendOtpResult.cs')) `
    'OTP/JWT DTO file is missing from Application.'

$domainLeaks = Get-ChildItem `
    $domain `
    -Recurse `
    -Filter *.cs `
    -File `
    -ErrorAction SilentlyContinue |
    Select-String `
        -Pattern 'namespace\s+PropertyApi\.(Application|Infrastructure)' `
        -ErrorAction SilentlyContinue

Assert-Condition `
    ($null -eq $domainLeaks) `
    'Domain contains Application or Infrastructure namespaces.'

if (Test-Path $program) {
    $programText = Get-Content $program -Raw

    Assert-Condition `
        ($programText -notmatch 'TrustAllProxies') `
        'Program.cs still contains TrustAllProxies.'

    Assert-Condition `
        ($programText -match 'AddTrustedForwardedHeaders') `
        'Trusted Forwarded Headers registration was not found.'

    Assert-Condition `
        ($programText -match 'UseForwardedHeaders\s*\(') `
        'UseForwardedHeaders was not found.'

    Assert-Condition `
        ($programText -match 'AddScaleOutOutputCaching') `
        'Scale-out Output Cache registration was not found.'

    Assert-Condition `
        ($programText -match 'UseOutputCache\s*\(') `
        'UseOutputCache was not found.'

    Assert-Condition `
        ($programText -match 'MapOperationalHealthEndpoints\s*\(') `
        'Operational health endpoint mapping was not found.'
}

Assert-Condition `
    (Test-Path $forwardedRegistration) `
    'ForwardedHeadersRegistration.cs was not found.'

Assert-Condition `
    (Test-Path $outputCacheRegistration) `
    'OutputCacheRegistration.cs was not found.'

Assert-Condition `
    (Test-Path $healthEndpoints) `
    'HealthEndpointExtensions.cs was not found.'

$productionRoots = @(
    (Join-Path $RepositoryRoot 'PropertyApi'),
    (Join-Path $RepositoryRoot 'PropertyApi.Application'),
    (Join-Path $RepositoryRoot 'PropertyApi.Domain'),
    (Join-Path $RepositoryRoot 'PropertyApi.Infrastructure')
)

$allCs = foreach ($root in $productionRoots) {
    if (Test-Path $root) {
        Get-ChildItem `
            $root `
            -Recurse `
            -Filter *.cs `
            -File `
            -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch '\\(bin|obj|Migrations)\\'
            }
    }
}

$startupMigrations = $allCs |
    Select-String `
        -Pattern 'Database\.Migrate(?:Async)?\s*\(|ApplyPendingMigrationsAsync|SeedStartupDataAsync' `
        -ErrorAction SilentlyContinue

Assert-Condition `
    ($null -eq $startupMigrations) `
    'Migration or startup seed calls exist inside production API projects.'

$redisOutputCache = $allCs |
    Select-String `
        -Pattern 'AddStackExchangeRedisOutputCache' `
        -ErrorAction SilentlyContinue

Assert-Condition `
    ($null -ne $redisOutputCache) `
    'Redis Output Cache store registration was not found.'

$wildcardVary = $allCs |
    Select-String `
        -Pattern 'SetVaryByQuery\s*\(\s*"\*"' `
        -ErrorAction SilentlyContinue

Assert-Condition `
    ($null -eq $wildcardVary) `
    'Wildcard Output Cache query variation still exists.'

if ($failures.Count -gt 0) {
    Write-Error (
        "Production Gate verification failed:`n- " +
        ($failures -join "`n- ")
    )

    exit 1
}

Write-Host `
    'Static Production Gate checks passed.' `
    -ForegroundColor Green
