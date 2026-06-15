param(
    [string]$RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Copy-Item (Join-Path $sourceRoot 'tests') $RepoRoot -Recurse -Force

# Ensure MSBuild does not reuse stale compiled test DLLs from a previous package extraction.
Get-ChildItem -Path $RepoRoot -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force

# These values are test-only. They let Production-mode WebApplicationFactory tests start
# without weakening the real Program.cs guard that requires trusted proxies/networks.
$env:ASPNETCORE_ENVIRONMENT = 'Testing'
$env:DATABASE_URL = 'Host=localhost;Port=5432;Database=propertyapi_test;Username=postgres;Password=postgres;SSL Mode=Require;'
$env:ForwardedHeaders__ForwardLimit = '1'
$env:ForwardedHeaders__KnownProxies__0 = '203.0.113.1'
Remove-Item Env:ForwardedHeaders__KnownNetworks__0 -ErrorAction SilentlyContinue
$env:Cors__AllowedOrigins__0 = 'https://frontend.example'

$env:Jwt__Issuer = 'PropertyApi'
$env:Jwt__Audience = 'PropertyApiClient'
$env:Jwt__Key = 'TEST_ONLY_SECRET_KEY_1234567890_1234567890'
$env:OtpSettings__SecretKey = 'TEST_ONLY_OTP_SECRET_KEY_1234567890_1234567890'

Push-Location $RepoRoot
try {
    dotnet restore
    dotnet build --no-restore
    dotnet test --no-build
}
finally {
    Pop-Location
}
