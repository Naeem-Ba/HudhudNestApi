# Central Package Management

HudhudNestApi uses NuGet Central Package Management through `Directory.Packages.props`.

## Rules

- All direct NuGet versions live in `Directory.Packages.props`.
- Project files keep `<PackageReference Include="Package.Name" />` only.
- Project-specific metadata such as `PrivateAssets` and `IncludeAssets` stays in the project file.
- `VersionOverride` is disabled through `CentralPackageVersionOverrideEnabled=false`.
- Floating, ranged, and preview versions are not allowed unless explicitly approved in `ci/package-policy.json`.
- `ci/package-policy.json` lists required test packages by name only; their versions have a single source of truth in `Directory.Packages.props` and are made reproducible by lock files.

## Test Stack

| Package | Central Version |
| --- | ---: |
| `Microsoft.NET.Test.Sdk` | `18.10.0` |
| `xunit` | `2.9.3` |
| `xunit.runner.visualstudio` | `4.0.0` |
| `coverlet.collector` | `10.0.1` |

These versions were selected because they are already compatible with the repository's .NET 10 test execution path and existing xUnit tests. Versions are kept current with `Directory.Packages.props` as the single source of truth (last verified 2026-09-18 during a `docs/audit/` review).

## Restore Reproducibility

`Directory.Build.props` enables `RestorePackagesWithLockFile`.

CI and production gate restore with:

```bash
dotnet restore HudhudNestApi.sln --locked-mode
```

Any dependency graph change must update the relevant `packages.lock.json` files intentionally.

Dependabot updates only the lock file of the project that references the package directly. Test projects that reach it through `HudhudNestApi.csproj` record it as `CentralTransitive` and are left stale, so `--locked-mode` fails with NU1004 (MessagePack 2.5.307, PR #285). Such a bump lands through a branch that runs `dotnet restore HudhudNestApi.sln --force-evaluate` and commits every changed lock file.

### Dependabot lock files (automated)

`.github/workflows/dependabot-lockfiles.yml` does that regeneration for every `dependabot/nuget/*` pull request: it runs `dotnet restore HudhudNestApi.sln --force-evaluate` and pushes the changed `packages.lock.json` files to the Dependabot branch, which restarts CI on the new commit. `--locked-mode` stays on in CI; the workflow never relaxes it, and it refuses to push anything that is not a lock file.

One-time setup (repository owner):

1. Create a fine-grained personal access token: repository `HudhudNestApi` only, permission **Contents: Read and write**, nothing else. Give it the longest expiry GitHub allows and put the renewal date in your calendar.
2. Add it under Settings > Secrets and variables > **Dependabot** > New repository secret, name `LOCKFILE_PUSH_TOKEN`. It must be a *Dependabot* secret: Dependabot-triggered runs cannot read ordinary Actions secrets.

Why a token and not `GITHUB_TOKEN`: a push made with `GITHUB_TOKEN` does not start new workflow runs, so CI would never re-run on the fixed commit. The push comes from the token's owner, and that is also the loop guard: the follow-up `synchronize` event is not from `dependabot[bot]`, so the workflow skips itself.

Behaviour to know:

- If the secret is missing or expired the job fails with an explicit message and the PR stays red as before; regenerate the lock files by hand with the recipe above.
- After the workflow pushes, Dependabot no longer rebases that PR on its own (it does not touch branches it did not write last). To refresh one, comment `@dependabot recreate`; the workflow runs again on the new branch.
- The first CI run on a fresh Dependabot PR still fails and is cancelled or superseded when the lock-file commit arrives.


## NuGet Source Policy

The repository `NuGet.config` clears inherited package sources and allows only:

```text
https://api.nuget.org/v3/index.json
```

No package source credentials may be committed. If private packages are introduced later, package source mapping must be updated so internal package names cannot resolve from public sources by accident.

## Security Updates

1. Update the central version in `Directory.Packages.props`.
2. Run `dotnet restore HudhudNestApi.sln --force-evaluate` so every lock file that records the package is rewritten, including test projects that only see it as `CentralTransitive`.
3. Run `dotnet restore HudhudNestApi.sln --locked-mode`.
4. Run the full test suite and vulnerability gate.
5. Document any temporary exception in `ci/vulnerability-exceptions.json` with owner, expiration, and remediation issue.

## Transitive Pinning

`CentralPackageTransitivePinningEnabled` is intentionally disabled. The current dependency graph includes many framework-era transitive packages, and pinning every transitive dependency would add churn without a clear compatibility gain. Direct pins are used only when the application uses the package directly or a security fix must override a vulnerable transitive version.
