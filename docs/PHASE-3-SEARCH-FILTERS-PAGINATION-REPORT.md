# Phase 3 — Search, Filters, Location Hierarchy & Pagination

**Repos:** `HudhudNestApi` (branch `feature/phase3-search-filters-pagination` off `master`) and `HudhudNest` (branch `feature/phase3-search-filters-pagination` off `main`).
**Scope executed:** §13 Search fix, §14/15 City↔District separation & linkage, §16 Search+Filters merge, §17 Reset Filters, §18 Pagination. Favorites/Sharing/Dashboard restructuring were not touched, per the phase's explicit exclusions.

---

## 1. Documentation Reviewed

| Document | Relevant requirements | Impact on implementation |
|---|---|---|
| `HudhudNestApi/docs/phase-0-implementation.md` | History of the location model: legacy free-text `City`/`Region` → structured `Governorate → District → Neighborhood` FKs; "no filter set" must mean "any". | Established that the real hierarchy has no `City` tier — see Conflict #1 below. Confirmed empty-filter semantics already match spec. |
| `HudhudNestApi/docs/database/erd.md`, `DATABASE-PRODUCTION-READINESS.md` | ERD: `Governorate ||--o{ District ||--o{ Neighborhood`, all nullable FKs on `Property`, `Restrict` delete. No `City` entity anywhere in the schema. | Confirmed the schema-level hierarchy; drove the City/Governorate conflict resolution. |
| `HudhudNestApi.Application/Properties/DTOs/PropertyFilterDto.cs` (source, de-facto contract) | Full filter surface: pagination, legacy City/Region, structured Governorate/District/Neighborhood/PropertyType, price/room/area ranges, amenities, sort. No keyword field existed. | This *is* the combined search+filter DTO already — confirmed §16's "merge into one query" was mostly a frontend problem, except that a keyword field had to be added. |
| `HudhudNestApi.Infrastructure/Repositories/PropertyRepository.cs` (`ApplyFilter`/`GetPagedAsync`) | Filter → Count → Sort → Skip/Take ordering; all filters AND-composed on one `IQueryable`. | Confirmed as the authoritative, already-correct pagination/filter pipeline — no reordering needed. |
| `public/i18n/{en,de,ar}.json` → `PROPERTY_LIST.SEARCH_PLACEHOLDER` | English key already read *"Search by title or description..."* | Confirmed the product intent for the search box was always Title/Description keyword search — validated the backend field choice (see §5). |
| `docs/testing/coverage-improvement-plan.md` | Repositories/handlers must not be excluded from coverage; PR gate ≥70% line coverage on changed lines. | New `PropertyFilterDtoValidatorTests.cs` and `property-list.page.spec.ts` added to satisfy this on both repos. |
| `dotnet format` / Architecture.Tests conventions | `dotnet format --verify-no-changes` gate; `HudhudNestApi.Architecture.Tests` enforces anonymous-endpoint/rate-limit invariants. | Verified clean after every backend change; full 164-test Architecture.Tests suite re-run. |

**Conflict found and resolved:** The phase brief describes the hierarchy as *"Governorate → City → District, or the project's actual hierarchy if different"* (its own words hedge this). The actual, current schema has **no `City` entity** — it is `Governorate → District → Neighborhood`; `Property.City` is a separate, deprecated free-text column with no FK relationship to anything. Per the brief's own fallback clause, **"City" in §14/15 is read as the `Governorate` tier** for this implementation. This is a genuinely new-schema-work interpretation only if the user meant a literal, distinct `City` tier — flagged here explicitly rather than guessed silently, per Rule 1.

---

## 2. Existing Architecture (before this phase)

