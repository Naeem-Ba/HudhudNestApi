# App Update Management (Phase 1 — Backend)

## Scope

Backend only, .NET 8 Clean Architecture, PostgreSQL. This phase gives the backend a way to
answer, for a given platform and client app version, whether an update exists and whether it is
mandatory, plus admin CRUD to manage that data.

**Explicitly out of scope for this phase** (future work, in the separate Angular/Capacitor
frontend repo): the update-prompt UI (optional vs. mandatory dialogs), native Google Play /
App Store deep-linking, CI/CD version-bump automation, and the admin dashboard *screen*. This
document only covers the backend contract those phases will build against.

## Data model

`AppRelease` (`PropertyApi.Domain.AppUpdates.Entities.AppRelease`, extends `AuditableEntity`):

| Field | Notes |
|---|---|
| `Platform` | `Android` \| `IOS` \| `Web`, stored as string, extensible to new platforms without a schema redesign |
| `Version` | The version this row publishes, `major.minor.patch` |
| `MinimumSupportedVersion` | The floor still allowed to keep working, as of this release |
| `StoreUrl` | Nullable — no real Google Play / App Store listing exists yet |
| `ReleaseNotesAr` / `En` / `De` | Parallel-column localization, matching the app's ar/en/de support |
| `ReleaseDate`, `IsEnabled` | |
| `IsDeleted` / `DeletedAt` (inherited) | Soft delete only — deleting a release never physically removes the row |

**Why there is no `IsMandatory` column.** Mandatory is *derived*, never stored. A release
existing with a higher `Version` never forces anything by itself — a client is only forced to
update when its own version falls below the current effective release's
`MinimumSupportedVersion`. Conflating "a newer version exists" with "your version is no longer
supported" is the one mistake this feature is designed not to make. Raising
`MinimumSupportedVersion` on a new release *is* the admin action that turns an update mandatory
for older clients — there is no separate flag to flip.

## Effective release resolution

The effective release for a platform is the row with the **highest `Version`** (compared
numerically, never lexicographically) among rows where `IsEnabled = true AND IsDeleted = false`
for that platform.

`Version` is used instead of `ReleaseDate` because `ReleaseDate` is free-form admin-entered
metadata — it can be backfilled, scheduled in the future, or simply wrong, so it is not a
reliable proxy for "what's actually the current build." `Version` has a real total order tied
to what's actually installed on devices, and a partial unique index (below) keeps it unique
per platform among enabled rows.

If no enabled, non-deleted row exists for a platform, the check endpoint reports "no update
available" — **never an error, never 404**.

## Public API

```
GET /api/app-updates/check?platform={Android|IOS|Web}&currentVersion={major.minor.patch}&lang={ar|en|de}
```

- `[AllowAnonymous]`, rate-limited under the existing `public-read` policy (120/min). Works
  identically for authenticated and anonymous callers, and returns nothing sensitive.
- `lang` selects the release-notes language; defaults to English when omitted or unrecognized
  (the query validator still rejects a value outside `ar`/`en`/`de`).

Response:

```json
{
  "updateAvailable": true,
  "mandatory": false,
  "latestVersion": "1.4.0",
  "minimumSupportedVersion": "1.2.0",
  "storeUrl": null,
  "releaseNotes": "Bug fixes and performance improvements.",
  "releaseDate": "2026-09-18T00:00:00Z"
}
```

**Errors**: a `platform` that is not `Android`/`IOS`/`Web` fails model binding and returns `400`; an invalid
`currentVersion` or `lang`, and every admin-endpoint validation failure (malformed `storeUrl`,
`minimumSupportedVersion` above `version`, ...), returns `422` — the API-wide FluentValidation convention.

`mandatory` is computed as `currentVersion < minimumSupportedVersion` — **not** compared against
`latestVersion`.

**Failure handling**: this is the hottest read path in the feature (hit on every app launch).
Two independent layers of defense keep a cache or database outage from ever surfacing as an
app-launch failure:
1. `CachedAppReleaseCacheService` falls back to a direct database read if the distributed cache
   throws, and returns `null` (fail-closed to "no update") if the database read *also* fails.
2. `CheckAppUpdateQueryHandler` wraps its own call to that service in a catch-all, as a second,
   independent guarantee.

## Admin API

```
GET    /api/admin/app-releases                 — paged list, filter by platform/enabled
GET    /api/admin/app-releases/{id}             — detail
POST   /api/admin/app-releases                  — create
PUT    /api/admin/app-releases/{id}             — update (Platform is immutable — create a new row for a different platform)
POST   /api/admin/app-releases/{id}/enable
POST   /api/admin/app-releases/{id}/disable
DELETE /api/admin/app-releases/{id}             — soft delete
```

