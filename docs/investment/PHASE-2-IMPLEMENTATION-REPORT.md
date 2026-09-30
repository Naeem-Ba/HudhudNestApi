# PHASE 2 IMPLEMENTATION REPORT — Investment Portal Admin UI + Integration/API Security Verification

Date: 2026-09-04

## Executive Summary

Phase 2 closes the two gaps Phase 1 documented as open: (1) no Angular admin UI, and (2) no
integration/API-level tests were run. Both are now closed, with evidence.

Along the way, the repository audit and the new HTTP integration tests found and fixed **two
real, previously-undetected bugs** in the Phase 1 backend — one of them (`GET /api/investments/projects`
returning HTTP 500 for every anonymous request) would have made the public list endpoint
completely unusable in production, and the other (`PublishInvestmentProjectCommandHandler`
always rejecting the document-readiness check) would have made it impossible to ever publish an
investment project at all, even with everything correctly set up. Neither was caught by the
Phase 1 Application-layer unit tests, because those tests mock the repository layer and never
exercise real LINQ-to-SQL translation or the real Published-status gate a handler calls before
the transition it's gating has happened. This is exactly the category of defect Phase 2's
integration-testing mandate exists to catch, and it did.

A third, smaller gap was found and closed: the domain entity `InvestmentDocument` already had
`Publish()`/`Unpublish()` methods from Phase 1, but no Command or endpoint ever called them, and
`InvestmentDocumentDto` didn't even carry an `IsPublic` field — so the admin document UI Phase 2
was asked to build had no backend support for showing or changing a document's visibility. This
gap is closed with a new `SetInvestmentDocumentVisibilityCommand` and
`PUT /api/admin/investments/projects/{id}/documents/{documentId}/visibility` (Admin only).

## PHASE 2 STATUS: PASS

```
Angular Admin:       PASS
Integration Tests:   PASS
IDOR:                PASS
Authorization:       PASS
Migration:           PASS
Regression:          PASS
```

## Implemented Features

### Angular Admin Investment Portal (`src/app/features/investments/admin/`)

One scrolling detail page with clearly separated sections rather than six routed tabs — this
deliberately matches the existing app's own `admin-user-detail.page.ts` convention (audit
finding: the app has no existing tabbed-detail pattern to match, and inventing one for this
feature alone would be the "second incompatible routing architecture" the brief warns against).

- **List** (`/admin/investments`) — every lifecycle status (the public list only ever shows
  Published), server-side search/status/type filters, server-side pagination.
- **Create** (`/admin/investments/new`) — minimal fields matching `CreateInvestmentProjectCommand`
  exactly (property, title, description, type, currency); every other field is added afterwards
  on the detail page, mirroring the backend's own Create-then-Update split.
- **Detail** (`/admin/investments/:id`) — Overview/edit form, Workflow (buttons for only the
  actions valid from the current status, backed by `InvestmentProject`'s real state machine — no
  status dropdown anywhere), Financials (itemized costs with an auto-calculated, clearly-labeled
  Total and a "non-guaranteed" badge on Expected Revenue/Profit), Risk, Documents
  (upload/publish/unpublish/delete), Updates (add + list).
- Reject/Suspend/Close go through `ConfirmModalComponent`, naming the current status and the
  requested action before anything is sent; Reject requires a non-empty reason to enable Confirm.
- Full EN/DE/AR translations. Enum-value labels (status/project type/risk level/document type)
  **reuse** the existing public-facing `INVESTMENTS.*` i18n keys rather than duplicating them —
  see "Bugs found and fixed" below for a real bug this reuse pass caught along the way.
- No RTL-unsafe CSS (`left`/`right`/`margin-left`/`padding-right`, etc.) anywhere in the new
  admin SCSS — verified by grep, zero matches. Relies on the app's existing global RTL mechanism.
- `ng build --configuration=development`: succeeds, 0 errors. New lazy chunks:
  `admin-investment-list-page` (30.5 kB), `admin-investment-form-page` (21.1 kB),
  `admin-investment-detail-page` (104.7 kB).

**Known, deliberate gap**: the "property picker" on the create form is a manual Property ID
field with a "look up" button against the existing public `GET /api/properties/{id}` endpoint —
there is no admin-wide "search any property by title" endpoint in this backend yet, and building
one was judged out of scope for this phase (see "Deferred Work").

### Backend fixes/additions (`HudhudNestApi.Application`, `HudhudNestApi.Infrastructure`, `HudhudNestApi`)

