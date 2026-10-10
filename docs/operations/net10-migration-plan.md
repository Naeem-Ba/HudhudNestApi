# .NET 10 migration plan

**Status:** code migrated on 2026-10-10 (this PR); not yet deployed — see "What the migration found" and "Rollout" below
**Deadline:** 2026-11-10 — the day .NET 8 leaves support
**Written:** 2026-08-21
**Owner:** Naeem-Ba

---

## Why this has a date on it

.NET 8 is an LTS release and its support window closes on **10 November 2026**. After
that date Microsoft ships no security patches for the runtime, ASP.NET Core, or EF Core
on the 8.0.x line. Every advisory found after that point stays open in this codebase
permanently, and `dotnet list package --vulnerable` will keep reporting clean because
unsupported versions stop receiving advisories, not because the code is safe.

From the date this plan was written that is **roughly 12 weeks**. The work below is not
large, but it is not a single afternoon either, and the last of those weeks is the worst
time to discover a provider-level breaking change.

.NET 10 is the current LTS. All first-party packages this solution uses already publish
10.0.x — that was verified against nuget.org on 2026-08-21, with `Microsoft.EntityFrameworkCore`
at 10.0.11.

## Where the solution stood before this migration

Phase 03 of the security remediation raised the whole 8.0.x stack from **8.0.11
(November 2024)** to **8.0.30**, and every other dependency to its newest in-band
release. `ci/vulnerability-baseline.json` is empty and the scan reports 15 of 15
projects clean.

That buys time; it does not change the deadline. Being current *within* 8.0.x is
worth exactly as much as 8.0.x still being supported.

| | |
|---|---|
| Target framework | `net8.0`, set once in `Directory.Build.props` |
| Projects | 4 production, 8 test, 3 tools |
| Package management | Central, with lock files and `--locked-mode` in CI |
| Runtime dependencies | PostgreSQL + PostGIS, Redis, Cloudinary, Twilio, SMTP |
| Container base images | `mcr.microsoft.com/dotnet/{sdk,aspnet}:8.0` |

## What the migration found (2026-10-10)

The version bump itself was mechanical. These are the places where .NET 10 / EF Core 10 /
Npgsql 10 behaved differently from .NET 8 and what was done about each. All of them are
covered by a test or by a CI step so they cannot regress silently.

