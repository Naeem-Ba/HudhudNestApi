# PropertyApi Data Protection Keys in PostgreSQL

This patch stores ASP.NET Core Data Protection keys in PostgreSQL/Supabase instead of the local filesystem.

## Changed files

- `PropertyApi.Infrastructure/PropertyApi.Infrastructure.csproj`
- `PropertyApi.Infrastructure/DependencyInjection.cs`
- `PropertyApi.Infrastructure/Persistence/AppDbContext.cs`
- `PropertyApi.Infrastructure/Persistence/DataProtectionKeyDbContext.cs`

## Apply

```powershell
cd C:\Users\naeem\Source\Repos\PropertyApi
powershell -ExecutionPolicy Bypass -File .\dbkeys_patch\Apply-PropertyApiDataProtectionDbKeys.ps1 -ProjectRoot .
```

## Migration

```powershell
dotnet restore PropertyApi.sln
dotnet build PropertyApi.sln --configuration Release

dotnet ef migrations add AddDataProtectionKeys `
  --project PropertyApi.Infrastructure `
  --startup-project PropertyApi `
  --context AppDbContext

dotnet ef database update `
  --project PropertyApi.Infrastructure `
  --startup-project PropertyApi `
  --context AppDbContext
```

## Production

Set this Render environment variable:

```text
DataProtection__PersistKeysToDatabase=true
```

The code also defaults to DB persistence in `Production`, so this variable is explicit but not strictly required.

You no longer need:

```text
DATA_PROTECTION_KEYS_PATH
```

when using DB key persistence.
