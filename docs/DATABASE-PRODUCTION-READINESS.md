# HudhudNestApi — Database Production Readiness (Phase 2)

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

## Remediation update — 2026-09-07/08

Findings F1, F2, F3, F4, and F7 below were remediated in a follow-up session, in that order,
each with its own migration and live-Postgres tests (a disposable `postgis/postgis:17-3.5`
container, matching F1's own fix). Each finding's original section is left intact as the
historical record of what the audit found; a **RESOLUTION** block was added directly under each
one describing the fix, the exact evidence, and what remains open. Summary:

- **F1 (Postgres version drift)** — RESOLVED. `ci.yml`, `production-gate.yml`, both compose
  files, and the recovery-drill compose file all now pin `postgis/postgis:17-3.5` from one
  canonical file (`ci/postgres-version.env`), enforced by a new CI step
  (`scripts/database/check-postgres-version-consistency.sh`).
- **F2 (UserAccounts soft-delete boundary)** — RESOLVED, with a deliberate deviation from the
  literal "Global Query Filter" wording — see its RESOLUTION block for why a blanket EF Core
  filter was rejected as unsafe here, and what was done instead.
- **F3 (email case-insensitive uniqueness)** — RESOLVED via a real, guarded database constraint.
- **F4 (numeric CHECK constraints)** — RESOLVED for Area/Rooms/ColdRent/WarmRent/PurchasePrice,
  with a scope correction from the original plan — see its RESOLUTION block. Rating intentionally
  left unconstrained pending an owner decision, per instruction.
- **F7 (account deletion delay window + data export)** — RESOLVED on top of the account-deletion
  work this branch had already shipped separately (`DeleteUserCommandHandler`,
  `UserAccount.Anonymize()` — see `docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md`), which this
  update does not replace.

---

## 1. Executive Summary

**PASS WITH WARNINGS** (updated 2026-09-07/08 — was also PASS WITH WARNINGS at the original
2026-09-04 audit; see the remediation note below for what changed).

The schema itself — entity design, PostGIS, image storage, money types, concurrency tokens,
delete behavior, migration history, and the backup/restore engineering — is unusually mature
for a project at this stage; this is not a first-pass database. Migration reproducibility was
verified live this session. No BLOCKER was found in the schema or migrations.

**Update**: the three findings originally listed here (F1 HIGH, F2 HIGH, F3 MEDIUM), plus F4 and
F7 from further down this document, have all been remediated in a follow-up session — see
"Remediation update — 2026-09-07/08" directly below and each finding's own RESOLUTION block for
full evidence. Original text preserved for the historical record:

- ~~**HIGH** — the CI build and the Production Deployment Gate test against PostgreSQL
  16/PostGIS 3.4, while production runs PostgreSQL 17 (§40, §42, Finding F1).~~ RESOLVED.
- ~~**HIGH** — no database-level protection makes soft-deleted users disappear from the
  business-profile table (`UserAccounts`) that `Property.Owner`, reviews, and favorites
  actually reference — only the paired auth table (`Users`) is filtered (§25, §32, Finding F2).~~
  RESOLVED (as a scoped fix, not the literal global-filter mechanism originally suggested — see
  F2's RESOLUTION block for a hazard that mechanism would have caused).
- ~~**MEDIUM** — email case-insensitive uniqueness is not enforced by a database constraint;
  it currently holds only as a side effect of one implementation detail (§6, Finding F3).~~
  RESOLVED.

This document's status stays **PASS WITH WARNINGS** rather than a clean PASS because two
categories of warning are unchanged from the original audit and were explicitly out of this
remediation's scope: (1) the backup/restore program's production-storage drill and Render
deploy-gate wiring, which the project's own checklist already lists as open and which this
report does not have credentials to close (see §13); and (2) `PropertyReview.Rating`'s numeric
range, deliberately left unconstrained at the database level pending an explicit owner decision
on what range to allow, per instruction.

Backup/restore engineering is extensive and a local drill passed with RPO 1.72 min / RTO
4.57 min, but the project's own checklist still lists the production-storage drill, protected
GitHub environments, and the Render deploy-gate wiring as open — this report does not
override that; see §13.

## 2. Current Database Architecture

- **Engine**: PostgreSQL, PostGIS extension. Provider: `Npgsql.EntityFrameworkCore.PostgreSQL`
  8.0.11 on EF Core 8 / .NET 8 (`Directory.Packages.props`, `HudhudNestApi.Infrastructure.csproj`).
- **Context**: one primary `AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`
  ([AppDbContext.cs](../HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs)) plus a second,
  separate `DataProtectionKeyDbContext` for ASP.NET Data Protection keys.
- **Design-time**: `AppDbContextFactory` ([AppDbContextFactory.cs](../HudhudNestApi.Infrastructure/Persistence/AppDbContextFactory.cs)).
- **Migration execution**: a dedicated console tool, `tools/HudhudNestApi.Migrator`, applies
  migrations and runs `DatabaseSeeder`. Startup-time auto-migration is deliberately not used in
  the API process (confirmed both in code — no `Database.Migrate()` call in `HudhudNestApi`'s
  `Program.cs` — and in the migration-recovery runbook).
  This is also memory of a live Render Staging deploy: `Database__ApplyMigrationsOnStartup`
  is a vestigial config key nothing reads there either.
- **Connection resolution**: `PostgresConnectionStringResolver` ([PostgresConnectionStringResolver.cs](../HudhudNestApi.Infrastructure/PostgresConnectionStringResolver.cs))
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
`DbSet<>` list in [AppDbContext.cs:56-114](../HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs#L56-L114):

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
  [ApplicationUser.cs](../HudhudNestApi.Infrastructure/Identity/Entities/ApplicationUser.cs)) —
  authentication and account-security state: password hash, security stamp, lockout,
  phone-verification lifecycle, ban state, soft-delete.
- **`UserAccounts`** (`UserAccount`,
  [UserAccount.cs](../HudhudNestApi.Domain/Users/Entities/UserAccount.cs)) — the business profile
  that `Property.Owner`, reviews, favorites, and agency membership actually reference. Shared
  primary key with `Users` (`UserAccounts.Id = Users.Id`), FK `Restrict` (not cascade) so the
  pairing cannot silently break — [UserAccountConfiguration.cs:26-29](../HudhudNestApi.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs#L26-L29).

| Field | Present | Notes |
|---|---|---|
| Primary Key | ✅ | `Guid`, shared between `Users` and `UserAccounts` |
| Email | ✅ | `Users.Email`, `varchar(320)`, required |
| Normalized Email | ✅ | `Users.NormalizedEmail`, but **not** the unique column — see Finding F3 |
| Username | ✅ | Always set equal to Email (or phone, for phone-only accounts) — [IdentityAccountCreator.cs:34](../HudhudNestApi.Infrastructure/Identity/Services/IdentityAccountCreator.cs#L34) |
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
  [PersistenceInfrastructureRegistration.cs:52](../HudhudNestApi.Infrastructure/Persistence/PersistenceInfrastructureRegistration.cs#L52).
  This disables ASP.NET Identity's own case-insensitive email-uniqueness check.
- The only unique index on email is `IX_Users_Email UNIQUE btree ("Email")` — on the **raw,
  case-sensitive** column — [ApplicationUserConfiguration.cs:15-16](../HudhudNestApi.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs#L15-L16),
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
  ([RegisterAtomicityPostgresTests.cs:163-238](../tests/HudhudNestApi.Integration.Tests/Auth/RegisterAtomicityPostgresTests.cs#L163)) proves same-case concurrent duplicates are blocked, but does not exercise the case-variant scenario.
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

### RESOLUTION (2026-09-07/08)

Implemented as a functional unique index rather than either recommended option above, after
finding both had a real problem: a plain index on `NormalizedEmail` still wouldn't match this
codebase's actual stored casing convention on its own, and `RequireUniqueEmail` was confirmed to
be a no-op regardless of its value — every account here is created directly through
`IdentityAccountCreator.cs`, bypassing `UserManager.CreateAsync` (the only code path that flag
affects) entirely.

[`AddEmailLowerCaseUniqueIndex`](../HudhudNestApi.Infrastructure/Migrations/20260907201638_AddEmailLowerCaseUniqueIndex.cs)
adds `IX_Users_Email_Lower`, a genuine `CREATE UNIQUE INDEX ... ON "Users" (lower("Email"))` — a
real constraint on `Email` itself, not on `UserName`. Guarded: the migration first runs a
`DO $$ ... RAISE EXCEPTION ...` pre-check for existing case-variant duplicate groups and aborts
(no row touched) if any are found, rather than silently merging or deleting an account — the
count is logged, not the values, so a failure doesn't leak PII into migration logs. The
pre-existing `IX_Users_Email` is left in place (an unrelated, separately-reviewable decision).
`PersistenceInfrastructureRegistration.cs:52`'s `RequireUniqueEmail = false` was **left
unchanged** for the reason above — flipping it would not have changed any actual behavior in
this codebase.

Live-verified against a real Postgres 17 database: `test@example.com` / `TEST@example.com` /
`Test@Example.Com` are rejected as the same account **even when inserted with independently-
chosen, non-matching `UserName` values** — the exact scenario this finding proved was
previously unprotected
([EmailUniquenessPostgresTests.cs](../tests/HudhudNestApi.Integration.Tests/Auth/EmailUniquenessPostgresTests.cs)).
A second, dedicated test class
([EmailUniquenessMigrationGuardTests.cs](../tests/HudhudNestApi.Integration.Tests/Auth/EmailUniquenessMigrationGuardTests.cs))
proves the migration's guard itself: seeded a real case-variant duplicate directly via SQL on a
disposable database migrated to one step short of this one, then confirmed the migration raises,
leaves nothing applied, and creates no index — and, separately, that it applies and creates the
index cleanly when no duplicate exists. All 7 tests across both files pass.

`scripts/database/audit-f4-numeric-constraints.sql`'s Finding F4 sibling note applies here too:
**production's actual data was not checked for existing case-variant duplicates in this
session** (no credentials) — the migration's own guard is the safety net for that environment,
and per its design it will refuse to apply rather than silently corrupt data if one exists.

## 7. Password Security

`Users.PasswordHash` is Identity's standard hash (PBKDF2 via `PasswordHasher<TUser>` unless
overridden — no custom hasher was found in `Auth/Security`). No plaintext or reversibly-encrypted
password field exists anywhere in the schema (confirmed: no `Password` column, only
`PasswordHash`, across every migration and configuration reviewed). No hash values are
reproduced anywhere in this report or its evidence queries.

## 8. Refresh Tokens

`RefreshTokens` ([RefreshToken.cs](../HudhudNestApi.Infrastructure/Identity/Entities/RefreshToken.cs),
[RefreshTokenConfiguration.cs](../HudhudNestApi.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs)):

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

- `PhoneOtpChallenges` ([PhoneOtpChallenge.cs](../HudhudNestApi.Domain/Auth/Entities/PhoneOtpChallenge.cs)) —
  covers `PhoneRegistration`, `PhonePasswordReset`, `PhoneReverification`, `PhoneNumberChange`
  (`OtpPurpose` enum, [OtpPurpose.cs](../HudhudNestApi.Domain/Enums/OtpPurpose.cs)). Has
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
[DatabaseSeeder.cs:36-49](../HudhudNestApi.Infrastructure/Persistence/Seeds/DatabaseSeeder.cs#L36))
so concurrent app-instance startups cannot race the seed. Standard Identity `UserRoles` table
carries the FK/uniqueness (composite PK `(UserId, RoleId)`).

## 12–19. Property Domain Model

Reviewed directly from [Property.cs](../HudhudNestApi.Domain/Listings/Entities/Property.cs) and
[PropertyConfiguration.cs](../HudhudNestApi.Infrastructure/Persistence/Configurations/PropertyConfiguration.cs).

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
([20260611065011_AddPropertyGeoLocationPostGis.cs](../HudhudNestApi.Infrastructure/Migrations/20260611065011_AddPropertyGeoLocationPostGis.cs)) —
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

`PropertyImages` ([PropertyImage.cs](../HudhudNestApi.Domain/Listings/Entities/PropertyImage.cs),
[PropertyImageConfiguration.cs](../HudhudNestApi.Infrastructure/Persistence/Configurations/PropertyImageConfiguration.cs)):
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
([PlanConfiguration.cs:50](../HudhudNestApi.Infrastructure/Persistence/Configurations/PlanConfiguration.cs#L50)).
(Every other CHECK returned by that query belongs to PostGIS/TIGER-geocoder system tables, not
application tables.)

Not database-enforced anywhere else: `Property.Rooms >= 0`, `Property.Area > 0`,
`Property.ColdRent/WarmRent/PurchasePrice/AdditionalCosts/Deposit >= 0`,
`PropertyReview.Rating BETWEEN 1 AND 5` (enforced only in the domain factory —
[PropertyReview.cs:29](../HudhudNestApi.Domain/Reviews/Entities/PropertyReview.cs#L29) — and a
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

### RESOLUTION (2026-09-07/08)

**Scope correction found during implementation**: the original recommendation above assumed
`Area` could safely become `NOT NULL`. It cannot —
`CreatePropertyCommandValidator.cs` only requires `Area` for `Land`-category listings; every
other type leaves it optional today, by design (in-code comment: *"every other field...is
already optional and needs no new rule"*). Confirmed with the project owner: `Area` is treated
the same as `Rooms`/the price columns — nullable, with the constraint only ruling out a
zero/negative *value when present*, not requiring one.

Applied via [`AddPropertyNumericConstraints`](../HudhudNestApi.Infrastructure/Migrations/20260907210822_AddPropertyNumericConstraints.cs)
(scaffolded from a matching `HasCheckConstraint` declaration in
[PropertyConfiguration.cs](../HudhudNestApi.Infrastructure/Persistence/Configurations/PropertyConfiguration.cs)
so the EF model and schema cannot drift apart): five constraints, each shaped
`col IS NULL OR col > 0` —
`CK_Properties_Area_PositiveOrNull`, `CK_Properties_Rooms_PositiveOrNull`,
`CK_Properties_ColdRent_PositiveOrNull`, `CK_Properties_WarmRent_PositiveOrNull`,
`CK_Properties_PurchasePrice_PositiveOrNull`.

Live-verified this session: applied cleanly to a fresh `postgis/postgis:17-3.5` database
(confirmed present via `pg_constraint`); 21 new integration tests
([PropertyNumericConstraintsPostgresTests.cs](../tests/HudhudNestApi.Integration.Tests/Listings/PropertyNumericConstraintsPostgresTests.cs))
prove each column rejects `0`/negative via direct `SaveChangesAsync` (bypassing domain
validation, the same technique that originally proved the gap) while still accepting `NULL` and
a positive value; all pass.

`scripts/database/audit-f4-numeric-constraints.sql` (new, read-only) reports how many existing
rows would violate each constraint — **run this against Staging/Production before deploying this
migration there**; it was not run against production data in this session (no credentials).
Because Postgres refuses to add a CHECK constraint any existing row already violates, the
migration itself fails atomically (no row modified) if that audit would have shown a nonzero
count — this is a safety property, not a defect, per the instruction not to silently correct or
delete existing data.

**Rating is still intentionally unconstrained** — no CHECK was added, per the explicit
instruction not to finalize its range without an owner decision. `PropertyReview.Create`'s
domain-level `1..5` enforcement is unchanged and remains the only guard.

## Finding F2 (HIGH) — Soft-delete boundary does not cover the table most relationships reference

- `ApplicationUser` (`Users`) has `IsDeleted`/`DeletedAt` and a global query filter:
  `builder.Entity<ApplicationUser>().HasQueryFilter(e => !e.IsDeleted)` —
  [AppDbContext.cs:144](../HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs#L144).
- `UserAccount` (`UserAccounts`) — the table `Property.Owner`, `Property.Agent`,
  `PropertyReview.Reviewer`, `Favorite.User`, `VisitRequest.Requester`, `UserRating`, and agency
  membership all actually FK to
  ([PropertyConfiguration.cs:149,230](../HudhudNestApi.Infrastructure/Persistence/Configurations/PropertyConfiguration.cs#L149)) —
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

### RESOLUTION (2026-09-07/08)

`UserAccounts.IsDeleted` added
([`AddUserAccountsIsDeleted`](../HudhudNestApi.Infrastructure/Migrations/20260907195225_AddUserAccountsIsDeleted.cs),
additive, default `false`), set by `UserAccount.Anonymize()` — the same method
`DeleteUserCommandHandler` already called, so no new call site was needed in the deletion flow.

**A real hazard was found and is why this is *not* a blanket `HasQueryFilter`, despite the
original recommendation's literal wording**: `Property.OwnerId`, `PropertyReview.ReviewerId`,
`VisitRequest.RequesterId`, and `Transaction.PayerId`/`ReceiverId` are all non-nullable
("required") FKs into `UserAccounts`, and `PropertyRepository.cs` already does
`.Include(p => p.Owner)` in its main listing/detail queries. EF Core's own documented behavior
turns a global filter on the target of a required relationship into an `INNER JOIN` — confirmed
independently by EF Core itself during this work: running `dotnet ef migrations add` emitted
*"Entity 'ShortStayListing' has a global query filter defined and is the required end of a
relationship..."* for three **pre-existing, unrelated** filters already in this schema
(`ShortStayListing`↔`RoomType`, `Amenity`↔`ShortStayListingAmenity`,
`ShortStayListing`↔`ShortStayListingPhoto`) — independent, live proof that this exact hazard is
real in this codebase, not a hypothetical. A blanket filter on `UserAccounts` would have silently
dropped a deleted user's *properties and reviews from every listing*, reversing the
already-shipped, tested retention policy in `docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md`
("properties/reviews are retained, owner shows as Deleted User").

Resolved (confirmed with the project owner) as a **scoped filter**: `IsDeleted` is applied
explicitly only where a live gap was actually found —
[`AgencyRepository.GetMembersAsync`/`CountMembersAsync`](../HudhudNestApi.Infrastructure/Repositories/AgencyRepository.cs)
now add `&& !u.IsDeleted`, since neither previously excluded a deleted former member from an
agency's member list/count. `GetUserByIdQueryHandler`/`GetUserProfileQueryHandler` needed no
change — both already return null/404 for a deleted user by checking the paired
`ApplicationUser.IsDeleted` first (verified by reading both handlers end to end).
`AdminUserQueryRepository` also needed no change — it deliberately keeps showing deleted
accounts to admins (`AccountStatus = "Disabled"`), which is correct, not a gap. A repo-wide
`grep -rn "\.UserAccounts\b"` sweep across `HudhudNestApi.Infrastructure`/`HudhudNestApi.Application`
found no other direct query site.

Live-verified: two new integration tests
([AgencyMembershipDeletedUserTests.cs](../tests/HudhudNestApi.Integration.Tests/Users/AgencyMembershipDeletedUserTests.cs))
against a real Postgres database — a deleted member is excluded from both the list and the
count; active members are unaffected — both pass. The existing
`DeleteUserCommandHandlerTests` happy-path test was extended with `Assert.True(account.IsDeleted)`
(still passes, 12/12).

**The banned-account and email/phone-slot-reuse questions from the original finding are
unchanged and still open** — no code path in this repository currently sets `Users.IsBanned`
(confirmed by `grep`: only read, never assigned, outside migrations/tests), so that part of the
finding is dormant rather than resolved; if a ban feature is ever built, it should reuse this
same `UserAccounts.IsDeleted` mechanism rather than inventing a parallel one.

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

### RESOLUTION (2026-09-07)

Done exactly as recommended, plus a drift-prevention control. `ci.yml`, `production-gate.yml`,
`ci/docker-compose.production-gate.yml`, and `performance/docker-compose.performance.yml` all
now pin `postgis/postgis:17-3.5`. A new canonical file,
[`ci/postgres-version.env`](../ci/postgres-version.env), is the single source of truth (mirrors
the existing `DOTNET_VERSION` env-var-pin convention already in `ci.yml`, rather than inventing a
new mechanism); a new dependency-free bash script,
[`scripts/database/check-postgres-version-consistency.sh`](../scripts/database/check-postgres-version-consistency.sh)
(no `python3`, unlike `analyze-migrations.sh` — confirmed `python3` is still not installed on
this machine), scans every workflow/compose file for `postgis/postgis:` references and fails if
any differ from the canonical value. Wired into `ci.yml` as the very first step. Verified this
session: passes against the corrected repository (`6/6` references match); deliberately
reintroducing a stale `16-3.4` reference and re-running the script produces a clear failure
naming the exact file/line, then passes again once reverted.

The full existing test suite (`HudhudNestApi.Application.Tests`, `HudhudNestApi.Auth.Tests`,
`HudhudNestApi.Architecture.Tests`, and the Postgres-backed parts of
`HudhudNestApi.Integration.Tests`) was run against a disposable `postgis/postgis:17-3.5` container
as part of this and the following findings' work — no PostGIS 3.4→3.5 or Postgres 16→17
behavior difference surfaced.

**Still NOT VERIFIED** (unchanged — no credentials in this session): production's actual
Postgres major version was not re-confirmed against a live hosting dashboard; this fix trusts
the prior session's commit-message evidence (`fcd50b5`). Whether Render's managed Postgres
offering (if that is what production actually runs, rather than a container from this image) is
even on major version 17 is a provider-configuration fact this repository's files cannot prove
either way.

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

### Update (2026-09-07/08)

`ci/migration-risk-baseline.json` now also carries entries for the four migrations added while
resolving F1–F4/F7 (`AddUserAccountsIsDeleted` — ADDITIVE; `AddEmailLowerCaseUniqueIndex` —
HIGH_RISK, since it uses raw `migrationBuilder.Sql(...)` for its guard, and genuinely does
require a baseline entry for the gate to pass, unlike the two migrations this finding originally
covered; `AddPropertyNumericConstraints` — ADDITIVE; `AddUserAccountDeletionSchedule` —
ADDITIVE). The original gap (`AddUserAccountSubscriptionLifecycle`,
`AddInvestmentDiscoveryModule`, and — found newly present on this branch since the original
audit — `AddConsentRecords`, `AddMarketingLeadsAndOffers`, `AddMarketingSurveysAndEvents`, from
other, parallel sessions working on this same repository) is **not** closed by this update; it
was out of scope for this remediation pass. The JSON file remains the authoritative source; the
markdown table in `database-migration-recovery-runbook.md` was not refreshed either and is now
further behind still.

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

Searched `HudhudNestApi.Application` for account-deletion or data-export capability
(`DeleteAccount`, `AccountDeletion`, `ExportData`, `DataExport`, `GDPR` — zero matches). What
exists: OTP/refresh-token expiration, admin-driven soft-delete (`IsDeleted` + `MarkAsDeleted`
domain methods on several entities), and Data-Protection-encrypted storage for phone/WhatsApp/
tax-number fields (§14). What does not appear to exist: a self-service "delete my account" or
"export my data" endpoint. This is recorded as a **technical-capability gap**, not a legal
compliance claim, per the instruction in scope for this audit.

### RESOLUTION (2026-09-07/08)

**Superseded in part before this remediation started**: a separate session on this same branch
had already shipped real, tested account deletion (`DeleteUserCommandHandler`,
`UserAccount.Anonymize()`, re-authentication, credential/token cleanup, Cloudinary cleanup — see
`docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md` for that work's own full readiness assessment).
That closed the "no deletion path at all" gap this finding originally described, but the newly
approved policy asked for two things that implementation didn't have: a cancellable delay window
before deletion executes, and a data-export endpoint. Both were added on top of the existing
work, without modifying its anonymization logic:

- **Delay window**: `UserAccount.RequestDeletion`/`CancelDeletionRequest`/
  `HasPendingDeletionRequest` (new domain methods) plus two new nullable columns
  (`DeletionRequestedAt`/`DeletionScheduledFor`, migration
  [`AddUserAccountDeletionSchedule`](../HudhudNestApi.Infrastructure/Migrations/20260907211928_AddUserAccountDeletionSchedule.cs)).
  `DELETE /api/Users/me` now calls a new `RequestDeleteUserCommandHandler` (re-authenticates,
  then schedules) instead of anonymizing synchronously — **this changes the endpoint's response
  from `204 No Content` to `202 Accepted` with `{ scheduledFor }`, a breaking contract change for
  the existing Angular client**, which is outside this session's scope (a separate repository)
  and is listed under Manual Actions. `DeleteUserCommandHandler.Handle` (the original,
  fully-tested immediate path) was extended, not replaced: its transaction body was extracted
  into a shared `AnonymizeAndFinalizeAsync` method, now also reachable via a new
  `ExecuteScheduledDeletionAsync(userId, ct)` entry point that skips re-authentication — the
  original 12 `DeleteUserCommandHandlerTests` pass unchanged (same constructor, same `Handle`
  behavior). A new `AccountDeletionSweepHostedService` (mirrors `AuditLogRetentionHostedService`'s
  shape: `IServiceScopeFactory` + `PeriodicTimer` + Postgres advisory lock) executes matured
  requests hourly. `POST /api/Users/me/deletion/cancel` (new) clears a pending request, no
  re-authentication required (an authenticated session is already at least as strong a bar as
  the one that started the request). The delay is configurable
  (`AccountDeletion:DelayDays`, default 30) via a new `IAccountDeletionSettings` abstraction
  (mirrors `IJwtTokenSettings`'s Application/Infrastructure boundary pattern) rather than a
  hardcoded value.

  **Assumption applied, not decided unilaterally**: every self-service deletion now goes through
  the delay window with no immediate-deletion bypass exposed by the API. If the business wants an
  admin-forced immediate path later, `DeleteUserCommandHandler.Handle` still exists and is still
  fully tested — it is simply no longer wired to a controller action.

  Live-verified against a real Postgres database: a due request executes (anonymized,
  soft-deleted) and a not-yet-due request is left completely untouched by the same sweep run; a
  cancelled request is never picked up even though its (cleared) schedule was once in the past
  ([AccountDeletionSweepTests.cs](../tests/HudhudNestApi.Integration.Tests/Users/AccountDeletionSweepTests.cs),
  2/2 pass, exercised through the real DI container/`Program` host). Seven new unit tests cover
  the domain methods
  ([UserAccountDeletionScheduleTests.cs](../tests/HudhudNestApi.Application.Tests/Users/UserAccountDeletionScheduleTests.cs))
  and the two new handlers' re-authentication/scheduling/cancellation branches (19 tests total
  across `RequestDeleteUserCommandHandlerTests.cs`/`CancelAccountDeletionCommandHandlerTests.cs`).

- **JSON export**: new `GET /api/Users/me/export` (`[Authorize]`, `userId` always from the
  authenticated principal, same IDOR-safe-by-construction pattern as every other `/me` endpoint),
  backed by a new `IAccountDataExportRepository`/`ExportMyDataQueryHandler` that assembles one
  `AccountDataExportDto` field by field — profile, owned properties, reviews written, favorites,
  visit requests made, consent records, plan tier, ratings given/received. Explicitly excludes
  (by construction, not by generic-serializer omission): `PasswordHash`, `SecurityStamp`,
  `RefreshTokens`, `PhoneNumberLookupHash`, `DataProtectionKeys`, any other user's data.
  Rate-limited (`data-export`, 5/24h, registered in both the in-memory and Redis-backed
  limiters). Audit-logged (`AuditActions.DataExportRequested` — who/when, not the payload
  content, matching this codebase's existing PII-logging discipline).

  **Delivery choice, flagged rather than silently decided**: the JSON is returned directly as
  the HTTP response body (`Content-Disposition: attachment`), not written to a stored file
  behind a short-lived download link — this repository has no existing arbitrary-file-storage
  mechanism (Cloudinary is image-specific), and building one solely for this endpoint was judged
  out of proportion. The approved policy's "short-lived link" requirement is conditional on
  choosing link-based delivery, which this implementation does not use.

  Live-verified with a real two-user HTTP test against a real Postgres database
  ([AccountDataExportTests.cs](../tests/HudhudNestApi.Integration.Tests/Users/AccountDataExportTests.cs)):
  the export contains the caller's own property, and the raw JSON response body contains neither
  the string `"PasswordHash"`/`"SecurityStamp"` nor a second seeded user's email or property
  title — the actual IDOR proof, not just a code-inspection argument. Both tests pass.

- **Review/message retention with anonymized attribution** — already correctly decided by the
  pre-existing `Anonymize()` work (reviews/messages are retained, not deleted; the author's name
  renders as "Deleted User" via the shared, now-`IsDeleted`-flagged `UserAccounts` row). No
  further decision was needed here.

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
[PropertyRepository.cs:81](../HudhudNestApi.Infrastructure/Repositories/PropertyRepository.cs#L81)
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
([AppDbContext.cs:238-253](../HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs#L238)) — so
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
[PropertyReviewConfiguration.cs:29-30](../HudhudNestApi.Infrastructure/Persistence/Configurations/PropertyReviewConfiguration.cs#L29)),
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
| `Users.PhoneNumber` | Encrypted | `DataProtectionStringConverter` (AES via ASP.NET Data Protection) | [AppDbContext.cs:173-177](../HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs#L173) |
| `Users.PhoneNumberLookupHash` | HMAC hash | Deterministic HMAC (for exact-match lookup without decrypting) | Unique index |
| `RefreshTokens.TokenHash` | Hash | SHA-256-class hash, not plaintext | §8 |
| `PhoneOtpChallenges.CodeHash` | Hash | Not the raw OTP | §9 |
| `UserAccounts.WhatsAppNumber` | Encrypted | `DataProtectionStringConverter` | [AppDbContext.cs:194-198](../HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs#L194) |
| `UserAccounts.TaxNumber` | Encrypted | `DataProtectionStringConverter` | [AppDbContext.cs:200-204](../HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs#L200) |
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
`HudhudNestApi.PerformanceDataGenerator` tool's representative dataset, which was out of scope for
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
`AuthDbAssertions.cs` (all in `tests/HudhudNestApi.Integration.Tests/Auth/`), built on
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

*Updated 2026-09-07/08 to reflect the F1–F4/F7 remediation. Original per-area statuses are
preserved in each numbered section above; this table reflects the current, post-fix state.*

| Area | Status | Severity | Evidence | Action |
|---|---|---|---|---|
| Users | PASS | — | §5 | — |
| Roles | PASS | — | §11 | — |
| Refresh Tokens | PASS | — | §8 | — |
| Verification | PASS | — | §9 | — |
| Password Reset | PASS (Identity-standard) | — | §9 | — |
| Sessions | NOT IMPLEMENTED | INFO | §10 | None — not a gap |
| Properties | PASS | — | §12-19, Finding F4 RESOLVED | — |
| Property Types | PASS | — | §15 | — |
| Status | PASS | — | §14 | — |
| Location/PostGIS | PASS (strength) | — | §20-21, live-verified | — |
| Price | PASS | — | §16, Finding F4 RESOLVED — `CK_Properties_{ColdRent,WarmRent,PurchasePrice}_PositiveOrNull` | — |
| Currency | PASS | — | §17 | — |
| Area | PASS | — | §18, Finding F4 RESOLVED — `CK_Properties_Area_PositiveOrNull` (nullable, per corrected scope) | — |
| Rooms | PASS | — | §19, Finding F4 RESOLVED — `CK_Properties_Rooms_PositiveOrNull` | — |
| Images | PASS | — | §22-23 | — |
| Relationships | PASS (strength) | — | §24-25 | — |
| Constraints | PASS w/ note | LOW | Finding F4 RESOLVED for Area/Rooms/Price; Rating deliberately left unconstrained pending owner decision | Decide Rating's numeric range |
| Indexes | PASS | — | §28-29 | — |
| Queries | PASS | — | §28-31, existing CI gate | — |
| Migrations | PASS (live-verified) w/ note | LOW | §40-42, Finding F1 RESOLVED; Finding F5 partially updated | Refresh baseline/markdown docs for migrations from other parallel sessions |
| Backup | PASS-implemented, NOT VERIFIED-in-production | — | §43-44 | Close remaining checklist items (project's own list) — unchanged, out of this remediation's scope |
| Restore | PASS-local, NOT VERIFIED-production-storage | — | §44 | Run drill against real production storage — unchanged |
| Security | PASS w/ note | LOW | §45-46, Findings F2 RESOLVED (scoped fix), F3 RESOLVED | Confirm banned-account visibility intent (dormant, no live code path today) |
| Data Retention | PASS w/ note | LOW | §47, Finding F7 RESOLVED (delay window + export, built on the branch's pre-existing anonymization work) | Update Angular client for the new 202/cancel contract; confirm 30-day default and "no bypass" assumption |
| Integration Tests | PASS (strength) | — | §56-57; this remediation added 8 new real-Postgres test files | — |

---

## 18. Production Blockers

**None**, unchanged from the original audit, and no new one was introduced by this remediation:
every new migration was live-verified end to end (empty database → all 42 migrations, including
the 4 new ones, on a fresh `postgis/postgis:17-3.5` container) and every existing test suite
(`Application.Tests` 604, `Auth.Tests` 254, `Architecture.Tests` 109, plus the Postgres-backed
parts of `Integration.Tests` touched by this work) passes with zero regressions.

## 19. Manual Actions Required

*Updated 2026-09-07/08 — items resolved by this remediation are marked; the rest are unchanged.*

1. ~~Confirm production's actual PostgreSQL major version...~~ — **RESOLVED** (F1: all
   CI/gate/performance references now pin `17-3.5`, with an automated drift check). Still open:
   independently re-confirm the actual production/Staging versions against the hosting
   dashboard — this session had no such credentials.
2. Product/engineering decision: should a banned `Users` row (`IsBanned = true`) also hide the
   paired `UserAccounts` profile? **Still open** — no code path currently sets `IsBanned`, so
   this is dormant rather than urgent; if a ban feature is built, reuse the new
   `UserAccounts.IsDeleted` mechanism (F2) rather than inventing a parallel one.
3. ~~Decide the email-uniqueness fix direction...~~ — **RESOLVED** (F3: functional unique index
   on `lower(Email)`, guarded migration).
4. Confirm with the team whether the items this repository's own recovery checklist marks
   unchecked (production-storage restore drill, Render gate wiring, protected environments) are
   scheduled, and by whom — **unchanged, out of this remediation's scope**.
5. ~~Decide whether numeric CHECK constraints are worth the migration now...~~ — **RESOLVED**
   for Area/Rooms/ColdRent/WarmRent/PurchasePrice (F4). **Still open**: Rating's numeric range —
   deliberately not decided here, per explicit instruction.
6. ~~Decide on GDPR self-service capability scope and timeline...~~ — **RESOLVED** (F7: delay
   window + cancellation + JSON export, built on the branch's pre-existing account-deletion
   work). **Still open**: update the Angular client for `DELETE /api/Users/me`'s new
   `202 Accepted { scheduledFor }` response (was `204 No Content`) and wire up the new
   cancel/export endpoints — separate repository, out of this session's reach; confirm the
   30-day default delay and the "no immediate-deletion bypass" assumption are acceptable.
7. Least-privilege database role/grants for the application user were not verifiable from this
   repository — confirm directly against the production database. **Unchanged.**
8. *(New)* Run `scripts/database/audit-f4-numeric-constraints.sql` against Staging/Production
   before the `AddPropertyNumericConstraints` migration reaches either environment.
9. *(New)* Run the case-variant-duplicate-email check (the `SELECT ... GROUP BY lower("Email")
   HAVING count(*) > 1` query inside the `AddEmailLowerCaseUniqueIndex` migration) against
   Staging/Production before that migration reaches either environment — the migration's own
   guard will refuse to apply if it finds one, but running it ahead of time avoids a failed
   deploy discovering that live.
10. *(New)* `ci/migration-risk-baseline.json` and `database-migration-recovery-runbook.md`'s
    markdown table are now behind by several migrations from other, parallel sessions
    (`AddConsentRecords`, `AddMarketingLeadsAndOffers`, `AddMarketingSurveysAndEvents`) that this
    remediation did not classify — out of scope here, but worth a dedicated pass.

## 20. Recommended Next Step

Do not start Phase 3 (per this task's own instruction). Route Findings F1-F3 to the team for a
decision (they are architecture/business-rule decisions, not implementation bugs this audit is
authorized to silently fix), then re-run the live-migration verification in §40-41 after F1's
Postgres-version alignment lands, so the "PASS" here is re-confirmed against the corrected pin.