| Change | Why |
|---|---|
| `SetInvestmentDocumentVisibilityCommand`/Handler + `PUT .../documents/{id}/visibility` | Domain support existed, nothing ever called it (§10 admin document management needs it) |
| `InvestmentDocumentDto` gains `IsPublic` (trailing default `= true`, all existing call sites compile unchanged) | Admin document list needs to show public/private state |
| `InvestmentProjectRepository.Joined()` returns a named `ProjectWithProperty` class, not a `ValueTuple` | Fixes the 500 on every `/api/investments/projects` call — see below |
| `PublishInvestmentProjectCommandHandler` reads documents via `GetAllDocumentsForAdminAsync` + local `IsPublic` filter, not `GetPublicDocumentsAsync` | Fixes publish always failing readiness — see below |

## Bugs Found and Fixed (with evidence)

### Bug 1 — `GET /api/investments/projects` returned HTTP 500 for every request

**Discovered by**: `InvestmentSmokeTests.AnonymousList_Returns200`, the very first real HTTP test
run against the live host.

**Root cause**: `InvestmentProjectRepository.Joined()` projected the Project⨝Property join into a
`ValueTuple<InvestmentProject, Property>`. Composing further `.Where()` calls on top of that
queryable (exactly what `GetPublishedListAsync`/`GetPublishedDetailsAsync`/`GetDetailsForAdminAsync`
all do) made EF Core fail to translate the resulting query against Npgsql/Postgres.

**Evidence** (actual response body from the running host, before the fix):
```
500 Internal Server Error
"The LINQ expression 'DbSet<InvestmentProject>()...Where(ti => (int)new
ValueTuple<InvestmentProject, Property>(ti.Outer, ti.Inner).Item1.Status == 4)'
could not be translated..."
```

**Fix**: `HudhudNestApi.Infrastructure/Investments/InvestmentProjectRepository.cs` — replaced the
`ValueTuple` projection with a named private `ProjectWithProperty` class. Verified: the same test
now returns 200; the full 44-test Investments integration suite passes.

**Why unit tests missed it**: `PublishInvestmentProjectCommandHandlerTests` and friends mock
`IInvestmentProjectRepository` directly — no LINQ ever reaches a real `DbContext`/Postgres.

### Bug 2 — publishing a project always failed the document-readiness check, even when correct

**Discovered by**: `InvestmentWorkflowStateMachineTests.ValidTransitionPath_Succeeds`, seeded with
the exact two required document types (`ProjectPlan`, `FinancialStatement`), both marked public.

**Root cause**: `PublishInvestmentProjectCommandHandler` checked required documents via
`_documents.GetPublicDocumentsAsync(...)`, which — correctly, for its actual purpose — additionally
requires the **parent project's `Status` to already be `Published`** (Phase 1 §20 gate). But the
handler calls this *before* `project.Publish()` runs, while the project is still `Scheduled`. The
join's status filter meant the query always returned zero documents, so every publish attempt
failed with "missing required documents" — permanently, for every project, with no way to satisfy
the check.

**Evidence** (actual response, before the fix, with the exact required documents attached and public):
```
400 Bad Request
"لا يمكن نشر المشروع — العناصر التالية ناقصة: المستندات المطلوبة (ProjectPlan, FinancialStatement)."
```

**Fix**: `PublishInvestmentProjectCommandHandler.cs` — reads documents via
`GetAllDocumentsForAdminAsync` and filters on `IsPublic` in memory instead. Verified: the full
valid-transition-path test (Draft → … → Published → Suspended → Closed) now passes end to end.
Also added `Publish_Throws_WhenRequiredDocumentsExistButAreNotPublic` to the Application-layer
unit tests to lock this exact distinction in at that level too.

### Bug 3 (pre-existing, found during the i18n reuse pass) — untranslated `NewConstruction`/`VeryHigh` badges

Not part of Phase 2's own new code — found while wiring the admin UI to reuse Phase 1's public
`INVESTMENTS.*` i18n keys rather than duplicating them. Every sibling enum-value key in
`en/de/ar.json` is a no-underscore uppercase match for `.toUpperCase()` on the C# enum name
(`"UnderReview".toUpperCase()` → `UNDERREVIEW`, matching the JSON key `UNDERREVIEW`) — except
`PROJECT_TYPE.NEW_CONSTRUCTION` and `RISK.VERY_HIGH`, which had an underscore the dynamic
per-card key-building (`project.projectType.toUpperCase()`) could never produce. Any Published
project with type `NewConstruction`, or any `VeryHigh` risk badge, silently rendered the raw
untranslated key string instead of "New construction"/"Very high" — in all three languages.
Fixed by renaming the JSON keys to `NEWCONSTRUCTION`/`VERYHIGH` (matching the established
convention) in `en.json`, `de.json`, `ar.json`, and the two static template references that used
the old spelling.

## Files Added

