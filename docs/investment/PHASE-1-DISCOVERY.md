# Phase 1 — Investment Discovery — Repository Discovery Report

Date: 2026-09-04
Scope: Read-only analysis performed before any Investment module code was written, per the
Phase 1 spec's rule that implementation must not start before this document exists.

## 1. Current Architecture (relevant facts)

- **Solution layout**: `PropertyApi.Domain` → `PropertyApi.Application` → `PropertyApi.Infrastructure` → `PropertyApi` (Web API).
  Classic Clean Architecture, one solution, one deployable — no microservices, confirmed by
  `PropertyApi.sln` and `Directory.Build.props`. Phase 1 stays inside this same shape.
- **Modular-monolith convention**: each bounded context is a folder repeated across all four
  projects with the *same name* (`Listings`, `Bookings`, `Favorites`, `Services`, `ShortStay`,
  `Agencies`, …): `Domain/<Module>/Entities|Enums`, `Application/<Module>/Commands|Queries|DTOs|
  Interfaces|Mapping|Validators`, `Infrastructure/<Module>/*Repository.cs`, and one
  `[Module]Controller` in `PropertyApi/Controllers`. The most structurally similar precedent is
  **ShortStay** (its own aggregate, optional link to `Property` via nullable `PropertyId`,
  Draft/Publish lifecycle, its own `Infrastructure/Persistence/Configurations/ShortStay/`
  sub-folder) — Investment follows the same shape.
- **Entities**: `BaseEntity` (Guid Id, CreatedAt/UpdatedAt UTC, IsDeleted/DeletedAt soft-delete)
  and `AuditableEntity` (+ CreatedByUserId/UpdatedByUserId/DeletedByUserId) in
  `PropertyApi.Domain/Common/Entities`. DDD style throughout: private setters, a static
  `Create(...)` factory that throws `DomainException` (`PropertyApi.Domain.Common.Exceptions`)
  on invalid input, and grouped `UpdateXxx()` / state-machine methods (`Confirm`, `Decline`,
  `Publish`, …) instead of public setters for anything business-meaningful. `VisitRequest` and
  `ShortStayListing` are the two best state-machine exemplars.
- **CQRS**: MediatR (`IRequest<T>`/`IRequestHandler<,>`), one file per
  Command/Query/Handler/Validator, always under a `<Verb><Noun>/` folder
  (`Commands/RequestVisit/RequestVisitCommand.cs` + `...Handler.cs` + `...Validator.cs`).
  Pipeline (`PropertyApi.Application.DependencyInjection.AddApplication`): Telemetry → Logging →
  Validation (FluentValidation, auto-scanned) → Handler. `IUnitOfWork.SaveChangesAsync` is the
  only place SaveChanges is called — repositories never call it themselves
  (`RepositoryWorkflowBoundaryTests` enforces this).
- **DTOs**: never return entities from a handler; every module has its own
  `DTOs/*.cs` records. Pagination uses the shared
  `PropertyApi.Application.Properties.DTOs.PagedResult<T>` (Items/TotalCount/Page/PageSize).
  Simple mutation results follow the `FavoriteMutationResult`
  (`enum Status {Success, NotFound, Conflict}` + static factories) pattern — reused for
  Watchlist/Interest mutations instead of throwing for expected "already exists" cases.
- **Errors**: `PropertyApi/Middleware/ExceptionHandlingMiddleware.cs` maps
  `Domain.Common.Exceptions.DomainException` → 400 (one documented 409 special-case),
  FluentValidation failures → 422, `Application.Common.Exceptions.NotFoundException` → 404,
  `ConflictException` → 409, `ForbiddenException` → 403. Phase 1 reuses these — no new error
  contract.
- **EF Core / PostgreSQL**: single `AppDbContext : IdentityDbContext<ApplicationUser,
  ApplicationRole, Guid>`. One `IEntityTypeConfiguration<T>` per entity under
  `Infrastructure/Persistence/Configurations/` (sub-folder per module, e.g. `ShortStay/`).
  Conventions confirmed from `ShortStayListingConfiguration`/`FavoriteConfiguration`: money as
  `decimal` with explicit `HasColumnType("decimal(18,4)")` (never `double`), coordinates
  `decimal(9,6)`, enums stored as `HasConversion<string>()` with a `HasMaxLength`, explicit
  indexes via `HasIndex`, soft-delete `HasQueryFilter`, optimistic concurrency via the Postgres
  `xmin` shadow property on aggregates that see concurrent writes. Migrations live in
  `PropertyApi.Infrastructure/Migrations`, timestamp-prefixed, one PR generates exactly one new
  migration (most recent: `20260901200038_AddUserAccountSubscriptionLifecycle`). Design-time
  factory (`AppDbContextFactory`) does not require a live DB connection to run
  `migrations add`.