- **Frontend search flow:** `property-list.page.ts` owned `searchText`, `cityFilter`, `governorateId/districtId/neighborhoodId`, price/amenity/sort fields as plain component fields; `applyFilters()` built one `PropertyFilterDto` and called `loadProperties()` directly.
- **Backend search flow:** `PropertiesController.GetAll` → `GetPropertiesListQuery` → `PropertyRepository.GetPagedAsync` (`ApplyFilter` → `CountAsync` → `ApplySort` → `Skip/Take`).
- **Location flow:** `Governorate → District → Neighborhood`, each with cascading `GET /api/lookups/{districts,neighborhoods}?parentId=`, already correctly wired to clear the child selection and reload options when the parent changes.
- **Filter flow:** All active filters AND-composed onto one `IQueryable<Property>` — already correct.
- **Pagination flow:** `Page`/`PageSize` → `TotalCount` computed after filtering, before paging — already correct; `goToPage()` already preserved filters; `applyFilters()` already reset to page 1.
- **State/URL flow:** `route.snapshot.queryParamMap` was read **once** in `ngOnInit` (to support a saved-search/home-hero deep link only); no subsequent user action ever wrote back to the URL.

---

## 3. Problems Found

| # | Problem | Root Cause | Affected Layer | Impact |
|---|---|---|---|---|
| 1 | Search box did nothing | `searchText` was tracked in component state and counted toward `activeFiltersCount`, but never included in the `PropertyFilterDto` sent to the API — and the backend had no field to receive it even if it had been sent (`PropertyFilterDto` had no keyword field at all). | Frontend (`property-list.page.ts`) + Backend (`PropertyFilterDto.cs`, `PropertyRepository.ApplyFilter`) | The visible, prominently-labeled search input was fully decorative. |
| 2 | No URL↔filter-state binding after initial load | `applyFiltersFromQueryParams()` was a one-time snapshot read; `applyFilters()`/`goToPage()`/`resetFilters()` never called `Router.navigate` with the new state. | Frontend (`property-list.page.ts`) | Refresh, direct link sharing, and browser back/forward all lost the user's current search/filters/page. |
| 3 | No race-condition protection | Every HTTP call used a plain `.subscribe()` with `takeUntilDestroyed` only — no `switchMap`/cancellation anywhere in the search/filter/pagination pipeline. | Frontend | A slow response to an old filter could arrive after and overwrite a newer filter's results. |
| 4 | `CurrencyCode` filter silently ignored | `PropertyFilterDto.CurrencyCode` was defined and validated by the geo-search endpoint but never referenced in `PropertyRepository.ApplyFilter` (the main list's filter chain). | Backend | `?currencyCode=USD` on the main list endpoint matched listings in any currency. |
| 5 | Pagination aria-labels hardcoded in Arabic | `property-list.page.html`'s pagination nav/buttons and the filter-sidebar `aria-label` used literal Arabic strings instead of `translate`. | Frontend i18n | Screen-reader users on `en`/`de` locales heard Arabic labels. |

**Considered, then confirmed NOT a bug (documented per Rule 1, no change made):**
- *District/Neighborhood "missing" index* — the initial research pass flagged `Properties.DistrictId`/`NeighborhoodId` as missing an index (only `GovernorateId` had an explicit `HasIndex()` call in `PropertyConfiguration.cs`). Generating a migration for it produced an **empty** `Up()`/`Down()` — EF Core had already auto-created these indexes from the FK relationship configuration (visible in migration `AddSyrianMarketLookupsAndApplicationRole`). The change was reverted; this was a false positive from checking only for explicit `HasIndex()` calls.
- *City→District cascading* — already correctly clears the child selection and reloads options when the parent (Governorate) changes. No change needed.
- *Filter combination, TotalCount timing, page-preservation during pagination, page-reset on filter apply* — all verified correct in the existing backend/frontend code. No change needed.

---

## 4. Changes Made

### Backend (`HudhudNestApi`)

| File | Change | Reason |
|---|---|---|
| [PropertyFilterDto.cs](HudhudNestApi.Application/Properties/DTOs/PropertyFilterDto.cs) | Added `SearchTerm` (string?). | New keyword-search field (§13). |
| [PropertyRepository.cs](HudhudNestApi.Infrastructure/Repositories/PropertyRepository.cs) `ApplyFilter` | `SearchTerm` matched via `ILIKE` against `Title` OR `Description` (trimmed, case-insensitive, skipped when empty/whitespace); `City`/`Region` matches now trim the input before building the `ILIKE` pattern; `CurrencyCode` now actually applied (equality, uppercased). | Problems #1 and #4. |
| [PropertyFilterDtoValidator.cs](HudhudNestApi.Application/Properties/Validators/PropertyFilterDtoValidator.cs) | Added `SearchTerm` max-length (200) and `CurrencyCode` length (3) rules. | Consistency with the geo-search validator's existing `CurrencyCode` rule; basic input-size guard for the new field. |
| [PropertyFilterDtoValidatorTests.cs](tests/HudhudNestApi.Application.Tests/Properties/PropertyFilterDtoValidatorTests.cs) *(new)* | 10 tests covering the new rules plus existing ones (page size, min/max price ordering). | No prior coverage existed for this validator. |

### Frontend (`HudhudNest`)

| File | Change | Reason |
|---|---|---|
| [property.model.ts](src/app/core/models/property.model.ts) | Added `searchTerm?: string` to `PropertyFilterDto`. | Mirrors the backend field so `toQuery()`'s generic key iteration picks it up automatically. |
| [property-list.page.ts](src/app/features/properties/property-list/property-list.page.ts) | Replaced the one-time `applyFiltersFromQueryParams()` snapshot with a live `route.queryParamMap` subscription (`tap` → sync UI state → `switchMap` → fetch). All state mutations (`applyFilters`, `goToPage`, `resetFilters`) now call `router.navigate([], { queryParams })` instead of calling the API directly; the subscription is the single place a request is actually fired for user-driven changes. Removed the now-redundant `filter` signal. Added `searchTerm` to the request payload. | Problems #1, #2, #3. |
| [property-list.page.html](src/app/features/properties/property-list/property-list.page.html) | Pagination nav/prev/next/page-number `aria-label`s and the filter-sidebar `aria-label` now use `translate` instead of hardcoded Arabic; added `aria-current="page"` on the active page button. | Problem #5. |
| [en.json](public/i18n/en.json) / [de.json](public/i18n/de.json) / [ar.json](public/i18n/ar.json) | Added a `PAGINATION` namespace (`NAV_LABEL`, `PREVIOUS`, `NEXT`, `PAGE`) in all three languages. | Backing keys for the above. |
| [saved-searches.page.ts](src/app/features/saved-searches/saved-searches.page.ts) | Updated a stale doc comment referencing the removed `applyFiltersFromQueryParams`. | Accuracy only — no behavior change; `runSearch()`'s query-param names are unchanged and remain compatible. |
| [property-list.page.spec.ts](src/app/features/properties/property-list/property-list.page.spec.ts) *(new)* | 12 tests: default request shape, search-term wiring, empty-search handling, combined multi-filter request, page-reset-on-filter-change, filter-preservation-during-pagination, full Reset (state + URL), deep-link parsing, external `queryParamMap` change (back/forward simulation), and a `switchMap` race-condition regression test. | Zero prior coverage existed for this component. |

**Not changed / explicitly decided against:**
- No index added for `Rooms`, `ColdRent`/`WarmRent`/`PurchasePrice`, or a `pg_trgm` index for the new `Title`/`Description` `ILIKE`. See §10.
- No debounce added to the search input — it is submit-triggered (button/Enter), not fired per keystroke, so a debounce would add nothing (§ "Debouncing" in the brief: evaluated, not applied).
- Geo-search ("near me") was left untouched and out of the URL-binding refactor — it uses a separate DTO/pagination shape by design and isn't in the phase's explicit scope (§13–18 don't mention geo search); noted as a pre-existing asymmetry in §11.
- `Property.GovernorateId/DistrictId/NeighborhoodId` cross-consistency at write time remains unenforced — this is a documented, pre-existing gap in `CreatePropertyCommandHandler`, not a search/filter/pagination bug, and touching it is out of this phase's scope.

---

## 5. Search Behavior (after fix)

`SearchTerm` is matched via `EF.Functions.ILike` against `Title` **OR** `Description`, case-insensitive, trimmed, and skipped entirely when empty/whitespace-only (falls back to "no filter", not an error — verified by both a backend validator test and a frontend unit test). It is AND-composed with every other active filter in the same request. Verified live in the browser that Arabic/Unicode text (`شقة دمشق`) survives the full pipeline (component → `HttpParams` → query string) unmangled at the request-building layer; live database-level ILIKE behavior against Arabic text was not verified end-to-end (no reachable Postgres instance this session — see §11).

## 6. Filter Behavior

Search + City + Governorate/District/Neighborhood + price/rooms/area/amenities/listing-type all combine into **one** `PropertyFilterDto` → one `GET /properties` request, AND-composed server-side. Verified via an automated test asserting all six of `searchTerm`, `city`, `governorateId`, `minPrice`, `maxPrice`, `listingType` land in a single request when set together, and live in the browser (the actual outgoing, CORS-blocked request URL showed `searchTerm=apartment&city=Damascus` together).

## 7. Location Hierarchy

Confirmed, unchanged: `Governorate → District → Neighborhood` (see Conflict #1 in §1 for why "City" in the brief maps to `Governorate` here). Selecting a Governorate correctly clears any previously-selected District/Neighborhood and reloads the child options; selecting a District correctly clears/reloads Neighborhood. The legacy free-text `Property.City`/`Region` columns remain as a separate, independent filter (not part of the FK hierarchy) — left untouched since removing it would hide legacy rows that have `City` set but no `GovernorateId`.

## 8. Pagination

`Page`/`PageSize`/`TotalCount`/`TotalPages`/`HasNext`/`HasPrevious` contract unchanged (verified already correct — no bug). New in this phase: page and pageSize (when non-default) are now written to the URL; filters are preserved across `goToPage()`; any filter/search change or Reset resets to page 1. Verified live: browser back-navigation after a Reset correctly restored `?search=apartment&city=Damascus` and re-fired the matching request.

## 9. Tests

| Test | Result |
|---|---|
| Search (keyword reaches request) | PASS (unit + live browser) |
| Arabic Search (request-building layer) | PASS (unit test); DB-level ILIKE behavior — NOT VERIFIED (no reachable Postgres this session) |
| City (Governorate) | PASS (pre-existing, re-verified unaffected) |
| District | PASS (pre-existing, re-verified unaffected) |
| Governorate + District cascade | PASS (pre-existing, re-verified unaffected) |
| Search + Filters combined | PASS (unit + live browser) |
| Reset | PASS (unit + live browser — URL fully cleared) |
| Pagination (page persists filters; filter resets page) | PASS (unit) |
| Refresh / URL restores state | PASS (live browser, via back-navigation re-firing the correct request) |
| Back/Forward | PASS (unit simulation + live browser) |
| Race condition (switchMap) | PASS (unit test — stale response does not overwrite newer result) |
| Anonymous Search | PASS (live browser session was unauthenticated throughout; endpoint remains `[AllowAnonymous]`, unchanged) |
| Authenticated Search | NOT LIVE-VERIFIED (no test account available this session); no auth-related code was touched, and the full 308-test frontend suite plus 164-test backend Architecture.Tests suite (which covers auth/anonymous-endpoint invariants) both pass with zero regressions |
| Phase 1 Regression | PASS (Architecture.Tests 164/164; frontend 308/308) |
| Phase 2 Regression | PASS (same suites — session-expiry, api-error-interceptor, and auth specs all included and passing) |

**Backend:** `dotnet build` clean, `dotnet format --verify-no-changes` clean, `HudhudNestApi.Application.Tests` (new: 10/10), `HudhudNestApi.Architecture.Tests` (164/164, includes `PropertySortOrderTests`).
**Frontend:** `npm run typecheck` clean, `npm run build -- --configuration=production` clean, full Karma suite **308/308** (new: 12/12 in `property-list.page.spec.ts`).

## 10. Performance Findings

- **`DistrictId`/`NeighborhoodId` "missing index"**: investigated by generating an EF Core migration after adding explicit `HasIndex()` calls — the migration came back **empty**, proving both columns already have an index auto-created from their FK configuration (confirmed present since migration `AddSyrianMarketLookupsAndApplicationRole`). No change made; this was a false positive in the initial research pass, corrected before implementation, not shipped as a no-op migration.
- **`Rooms`, `ColdRent`/`WarmRent`/`PurchasePrice` indexes**: genuinely absent (not FK columns, no auto-index). **Not added in this phase** — the project's own documented index-change procedure (`docs/performance/postgresql-query-analysis.md`) requires disposable-DB rehearsal with before/after `EXPLAIN` evidence across 3 reruns, and no live Postgres instance was reachable this session (Docker Desktop engine not running, same limitation as Phase 2). Adding an index without that evidence would violate the project's own "no speculative indexing" rule. Recommended for a dedicated performance-analysis pass with DB access.
- **New `Title`/`Description` `ILIKE` search**: no supporting index (no `pg_trgm` GIN index on these columns, unlike the existing trigram indexes on the legacy `City`/`Region` columns). This is a real, honest limitation for large tables — flagged in §11, not silently shipped as "done."

## 11. Remaining Issues

| Issue | Reason | Impact | Recommended Phase |
|---|---|---|---|
| No live Postgres/Testcontainers integration test for `PropertyRepository.ApplyFilter` (new `SearchTerm`/`CurrencyCode` clauses use `EF.Functions.ILike`, which cannot be exercised against EF Core's InMemory provider) | Docker Desktop unavailable this session (same limitation documented in the Phase 2 report); no `Testcontainers.PostgreSql` package present in the test projects | New backend filter logic is verified by build + validator unit tests only, not by a query executed against a real database | Dedicated "Testing Infrastructure" phase (previously recommended in the Phase 2 report too) |
| ~~No `pg_trgm` index backing the new `Title`/`Description` `ILIKE` search~~ **Fixed 2026-09-24** | Migration `20260924120000_AddPropertyTitleDescriptionTrigramIndexes` (GIN `gin_trgm_ops`); EXPLAIN on PostgreSQL shows a Bitmap Index Scan for the keyword predicate | — | `Rooms`/price btree indexes still open: no query-plan evidence yet that they help |
| `Rooms`/`ColdRent`/`WarmRent`/`PurchasePrice` have no supporting index despite being active range filters | Same DB-access limitation | Range-filtered searches on these columns do a full scan | Performance-analysis phase |
| Geo-search ("near me") uses a separate DTO/pagination shape (`COUNT(*) OVER()`, single round-trip) from the main list's two-query approach, and wasn't folded into the URL-state binding added this phase | Out of this phase's explicit scope (§13–18 don't mention geo search); unifying it is a larger, separate architectural decision | Geo search's filters/page still aren't shareable via URL, unlike the regular list now | Future phase, if geo search's UX is prioritized |
| ~~`Property.GovernorateId`/`DistrictId`/`NeighborhoodId` cross-consistency is unenforced at write time~~ **Fixed 2026-09-24** | `ILocationHierarchyChecker`: create rejects inconsistent ids (validator); update checks the merged values only when the request changes the location, so older listings stay editable | — | — |
| No E2E framework exists (confirmed again this phase) | Same as Phase 2's finding | The 8 acceptance scenarios in the brief are covered by a mix of unit tests and one-time manual browser verification, not scripted, repeatable E2E | Testing Infrastructure phase |
| Staging environment has a pre-existing CORS misconfiguration (documented in the Phase 2 report) | Out of scope for this phase | Live browser verification against staging could only confirm request-building correctness (URL/query params), not actual result rendering with real data | N/A — already tracked |
| Location entities (`Governorate`/`District`/`Neighborhood`) have no `NameDe` field | Pre-existing (documented in `phase-0-implementation.md`) | German-locale users see English or Arabic location names, not German ones | Future localization phase, if in scope |

---

*Generated as part of Phase 3 execution. Branches not yet committed/pushed/PR'd — awaiting explicit instruction, per this session's established workflow.*