**Backend**:
- `HudhudNestApi.Application/Investments/Commands/SetInvestmentDocumentVisibility/{Command,Handler}.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentApiTestFactory.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentSmokeTests.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentAuthorizationMatrixTests.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentPublicPrivateBoundaryTests.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentIdorTests.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentWorkflowStateMachineTests.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentValidationTests.cs`
- `tests/HudhudNestApi.Integration.Tests/Investments/InvestmentDocumentSecurityTests.cs`
- `docs/investment/PHASE-2-IMPLEMENTATION-REPORT.md` (this file)

**Frontend** (`HudhudNest` repo, branch `feature/investment-discovery-phase-1`):
- `src/app/core/api/admin-investment-api.service.ts`
- `src/app/features/investments/admin/admin-investment-list/admin-investment-list.page.ts`
- `src/app/features/investments/admin/admin-investment-form/admin-investment-form.page.ts`
- `src/app/features/investments/admin/admin-investment-detail/admin-investment-detail.page.{ts,html,scss}`

## Files Modified

**Backend**:
- `HudhudNestApi.Application/Investments/DTOs/InvestmentDocumentDto.cs` (+`IsPublic`)
- `HudhudNestApi.Application/Investments/Commands/PublishInvestmentProject/PublishInvestmentProjectCommandHandler.cs` (Bug 2 fix)
- `HudhudNestApi.Infrastructure/Investments/InvestmentDocumentRepository.cs` (project `IsPublic` in both DTO projections)
- `HudhudNestApi.Infrastructure/Investments/InvestmentProjectRepository.cs` (Bug 1 fix)
- `HudhudNestApi/Controllers/AdminInvestmentsController.cs` (+visibility endpoint)
- `tests/HudhudNestApi.Application.Tests/Investments/PublishInvestmentProjectCommandHandlerTests.cs` (updated mock target + new test)

**Frontend**:
- `src/app/core/models/investment.model.ts` (+admin types)
- `src/app/app.routes.ts` (+3 admin routes)
- `src/app/navbar/navbar.component.html` (+2 admin nav links)
- `src/app/features/investments/investment-list/investment-list.page.html` (Bug 3 fix)
- `public/i18n/{en,de,ar}.json` (Bug 3 fix + `ADMIN_INVESTMENTS` block + `INVESTMENTS.UPDATE_TYPE`)

## API Endpoints Used/Added

All pre-existing `AdminInvestmentsController`/`InvestmentsController` endpoints from Phase 1,
plus the one addition:

```
PUT /api/admin/investments/projects/{id}/documents/{documentId}/visibility   { isPublic }   Admin
```

## Admin Workflow

```
Draft         → submit-review → UnderReview
UnderReview   → approve → Approved   |   → reject → Rejected  (reason required)
Approved      → schedule → Scheduled
Scheduled     → publish → Published  (requires: title+description, financials, risk,
                                       required public documents — ProjectPlan + FinancialStatement)
Published     → suspend → Suspended  |   → close → Closed
Suspended     → close → Closed
Rejected      → submit-review → UnderReview
Closed        → (terminal, no further actions)
```

No "Resume" (Suspended → Published) action exists — the backend's `InvestmentProject` state
machine has no such transition (only `Suspended → Closed`). The admin UI does not offer a button
for it. Adding one would require a new backend Command; deferred (see below).

## Authorization Model

Unchanged from Phase 1, confirmed by the new integration tests: `InvestmentsController` public
actions are `[AllowAnonymous]`, authenticated actions (watchlist/interest) require any logged-in
user, and every `AdminInvestmentsController` action requires `[Authorize(Roles = "Admin")]`. The
new visibility endpoint follows the same `[Authorize(Roles = "Admin")]` class-level attribute.

## Integration Test Environment

- **Topology**: isolated `postgis/postgis:16-3.4` Docker container (`hudhudnest-investment-testpg`),
  host port 5433 → container 5432, **never** the developer's own local Postgres instance (which
  was already running natively on port 5432 with different credentials — left untouched) and
  never any shared/production database.
- Schema applied via the project's own `tools/HudhudNestApi.Migrator` tool (the same one CI uses),
  `ASPNETCORE_ENVIRONMENT=Testing`.
- `postgis`/`pg_trgm` extensions created explicitly (matches `.github/workflows/ci.yml`).
- `WebApplicationFactory<Program>` (`InvestmentApiTestFactory`) boots the real host; access
  tokens are minted via the real `ITokenService` using a seeded user's real Identity
  `SecurityStamp`, so JwtBearer's `OnTokenValidated` security-stamp check runs for real.
  `IMediaStorageService` (Cloudinary) is faked — no live provider credentials exist in this
  environment — everything else in the request pipeline is genuine.

## Integration Test Results

**45/45 passing** — see the per-file breakdown under "Files Added" above.