- **DI registration**: split by concern — `PersistenceInfrastructureRegistration`
  (DbContext/Identity/UnitOfWork), `RepositoryInfrastructureRegistration` (one
  `services.AddScoped<IXxxRepository, XxxRepository>()` line per repo — this is where Investment
  repos get registered), `MediaInfrastructureRegistration` (Cloudinary), `CacheInfrastructureRegistration`
  (Redis, `IDistributedCache`, no bespoke `ICacheService` abstraction exists yet — Phase 1 does
  not need to introduce caching to satisfy the spec, which lists it as optional).
- **Auth/Authorization**: JWT bearer + ASP.NET Identity. Roles are plain strings in
  `PropertyApi.Domain.Users.Constants.RoleNames` (`User`, `Agent`, `Admin`, `AgencyOwner`,
  `AgencyAgent`) — no separate policy-based scheme beyond `[Authorize]` /
  `[Authorize(Roles = RoleNames.Admin)]`. Controllers read the current user id from
  `ClaimTypes.NameIdentifier` via a small `GetUserId()`/`GetCurrentUserId()` helper (two
  slightly different existing variants — Phase 1 follows the `VisitsController` variant that
  throws `UnauthorizedAccessException` rather than returning nullable, since Investment's
  authenticated endpoints are all `[Authorize]`-gated already).
- **Media storage**: `IMediaStorageService` (Application) → `CloudinaryMediaStorageService`
  (Infrastructure) is the only storage abstraction that exists. It is image-oriented
  (`UploadImageAsync`/`DeleteImageAsync`/`GetImageAsync` returning `MediaUploadResult` with a
  provider `PublicId` + `Url`). Reused as-is for `InvestmentDocument` file storage (documents are
  frequently PDFs, not images — Cloudinary handles arbitrary "raw" resource types under the same
  API keys, so no new provider is introduced; the existing interface's shape is generic enough to
  reuse for the URL+PublicId pair even though its method names say "Image").
- **Notifications/Audit**: `INotificationService` (Infrastructure `NotificationService`) is the
  existing notification pipe (`NotificationType` enum + `NotifyPropertyUpdateAsync`-style calls,
  always wrapped in try/catch so a notification failure never fails the primary write —
  see `RequestVisitCommandHandler`). No generic `IAuditLogService` implementation beyond auth
  events was found wired into feature handlers; Phase 1 does not build a new audit framework
  (per spec §26) and instead logs sensitive lifecycle events via the existing `ILogger` the same
  way other handlers do, which is consistent with current practice outside of auth.
- **Frontend**: the Angular SPA is **not** in this repository — it lives in a separate repo at
  `Wohnungsmieten` (see the `frontend-repo-location` memory). Angular 20, standalone components,
  `@ngx-translate/core` for i18n (en/de/ar already present), `src/app/features/<feature>/` per
  feature. Closest structural precedents: `features/short-stay` (listing+booking flow) and
  `features/favorites` (watchlist-shaped).

## 2. Existing reusable components (do NOT recreate)

| Need | Reuse |
|---|---|
| Base entity / soft delete / audit stamps | `BaseEntity`, `AuditableEntity` |
| Unit of work / SaveChanges | `IUnitOfWork` |
| Current user id in a handler | `ICurrentUserService` (Application) — used by new query/command wiring where the controller doesn't already resolve it; controllers keep resolving from `ClaimTypes.NameIdentifier` like every other controller |
| Pagination | `PropertyApi.Application.Properties.DTOs.PagedResult<T>` |
| Mutation status result shape | `FavoriteMutationResult`-style record |
| File/image storage | `IMediaStorageService` (Cloudinary) |
| Notifications | `INotificationService` + a new `NotificationType` member |
| Validation | FluentValidation (`AbstractValidator<T>`, auto-registered) |
| Error → HTTP mapping | Existing `DomainException`/`NotFoundException`/`ConflictException`/`ForbiddenException` |
| Property linkage | `Property.Id` / `Property.OwnerId` — Investment never copies address/images/coordinates, only references `PropertyId` |
| Role checks | `RoleNames.Admin` |

No existing Favorite/Bookmark abstraction is generic enough to reuse for Watchlist (`Favorite`
is hard-wired to `Property`, composite PK, no room for an Investment target) — a new
`InvestmentWatchlistItem` is required, per spec §11's own fallback rule.

## 3. Integration points

- `InvestmentProject.PropertyId` → FK to existing `Properties.Id` (no cascade delete — an
  investment project outliving a hard-deleted property is not meaningful, but Property is
  soft-deleted in this codebase, never hard-deleted, so `Restrict` is safe and matches the
  `AccommodationType` FK style in `ShortStayListingConfiguration`).
