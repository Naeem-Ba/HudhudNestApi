# Central Package Management

PropertyApi uses NuGet Central Package Management through `Directory.Packages.props`.

## Rules

- All direct NuGet versions live in `Directory.Packages.props`.
- Project files keep `<PackageReference Include="Package.Name" />` only.
- Project-specific metadata such as `PrivateAssets` and `IncludeAssets` stays in the project file.
- `VersionOverride` is disabled through `CentralPackageVersionOverrideEnabled=false`.
- Floating, ranged, and preview versions are not allowed unless explicitly approved in `ci/package-policy.json`.

## Test Stack

| Package | Central Version |
| --- | ---: |
| `Microsoft.NET.Test.Sdk` | `17.8.0` |
| `xunit` | `2.6.6` |
| `xunit.runner.visualstudio` | `2.5.6` |
| `coverlet.collector` | `6.0.0` |

These versions were selected because they are already compatible with the repository's .NET 8 test execution path and existing xUnit tests.

## Restore Reproducibility

`Directory.Build.props` enables `RestorePackagesWithLockFile`.

CI and production gate restore with:

```bash
dotnet restore PropertyApi.sln --locked-mode
```

Any dependency graph change must update the relevant `packages.lock.json` files intentionally.

## NuGet Source Policy

The repository `NuGet.config` clears inherited package sources and allows only:

```text
https://api.nuget.org/v3/index.json
```

No package source credentials may be committed. If private packages are introduced later, package source mapping must be updated so internal package names cannot resolve from public sources by accident.

## Security Updates

1. Update the central version in `Directory.Packages.props`.
2. Run `dotnet restore PropertyApi.sln --use-lock-file`.
3. Run `dotnet restore PropertyApi.sln --locked-mode`.
4. Run the full test suite and vulnerability gate.
5. Document any temporary exception in `ci/vulnerability-exceptions.json` with owner, expiration, and remediation issue.

## Transitive Pinning

`CentralPackageTransitivePinningEnabled` is intentionally disabled. The current dependency graph includes many framework-era transitive packages, and pinning every transitive dependency would add churn without a clear compatibility gain. Direct pins are used only when the application uses the package directly or a security fix must override a vulnerable transitive version.
