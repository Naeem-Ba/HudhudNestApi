# PropertyApi Security Hardening Patch

This patch implements:

1. Refresh Token Rotation with reuse detection.
2. Encryption at rest for `PhoneNumber`, `WhatsAppNumber`, and `TaxNumber` using ASP.NET Core Data Protection + EF Core ValueConverter.
3. Centralized IDOR protection through `IPropertyOwnershipService`.
4. `AuditLogs` entity/table and audit recording for login, password changes, role changes, property deletion, and refresh-token reuse detection.

## Apply

From the root of your PropertyApi repository:

```powershell
powershell -ExecutionPolicy Bypass -File .\Apply-PropertyApiSecurityHardening.ps1
```

Or run all steps:

```powershell
powershell -ExecutionPolicy Bypass -File .\Apply-PropertyApiSecurityHardening.ps1 -CreateMigration -UpdateDatabase -RunTests
```

## Production requirement

Set a persistent key folder before production deployment:

```powershell
$env:DATA_PROTECTION_KEYS_PATH="C:\propertyapi-keys"
```

On Render/Linux use a persistent disk path, for example:

```bash
DATA_PROTECTION_KEYS_PATH=/var/data/propertyapi-keys
```