- `InvestmentInterest`/`InvestmentWatchlistItem` → FK to `AspNetUsers`/`UserAccounts` via
  `UserId` (mirrors `Favorite`).
- `AppDbContext` gains 7 new `DbSet<T>` properties + `OnModelCreating` query filters, following
  the exact block style already used for the ShortStay/Services additions.
- `RepositoryInfrastructureRegistration` gains one `AddScoped` line per new repository.
- `PropertyApi.Application.DependencyInjection` needs no changes (MediatR/FluentValidation
  scanning is assembly-wide, so new Commands/Queries/Validators are picked up automatically).
- `NotificationType` enum gains new members for interest-related notifications (kept
  best-effort/non-blocking, matching `RequestVisitCommandHandler`'s try/catch pattern).
- Swagger: already wired generically (`AddEndpointsApiExplorer`/`AddSwaggerGen` in `Program.cs`);
  new controllers are picked up automatically, only XML doc comments are added per endpoint.

## 4. Conflicts / risks identified

- None found that block Phase 1. No existing `Investment*` namespace, table, enum, or route
  exists anywhere in the solution (`grep -ri investment` across `PropertyApi.Domain`/
  `Application`/`Infrastructure`/`PropertyApi` returned no hits before this change).
- `PropertyApi.Architecture.Tests` enforces layering (namespace-per-folder, no Domain→
  Infrastructure reference, DI-composition completeness, migration completeness against the
  live model). New code must satisfy these; verified via `dotnet test
  tests/PropertyApi.Architecture.Tests` after implementation.
- Per the user's own decision for this task, the EF Core migration is **generated only** (`dotnet
  ef migrations add AddInvestmentDiscoveryModule`) and **not applied** to a local database in
  this session — no `dotnet ef database update` is run.

## 5. Required additions (this repo)

**Domain** (`PropertyApi.Domain/Investments/`):
`Entities/InvestmentProject.cs`, `InvestmentProjectFinancials.cs`, `InvestmentRiskAssessment.cs`,
`InvestmentDocument.cs`, `InvestmentUpdate.cs`, `InvestmentWatchlistItem.cs`,
`InvestmentInterest.cs`; `Enums/InvestmentProjectStatus.cs`, `InvestmentProjectType.cs`,
`InvestmentRiskLevel.cs`, `InvestmentDocumentType.cs`, `InvestmentUpdateType.cs`,
`InvestmentInterestStatus.cs`.

**Infrastructure**: 7 `IEntityTypeConfiguration<T>` classes under
`Persistence/Configurations/Investments/`, 1 EF migration, repositories under
`Infrastructure/Investments/` implementing the Application-layer repository interfaces,
registered in `RepositoryInfrastructureRegistration`.

**Application** (`PropertyApi.Application/Investments/`): DTOs, `Interfaces/` (repository
contracts), Commands (Create/Update/SubmitForReview/Approve/Reject/Schedule/Publish/
Suspend/Close project; AddToWatchlist/RemoveFromWatchlist; ExpressInterest/WithdrawInterest;
document publish/remove; update create), Queries (public list/details/financials/risk/
documents/updates; my-watchlist/my-interests; admin list/review), Validators, a pure
`InvestmentCalculatorService` (no persistence — indicative-only per spec §21).

**API** (`PropertyApi/Controllers/`): `InvestmentsController` (public + authenticated
watchlist/interest routes) and `AdminInvestmentsController` (workflow + document/update
management), matching the existing one-controller-per-module convention.

**Tests**: new test files under `tests/PropertyApi.Application.Tests` (state transitions,
financial validation, duplicate watchlist/interest, calculator) and
`tests/PropertyApi.Integration.Tests` (EF mapping/constraints, API authorization/IDOR).

## 6. Files that will be modified

- `PropertyApi.Infrastructure/Persistence/AppDbContext.cs` (new DbSets + query filters)
- `PropertyApi.Infrastructure/Repositories/RepositoryInfrastructureRegistration.cs` (new registrations)
- `PropertyApi.Domain/Notifications/Enums/NotificationType.cs` (new members for interest events)
- `PropertyApi/Program.cs` — only if a Swagger grouping/tag tweak is needed (checked, not expected)

## 7. Files that will be created

Enumerated in §5 above; exact list finalized as each commit lands (domain → EF/migration →
application → API → tests), per the spec's own staged-commit strategy (§45).

## 8. Explicit non-goals for Phase 1 (guardrails, restated from the spec)

No payment, PaymentIntent, KYC/AML, investor verification/classification, wallet, custody,
securities/shares issuance, SPV, profit distribution, real returns, withdrawal, refund, or
investment-contract signing. "أرغب بالاستثمار" only ever creates an `InvestmentInterest`
row — never a financial transaction. The Investment Calculator is indicative-only and never
persists or debits anything.