| # | Change in the new stack | Effect here | Fix |
|---|---|---|---|
| 1 | EF Core 9+ compares the model with the migration snapshot before `MigrateAsync` and throws `PendingModelChangesWarning` when they differ. | The Migrator tool (and the recovery verifier's rehearsal) do not run `Program.cs`, so `Npgsql.EnableLegacyTimestampBehavior` was never set there: they built a `timestamptz` model against a `timestamp` snapshot and refused to migrate a fresh database. EF Core 8 never looked, so this had been invisible. | `AddPersistenceInfrastructure` now sets the switch (the Migrator goes through `AddInfrastructure`), and the recovery verifier sets it directly. An Architecture test requires one of the two for every tool that calls `MigrateAsync`; CI's "Apply EF Core migrations" step exercises the real thing. |
| 2 | `ForwardedHeadersOptions.KnownNetworks` and `Microsoft.AspNetCore.HttpOverrides.IPNetwork` are obsolete; the replacements are `KnownIPNetworks` and `System.Net.IPNetwork`, and the new type **throws** when host bits are set (for example `10.1.2.3/8`). | The Production allowlist comes from an environment variable, so a value that booted on .NET 8 could have failed the whole service on .NET 10. | `ForwardedHeadersRegistration` masks the host bits before building the network, so every value that worked still works. The configuration key (`ForwardedHeaders:KnownNetworks`) is unchanged. Covered by new tests for host-bits values and malformed CIDRs. |
| 3 | `AddDbContext` also registers an `IDbContextOptionsConfiguration<TContext>`. | The integration test factories replaced only `DbContextOptions<AppDbContext>` with InMemory, so Npgsql stayed registered next to it and EF refused to build the context. Test-only. | One shared helper (`UseInMemoryAppDbContext`) removes both registrations. |
| 4 | NuGet audit (NU1510): `System.Security.Cryptography.Xml` is part of the shared framework. | The direct 8.0.4 security pin is now redundant. | Pin removed. |
| 5 | Container base images: the Ubuntu base of the `10.0` images is 24.04 (noble); there is no jammy tag to stay on. | The runtime image changes from jammy to `noble-chiseled-extra`. | Dockerfiles updated; the image tag was verified on MCR. |

Checked on the new stack rather than assumed:

- **Tests:** Application, Auth, Architecture, Infrastructure and Observability suites, and the
  whole Integration suite (613 tests), pass on `net10.0`.
- **Schema:** a fresh PostGIS database migrated by the .NET 8 Migrator and one migrated by the
  .NET 10 Migrator produce identical `pg_dump --schema-only` output (68 migrations). Seed data is
  identical apart from generated ids and timestamps, the EF product version in the history table,
  and `Roles.ConcurrencyStamp`, which Identity 10 now fills in on create (it was `NULL`).
- **Data Protection:** a payload protected on .NET 8 is unprotected on .NET 10 and the other way
  round, and a key ring created by either version is read by the other (throwaway console
  experiment with the default algorithms). Keys stored in the database by the running service
  stay readable after the upgrade.
- **Passwords:** Identity's default hasher still uses 100,000 iterations, and hashes verify in
  both directions, so existing accounts keep logging in and the dummy-hash timing fix keeps its cost.
- **Startup:** the Release build as Production behaves like the .NET 8 build on every refusal
  probe, boots with the well-formed baseline, answers `/health/ready` 200, and accepts a
  host-bits CIDR while refusing a malformed one.
- **Packages:** `dotnet list package --vulnerable --include-transitive` reports nothing for all
  16 projects.

Not covered by the above, and left to the gates: building the container image and its Trivy
scan (there is no Docker daemon in the development sandbox), the performance baselines, and the
restore drill. `Microsoft.IdentityModel` moved from 7.7.3 to 8.19.2 with the JWT bearer package;
the JWT, refresh-token and security-stamp tests pass, but no token minted by a .NET 8 build was
replayed against a .NET 10 build, so a user holding an access token across the deploy may be
asked to sign in again.

## Rollout

1. Merge this PR into `master` only after the Production secrets guard PR is deployed, so
   the two behaviour changes never reach Production in the same release.
2. The Production Gate runs on the merge commit; the manual deploy to Render rebuilds the
   image from `HudhudNestApi/Dockerfile` (new SDK and runtime base images).
3. Rollback is a redeploy of the previous Render deploy; no migration is part of this change,
   so the database needs no rollback.

## Known work items

Ordered by how likely each is to cost more than a version bump.

### 1. Npgsql provider — the item to settle first

`Npgsql.EntityFrameworkCore.PostgreSQL` is pinned at `8.0.11` and, unlike the Microsoft
packages, has no newer 8.0.x release. It tracks EF Core's major version, so EF Core 10
requires the matching Npgsql major.

This is the dependency most likely to carry behaviour changes that touch real query
results, because the codebase leans on provider specifics:

- `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` in `Program.cs`.
  This switch exists to keep pre-6.0 `timestamp`/`DateTime` mapping. Confirm it still
  exists and still means the same thing, or migrate off it deliberately.
- The raw PostGIS SQL in `PropertyGeoSearchRepository` — `ST_DWithin`, the `<->` distance
  operator, and the `geography(Point,4326)` column — runs through `DbConnection` directly
  and must keep producing the same plan against the same GiST index.
- `ExecuteDeleteAsync` / `ExecuteUpdateAsync` are used in several places, including the
  staging cleanup path.

**Do this first.** If Npgsql forces changes, everything after it is scheduling.

### 2. Framework and package bump

- `Directory.Build.props`: `net8.0` → `net10.0`. _(done)_
- All `Microsoft.*`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*` and
  `Microsoft.Extensions.*` entries in `Directory.Packages.props` → 10.0.x.
- Regenerate every `packages.lock.json` (`dotnet restore --force-evaluate`). CI restores
  with `--locked-mode`, so stale lock files fail the build.
- `ci/Dockerfile.migrator`, `ci/Dockerfile.database-recovery`, `HudhudNestApi/Dockerfile`:
  base images → `10.0`.
- `.github/workflows/*.yml`: `DOTNET_VERSION: '8.0.x'` → `'10.0.x'`.
- `global.json`, if one is added before then.

### 3. Identity and JWT

`Microsoft.AspNetCore.Identity.EntityFrameworkCore` and
`System.IdentityModel.Tokens.Jwt` sit directly under the authentication paths reworked
in Phases 01–02. Re-run the auth suites and confirm in particular:

- the security-stamp validation in the `OnTokenValidated` handler,
- refresh-token rotation and reuse detection,
- the `PasswordHasher` output — `IdentityAccessService.VerifyDummyPasswordAsync` depends
  on hashing costing what a real verification costs, so a change to the default hashing
  parameters weakens the timing-oracle fix if it goes unnoticed.

### 4. Opportunities the upgrade opens

Not required, but cheap once on net10.0 and worth folding in:

- `System.Threading.Lock` — `PwnedPasswordsCircuitBreaker` uses `object` for its gate
  purely because `Lock` is .NET 9+.
- Hybrid caching, which may simplify the two-tier security-stamp cache.

### 5. Verification before merge

- Full test suite including the integration, concurrency and performance suites — the
  three that need Docker and were not runnable during Phases 01–03.
- `dotnet list package --vulnerable --include-transitive` clean, and
  `ci/check-vulnerable-packages.ps1` passing with an empty baseline.
- Performance gate re-run against the recorded baselines. Geo-search plan evidence must
  still show the GiST index in use; see `docs/performance/`.
- Database restore drill on the new runtime.

## Suggested schedule

| Window | Work |
|---|---|
| By 2026-09-05 | Assign an owner. Spike the Npgsql 10 upgrade on a branch, EF Core query behaviour only. |
| By 2026-09-19 | Framework bump on a branch; solution builds and unit suites pass. |
| By 2026-10-10 | Integration, concurrency and performance suites green. Container images rebuilt. |
| By 2026-10-24 | Deployed to Staging, smoke and restore drill passed. |
| By 2026-11-07 | Production. |
| 2026-11-10 | .NET 8 support ends. |

The gap between the last two rows is three days on purpose: if the schedule slips, it
should slip into a buffer rather than past the deadline.

## Tracking

This document is the plan. The issue tracking the work is not yet open — it needs a
maintainer's decision on the owner and on whether the schedule above fits alongside
other commitments.

Suggested issue title: **Migrate HudhudNestApi from .NET 8 to .NET 10 before 2026-11-10**
