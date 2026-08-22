# .NET 10 migration plan

**Status:** planned, not started
**Deadline:** 2026-11-10 — the day .NET 8 leaves support
**Written:** 2026-08-21
**Owner:** _unassigned — assign before the first work item starts_

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

## Where the solution stands

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

- `Directory.Build.props`: `net8.0` → `net10.0`.
- All `Microsoft.*`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*` and
  `Microsoft.Extensions.*` entries in `Directory.Packages.props` → 10.0.x.
- Regenerate every `packages.lock.json` (`dotnet restore --force-evaluate`). CI restores
  with `--locked-mode`, so stale lock files fail the build.
- `ci/Dockerfile.migrator`, `ci/Dockerfile.database-recovery`, `PropertyApi/Dockerfile`:
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

Suggested issue title: **Migrate PropertyApi from .NET 8 to .NET 10 before 2026-11-10**