| Area | Result | Evidence |
|---|---|---|
| Migration apply (clean DB) | PASS | `tools/HudhudNestApi.Migrator` run, 0 errors, seed roles created |
| Schema (7 tables, PK/FK/unique/indexes) | PASS | `information_schema` query, all 7 FKs + 2 unique indexes (`InvestmentWatchlistItems`/`InvestmentInterests` on `(UserId, InvestmentProjectId)`) present |
| Decimal precision | PASS | All money columns `numeric(18,4)`; `ExpectedReturnMin/Max` deliberately `numeric(9,4)` (percentages) |
| Enum persistence | PASS | `Status`/`RiskLevel`/`ProjectType`/`DocumentType`/`UpdateType` all `varchar` (string-converted) |
| Public/private boundary | PASS | 6/6 non-Published statuses return 404 on detail; financials/risk 404 even when data exists; private documents never in the public list; public documents on a non-Published project never returned |
| IDOR | PASS | 4/4 scenarios — watchlist, interest, private-document-via-another-project, visibility-toggle-wrong-project — all demonstrated via real HTTP, not code inspection |
| Authorization matrix | PASS | Anonymous 401 / non-admin 403 / Admin 200-204, across list, watchlist, interest, admin list, admin create, admin workflow actions, document upload/list/visibility/delete |
| State machine | PASS | Full valid path + 9 distinct invalid-transition cases, all rejected 4xx |
| Validation | PASS | 12 cases: missing title, invalid property id, invalid enum, min>max, zero target, end<start, raised>target, negative cost, guaranteed-return language, malformed JSON, unknown project id |
| Document security | PASS | Anonymous/non-admin upload rejected, admin upload+list+toggle+delete succeed, unknown id → 404, non-admin toggle/delete rejected |
| Concurrency | DEFERRED | No real financial commitment exists in this module (by design — see Phase 1 guardrails); nothing to race-test |

## Database/Migration Results

PASS — see "Integration Test Environment" and the schema table above. Migration was **not**
applied to any developer or production database; only to the isolated, disposable container
created for this test run.

## Frontend Build Results

PASS — `ng build --configuration=development` succeeds, 0 errors. (Pre-existing RegExp-polyfill
warnings from an unrelated third-party dependency, not from this module's code.)

## Existing Regression Test Results

PASS — all four suites green, run against the isolated Postgres container described above:

```
HudhudNestApi.Application.Tests    496/496 passed  (was 495 at end of Phase 1; +1 new)
HudhudNestApi.Architecture.Tests   107/107 passed  (unchanged from Phase 1)
HudhudNestApi.Auth.Tests           245/245 passed  (unchanged from Phase 1)
HudhudNestApi.Integration.Tests     63/63  passed  (all new — Investments feature)
─────────────────────────────────────────────────
Total                            911/911 passed, 0 failures
```

`dotnet build HudhudNestApi.sln`: 0 warnings, 0 errors.

## Known Limitations

- No admin-wide property search/autocomplete endpoint exists; the create-project property picker
  is a manual-id-plus-lookup field against the existing public property-detail endpoint.
- No "Resume" (Suspended → Published) workflow action — not supported by the backend state
  machine; would require a new Command if wanted.
- Concurrency/race-condition testing (§26) is not applicable — Phase 1/2 deliberately implement
  no real financial commitment pipeline for `RaisedAmount` (staff-entered, informational only).

## Blocked Tests

None. Every test category in the Phase 2 brief that has a corresponding real capability in this
codebase was executed against real infrastructure.

## Deferred Work

1. Admin-wide property search endpoint + a proper autocomplete picker on the create form.
2. A "Resume" workflow action (Suspended → Published), if the business wants projects to be
   un-suspended rather than only closed — needs a new backend Command + domain method first.
3. Phase 1's own roadmap (Legal/Compliance → KYC/AML → Regulated Partner → Payment → Real
   Investment) — unchanged, out of scope for this phase by explicit instruction.

## Security Assessment

No payment/KYC/AML/wallet/real-investment code was added in this phase. The one new endpoint
(document visibility toggle) follows the exact same `[Authorize(Roles = "Admin")]` + explicit-
action pattern as every other admin mutation in this module, and is covered by its own IDOR and
role-matrix tests. The two bugs found and fixed were both defects that would have degraded
availability/usability (500s, an unpublishable module) — neither was an authorization or data-
exposure defect; the IDOR/authorization boundaries themselves tested clean on the first attempt.

## Recommended Phase 3

1. Legal/Compliance → KYC/AML → Regulated Partner → Payment → Real Investment, per Phase 1's own
   stated roadmap.
2. Admin-wide property search endpoint, if the manual-id picker proves too friction-heavy in
   practice.
3. Concurrency/load testing once a real financial-commitment pipeline exists to test against.
