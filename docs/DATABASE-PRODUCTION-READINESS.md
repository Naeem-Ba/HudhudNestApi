# PropertyApi — Database Production Readiness (Phase 2)

Audit date: 2026-09-04. Scope: PostgreSQL/PostGIS database only (Phase 2 of the production
readiness gate). Method: static inventory of the actual EF Core model, entity configurations,
and migrations in this repository, cross-checked against the project's own operational
runbooks, plus a live verification pass — all 35 migrations were applied from an empty
database to a disposable `postgis/postgis:17-3.5` container started for this audit
(`docker run postgis/postgis:17-3.5`, cleaned up afterward), and the resulting schema was
queried directly with `psql`. Nothing below asserts "production ready" on the strength of
`dotnet build` or the presence of migration files alone.

Everything in this document is evidence-based: either read from the repository (file/line
cited), or observed against the live disposable database this session provisioned. Where
something could not be verified — because it requires production credentials, production
data, or a Render dashboard this session has no access to — it is marked **NOT VERIFIED**,
not PASS.

---

## 1. Executive Summary

**PASS WITH WARNINGS.**

The schema itself — entity design, PostGIS, image storage, money types, concurrency tokens,
delete behavior, migration history, and the backup/restore engineering — is unusually mature
for a project at this stage; this is not a first-pass database. Migration reproducibility was
verified live this session. No BLOCKER was found in the schema or migrations.

Three findings keep this from a clean PASS, none of them BLOCKER-severity today, but two are
HIGH and should be closed before real user data accumulates:

- **HIGH** — the CI build and the Production Deployment Gate test against PostgreSQL
  16/PostGIS 3.4, while production runs PostgreSQL 17 (§40, §42, Finding F1).
- **HIGH** — no database-level protection makes soft-deleted users disappear from the
  business-profile table (`UserAccounts`) that `Property.Owner`, reviews, and favorites
  actually reference — only the paired auth table (`Users`) is filtered (§25, §32, Finding F2).
- **MEDIUM** — email case-insensitive uniqueness is not enforced by a database constraint;
  it currently holds only as a side effect of one implementation detail (§6, Finding F3).

Backup/restore engineering is extensive and a local drill passed with RPO 1.72 min / RTO
4.57 min, but the project's own checklist still lists the production-storage drill, protected
GitHub environments, and the Render deploy-gate wiring as open — this report does not
override that; see §13.

## 2. Current Database Architecture

- **Engine**: PostgreSQL, PostGIS extension. Provider: `Npgsql.EntityFrameworkCore.PostgreSQL`
  8.0.11 on EF Core 8 / .NET 8 (`Directory.Packages.props`, `PropertyApi.Infrastructure.csproj`).
- **Context**: one primary `AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`
  ([AppDbContext.cs](../PropertyApi.Infrastructure/Persistence/AppDbContext.cs)) plus a second,
  separate `DataProtectionKeyDbContext` for ASP.NET Data Protection keys.
- **Design-time**: `AppDbContextFactory` ([AppDbContextFactory.cs](../PropertyApi.Infrastructure/Persistence/AppDbContextFactory.cs)).
- **Migration execution**: a dedicated console tool, `tools/PropertyApi.Migrator`, applies
  migrations and runs `DatabaseSeeder`. Startup-time auto-migration is deliberately not used in
  the API process (confirmed both in code — no `Database.Migrate()` call in `PropertyApi`'s
  `Program.cs` — and in the migration-recovery runbook).
  This is also memory of a live Render Staging deploy: `Database__ApplyMigrationsOnStartup`
  is a vestigial config key nothing reads there either.
- **Connection resolution**: `PostgresConnectionStringResolver` ([PostgresConnectionStringResolver.cs](../PropertyApi.Infrastructure/PostgresConnectionStringResolver.cs))
  reads `DATABASE_URL` in Production, `ConnectionStrings:DefaultConnection` otherwise; normalizes
  postgres:// URIs, forces SSL in Production, rejects placeholder passwords and
  `Trust Server Certificate=true`, and applies Supabase-pooler-aware pool-size defaults.
- **Environments observed**: three different PostgreSQL major versions are in active use
  across this project's own environments — see Finding F1.

## 3. ERD / Relationship Summary

Reflects the actual applied schema (verified live: 64 tables from a clean migration run).
Only entities that exist in `AppDbContext` are shown; no proposed/future entities. Full
diagram saved separately at [docs/database/erd.md](database/erd.md) (kept out of this file to
stay readable — GitHub and most Markdown viewers render the `mermaid` fence natively).

Core relationship shape:

```
Users (ApplicationUser, Identity)  1───1  UserAccounts (profile)
UserAccounts  1───*  Properties (Owner)         Properties  *───1  PropertyType
UserAccounts  0..1─* Properties (Agent)         Properties  *───1  Governorate/District/Neighborhood
Properties    1───*  PropertyImages
Properties    1───*  PropertyAmenities  *───1  Amenities
Properties    1───*  Favorites  *───1  UserAccounts
Properties    1───*  PropertyReviews  *───1  UserAccounts (Reviewer)
Properties    1───*  VisitRequests  *───1  UserAccounts (Requester)
Users         1───*  RefreshTokens
Users         0..1─* AuditLogs (SetNull)
UserAccounts  1───*  UserRatings (Rater/Rated)
UserAccounts  0..1─1 Agencies (membership)
UserAccounts  0..1─* Plans (subscription)
Properties/UserAccounts/VisitRequests  ← Transactions (Restrict/Restrict/SetNull)
ShortStayListings 1─* AccommodationUnits 1─* UnitBookingRanges (EXCLUDE, no-overlap)
InvestmentProjects 1─1 InvestmentProjectFinancials, 1─* InvestmentDocuments/Updates/Interests
```

See §7 for `Users` vs `UserAccounts` and why they are two tables, and Finding F2 for the
consistency gap between them.

## 4. Tables Inventory

