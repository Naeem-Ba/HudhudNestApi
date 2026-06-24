param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path $ProjectRoot).Path
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Copy-PatchFile {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $source = Join-Path $scriptRoot $RelativePath
    $target = Join-Path $root $RelativePath
    $targetDir = Split-Path -Parent $target

    if (-not (Test-Path $source)) {
        throw "Patch source file not found: $source"
    }

    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Copy-Item -Force -Path $source -Destination $target
    Write-Host "Updated: $RelativePath"
}

Copy-PatchFile "PropertyApi.Infrastructure\PropertyApi.Infrastructure.csproj"
Copy-PatchFile "PropertyApi.Infrastructure\DependencyInjection.cs"
Copy-PatchFile "PropertyApi.Infrastructure\Persistence\AppDbContext.cs"
Copy-PatchFile "PropertyApi.Infrastructure\Persistence\DataProtectionKeyDbContext.cs"

Write-Host ""
Write-Host "Data Protection DB key storage patch applied."
Write-Host "Next commands:"
Write-Host "  dotnet restore PropertyApi.sln"
Write-Host "  dotnet build PropertyApi.sln --configuration Release"
Write-Host "  dotnet ef migrations add AddDataProtectionKeys --project PropertyApi.Infrastructure --startup-project PropertyApi --context AppDbContext"
Write-Host "  dotnet ef database update --project PropertyApi.Infrastructure --startup-project PropertyApi --context AppDbContext"
