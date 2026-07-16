[CmdletBinding()]
param(
    [string] $HostName = "localhost",
    [int] $Port = 5432,
    [string] $AdminUser = "postgres",
    [string] $AdminPassword = $env:PGPASSWORD,
    [string] $Database = "propertyapi_testing",
    [string] $PsqlPath = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
)

$ErrorActionPreference = "Stop"

if ($Database -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw "Database must be a simple PostgreSQL identifier."
}

if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw "Provide -AdminPassword or set PGPASSWORD before running this script."
}

if (-not (Test-Path $PsqlPath)) {
    $command = Get-Command psql -ErrorAction SilentlyContinue

    if ($null -eq $command) {
        throw "psql was not found. Pass -PsqlPath or add PostgreSQL bin to PATH."
    }

    $PsqlPath = $command.Source
}

$env:PGPASSWORD = $AdminPassword

$existing = & $PsqlPath `
    -h $HostName `
    -p $Port `
    -U $AdminUser `
    -d postgres `
    -At `
    -v ON_ERROR_STOP=1 `
    -c "SELECT datname FROM pg_database WHERE datname = '$Database';"

if ($LASTEXITCODE -ne 0) {
    throw "Could not connect to PostgreSQL as '$AdminUser'."
}

if ([string]::IsNullOrWhiteSpace($existing)) {
    & $PsqlPath `
        -h $HostName `
        -p $Port `
        -U $AdminUser `
        -d postgres `
        -v ON_ERROR_STOP=1 `
        -c "CREATE DATABASE ""$Database"";"

    if ($LASTEXITCODE -ne 0) {
        throw "Could not create database '$Database'."
    }
}

$testConnectionString =
    "Host=$HostName;Port=$Port;Database=$Database;Username=$AdminUser;Password=<same-password-used-above>;Trust Server Certificate=true"

Write-Host "PostgreSQL test database is ready: $Database"
Write-Host "For this PowerShell session run:"
Write-Host "`$env:TEST_POSTGRES_CONNECTION_STRING = '$testConnectionString'"
