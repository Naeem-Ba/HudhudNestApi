[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot = (Get-Location).Path,

    [Parameter()]
    [string]$OutputPath = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RelativePathCompatible {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$BasePath,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $directorySeparator = [System.IO.Path]::DirectorySeparatorChar
    $normalizedBasePath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd([char[]]@('\', '/')) + $directorySeparator
    $normalizedPath = [System.IO.Path]::GetFullPath($Path)

    if (-not $normalizedPath.StartsWith(
        $normalizedBasePath,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$normalizedPath' is outside repository root '$normalizedBasePath'."
    }

    return $normalizedPath.Substring($normalizedBasePath.Length)
}


$root = (Resolve-Path $RepositoryRoot).Path

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $root "security-stamp-resilience-sources-$timestamp.zip"
}

$requiredFiles = @(
    'PropertyApi.Infrastructure/Identity/Services/CachedSecurityStampValidator.cs',
    'PropertyApi.Infrastructure/Identity/Services/IdentitySecurityStampReader.cs',
    'PropertyApi.Infrastructure/Identity/Services/DistributedSecurityStampCacheInvalidator.cs',
    'PropertyApi.Infrastructure/DependencyInjection.cs',
    'PropertyApi.Infrastructure/PropertyApi.Infrastructure.csproj',
    'PropertyApi/Program.cs'
)

$missing = @()
foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $missing += $relativePath
    }
}

if ($missing.Count -gt 0) {
    throw "Required files are missing:`n - $($missing -join "`n - ")"
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PropertyApi-SecurityStamp-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    foreach ($relativePath in $requiredFiles) {
        $source = Join-Path $root $relativePath
        $destination = Join-Path $tempRoot $relativePath
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }

    $applicationAuthRoot = Join-Path $root 'PropertyApi.Application/Auth'
    if (Test-Path -LiteralPath $applicationAuthRoot) {
        Get-ChildItem -LiteralPath $applicationAuthRoot -Recurse -File -Filter '*.cs' |
            Where-Object {
                Select-String -LiteralPath $_.FullName -Pattern 'IUserSecurityStamp|SecurityStampValidation' -Quiet
            } |
            ForEach-Object {
                $relativePath = Get-RelativePathCompatible -BasePath $root -Path $_.FullName
                $destination = Join-Path $tempRoot $relativePath
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
            }
    }

    $testRoot = Join-Path $root 'tests'
    if (Test-Path -LiteralPath $testRoot) {
        Get-ChildItem -LiteralPath $testRoot -Recurse -File -Filter '*.cs' |
            Where-Object {
                Select-String -LiteralPath $_.FullName -Pattern 'CachedSecurityStampValidator|IUserSecurityStampValidator|SecurityStamp' -Quiet
            } |
            ForEach-Object {
                $relativePath = Get-RelativePathCompatible -BasePath $root -Path $_.FullName
                $destination = Join-Path $tempRoot $relativePath
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
            }
    }

    $supportFiles = @(
        'Directory.Packages.props',
        'Directory.Build.props',
        'PropertyApi.sln'
    )

    foreach ($relativePath in $supportFiles) {
        $source = Join-Path $root $relativePath
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            $destination = Join-Path $tempRoot $relativePath
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
    }

    $configEvidencePath = Join-Path $tempRoot 'configuration-key-evidence.txt'
    $configFiles = Get-ChildItem -LiteralPath (Join-Path $root 'PropertyApi') -File -Filter 'appsettings*.json' -ErrorAction SilentlyContinue

    @(
        '# Configuration key evidence only; secret values intentionally excluded.'
        '# Presence is reported without copying values.'
        ''
    ) | Set-Content -LiteralPath $configEvidencePath -Encoding utf8

    $keys = @(
        'SecurityStampValidation',
        'Redis',
        'ConnectionStrings',
        'Jwt'
    )

    foreach ($file in $configFiles) {
        Add-Content -LiteralPath $configEvidencePath -Value "FILE: $($file.Name)"
        foreach ($key in $keys) {
            $present = Select-String -LiteralPath $file.FullName -Pattern ('"' + [regex]::Escape($key) + '"') -Quiet
            Add-Content -LiteralPath $configEvidencePath -Value ("  {0}: {1}" -f $key, $(if ($present) { 'present' } else { 'absent' }))
        }
        Add-Content -LiteralPath $configEvidencePath -Value ''
    }

    $manifest = Get-ChildItem -LiteralPath $tempRoot -Recurse -File |
        ForEach-Object { Get-RelativePathCompatible -BasePath $tempRoot -Path $_.FullName } |
        Sort-Object

    $manifest | Set-Content -LiteralPath (Join-Path $tempRoot 'MANIFEST.txt') -Encoding utf8

    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    Compress-Archive -Path (Join-Path $tempRoot '*') -DestinationPath $OutputPath -CompressionLevel Optimal

    Write-Host "Created: $OutputPath" -ForegroundColor Green
    Write-Host "Files included: $($manifest.Count)" -ForegroundColor Green
    Write-Host 'No appsettings values or environment-variable values were copied.' -ForegroundColor Cyan
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
