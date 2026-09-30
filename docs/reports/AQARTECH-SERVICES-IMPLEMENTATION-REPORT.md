# HudhudNest Services Marketplace — Implementation Report

**Session scope (as agreed with the user):** Foundation + one full vertical slice —
the shared Services domain plus one fully working, fully tested, end-to-end service
(**HudhudNest Verify**). The remaining four MVP services (Valuation, Inspect, Media,
Care) are reserved in the domain model but are not working products yet.

## 1. What changed, and why

### 1.1 Backend (`HudhudNestApi`, this repo)

**Domain** (`HudhudNestApi.Domain/Services/`) — new bounded context, DDD style
(private setters, static `Create()` factories, domain state-machine methods),
directly mirroring `VisitRequest`:

| File | Purpose |
|---|---|
| `Enums/ServiceCategory.cs` | 13 values — 5 MVP (`Verification` built; `Valuation`/`Inspection`/`Photography`/`PropertyManagement` reserved) + 8 future categories, so nothing needs renumbering later. |
| `Enums/ServiceRequestStatus.cs` | `Submitted, UnderReview, Accepted, Scheduled, InProgress, Completed, Reviewed, Rejected, Cancelled`. |
| `Enums/ServiceProviderVerificationLevel.cs` | Graded (`None → AccountVerified → IdentityVerified → BusinessVerified → ServiceVerified`) — never a bare boolean. |
| `Entities/ServiceProvider.cs` | The business behind a listing of offerings. `AgencyId` is its own independent field, deliberately not derived from `UserAccount.AgencyId` (see the entity's doc comment for why). |
| `Entities/ServiceOffering.cs` | One sellable service in one category, reuses the existing `Currency` FK. |
| `Entities/ServiceRequest.cs` | The central marketplace entity — full lifecycle: `Create → Accept/Reject → Schedule → Start → Complete → MarkReviewed`, plus `Cancel` from any non-terminal state. |
| `Entities/ServiceRequestStatusHistory.cs` | Append-only audit trail. |
| `Entities/ServiceReviewDocument.cs` | Uploaded deliverable/report metadata. |
| `Entities/ServiceReview.cs` | Distinct from `PropertyReview`/`UserRating` — gated on `ServiceRequest.Status == Completed`. |
| `Common/Exceptions/InvalidStateTransitionException.cs` | Subclass of `DomainException`; the one deliberate deviation from `VisitRequest`'s 400-for-everything precedent — mapped to **409** by the middleware. |
| `Notifications/Enums/NotificationType.cs` | Appended `19..25` (`ServiceRequestSubmitted/Accepted/Rejected/Scheduled/Completed/Cancelled`, `ServiceReviewAdded`) — nothing renumbered. |

**Application** (`HudhudNestApi.Application/Services/`) — 14 commands, 10 queries
(CQRS/MediatR + FluentValidation), a `ServiceMapper`, 7 interfaces
(`IServiceProviderRepository`, `IServiceOfferingRepository`, `IServiceRequestRepository`,
`IServiceRequestStatusHistoryRepository`, `IServiceReviewRepository`,
`IServiceReviewDocumentRepository`, `IServiceRequestNumberGenerator`). Every
lifecycle command resolves `ServiceProvider.UserId`/`ServiceRequest.RequesterId`
server-side and compares it to the authenticated caller — **no new Identity role**.
`RequestNumber`, `Status`, `CreatedAt`, `FinalPrice` etc. are never accepted from
the client.

**Infrastructure** (`HudhudNestApi.Infrastructure/`):
- 6 `IEntityTypeConfiguration<T>` classes (`Persistence/Configurations/Service*.cs`) —
  `HasConversion<string>()` for the three enums (majority convention), filtered-unique
  indexes (`ServiceProviders.UserId`, `ServiceRequests.RequestNumber`,
  `ServiceReviews.ServiceRequestId`, all `WHERE "IsDeleted" = false`), and the indexes
  the spec asked for (`RequesterId`, `ServiceProviderId`, `PropertyId`, `Status`,
  `CreatedAt`).
- One migration, `20260828064403_AddAqarTechServicesMarketplace` — 6 tables plus a
  native Postgres sequence (`CREATE SEQUENCE "ServiceRequestNumberSeq"`) backing
  `RequestNumber` generation.
- 7 repository implementations in `Infrastructure/Services/*.cs` (one context folder,
  matching `Infrastructure/Bookings/VisitRepository.cs`'s pattern) + `ServiceRequestNumberGenerator`
  (`SELECT nextval(...)` via `Database.SqlQueryRaw<long>`).
- Registered in `RepositoryInfrastructureRegistration.cs`. **`INotificationService`
  itself was not touched** — every Services notification reuses the existing generic
  `NotifyPropertyUpdateAsync(recipientId, propertyId, propertyTitle, type, detail, ct)`,
  which already falls through to a generic message template for any `NotificationType`
  it doesn't special-case.

**API** (`HudhudNestApi/Controllers/`):
- `ServiceProvidersController`, `ServiceOfferingsController`, `ServiceRequestsController` —
  same `GetUserId()`/`[Authorize]`/thin-controller convention as `VisitsController`.
- New rate-limit policies `service-requests` (10/hour) and `service-request-documents`
  (20/hour), registered in **both** `RateLimitingRegistration.cs` and
  `RedisRateLimitingDefaults.cs`.
- 4 new anonymous browse endpoints (`ServiceOfferingsController.GetCategories/GetByCategory/GetById`,
  `ServiceProvidersController.GetReviews`) added to `PublicEndpointPolicyTests`'
  allow-list, all carrying `[EnableRateLimiting("public-read")]`.
- `ExceptionHandlingMiddleware.cs`: one new `catch (InvalidStateTransitionException)`
  block, placed **before** the existing `catch (DomainException)` block (C# picks the
  first matching catch — order matters), mapping to 409.

### 1.2 Frontend (`HudhudNest`, separate repo)

Consumes the new backend for the same one working flow: request HudhudNest Verify from
a property page, see it in "my requests," cancel it, review it once completed.

- `core/models/service.model.ts`, `core/api/service-offering.service.ts`,
  `core/api/service-request.service.ts` — same DTO-tolerant-mapping pattern as
  `visit.model.ts`/`visit.service.ts`.
- `property-detail.page.ts/html`: **additive only** — a new CTA button next to the
  existing visit/message/review buttons, a new panel, new signals/methods. No
  existing signal, form, or method was changed.
- `features/services/my-service-requests.page.*` — new page, mirrors `visits.page.ts`
  (status filter, cancel, review-once-Completed).
- `app.routes.ts`: `/my-services` (`AuthGuard`). `navbar.component.html`: nav link
  (desktop account menu + mobile drawer).
- `public/i18n/{ar,de,en}.json`: `SERVICE_REQUEST`, `SERVICE_REQUESTS`,
  `SERVICES.CATEGORY`, `NAVBAR.MY_SERVICES` in all three languages.

## 2. Database changes

One migration: `HudhudNestApi.Infrastructure/Migrations/20260828064403_AddAqarTechServicesMarketplace.cs`.

- **New tables**: `ServiceProviders`, `ServiceOfferings`, `ServiceRequests`,
  `ServiceRequestStatusHistories`, `ServiceReviewDocuments`, `ServiceReviews`.
- **New sequence**: `"ServiceRequestNumberSeq"` (raw SQL — EF Core migrations have no
  first-class "add a bare sequence" builder call for this ownership shape, same reason
  the codebase's PostGIS geography column is also hand-written SQL).
- **No existing table was altered.**
- Migration is applied at deploy time exclusively via `tools/HudhudNestApi.Migrator`
  (not touched this session) — nothing about that process changed.

## 3. API surface added

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/api/ServiceOfferings/categories` | Anonymous | `public-read` |
| GET | `/api/ServiceOfferings?category=` | Anonymous | `public-read` |
| GET | `/api/ServiceOfferings/{id}` | Anonymous | `public-read` |
| POST | `/api/ServiceOfferings` | Provider | — |
| PUT | `/api/ServiceOfferings/{id}` | Provider (owner-checked) | — |
| GET/PUT | `/api/ServiceProviders/me` | Authenticated | — |
| POST | `/api/ServiceProviders` | **Admin** | onboards a provider |
| PUT | `/api/ServiceProviders/{id}/verification` | **Admin** | sets `ServiceProviderVerificationLevel` |
| GET | `/api/ServiceProviders/{id}/reviews` | Anonymous | `public-read` |
| POST | `/api/ServiceRequests` | Authenticated | `service-requests` (10/hr) |
| GET | `/api/ServiceRequests/mine` \| `/provider-inbox` \| `/{id}` \| `/{id}/history` \| `/{id}/documents` | Authenticated (ownership/admin-checked) | — |
| POST | `/api/ServiceRequests/{id}/documents` | Authenticated (owner-checked) | `service-request-documents` (20/hr), images only (JPEG/PNG/WEBP, 5 MB) |
| PUT | `/api/ServiceRequests/{id}/accept\|reject\|schedule\|start\|complete\|cancel` | Provider (or requester/admin for cancel) | **409** on wrong-state, not 400 |
| POST | `/api/ServiceRequests/{id}/review` | Requester only, `Completed` only | `reviews` policy (5/24h) |

## 4. Angular routes added

- `/my-services` (`AuthGuard`) — my service requests list.
- No standalone `/services` browse page this session (see §7) — browsing/requesting
  happens inline on `/properties/:id` / `/wohnungen/:id`.

## 5. Security

- No new Identity role. Every write is gated by comparing the authenticated
  `ClaimTypes.NameIdentifier` to `ServiceRequest.RequesterId` or the resolved
  `ServiceProvider.UserId`, or `RoleNames.Admin` for the two provider-management
  commands — exactly the authorization matrix agreed in planning.
- `RequestNumber`, `Status`, `CreatedAt`, `FinalPrice`/`QuotedPrice` fields are
  never client-suppliable — always server-computed in the handler.
- Document upload reuses `UploadPropertyImagesCommandHandler`'s exact validation
  (size cap, MIME allow-list, magic-byte signature check) — no new attack surface
  in that path.
- Property owners **can** request Verify on their own property (deliberately
  different from `VisitRequest`, which blocks self-requests) — checking one's own
  listing's documents is a normal use case, not a conflict of interest.

## 6. Tests executed and results

| Suite | Result | Notes |
|---|---|---|
| `HudhudNestApi.Auth.Tests` (Domain + Application handlers) | **314/314 passed** | includes 22 new `ServiceRequestTests` (state machine) + 12 new `ServiceRequestHandlerTests` (authorization, state-machine, validation gating) |
| `HudhudNestApi.Application.Tests` | **313/313 passed** | no regressions |
| `HudhudNestApi.Architecture.Tests` | **108/108 passed** | layering, migration-completeness, `PublicEndpointPolicyTests`, `RateLimitingGuardTests` all green with the new surface registered |
| `HudhudNestApi.Observability.Tests` | **9/9 passed** | no regressions |
| `HudhudNestApi.Integration.Tests` | **53/95 passed** | see below |
| `dotnet build HudhudNestApi.sln` | **Success**, 0 warnings, 0 errors | |
| Angular `npm run typecheck` | **Clean** | |
| Angular `ng build` (development) | **Success** | new `my-service-requests-page` chunk (39.75 kB dev / 14.19 kB prod) |
| Angular `ng build` (production) | **Success**, no budget warnings | |
| Angular `npm run audit:frontend` | **Passed** (pre-existing strict-mode warning only) | |

**Integration.Tests — the 42 failures are pre-existing and unrelated to this
change**, confirmed by direct inspection of each failure:
- 41 failures (`NotificationIntegrationTests`, `*PostgresTests`, `AdvisoryLockConcurrencyTests`,
  concurrency-token tests) throw `"A PostgreSQL test connection string is required."` —
  this sandboxed dev environment has no live Postgres/Redis instance (CI provides these
  via Docker services). None of these tests touch Services code.
- 1 failure (`StartupIntegrationTests.Program_Should_Register_RateLimiter_Once...`)
  is a stale regex assertion against `Program.cs` that predates this session — it
  looks for a literal `AddRateLimiter(` call in `Program.cs`, but an earlier,
  unrelated commit (`refactor: split Program.cs into focused composition-root files`,
  84ad644) moved that call into `Configuration/RateLimitingRegistration.cs`. Not
  caused by, or fixed by, this change.

A true `WebApplicationFactory` HTTP round-trip test for `CreateServiceRequest` was
**not written**: its handler depends on `IServiceRequestNumberGenerator`, which runs
a raw `nextval()` against Postgres — Integration.Tests uses EF Core **InMemory**,
which has no raw-SQL path. This is the same limitation the codebase's existing
`CreateAgencyInvitationCommandHandler` (also raw-SQL, via `pg_advisory_xact_lock`)
already has zero HTTP-level coverage for. Instead: controller-level tests
(`ServiceRequestsControllerTests.cs`, mocked `ISender`, same style as the existing
`PropertyImagesControllerTests.cs`) verify the controller→command wiring and
actor-id extraction.

## 7. Known limitations / explicitly deferred

Per the original spec's "detect → reuse → extend, smallest change" principle and the
agreed session scope:

- **Valuation, Inspection, Photography, PropertyManagement** are enum values only —
  no seeded provider/offering, no working UI. Fast-follow, reusing this exact pattern.
- **No payment gateway** — not investigated further, not stubbed, no `IPaymentService`.
- **No provider/admin dashboard UI** — `CreateServiceProvider`/`CreateServiceOffering`/
  `SetServiceProviderVerified` exist as real endpoints, callable via Swagger/seed script,
  but there is no Angular screen for them.
- **PDF document uploads** — `UploadServiceRequestDocumentCommand` accepts images
  only (JPEG/PNG/WEBP), reusing `IMediaStorageService.UploadImageAsync` unchanged.
  A verification "document" that's actually a scanned PDF needs a new
  `UploadFileAsync` method on that shared interface — deliberately not added this
  session to avoid widening a shared Infrastructure contract for one still-narrow
  vertical.
- **No standalone `/services` browse-by-category landing page** — request creation
  happens from the property-detail page's CTA (the spec's actual required flow:
  "browse → link to property → create request"), and status tracking happens on
  `/my-services`. A dedicated marketing/browse page is a reasonable fast-follow but
  wasn't necessary for the flow to work end-to-end.
- **No request-detail page** (status-history timeline, per-request document list) —
  the backend endpoints (`GET .../history`, `GET .../documents`) exist and are
  tested; `/my-services` shows status/notes/reason inline on the list instead of a
  separate detail route.
- **No real Postgres/Redis in this sandbox** — the `*PostgresTests` and
  `NotificationIntegrationTests` suites could not be run end-to-end here; they will
  run in CI, which does provision both.
- **Multi-provider routing** — `CreateServiceRequestCommandHandler` assumes exactly
  one active offering per category (the MVP's real state) and takes the first match;
  no matching/ranking logic exists.

## 8. Migration/build/CI impact

- `dotnet ef migrations add` generated cleanly against the existing model snapshot;
  `MigrationCompletenessTests` (offline model-vs-snapshot diff) passes.
- No CI workflow changes needed: new tests were added to the existing
  `HudhudNestApi.Auth.Tests` and `HudhudNestApi.Integration.Tests` projects (already
  "owned" in `ci.yml`), not new `.csproj` files.
- The Postgres/Redis-backed CI job will exercise the 41 currently-unrunnable
  Integration tests and should be watched on the first real CI run for this branch —
  none of them touch Services code, but this was not verified against a live
  database in this session.

## 9. Recommended next session

1. Pick the next MVP service (Valuation is the most natural second slice — same
   request/quote/complete shape as Verify) and repeat this exact pattern.
2. Add the `/services` browse-by-category landing page and a request-detail page
   (timeline + documents), now that the backend endpoints already exist and are
   tested.
3. Decide on PDF support for documents (adds `IMediaStorageService.UploadFileAsync`)
   before Inspect/Media ship, since photo/report deliverables for those categories
   are more likely to be PDFs.
4. Confirm CI's Postgres-backed job passes for this branch (the 41 environment-gated
   Integration tests could not be verified live in this session).
