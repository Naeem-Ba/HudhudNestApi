# PHASE 1 IMPLEMENTATION REPORT — Investment Discovery

Date: 2026-09-04

## Status by area

| Area | Status | Notes |
|---|---|---|
| Architecture | PASS | Modular monolith, no microservice/new DB; follows existing Clean Architecture/CQRS/MediatR/EF conventions throughout. |
| Domain | PASS | 7 entities, 6 enums, full state machine on `InvestmentProject`, DDD factory/guard style matching `Property`/`VisitRequest`/`ShortStayListing`. |
| Database | PASS (not applied) | 7 new tables, purely additive (no ALTER/DROP on any existing table). |
| Migration | PASS (generated only) | `AddInvestmentDiscoveryModule` generated per the user's explicit instruction; **not** run against any database this session. |
| CQRS | PASS | 16 commands, 12 queries, one file per Command/Query/Handler/Validator. |
| API | PASS | `InvestmentsController` (public + authenticated) and `AdminInvestmentsController` (`[Authorize(Roles = RoleNames.Admin)]`), explicit action-per-transition — no arbitrary status PUT. |
| Authorization | PASS | Anonymous/Authorize/Admin split matches spec §19; every new anonymous endpoint added to `PublicEndpointPolicyTests` and rate-limited. |
| Watchlist | PASS | Unique `(UserId, InvestmentProjectId)` DB constraint + app-level duplicate check, idempotent commands. |
| Interest | PASS | Never a payment/commitment; idempotent (Active→Conflict, Withdrawn→Reactivate same row). |
| Documents | PASS | Metadata-only in Postgres; binary via existing `IMediaStorageService` (Cloudinary); public documents gated on the parent project's Published status. |
| Risk | PASS | Indicative-only fields; shared FluentValidation rule rejects "guaranteed return"/"risk free" language on every free-text field. |
| Calculator | PASS | Pure/stateless, three scenarios, disclaimer always present, never persisted. |
| Angular | PARTIAL | Public list, details, and authenticated watchlist pages shipped and building clean. **No dedicated admin UI** — `AdminInvestmentsController` is fully implemented and directly usable, but the review/publish/financials/risk/document/update authoring screens described in spec §29/§48 were not built in this pass. |
| i18n | PASS | Full EN/DE/AR translations added to `public/i18n/*.json`, no hardcoded UI strings. |
| RTL | PASS | Relies on the app's existing global RTL mechanism (`language.service.ts`); pages use logical CSS properties (`inset-inline-*`) consistent with the rest of the codebase — no page-specific RTL overrides were needed or added elsewhere in this app either. |
| Tests | PARTIAL | See below. |
| Security | PASS by design; not integration-test-verified | No payment/KYC/AML/wallet/real-investment code anywhere. IDOR-shaped gaps closed at the repository layer (financials/risk/documents/updates all re-check the parent project's Published status, not just the top-level detail query). Not confirmed with a live-server IDOR/authorization test in this session. |
| Existing Regression Tests | PASS (for the suites run) | See below — not every test project could be run in this sandbox. |

## Tests — detail

**Written and passing (42 new tests):**
- Domain: `InvestmentProject` full state-machine happy path + every invalid transition, financial/schedule/raised-amount guards, `InvestmentProjectFinancials` cost recomputation, `InvestmentInterest` Withdraw/Reactivate, `InvestmentWatchlistItem` validation.
- Application: `PublishInvestmentProjectCommandHandler`'s cross-aggregate readiness checklist, `ExpressInvestmentInterestCommandHandler`'s idempotency, `AddInvestmentToWatchlistCommandHandler`'s duplicate prevention, `InvestmentCalculator`'s scenario math.

**Regression-verified in this session:**
- `dotnet test tests/HudhudNestApi.Application.Tests` — 495/495 passing (was 453 before this work; +42 new).
- `dotnet test tests/HudhudNestApi.Auth.Tests` — 245/245 passing (unaffected by this change, re-run to confirm the `PropertySummary` extension didn't regress it).
- `dotnet test tests/HudhudNestApi.Architecture.Tests` — 107/107 passing (2 initially failed on the new anonymous endpoints missing from the approved list/rate-limit policy — fixed).
- `dotnet build HudhudNestApi.sln` — 0 errors, 0 warnings.

**Not written / not run in this session (gap against the Definition of Done):**
- Integration tests (EF mapping/constraints/migration apply, against a real PostgreSQL) — `tests/HudhudNestApi.Integration.Tests` was not run; no live database was available in this sandboxed session, and the user explicitly asked for the migration to be generated only, not applied.
- API-level tests (anonymous/authenticated/admin access, invalid ids, invalid transitions) against a running `WebApplicationFactory`.
- Explicit IDOR tests (User A editing/viewing User B's watchlist/interest, or a private document) at the HTTP layer — the repository-layer gating that should prevent this is implemented and covered by the domain/handler unit tests above, but not exercised end-to-end.
- `HudhudNestApi.Concurrency.Tests`, `HudhudNestApi.Performance.Tests`, `HudhudNestApi.StagingSmokeTests` were not run (all require live infrastructure this sandbox doesn't have).

## Security checklist (Phase 1 scope guardrails)

- [x] No payment / PaymentIntent / payment provider
- [x] No KYC / AML
- [x] No investor verification/classification
- [x] No wallet / custody / ledger
- [x] No securities/shares issuance, SPV, profit distribution
- [x] No real investment, contract signing, or financial transaction anywhere in this module
- [x] `InvestmentInterest` is explicitly "Expression of Interest" — never named/modeled as an investment
- [x] Duplicate watchlist/interest prevented at both DB (unique index) and application layer
- [x] Financials/risk/documents/updates re-check Published status independently of the top-level project query (closes a probe-by-id gap)
- [x] No `StorageKey`/`StorageProvider`/internal fields in any public or authenticated DTO
- [ ] IDOR protection — implemented, not integration-test-verified (see Tests section)

## Files created

**Backend** (`HudhudNestApi` repo, branch `claude/investment-discovery-phase-1-ce4396`):
- `docs/investment/PHASE-1-DISCOVERY.md`, this report
- `HudhudNestApi.Domain/Investments/**` — 7 entities, 6 enums
- `HudhudNestApi.Infrastructure/Persistence/Configurations/Investments/**` — 7 EF configurations
- `HudhudNestApi.Infrastructure/Migrations/20260904074621_AddInvestmentDiscoveryModule.*`
- `HudhudNestApi.Infrastructure/Investments/**` — 7 repository implementations
- `HudhudNestApi.Application/Investments/**` — DTOs, interfaces, 16 commands, 12 queries, calculator service, shared validation rule
- `HudhudNestApi/Controllers/InvestmentsController.cs`, `AdminInvestmentsController.cs`
- `tests/HudhudNestApi.Application.Tests/Investments/**` — 8 test files, 42 tests

**Frontend** (`HudhudNest` repo, branch `feature/investment-discovery-phase-1`):
- `src/app/core/models/investment.model.ts`, `src/app/core/api/investment-api.service.ts`
- `src/app/features/investments/{investment-list,investment-details,investment-watchlist}/**`

## Files modified

**Backend:**
- `HudhudNestApi.Infrastructure/Persistence/AppDbContext.cs` (new DbSets)
- `HudhudNestApi.Infrastructure/Repositories/RepositoryInfrastructureRegistration.cs` (new registrations)
- `HudhudNestApi.Application/Common/Interfaces/IPropertyReadRepository.cs` + `HudhudNestApi.Infrastructure/Repositories/PropertyReadRepository.cs` (extended `PropertySummary` with a few optional fields, reused instead of a parallel abstraction)
- `tests/HudhudNestApi.Architecture.Tests/Api/PublicEndpointPolicyTests.cs` (approved-anonymous list)

**Frontend:**
- `src/app/app.routes.ts`, `src/app/navbar/navbar.component.html`, `public/i18n/{en,de,ar}.json`

## Migration

`AddInvestmentDiscoveryModule` (20260904074621) — generated, **not applied**. To apply locally:

```bash
dotnet ef database update --project HudhudNestApi.Infrastructure --startup-project HudhudNestApi
```

## Endpoints

See `docs/investment/PHASE-1-DISCOVERY.md` §5 and the controllers themselves for the full list;
summary: `GET/POST/DELETE /api/investments/**` (public + authenticated) and
`GET/POST/PUT /api/admin/investments/**` (`Admin` role).

## Next recommended phase

1. Apply the migration and run `HudhudNestApi.Integration.Tests`/API tests against a real database
   to close the Tests/Security gaps flagged above (highest priority — this is the main thing
   standing between this PR and "done" per the spec's own Definition of Done).
2. Build the Angular admin UI for `AdminInvestmentsController` (project list/review, the
   Draft→…→Published workflow buttons, financials/risk forms, document upload, update
   composer) — the API is ready for it.
3. Legal/Compliance → KYC/AML → Regulated Partner → Payment → Real Investment, per the spec's
   own stated roadmap — the domain model here (separate `InvestmentInterest` from any future
   investment/payment concept, financials/risk as their own aggregates) was deliberately shaped
   so none of it needs to be rebuilt to support that transition.