All admin endpoints require `[Authorize(Roles = RoleNames.Admin)]`. Every mutation writes an
`AuditLog` row (`AppReleaseCreatedByAdmin`, `UpdatedByAdmin`, `EnabledByAdmin`,
`DisabledByAdmin`, `DeletedByAdmin`) with an old/new JSON snapshot, `who`/`when` from the
existing audit system.

**Validation**: `Version`/`MinimumSupportedVersion` must parse as `major.minor.patch`;
`MinimumSupportedVersion` cannot exceed `Version`; `StoreUrl`, if provided, must be an absolute
`http`/`https` URL (it is optional — no real store URLs exist yet); creating/enabling a release
that duplicates an already-enabled platform+version returns `409 Conflict` (backed by a database
unique index, not just the application-level check).

## Caching

The effective-release lookup is cached per platform
(`app-update:effective-release:{platform}`, 5-minute TTL) via the existing
`IDistributedCache`/`GetOrCreateAsync` infrastructure. Every admin mutation invalidates the
affected platform's cache entry unconditionally, regardless of whether the mutated row is
currently enabled — the simplest rule that stays trivially auditable.

The cached value is a plain DTO (`EffectiveAppReleaseDto`), never the `AppRelease` domain entity
— entities in this codebase use private constructors/setters (DDD convention), which
`System.Text.Json` cannot deserialize, and `GetOrCreateAsync` always JSON-round-trips its value
regardless of which `IDistributedCache` backing store is used.

## Version comparison

`PropertyApi.Domain.AppUpdates.ValueObjects.AppVersion` compares `Major`/`Minor`/`Patch` as
integers, never as a string — `"1.10.0"` must sort after `"1.9.9"` even though `'1' < '9'`
lexicographically. Only `major.minor.patch` is accepted (no pre-release/build-metadata suffix,
no leading zeros).

## Migration

`AddAppReleases` creates the `AppReleases` table plus two indexes:
- **Unique partial**: `(Platform, Version) WHERE "IsEnabled" = true AND "IsDeleted" = false` —
  the actual enforcement of "no duplicate enabled release per platform+version"; disabled or
  soft-deleted duplicates are allowed to coexist.
- **Non-unique**: `(Platform, IsEnabled)` — the hot lookup path, hit on every app launch.

## Rollout status (all phases)

| Phase | Scope | Where |
|---|---|---|
| 1 | Backend: data model, check endpoint, admin CRUD, audit, cache, tests | this repo (PR #153) |
| 2 | Angular `AppUpdateService`, optional/mandatory dialogs, native store deep-link | Angular repo (HudhudNest) PR #80 |
| 3 | Admin dashboard `/admin/app-updates` | HudhudNest PR #81 |
| 4 | Native version tooling (`version:check` / `version:set`), CI check, release + rollback runbook | HudhudNest PR #82, `docs/app-release-procedure.md` |

The frontend repo documents its own side: `docs/app-update-admin-dashboard.md` and
`docs/app-release-procedure.md` (release order and rollback scenarios — read the latter before
raising a `MinimumSupportedVersion` in production).

## Troubleshooting

- **A user is stuck on the "update required" screen after a bad release.** Disable that release (or
  lower its `MinimumSupportedVersion`) in the admin dashboard. The cache entry is invalidated
  immediately; the client re-checks on its next app launch (not on resume).
- **The dashboard change is not visible to the check endpoint.** It should be immediate (every admin
  mutation invalidates the platform key). If Redis is unreachable the read path already bypasses the
  cache and reads the database, so a stale answer can only last up to the 5-minute TTL when the
  invalidation itself failed during a Redis outage.
- **Check returns `updateAvailable=false` for everyone.** No *enabled, non-deleted* release exists for
  that platform (the endpoint deliberately answers "no update" rather than an error). Note that
  `Platform` values are `Android`, `IOS`, `Web`.
- **422 from the check endpoint.** `currentVersion` is not `major.minor.patch` or `platform`/`lang`
  is invalid. The client normalizes a two-segment iOS `1.0` to `1.0.0` before calling.
- **409 when enabling/creating.** Another enabled release already exists for the same platform +
  version (partial unique index). Disable or edit the other one first.
- **Integration tests failing locally with `ObjectDisposedException` / "host is stopping".** A hosted
  service threw on a failed database call and stopped the test host (unmigrated tables on an empty
  database, or a local Postgres rejecting the test's hardcoded `postgres/postgres` credentials). Fixed in
  PR #204 (per-tick guards on the three unguarded timers); on older branches apply the migrations
  first, as CI does (`Apply EF Core migrations` in `ci.yml`).
