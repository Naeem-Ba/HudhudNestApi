param(
    [string]$ProjectRoot = (Get-Location).Path,
    [switch]$CreateMigration,
    [switch]$UpdateDatabase,
    [switch]$RunTests
)

$ErrorActionPreference = "Stop"

function Write-Step($message) {
    Write-Host "[PropertyApi Security Hardening] $message" -ForegroundColor Cyan
}

$PatchFilesRoot = Join-Path $PSScriptRoot "files"
if (-not (Test-Path $PatchFilesRoot)) {
    throw "Patch files folder was not found: $PatchFilesRoot"
}

$requiredPaths = @(
    "PropertyApi.sln",
    "PropertyApi",
    "PropertyApi.Application",
    "PropertyApi.Domain",
    "PropertyApi.Infrastructure"
)

foreach ($requiredPath in $requiredPaths) {
    $candidate = Join-Path $ProjectRoot $requiredPath
    if (-not (Test-Path $candidate)) {
        throw "ProjectRoot does not look like the PropertyApi repository. Missing: $candidate"
    }
}

$files = @(
    "PropertyApi.Domain/Audit/Constants/AuditActions.cs",
    "PropertyApi.Domain/Audit/Entities/AuditLog.cs",
    "PropertyApi.Application/Common/Interfaces/IAuditLogService.cs",
    "PropertyApi.Infrastructure/Audit/AuditLogService.cs",
    "PropertyApi.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs",
    "PropertyApi.Infrastructure/Security/DataProtection/SensitiveDataProtectionPurposes.cs",
    "PropertyApi.Infrastructure/Security/DataProtection/DataProtectionStringConverter.cs",
    "PropertyApi.Application/Listings/Interfaces/IPropertyOwnershipService.cs",
    "PropertyApi.Application/Listings/Services/PropertyOwnershipService.cs",
    "PropertyApi.Infrastructure/Persistence/AppDbContext.cs",
    "PropertyApi.Infrastructure/DependencyInjection.cs",
    "PropertyApi.Application/DependencyInjection.cs",
    "PropertyApi.Application/Auth/Commands/Login/LoginUserCommand.cs",
    "PropertyApi.Application/Auth/Commands/RefreshToken/RefreshTokenCommand.cs",
    "PropertyApi.Application/Auth/Commands/SocialLogin/SocialLoginCommandHandler.cs",
    "PropertyApi.Application/Users/Commands/ChangePassword/ChangePasswordCommand.cs",
    "PropertyApi.Application/Users/Commands/ChangePassword/ChangePasswordCommandHandler.cs",
    "PropertyApi/Controllers/UsersController.cs",
    "PropertyApi.Application/Admin/Interfaces/IAdminService.cs",
    "PropertyApi.Application/Admin/Interfaces/IAdminIdentityService.cs",
    "PropertyApi.Application/Admin/Services/AdminService.cs",
    "PropertyApi.Infrastructure/Admin/AdminIdentityService.cs",
    "PropertyApi/Controllers/AdminController.cs",
    "PropertyApi.Application/Listings/Commands/UpdateProperty/UpdatePropertyCommandHandler.cs",
    "PropertyApi.Application/Listings/Commands/DeleteProperty/DeletePropertyCommand.cs",
    "PropertyApi.Application/Listings/Commands/DeleteProperty/DeletePropertyCommandHandler.cs",
    "PropertyApi.Application/Listings/Commands/UploadPropertyImages/UploadPropertyImagesCommandHandler.cs",
    "PropertyApi.Application/Listings/Commands/SetMainPropertyImage/SetMainPropertyImageCommandHandler.cs",
    "PropertyApi.Application/Listings/Commands/DeletePropertyImage/DeletePropertyImageCommandHandler.cs",
    "PropertyApi/Controllers/PropertiesController.cs",
    "PropertyApi/appsettings.Development.example.json",
    "tests/PropertyApi.Application.Tests/Listings/PropertyOwnershipServiceTests.cs",
    "tests/PropertyApi.Application.Tests/Admin/AdminServiceTests.cs",
    "tests/PropertyApi.Auth.Tests/Application/Commands/EmailPasswordAuthHandlerTests.cs",
    "tests/PropertyApi.Auth.Tests/Application/Handlers/PropertyImagesHandlerTests.cs"
)

Write-Step "Copying security hardening files into: $ProjectRoot"

foreach ($relativePath in $files) {
    $source = Join-Path $PatchFilesRoot $relativePath
    $destination = Join-Path $ProjectRoot $relativePath

    if (-not (Test-Path $source)) {
        throw "Patch source file is missing: $source"
    }

    $destinationDirectory = Split-Path $destination -Parent
    if (-not (Test-Path $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Copy-Item -Path $source -Destination $destination -Force
    Write-Host "  updated $relativePath"
}

Write-Step "Files copied successfully."

Write-Host ""
Write-Host "IMPORTANT production setting:" -ForegroundColor Yellow
Write-Host "Set DATA_PROTECTION_KEYS_PATH to a persistent folder before deploying, for example: /var/data/propertyapi-keys"
Write-Host "Without persisted Data Protection keys, encrypted PhoneNumber/WhatsAppNumber/TaxNumber values may become unreadable after restart/redeploy."

if ($CreateMigration) {
    Write-Step "Creating EF Core migration..."
    dotnet ef migrations add AddSecurityHardeningAuditLogsAndEncryptedUserFields `
        --project "PropertyApi.Infrastructure" `
        --startup-project "PropertyApi" `
        --context "AppDbContext"
}

if ($UpdateDatabase) {
    Write-Step "Updating database..."
    dotnet ef database update `
        --project "PropertyApi.Infrastructure" `
        --startup-project "PropertyApi" `
        --context "AppDbContext"
}

if ($RunTests) {
    Write-Step "Running tests..."
    dotnet test "PropertyApi.sln"
}

Write-Host ""
Write-Step "Done. Recommended next commands:"
Write-Host "dotnet restore PropertyApi.sln"
Write-Host "dotnet build PropertyApi.sln --configuration Release"
Write-Host "dotnet ef migrations add AddSecurityHardeningAuditLogsAndEncryptedUserFields --project PropertyApi.Infrastructure --startup-project PropertyApi --context AppDbContext"
Write-Host "dotnet ef database update --project PropertyApi.Infrastructure --startup-project PropertyApi --context AppDbContext"
Write-Host "dotnet test PropertyApi.sln"