64 tables exist in the applied schema (7 are PostGIS/tiger-geocoder system tables created by
`CREATE EXTENSION postgis` itself — `spatial_ref_sys`, `topology`, `layer`, and the TIGER
geocoder tables — not application tables). Application tables, grouped by domain, from the
`DbSet<>` list in [AppDbContext.cs:56-114](../PropertyApi.Infrastructure/Persistence/AppDbContext.cs#L56-L114):

| Domain | Tables |
|---|---|
| Identity/Auth | `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `RoleClaims`, `UserTokens` (Identity defaults, renamed), `RefreshTokens`, `DataProtectionKeys` |
| User profile | `UserAccounts` |
| Verification | `PhoneOtpChallenges` (no separate email-verification table — see §9) |
| Listings | `Properties`, `PropertyImages`, `PropertyPriceHistories`, `Amenities`, `PropertyAmenities`, `Favorites` |
| Lookups | `Currencies`, `Governorates`, `Districts`, `Neighborhoods`, `PropertyTypes`, `LocationSuggestions` |
| Agencies | `Agencies`, `AgencyInvitations` |
| Plans | `Plans` |
| Bookings/Visits | `VisitRequests` |
| Reviews | `PropertyReviews`, `UserRatings` |
| Messaging | `Messages`, `ContactMessages` |
| Notifications | `Notifications` |
| Finance | `Transactions` |
| Search | `SavedSearches` |
| Audit | `AuditLogs` |
| Services marketplace | `ServiceProviders`, `ServiceOfferings`, `ServiceRequests`, `ServiceRequestStatusHistories`, `ServiceRequestDocuments`, `ServiceReviews` |
| Short-stay | `AccommodationTypes`, `ShortStayListings`, `RoomTypes` (`ShortStayRoomTypes`), `AccommodationUnits`, `PricingRules`, `MinimumStayRules`, `UnitBookingRanges`, `ShortStayListingAmenities`, `ShortStayListingPhotos`, `Bookings` (`ShortStayBookings`), `ShortStayReviews`, `HostVerificationRecords` |
| Investments | `InvestmentProjects`, `InvestmentProjectFinancials`, `InvestmentRiskAssessments`, `InvestmentDocuments`, `InvestmentUpdates`, `InvestmentWatchlistItems`, `InvestmentInterests` |

Per-table PK/FK/index/constraint detail for the tables that matter most to this gate is in
§5–§9 below rather than repeated 50 times here.

## 5. User / Identity Audit

Two tables carry what a single "User" concept usually would, split deliberately:

- **`Users`** (`ApplicationUser : IdentityUser<Guid>`,
  [ApplicationUser.cs](../PropertyApi.Infrastructure/Identity/Entities/ApplicationUser.cs)) —
  authentication and account-security state: password hash, security stamp, lockout,
  phone-verification lifecycle, ban state, soft-delete.
- **`UserAccounts`** (`UserAccount`,
  [UserAccount.cs](../PropertyApi.Domain/Users/Entities/UserAccount.cs)) — the business profile
  that `Property.Owner`, reviews, favorites, and agency membership actually reference. Shared
  primary key with `Users` (`UserAccounts.Id = Users.Id`), FK `Restrict` (not cascade) so the
  pairing cannot silently break — [UserAccountConfiguration.cs:26-29](../PropertyApi.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs#L26-L29).

| Field | Present | Notes |
|---|---|---|
| Primary Key | ✅ | `Guid`, shared between `Users` and `UserAccounts` |
| Email | ✅ | `Users.Email`, `varchar(320)`, required |
| Normalized Email | ✅ | `Users.NormalizedEmail`, but **not** the unique column — see Finding F3 |
| Username | ✅ | Always set equal to Email (or phone, for phone-only accounts) — [IdentityAccountCreator.cs:34](../PropertyApi.Infrastructure/Identity/Services/IdentityAccountCreator.cs#L34) |
| Password Hash | ✅ | Identity-managed `PasswordHash`; never logged (§7 below) |
| Phone | ✅ | Encrypted at rest via Data Protection (§14), plus a separate deterministic HMAC lookup hash column for exact-match queries |
| CreatedAt/UpdatedAt | ✅ | On both `Users` and `UserAccounts` |
| Status (banned) | ✅ | `IsBanned`/`BanReason` on `Users`, administrative, separate from Identity lockout |
| Email verification state | ✅ | Identity's own `EmailConfirmed` |
| Security stamp | ✅ | Identity default, with `CachedSecurityStampValidator` for distributed invalidation |
| Sessions | ❌ NOT IMPLEMENTED | No dedicated sessions table/concept exists; auth state is carried by `RefreshTokens` + Identity's security stamp. This is a legitimate architectural choice for a stateless-API + refresh-token design, not a gap — not invented here. |

## 6. User Identity Integrity — Finding F3 (MEDIUM)

**Email case-insensitive uniqueness is not enforced by a database constraint on the correct
column, and holds today only as a side effect of one implementation detail.**

Evidence:
- `Identity.Options.User.RequireUniqueEmail = false` —
  [PersistenceInfrastructureRegistration.cs:52](../PropertyApi.Infrastructure/Persistence/PersistenceInfrastructureRegistration.cs#L52).
  This disables ASP.NET Identity's own case-insensitive email-uniqueness check.
- The only unique index on email is `IX_Users_Email UNIQUE btree ("Email")` — on the **raw,
  case-sensitive** column — [ApplicationUserConfiguration.cs:15-16](../PropertyApi.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs#L15-L16),
  confirmed live: `CREATE UNIQUE INDEX "IX_Users_Email" ... USING btree ("Email")`.
- `NormalizedEmail` carries only Identity's own default **non-unique** index (`EmailIndex`),
  confirmed live.
- Live proof (this session, against the migrated PG17 database): two rows were inserted with
  `Email = 'user@example.com'` / `Email = 'USER@example.com'` (same `NormalizedEmail`,
  different `UserName`/`NormalizedUserName`, simulating any code path that does not set
  `UserName` equal to `Email`) — **both inserts succeeded**, producing two accounts for what a
  case-insensitive system must treat as the same address.
- Why this is not actively exploitable *today*: every current account-creation path
  (`IdentityAccountCreator.cs:34`, `PhoneAuthenticationWorkflow.cs:305` — the only two places
  `new ApplicationUser` is constructed) sets `UserName` equal to the exact `Email` string.
  Identity's own **unique** `UserNameIndex` is on `NormalizedUserName` (case-insensitively
  normalized regardless of the input's casing), so it incidentally rejects a concurrent
  case-variant duplicate today. `RegisterAtomicityPostgresTests.ConcurrentRequests_WithSameEmail_OnlyOneSucceeds_ByDatabaseUniqueConstraint`
  ([RegisterAtomicityPostgresTests.cs:163-238](../tests/PropertyApi.Integration.Tests/Auth/RegisterAtomicityPostgresTests.cs#L163)) proves same-case concurrent duplicates are blocked, but does not exercise the case-variant scenario.
- The risk: this protection is incidental, not declared. Any future code path that creates or
  edits a user with an `Email` but an independently-chosen `UserName` (a "pick your own
  username" feature, a different social-login binding path, an admin tool) silently loses this
  protection, because the column that is actually unique (`Email`, raw) does not implement the
  business rule ("case-insensitive email"), and the layer that is supposed to implement it
  (`RequireUniqueEmail`) is turned off.

**Recommendation** (fix is well-understood and backward-compatible, but changes login/lookup
behavior and touches every `FindByEmailAsync` caller — this is a decision, not applied here):
add a unique index on `NormalizedEmail` (filtered to exclude soft-deleted rows per Finding F2's
`IsDeleted` discussion), or move to `RequireUniqueEmail = true` with a corrective migration
that resolves any existing case-variant duplicates first.

## 7. Password Security

`Users.PasswordHash` is Identity's standard hash (PBKDF2 via `PasswordHasher<TUser>` unless
overridden — no custom hasher was found in `Auth/Security`). No plaintext or reversibly-encrypted
password field exists anywhere in the schema (confirmed: no `Password` column, only
`PasswordHash`, across every migration and configuration reviewed). No hash values are
reproduced anywhere in this report or its evidence queries.

## 8. Refresh Tokens

`RefreshTokens` ([RefreshToken.cs](../PropertyApi.Infrastructure/Identity/Entities/RefreshToken.cs),
[RefreshTokenConfiguration.cs](../PropertyApi.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs)):

| Capability | Status | Evidence |
|---|---|---|
| Stored as hash, not plaintext | ✅ | `TokenHash`, `ReplacedByTokenHash` — the in-code doc comment's "consider hashing" warning is stale; `HashRefreshTokens` migration (2026-06-07) already did this |
| Rotation | ✅ | `ReplacedByTokenHash` chain field |
| Expiration | ✅ | `ExpiresAt`, `IsExpired` computed |
| Revocation | ✅ | `IsRevoked`, `RevokedAt`, `RevokedByIp` |
| Reuse detection support | ✅ | `ReplacedByTokenHash` lets a reused-old-token check detect theft |
| User association | ✅ | `UserId` FK, `Cascade` delete |
| Device/session association | ❌ | No device fingerprint/session id column — only `CreatedByIp`/`RevokedByIp` |
| Indexes | ✅ | Unique on `TokenHash`; `UserId`; composite `(UserId, IsRevoked, ExpiresAt)` named `IX_RefreshTokens_User_Active_ExpiresAt` — matches the actual lookup pattern |

No gap rises above INFO here (missing device/session binding is a feature question, not a
integrity defect — not converted to a finding per the instruction not to invent requirements).

## 9. Verification / Password Reset

There is **no separate password-reset or email-verification token table**. This platform's
primary verification channel is phone OTP:

- `PhoneOtpChallenges` ([PhoneOtpChallenge.cs](../PropertyApi.Domain/Auth/Entities/PhoneOtpChallenge.cs)) —
  covers `PhoneRegistration`, `PhonePasswordReset`, `PhoneReverification`, `PhoneNumberChange`
  (`OtpPurpose` enum, [OtpPurpose.cs](../PropertyApi.Domain/Enums/OtpPurpose.cs)). Has
  `ExpiresAtUtc` (5-minute TTL, factory-enforced), `ConsumedAtUtc` (one-time use), `AttemptCount`,
  and a reservation mechanism (`ReservationId`/`ReservedUntilUtc`) that appears purpose-built to
  close a concurrent-verify race. `CodeHash`, not the raw code, is stored.
- Email confirmation/password-reset-by-email uses ASP.NET Identity's built-in token providers
  (`AddDefaultTokenProviders()` in `PersistenceInfrastructureRegistration.cs:61`), which are
  stateless HMAC tokens validated against the user's security stamp — by design there is no
  stored token row to expire or revoke; invalidating the security stamp invalidates every
  outstanding token at once. This is a standard, accepted Identity pattern, not a gap.

No unbounded-lifetime sensitive token was found.

## 10. Sessions

**STATUS = NOT IMPLEMENTED.** No sessions table or session-identifier concept exists in the
schema. Not added here — the instruction to this audit was explicit that a missing feature is
not itself a database defect.

## 11. Roles / Authorization

`ApplicationRolesSeed.cs` seeds five deterministic roles: `Admin`, `Agent`, `User`,
`AgencyOwner`, `AgencyAgent` (from `RoleNames`, not invented for this report). Idempotent
(`RoleExistsAsync` check before create), and it — along with every other seed — runs inside a
Postgres advisory-lock-guarded transaction
(`pg_advisory_xact_lock(20260621194421)`,
[DatabaseSeeder.cs:36-49](../PropertyApi.Infrastructure/Persistence/Seeds/DatabaseSeeder.cs#L36))
so concurrent app-instance startups cannot race the seed. Standard Identity `UserRoles` table
carries the FK/uniqueness (composite PK `(UserId, RoleId)`).

## 12–19. Property Domain Model

Reviewed directly from [Property.cs](../PropertyApi.Domain/Listings/Entities/Property.cs) and
[PropertyConfiguration.cs](../PropertyApi.Infrastructure/Persistence/Configurations/PropertyConfiguration.cs).

| Area | Status | Evidence |
|---|---|---|
| **Identity** (§13) | ✅ PASS | `Guid` PK, server-generated in the `Create` factory, no client-suppliable duplicate path |
| **Status** (§14) | ✅ PASS | `PropertyStatus` enum, `HasConversion<string>()`, `HasDefaultValue(Available)` — stored as readable text, immune to enum-reordering corruption |
| **Type** (§15) | ✅ PASS | `PropertyTypeId` FK → lookup table `PropertyTypes`, `Restrict` delete, seeded (`PropertyTypesSeed.cs`) |
| **Price** (§16) | ✅ PASS | `ColdRent`/`WarmRent`/`PurchasePrice`/`AdditionalCosts`/`Deposit` all `decimal(18,4)` — never `float`/`double` (confirmed: zero occurrences of `float`/`double` for money anywhere in `Domain`). No `>= 0` CHECK constraint — see Finding F4. |
| **Currency** (§17) | ✅ PASS | `CurrencyCode` fixed `char(3)`, ISO-4217, default `"SYP"`; separate `Currencies` lookup table with unique `Code`, `decimal(18,6)` exchange rate |
| **Area** (§18) | ⚠️ | `decimal(10,2)` with an explicit `AreaUnit` enum (no silent unit assumption) — correct typing, but no `> 0` CHECK — Finding F4 |
| **Rooms** (§19) | ⚠️ | Plain nullable `int`, no CHECK — Finding F4 |
| **Location/PostGIS** (§20–21) | ✅ PASS — see §7 (ERD) and live verification below | |
| **Images** (§22–23) | ✅ PASS | See below |

### PostGIS (§20-21) — live-verified

`GeoLocation geography(Point, 4326) GENERATED ALWAYS AS (... ST_MakePoint(Longitude, Latitude) ...) STORED`
([20260611065011_AddPropertyGeoLocationPostGis.cs](../PropertyApi.Infrastructure/Migrations/20260611065011_AddPropertyGeoLocationPostGis.cs)) —
a **generated/computed column**, not an app-synced or trigger-synced one, so `GeoLocation` can
never drift from `Latitude`/`Longitude`. Correct SRID (4326/WGS84), correct coordinate order
(longitude, then latitude, matching PostGIS's `ST_MakePoint(x, y)` convention).

Confirmed live against the freshly migrated PG17/PostGIS-3.5.2 database:
```
GeoLocation | geography(Point,4326) | generated always as (...) STORED
"IX_Properties_GeoLocation" gist ("GeoLocation")
"IX_Properties_GeoLocation_Published" gist ("GeoLocation")
   WHERE "IsPublished" = true AND "IsDeleted" = false AND "GeoLocation" IS NOT NULL
```
Two GiST indexes: an unconditional one, and a `CREATE INDEX CONCURRENTLY`-built partial index
(2026-07-28 migration) scoped to published, non-deleted rows with coordinates — sized to the
query that actually runs (radius search over live listings), and built without locking the
table on rollout. `docs/performance/postgresql-query-analysis.md` independently documents this
index as release-blocking if it disappears or stops being used, with a measured-evidence policy
for any future spatial index change. This whole area is a genuine strength, not a minimum-bar
pass.

### Image storage (§22-23) — PASS, no blocker

`PropertyImages` ([PropertyImage.cs](../PropertyApi.Domain/Listings/Entities/PropertyImage.cs),
[PropertyImageConfiguration.cs](../PropertyApi.Infrastructure/Persistence/Configurations/PropertyImageConfiguration.cs)):
`Url` (CDN URL, `varchar(2048)`) + `PublicId` (Cloudinary public id, `varchar(500)`) only.
**Zero `byte[]`/`varbinary`/Base64 columns found anywhere in the Domain layer** (explicit grep,
zero matches) — images are never stored as binary data in PostgreSQL. `PropertyId` FK,
`Cascade` delete (images die with their property, correct — they have no independent meaning).
A filtered unique index `IX_PropertyImages_OneMainPerProperty` on `(PropertyId, IsMain)` where
`IsMain = true` correctly enforces "at most one main image" at the database level, not just in
application code.

## Finding F4 (MEDIUM) — No CHECK constraints on core numeric invariants

Verified live against the fully-migrated schema: querying `pg_constraint` for every `contype='c'`
(CHECK) constraint returns exactly **one** application-defined check in the entire database —
`CK_Plans_ListingLimit_PositiveOrUnlimited` on `Plans.ListingLimit`
([PlanConfiguration.cs:50](../PropertyApi.Infrastructure/Persistence/Configurations/PlanConfiguration.cs#L50)).
(Every other CHECK returned by that query belongs to PostGIS/TIGER-geocoder system tables, not
application tables.)

Not database-enforced anywhere else: `Property.Rooms >= 0`, `Property.Area > 0`,
`Property.ColdRent/WarmRent/PurchasePrice/AdditionalCosts/Deposit >= 0`,
`PropertyReview.Rating BETWEEN 1 AND 5` (enforced only in the domain factory —
[PropertyReview.cs:29](../PropertyApi.Domain/Reviews/Entities/PropertyReview.cs#L29) — and a
FluentValidation validator; both are real, but both are application code, not schema),
`Transaction.Amount >= 0`, every Investments financial field.

This is not a BLOCKER: the write paths that matter (domain factories, FluentValidation
validators, EF Core) do enforce these invariants today, and were exercised in review. But per
the instruction not to rely on application validation alone for critical invariants, any direct
SQL write, a future raw-SQL migration/backfill, or an ORM bypass has no database-level backstop
against negative prices, zero-area listings, or an out-of-range rating. Recommended (not
applied — schema change requiring migration + review):
`CHECK ("Rooms" IS NULL OR "Rooms" >= 0)`, `CHECK ("Area" IS NULL OR "Area" > 0)`,
`CHECK ("Rating" BETWEEN 1 AND 5)`, and non-negative checks on the five Property money columns
and `Transaction.Amount`.

## Finding F2 (HIGH) — Soft-delete boundary does not cover the table most relationships reference

- `ApplicationUser` (`Users`) has `IsDeleted`/`DeletedAt` and a global query filter:
  `builder.Entity<ApplicationUser>().HasQueryFilter(e => !e.IsDeleted)` —
  [AppDbContext.cs:144](../PropertyApi.Infrastructure/Persistence/AppDbContext.cs#L144).
- `UserAccount` (`UserAccounts`) — the table `Property.Owner`, `Property.Agent`,
  `PropertyReview.Reviewer`, `Favorite.User`, `VisitRequest.Requester`, `UserRating`, and agency
  membership all actually FK to
  ([PropertyConfiguration.cs:149,230](../PropertyApi.Infrastructure/Persistence/Configurations/PropertyConfiguration.cs#L149)) —
  has **no `IsDeleted` column at all** (`UserAccount.cs` does not inherit from `BaseEntity`/
  `AuditableEntity`, confirmed by direct read) and therefore **no query filter**.
- Consequence: soft-deleting/banning a `Users` row does not hide, and cannot hide, the paired
  `UserAccounts` row from any read path that queries `UserAccounts`/`Properties`/reviews
  directly — which is most of the application's read paths, since `Owner`/`Reviewer`/`Requester`
  navigations are typed as `UserAccount`, not `ApplicationUser`. A banned or deleted user's
  display name, avatar, bio, and listing/review attribution can remain fully visible.

This was verified structurally (entity definitions, `OnModelCreating`, and the FK graph); it was
not exhaustively traced through every controller/query handler to check for a compensating
application-level filter elsewhere; if one exists outside the entities reviewed, it is not
database-enforced regardless. Given this affects moderation (banning) and privacy expectations,
this is rated **HIGH** pending confirmation from the team on whether this is intentional
(e.g., "profile data intentionally survives a login-level ban") or an oversight — per the
instruction not to unilaterally decide a business rule, no change is applied here.

Related, same root cause: the unique indexes on `Users.Email` and `Users.NormalizedPhoneNumber`
carry no `IsDeleted`-aware filter either, so a soft-deleted/banned account permanently occupies
its email and phone slot — nobody, including the same real person, can ever register again with
that email or phone. This may be an intended anti-abuse measure; it is flagged, not changed.

## Finding F1 (HIGH) — PostgreSQL major-version drift across this project's own environments

Directly observed in this repository, this session:

| Environment | Postgres image pinned | Source |
|---|---|---|
| CI build (`ci.yml`) | `postgis/postgis:16-3.4` | `.github/workflows/ci.yml:44` |
| Production Deployment Gate (`production-gate.yml`) | `postgis/postgis:16-3.4` | `.github/workflows/production-gate.yml:44`, `ci/docker-compose.production-gate.yml:14` |
| Performance baseline testing | `postgis/postgis:16-3.4` | `performance/docker-compose.performance.yml:64` |
| Database-recovery drill | `postgis/postgis:17-3.5` | `ci/docker-compose.database-recovery.yml:13,30` — **changed 2026-09-02**, commit `fcd50b5`, message: *"match restore-drill Postgres image to production's major version"*, code comment: *"real production reports server major 17, so the drill's disposable restore target must be 17 too, not a stale 16 pin"* |

That commit is a direct, first-party admission that **production runs PostgreSQL major version
17**, made three days before this audit. Nobody has yet propagated that fix to `ci.yml`,
`production-gate.yml`, or the performance-baseline compose file — all three still validate
against PostgreSQL 16/PostGIS 3.4.

Separately, per this project's own operational record (Render Staging deployment notes,
2026-09-04), the live Staging Postgres instance is **PostgreSQL 18** (Render Free tier). That
detail was not independently re-verified against a live Render dashboard in this session — it
is carried here as a recorded operational fact, not re-derived — but if accurate it means a
third major version is involved.

Why this matters for a database-readiness gate specifically: `pg_dump`/`pg_restore` across a
client/server major-version mismatch is refused by this project's own recovery tooling
(`assert_postgres_client_matches_server`, per the code comment above) — the project already
treats major-version mismatch as unsafe for backup/restore. The same reasoning applies to the
gate that is supposed to decide whether a migration is safe to ship: **the Production
Deployment Gate — the check Render is meant to require before deploying — validates schema and
migrations against a PostgreSQL major version the production database is not running.** A
planner behavior change, a catalog default change, or a PostGIS 3.4-vs-3.5 function difference
between 16 and 17 would not be caught by CI or the gate; it would only be caught by production
itself, or by the recovery drill (which is not on the deploy-blocking path).

**Recommendation**: bump `postgis/postgis:16-3.4` → `postgis/postgis:17-3.5` in `ci.yml`,
`production-gate.yml`, and `performance/docker-compose.performance.yml` to match what the
recovery-drill fix already established about production, and confirm the actual Staging/
Production versions with the hosting dashboard rather than relying on this document.

## Finding F5 (LOW) — Migration-risk baseline documentation is behind the enforced gate

`ci/migration-risk-baseline.json` (the file `scripts/database/analyze-migrations.sh` actually
reads) covers every migration through `20260829114909_AddShortStayAccommodation`. The two most
recent migrations — `20260901200038_AddUserAccountSubscriptionLifecycle` and
`20260904074621_AddInvestmentDiscoveryModule` — have no baseline entry. I could not execute the
script itself (`python3` is not installed on this machine — `analyze-migrations.sh:6` requires
it, confirmed by running it: `ERROR: Required command is missing: python3`), so I replicated its
exact classification regex by hand against both migrations' `Up()` methods:

- `AddUserAccountSubscriptionLifecycle`: only `AddColumn`, `CreateIndex`, `AddForeignKey` in `Up()`.
- `AddInvestmentDiscoveryModule`: only `CreateTable`, `CreateIndex` in `Up()`.

Neither matches the script's `DropTable|DropColumn|DeleteData` (destructive) or
`Sql(|AlterColumn|RenameColumn|RenameTable` (high-risk) patterns, so both classify as
`ADDITIVE` under the script's own rules — meaning **the automated gate would still pass**
without a baseline entry (`analyze-migrations.sh:44`: only non-`ADDITIVE` migrations require
one). This is a documentation/audit-trail completeness gap, not a control failure — the
markdown table in `database-migration-recovery-runbook.md` is even further behind (stops at
2026-07-16) and should be refreshed together with the JSON baseline.

## Finding F6 (LOW-MEDIUM) — No `EnableRetryOnFailure`

`options.UseNpgsql(connectionString)` in `PersistenceInfrastructureRegistration.cs:32` does not
enable EF Core's retrying execution strategy. `DatabaseSeeder.cs` calls
`context.Database.CreateExecutionStrategy()` for its own advisory-lock transaction, but that
resolves to the default (non-retrying) strategy given the DbContext registration above. A
transient network blip against a managed/pooled Postgres (the connection resolver already
anticipates Supabase's pooler) currently surfaces as a hard failure rather than a bounded retry.
Recommended, not applied (low risk, but changes retry/transaction semantics and needs review
against every place a manual `BeginTransactionAsync` is used, since retry strategies and
explicit transactions interact): `o.EnableRetryOnFailure(...)`.

## Finding F7 (MEDIUM) — GDPR technical capability: no account deletion / data export path found

Searched `PropertyApi.Application` for account-deletion or data-export capability
(`DeleteAccount`, `AccountDeletion`, `ExportData`, `DataExport`, `GDPR` — zero matches). What
exists: OTP/refresh-token expiration, admin-driven soft-delete (`IsDeleted` + `MarkAsDeleted`
domain methods on several entities), and Data-Protection-encrypted storage for phone/WhatsApp/
tax-number fields (§14). What does not appear to exist: a self-service "delete my account" or
"export my data" endpoint. This is recorded as a **technical-capability gap**, not a legal
compliance claim, per the instruction in scope for this audit.

## 24-25. Referential Integrity / Delete Behavior

Reviewed every `HasOne`/`HasMany`/`OnDelete` in the configurations touching the tables in scope.
The pattern is deliberate and consistently documented in-code (not incidental):

| Relationship | Delete behavior | Rationale (from code comments, paraphrased) |
|---|---|---|
| `Property → Owner (UserAccount)` | `Restrict` | Deleting a user must not cascade-delete their listings |
| `Property → Images/Messages/Favorites` | `Cascade` | Child rows have no meaning without the property |
| `Property → Agent/AgencyId` | `SetNull` | Optional attribution; losing the agent/agency must not remove the listing |
| `Property → Governorate/District/Neighborhood/PropertyType` | `Restrict` | Lookup rows referenced by live listings cannot be silently dropped |
| `UserAccount → ApplicationUser` (shared PK) | `Restrict` | The pairing must never silently break — app deletes both explicitly together |
| `UserAccount → Agency` | `SetNull` | Leaving/losing an agency doesn't delete the account |
| `UserAccount → Plan` | `Restrict` | A plan in use must be deactivated (`IsActive=false`), not deleted |
| `Transaction → Property/Payer/Receiver` | `Restrict` (all three) | "This is exactly the table where 'no DB-enforced integrity' is least acceptable" — financial ledger must outlive what it references |
| `Transaction → VisitRequest (BookingId)` | `SetNull` | A payment record must never disappear because the booking was removed |
| `AuditLog → User` | `SetNull` | Audit trail survives user deletion |
| `RefreshToken → User` | `Cascade` | Tokens have no independent value once the account is gone |
| `PropertyReview → Property` | `Cascade`; `→ Reviewer` | `Restrict` |
| `Favorite → User, → Property` | `Cascade`, `Cascade` (composite PK, no surrogate `Id`) |
| `UnitBookingRange → AccommodationUnit` | see EXCLUDE constraint below |

No orphan-permitting `NoAction` was found on any FK reviewed. No relationship was found where
deleting a `User`/`UserAccount` silently cascades into financial or audit data — the one place
that would matter most (`Transactions`) is `Restrict` on every FK. This is a genuine strength.

### Concurrency (§36) — verified, not assumed

`Property`, `Transaction`, and `UserAccount` all map the PostgreSQL system column `xmin` as an
EF Core concurrency token (`ValueGeneratedOnAddOrUpdate().IsRowVersion()`), each with an in-code
comment naming the specific read-check-write race it closes (e.g., a background sweep and a
concurrent user edit on the same `Property`; two concurrent fee confirmations on the same
`Transaction`). A second write now raises `DbUpdateConcurrencyException` (mapped to HTTP 409)
instead of silently overwriting. This is real optimistic-concurrency protection, not merely
declared — it uses Postgres's built-in row version, not a hand-rolled counter.

`UnitBookingRanges` additionally carries a genuine PostgreSQL **`EXCLUDE USING gist`**
constraint, confirmed live:
```
EX_UnitBookingRanges_NoOverlap
  EXCLUDE USING gist ("UnitId" WITH =, daterange("CheckIn","CheckOut",'[)') WITH &&)
  WHERE (("Status") IN ('Reserved','CheckedIn'))
```
This is database-enforced prevention of overlapping bookings for the same accommodation unit —
two concurrent booking transactions for overlapping dates on the same unit cannot both commit,
regardless of application-layer locking. This is a stronger guarantee than the unique-index
pattern used elsewhere and directly answers §36/§37's concurrency and booking-integrity
concerns for the one place in this schema where double-booking is a real risk.

## 26-27. Nullability / Constraints

Nullability was spot-checked against the fields §26 calls out by name: `Price`
(`ColdRent`/`WarmRent`/`PurchasePrice` are legitimately nullable — a listing is for-rent XOR
for-sale, so the inapplicable price fields are correctly `NULL`, not zero), `Currency`
(`NOT NULL`, defaulted), `PropertyType`/`GovernorateId`/`DistrictId` (nullable — explicitly
documented as "for compatibility with legacy data" — `PropertyTypeId`), `Status` (`NOT NULL`,
defaulted, enum-backed), `OwnerId` (`NOT NULL`), `CreatedAt` (`NOT NULL` via `AuditableEntity`
base). No column was found `NOT NULL` in a way that would reject legitimate domain states, and
no column was found nullable in a way that lets a clearly-required business fact go unset at
the database level. See Finding F4 for the separate CHECK-constraint gap (nullability and range
constraints are different controls).

## 28-29. Index Audit / Query Performance

Not re-derived from scratch: `docs/performance/postgresql-query-analysis.md` and
`docs/performance/current-performance-assessment.md` already document a controlled,
evidence-gated index-and-query-performance process for this exact schema — `pg_stat_statements`-based
capture, `EXPLAIN (ANALYZE, BUFFERS, ...)` plan capture for the five queries that matter most
(listing page, deep-offset page, property detail, combined filter search, PostGIS radius
search), and a live EF-interception-based N+1/query-count gate with budgets (list ≤ 3 queries,
detail ≤ 5, geo search ≤ 2) enforced in CI. That policy document states plainly: *"No index
migration is included without measured before/after evidence."* Spot-checking
`PropertyConfiguration.cs`'s ~15 indexes against it, every filtered/composite index present
(`IX_Properties_Syrian_Search`, `IX_Properties_Search`, `IX_Properties_Status_ExpiresAt`,
`IX_Properties_FeaturedUntil_Active`, `IX_Properties_GeoLocation_Published`, etc.) has an
in-code comment naming the specific query pattern it serves — none looked speculative.
`GetPropertiesListQueryHandler` delegates filtering, sorting, and paging to
`IPropertyRepository.GetPagedAsync`, which does the work as `IQueryable` (DB-side), not
in-memory. Page size is server-clamped: `Math.Clamp(filter.PageSize, 1, 100)` —
[PropertyRepository.cs:81](../PropertyApi.Infrastructure/Repositories/PropertyRepository.cs#L81)
— so a client cannot force an unbounded page regardless of what it requests. No unaudited N+1
pattern was found in the one handler reviewed in depth; the broader claim rests on the existing
enforced CI gate, which is a real, running control, not aspirational.

## 30-31. Pagination / Filtering — PASS

Confirmed above (§28-29): server-side clamp to 100, `Skip`/`Take` translated to SQL, filtering
in the repository's `IQueryable` pipeline.

## 32. Soft Delete

Implemented via `IsDeleted`/`DeletedAt` on `BaseEntity`/`AuditableEntity`-derived types, global
query filters in `AppDbContext.OnModelCreating` (`Property`, `PropertyImage`, `Amenity`,
`Message`, `ContactMessage`, `ApplicationUser`, `Notification`, `VisitRequest`, `PropertyReview`,
`UserRating`, `Transaction`, plus dependent filters on `Favorite`/`PropertyAmenity`/`RefreshToken`
that key off their parent's `IsDeleted`), and `AppDbContext.SaveChangesAsync` intercepts a hard
`Remove()` and rewrites it to a soft delete automatically
([AppDbContext.cs:238-253](../PropertyApi.Infrastructure/Persistence/AppDbContext.cs#L238)) — so
even a repository that calls `Remove()` cannot accidentally hard-delete a row. This is a strong,
centralized pattern. Its one gap is Finding F2 (`UserAccount` not covered).

## 33. Audit / History

`AuditLogs` exists (`Action`, `OldValue`/`NewValue` as `text`, `Timestamp`, indexed on
`UserId`/`Action`/`Timestamp`, `SetNull` on user delete). `PropertyPriceHistories` exists as a
dedicated table for price-change history. Property status/publication changes are not
separately versioned beyond `UpdatedAt` — recorded as a **RECOMMENDATION**, not a blocker, since
no explicit business requirement for full status-change history was found stated anywhere in
the repository.

## 34. Financial / Investment Data

`Transactions` and every `Investments/Entities/*` entity use `decimal` exclusively — confirmed
by direct grep of both directories; zero `float`/`double` occurrences. `Transaction` additionally
carries the `xmin` concurrency token (§36) and `Restrict` FKs on `Property`/`Payer`/`Receiver`
(§25). `InvestmentProjectFinancials`'s own doc comment states the `decimal`-only rule explicitly
("per Phase 1 spec §6. All amounts are `decimal` — never `double`"), matching what was found.

## 35. Transaction Integrity

`DatabaseSeeder` wraps all seeding in a single transaction guarded by an advisory lock (§11).
Registration atomicity (`Users` + `UserAccounts` + role assignment as one unit) is directly
covered by `RegisterAtomicityPostgresTests.cs`, run against a real PostgreSQL service in CI —
`CreateAsyncFailure_LeavesNoUserOrUserAccount` and `RoleFailure_RollsBackCreatedUserAndUserAccount`
both assert zero rows remain in either table after a mid-flow failure. This is genuine,
DB-verified atomicity evidence, not an assumption.

## 37. Booking / Visit Integrity

Two distinct booking concepts exist: `VisitRequests` (property viewing appointments — no
overlap-exclusivity requirement, since multiple visit requests for different times are valid)
and `UnitBookingRanges` (short-stay accommodation date ranges — genuinely exclusive). The latter
has the `EXCLUDE USING gist` constraint documented under §36 above; the former has FK integrity
(`Restrict` on both `Property` and `Requester`) and an index on `(PropertyId, RequesterId,
Status)` but no state-machine constraint on `Status` transitions — expected, since valid status
transitions are a business rule, correctly left to application code per the instruction not to
guess business rules.

## 38. Reviews

Covered under Finding F4 (rating range is application-only) and the review deep-dive above:
"one review per user per property" **is** database-enforced (`HasIndex(r => new
{ r.PropertyId, r.ReviewerId }).IsUnique()` —
[PropertyReviewConfiguration.cs:29-30](../PropertyApi.Infrastructure/Persistence/Configurations/PropertyReviewConfiguration.cs#L29)),
so that specific business rule is a genuine PASS at the database level, distinct from the rating
range which is not.

## 39. Seed Data — PASS

`DatabaseSeeder.SeedAsync` seeds only: currencies, governorates, neighborhoods, property types,
accommodation types, plans, and the five role definitions. **No default admin user, no default
password, no test user, and no fake property was found in any seed file** (`Seeds/*.cs` read in
full). Deterministic and idempotent (existence-checked before insert; Postgres advisory lock
guards concurrent app-instance startup races). Production-safe as found.

## 40-41. Migration Audit / Reproducibility

**Live-verified this session**: starting from the empty disposable database described at the
top of this document, all 35 migrations
(`20260601184159_InitialCleanArchitecture` → `20260904074621_AddInvestmentDiscoveryModule`)
applied cleanly via `dotnet ef database update --context AppDbContext`, producing 64 tables, the
PostGIS extension and generated `GeoLocation` column, both GiST indexes, the
`EX_UnitBookingRanges_NoOverlap` exclusion constraint, and the one application CHECK constraint
— all confirmed present afterward by direct SQL inspection (`pg_extension`, `pg_indexes`,
`pg_constraint`, `\d+`). This satisfies the Definition-of-Done item "migration reproducibility
verified" with actual evidence, not a build-succeeded inference. The container was destroyed
after verification; no data from it persists anywhere.

Destructive-migration classification is handled by a machine-enforced gate
(`ci/migration-risk-baseline.json` + `scripts/database/analyze-migrations.sh`, invoked from
`database-restore-drill.yml`'s migration-recovery job per `current-recovery-capability-assessment.md`),
which fails closed on any new `HIGH_RISK`/`DESTRUCTIVE` migration lacking a reviewed baseline
entry. See Finding F5 for the one gap found in it (documentation lag, not a control failure).

## 42. PostGIS Extension

`CREATE EXTENSION IF NOT EXISTS postgis;` is issued from within the
`AddPropertyGeoLocationPostGis` migration itself (not assumed to pre-exist in the Docker image)
— confirmed live: `postgis 3.5.2` (matching the `17-3.5` image tag) was created by the migration
run, not pre-provisioned. See Finding F1 for the version-pin inconsistency across this
project's own CI/gate/performance environments relative to what production and the (just-fixed)
recovery drill actually use.

## 43-44. Backup Readiness / Restore Test

Not re-derived — this project already has an extensive, independently-documented backup/restore
program: `docs/operations/database-backup-restore-runbook.md`, `rpo-rto.md`,
`database-migration-recovery-runbook.md`, `production-recovery-readiness-checklist.md`, and a
dated drill report (`recovery-drill-evidence-2026-07-28.md`). Summarizing what those documents
state (not independently re-run here — no production backup credentials are available to this
session):

- Encrypted (GPG AES-256), checksummed (SHA-256), S3-compatible durable storage with tiered
  retention (48 hourly / 30 daily / 12 weekly / 12 monthly) — **implemented**, per
  `current-recovery-capability-assessment.md`.
- A **local** end-to-end drill (`run-local-restore-drill.sh`) passed 2026-07-28 with measured
  RPO 1.72 min and RTO 4.57 min — both well inside the declared 60 min / 120 min objectives.
- The project's own `production-recovery-readiness-checklist.md` lists several items still
  unchecked as of this audit: the hourly schedule and missed-run monitor proven *in production*,
  private durable object storage *proven* (vs. implemented), a drill against real production
  storage, Render configured to gate on `Production Deployment Gate`, rollback exercised on
  Render, and protected GitHub environment approvals.

**NOT VERIFIED** (this session has no production credentials, no access to the configured S3
bucket, and no access to the Render dashboard): whether the hourly backup schedule is actually
firing in production today, whether the production object-storage bucket is actually configured
per the documented controls, and whether a restore from *real* production storage has ever been
attempted. This report does not upgrade the project's own "M-10, P0, production-blocking" status
on real-storage restore proof to PASS — it remains exactly what the project's own most recent
assessment says it is.

## 45-46. Database Security / Sensitive Data Inventory

- **TLS**: enforced in Production by `PostgresConnectionStringResolver`
  (`SslMode.Require`, rejects `Trust Server Certificate=true`, rejects placeholder passwords) —
  §2 above.
- **Least privilege**: not verifiable from this repository — the actual database role/grants
  used in production are provider-side configuration, not something committed here. **NOT
  VERIFIED.**
- **Sensitive fields inventory** (values never queried or displayed by this audit):

| Field | Stored as | Encrypted/Hashed | Notes |
|---|---|---|---|
| `Users.PasswordHash` | Hash | Hashed (Identity `PasswordHasher`) | Never logged |
| `Users.PhoneNumber` | Encrypted | `DataProtectionStringConverter` (AES via ASP.NET Data Protection) | [AppDbContext.cs:173-177](../PropertyApi.Infrastructure/Persistence/AppDbContext.cs#L173) |
| `Users.PhoneNumberLookupHash` | HMAC hash | Deterministic HMAC (for exact-match lookup without decrypting) | Unique index |
| `RefreshTokens.TokenHash` | Hash | SHA-256-class hash, not plaintext | §8 |
| `PhoneOtpChallenges.CodeHash` | Hash | Not the raw OTP | §9 |
| `UserAccounts.WhatsAppNumber` | Encrypted | `DataProtectionStringConverter` | [AppDbContext.cs:194-198](../PropertyApi.Infrastructure/Persistence/AppDbContext.cs#L194) |
| `UserAccounts.TaxNumber` | Encrypted | `DataProtectionStringConverter` | [AppDbContext.cs:200-204](../PropertyApi.Infrastructure/Persistence/AppDbContext.cs#L200) |
| `DataProtectionKeys` | Key material | This *is* the key store — its own backup/rollback risk is separately classified `Critical` in the migration-recovery runbook | |
| `ContactMessages.IpAddress`, `AuditLogs.IpAddress` | Plaintext | Not encrypted — operational/security metadata, retention not time-boxed at the schema level | Recorded, not flagged as a defect absent a stated retention requirement |

## 47. GDPR Technical Capability

See Finding F7. Present: encryption for the fields above, token/OTP expiration, soft-delete.
Absent: self-service account deletion, self-service data export. Not claimed as legal
compliance either way — technical capability only, per scope.

## 48-49. Orphan / Integrity Detection & Production Data Quality

No production-like dataset was available to this session (the only database touched was the
disposable, empty-then-migrated container created and destroyed for §40-41's verification, and
it was never seeded with data beyond schema). Per the instruction not to fabricate findings
against data that doesn't exist, this is recorded as **NOT VERIFIED** rather than PASS. The
orphan-detection queries below are provided so they can be run once real or representative data
exists (they follow directly from the FK graph in §24-25 and require no schema change):

```sql
-- Images without a Property
SELECT count(*) FROM "PropertyImages" i
  LEFT JOIN "Properties" p ON p."Id" = i."PropertyId" WHERE p."Id" IS NULL;

-- Reviews without a User or Property
SELECT count(*) FROM "PropertyReviews" r
  LEFT JOIN "UserAccounts" u ON u."Id" = r."ReviewerId"
  LEFT JOIN "Properties" p ON p."Id" = r."PropertyId"
  WHERE u."Id" IS NULL OR p."Id" IS NULL;

-- VisitRequests without a User or Property
SELECT count(*) FROM "VisitRequests" v
  LEFT JOIN "UserAccounts" u ON u."Id" = v."RequesterId"
  LEFT JOIN "Properties" p ON p."Id" = v."PropertyId"
  WHERE u."Id" IS NULL OR p."Id" IS NULL;

-- RefreshTokens without a User
SELECT count(*) FROM "RefreshTokens" t
  LEFT JOIN "Users" u ON u."Id" = t."UserId" WHERE u."Id" IS NULL;

-- Case-variant duplicate emails (the Finding F3 check, ready to run against real data)
SELECT "NormalizedEmail", count(*) FROM "Users"
  WHERE "NormalizedEmail" IS NOT NULL
  GROUP BY "NormalizedEmail" HAVING count(*) > 1;

-- Properties with a negative/zero price or area (the Finding F4 check)
SELECT "Id" FROM "Properties"
  WHERE ("ColdRent" IS NOT NULL AND "ColdRent" < 0)
     OR ("WarmRent" IS NOT NULL AND "WarmRent" < 0)
     OR ("PurchasePrice" IS NOT NULL AND "PurchasePrice" < 0)
     OR ("Area" IS NOT NULL AND "Area" <= 0)
     OR ("Rooms" IS NOT NULL AND "Rooms" < 0);
```

Because every FK reviewed in §24-25 is properly declared (no orphan-permitting relationship
found), and because no production data was reachable, actually running these against real data
remains a **NOT VERIFIED** item for whoever has production access next, not a known failure.

## 50-51. Performance Baseline / N+1 Detection

Not re-derived from a cold start — see §28-29. The project's own `pg_stat_statements`/`EXPLAIN
(ANALYZE, BUFFERS)`-based process and its CI-enforced query-count budgets are a real, running
control for exactly the queries this section asks about (listing, detail, search, location,
login lookup is covered by the `Users.Email`/`NormalizedPhoneNumber` unique indexes, refresh
token lookup by `IX_RefreshTokens_User_Active_ExpiresAt`). Re-running `EXPLAIN ANALYZE` against
a fresh dataset was not repeated in this session — doing so productively requires the
`PropertyApi.PerformanceDataGenerator` tool's representative dataset, which was out of scope for
a database-structure-focused Phase 2 pass on top of the schema verification already performed.

## 52. Database Connection Configuration — PASS

Not "manual configuration required" — genuinely implemented, confirmed by reading
`PostgresConnectionStringResolver.cs` in full: pooling on, `MaxPoolSize` 50 (20 for a detected
Supabase pooler host, correctly under pgbouncer transaction-mode limits), connect `Timeout` 30s,
`CommandTimeout` 60s, SSL required in Production, and explicit validation rejecting
placeholder passwords and `Trust Server Certificate=true`. The one related gap is Finding F6
(no `EnableRetryOnFailure`).

## 53. Supabase / Render Separation

The connection resolver explicitly branches on Supabase-pooler vs. direct hosts and validates
Supabase-specific username/port conventions in Production (§52) — environment-aware by design,
not by assumption. Per the project's own operational record, Staging and Production are
separate Render environment groupings with separate database instances (Staging's is Free-tier,
no backups, ~30-day expiry — explicitly not a production stand-in). Whether Render itself is
configured to gate deploys on the Production Deployment Gate check remains unchecked per the
project's own `production-recovery-readiness-checklist.md` — not independently verifiable from
this repository. **Presence of Render/Supabase is correctly not treated as production readiness
by the code itself** (the validation logic actively checks configuration correctness rather than
trusting the platform), which is the right posture; whether it's *wired up* end-to-end on the
Render side is **NOT VERIFIED**.

## 54. Database Naming Consistency

Table names are `PascalCase` and consistent; FK-derived index names follow EF Core's default
`IX_{Table}_{Column}` except where hand-named for clarity (`IX_Properties_Syrian_Search`,
`EX_UnitBookingRanges_NoOverlap`, etc.), which is a reasonable, intentional deviation, not
drift. No inconsistency was found significant enough to record as a finding; no renaming is
recommended.

## 55. EF Core Model vs Database Schema

Verified identical by construction: §40-41's live migration run applies the exact migrations
this repository ships, against a real PostgreSQL/PostGIS engine, and the resulting schema
(64 tables, PostGIS objects, the one CHECK constraint, the one EXCLUDE constraint) was directly
queried and matches what the entity configurations declare. No drift between the EF Core model
and the applied schema was found — because this session generated the applied schema from the
model itself, on a clean database, which is the strongest form of this check available without
a live production connection.

## 56-57. Database Tests / Integration Test Environment

Real PostgreSQL-backed integration tests exist and were read directly:
`RegisterAtomicityPostgresTests.cs`, `SocialLoginAtomicityPostgresTests.cs`,
`AuthDbAssertions.cs` (all in `tests/PropertyApi.Integration.Tests/Auth/`), built on
`PostgresAuthTestFactory`, which requires a real `TEST_POSTGRES_CONNECTION_STRING` (no
in-memory/SQLite fake — confirmed by reading the factory's constructor) — matching the
`postgis/postgis:16-3.4` service container declared in `.github/workflows/ci.yml`. These
specifically assert database-level behavior: zero-row rollback on mid-registration failure,
and that a concurrent same-email double-registration is resolved to exactly one row "by database
unique constraint" (the test's own name). This is genuine FK/unique-constraint/transaction
testing against a real engine, not a gap. Not independently re-run in this session (would
require the CI Postgres service, not the disposable container used for §40-41, whose data was
destroyed); recorded as **present and credible from source**, not re-executed here.

## Final Database Readiness Matrix

| Area | Status | Severity | Evidence | Action |
|---|---|---|---|---|
| Users | PASS | — | §5 | — |
| Roles | PASS | — | §11 | — |
| Refresh Tokens | PASS | — | §8 | — |
| Verification | PASS | — | §9 | — |
| Password Reset | PASS (Identity-standard) | — | §9 | — |
| Sessions | NOT IMPLEMENTED | INFO | §10 | None — not a gap |
| Properties | PASS w/ warning | MEDIUM | §12-19, Finding F4 | Add numeric CHECK constraints |
| Property Types | PASS | — | §15 | — |
| Status | PASS | — | §14 | — |
| Location/PostGIS | PASS (strength) | — | §20-21, live-verified | — |
| Price | PASS w/ warning | MEDIUM | §16, Finding F4 | Add `>= 0` CHECK constraints |
| Currency | PASS | — | §17 | — |
| Area | PASS w/ warning | MEDIUM | §18, Finding F4 | Add `> 0` CHECK constraint |
| Rooms | PASS w/ warning | MEDIUM | §19, Finding F4 | Add `>= 0` CHECK constraint |
| Images | PASS | — | §22-23 | — |
| Relationships | PASS (strength) | — | §24-25 | — |
| Constraints | WARNING | MEDIUM | Finding F4 | Add CHECK constraints (Rooms/Area/Price/Rating/Amount) |
| Indexes | PASS | — | §28-29 | — |
| Queries | PASS | — | §28-31, existing CI gate | — |
| Migrations | PASS (live-verified) w/ warnings | LOW/HIGH | §40-42, Findings F1, F5 | Fix Postgres version pins (F1); refresh baseline docs (F5) |
| Backup | PASS-implemented, NOT VERIFIED-in-production | — | §43-44 | Close remaining checklist items (project's own list) |
| Restore | PASS-local, NOT VERIFIED-production-storage | — | §44 | Run drill against real production storage |
| Security | PASS w/ gaps | MEDIUM | §45-46, Findings F2, F3 | Fix soft-delete boundary (F2); email uniqueness (F3) |
| Data Retention | GAP | MEDIUM | §47, Finding F7 | Decide/build account-deletion & export capability |
| Integration Tests | PASS | — | §56-57 | — |

---

## 18. Production Blockers

**None.** No finding in this audit rises to BLOCKER: no data loss in an applied migration, no
uncommitted schema (§55 verified identical), no plaintext passwords, no image blobs in
PostgreSQL, no unsafe money types, no corrupt referential integrity. The two HIGH findings
(F1, F2) are real and should be closed before this platform carries meaningful volumes of real
user data, but neither one, today, describes data already lost or corrupted — they describe
gaps that would let future data become inconsistent or let a gate miss a real defect.

## 19. Manual Actions Required

1. Confirm production's actual PostgreSQL major version directly from the hosting dashboard
   (not from this report or from code comments) and align `ci.yml`, `production-gate.yml`, and
   `performance/docker-compose.performance.yml` to it (Finding F1).
2. Product/engineering decision: should a banned/deleted `Users` row also hide the paired
   `UserAccounts` profile from public read paths? (Finding F2 — a business decision, not made
   here.)
3. Decide the email-uniqueness fix direction: unique index on `NormalizedEmail`, or
   `RequireUniqueEmail = true` plus a corrective migration (Finding F3).
4. Confirm with the team whether the current review this repository's own checklist marks
   unchecked (production-storage restore drill, Render gate wiring, protected environments) are
   scheduled, and by whom — this report does not have the access to close them.
5. Decide whether numeric CHECK constraints (Finding F4) are worth the migration now or are
   accepted as an application-layer-only invariant for the current stage.
6. Decide on GDPR self-service capability (Finding F7) scope and timeline.
7. Least-privilege database role/grants for the application user were not verifiable from this
   repository — confirm directly against the production database.

## 20. Recommended Next Step

Do not start Phase 3 (per this task's own instruction). Route Findings F1-F3 to the team for a
decision (they are architecture/business-rule decisions, not implementation bugs this audit is
authorized to silently fix), then re-run the live-migration verification in §40-41 after F1's
Postgres-version alignment lands, so the "PASS" here is re-confirmed against the corrected pin.
