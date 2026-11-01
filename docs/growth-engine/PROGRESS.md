# Growth Engine — Progress

## Current position (2026-11-01)

- Last completed phase: Phase 0 (audit) and planning documents.
- Last completed tasks: P1 (price history + saved-search fix), P2 (search requests + matching engine v1),
  P3 (saved-search alerts v2), API and app, tested locally, one PR per repository on branch
  `claude/project-thread-1oghdc` (later packages are added to the same PRs).
- Also built: P4 (growth event log + UTM on sign-up) and P5 (referrals), same branch.
- In progress: none.
- Next task: P6 area price statistics + statistical estimator.

## P1 — Price history and follower alerts

Branch `claude/project-thread-1oghdc` in both repositories.

API files:
- `HudhudNestApi.Domain/Listings/Entities/PropertyPriceHistory.cs` — `OldCurrencyCode`, `FollowersNotifiedAt`, `IsComparable`.
- `HudhudNestApi.Application/Listings/Commands/UpdateProperty/UpdatePropertyCommandHandler.cs` — records only
  published-to-published edits, records currency switches with the old currency (G7.2, G7.3).
- `HudhudNestApi.Application/Listings/Queries/GetPropertyPriceHistory/*` — public timeline query and DTOs.
- `HudhudNestApi.Application/Listings/PriceChangeDigest.cs` — net change of several pending rows.
- `HudhudNestApi.Infrastructure/Listings/PriceChangeFollowerNotificationHostedService.cs` — 10-minute sweep,
  advisory lock `PriceChangeFollowers`, 5-minute settle time, owner excluded (G7.1).
- `HudhudNestApi.Infrastructure/Search/SavedSearchMatchHostedService.cs` — cursor on `PublishedAt` (G3.1).
- Notification type `FavoritePriceChanged = 64` (category saved-search, opens the property), texts ar/en/de.
- `GET /api/properties/{id}/price-history` — anonymous, rate limit `public-read`, output cache evicted with
  the property tag, 404 when not publicly viewable, never returns `ChangedByUserId`.

Migration: `20261101172158_AddPriceHistoryCurrencyAndFollowerSweep` (two nullable columns, filtered index,
backfill `FollowersNotifiedAt = ChangedAt` so old changes are never announced). Baseline entry added.

App files: `src/app/core/api/price-history-api.service.ts`, `src/app/features/property-detail/price-history.component.ts`
(+ specs), detail page template, i18n `PROPERTY_DETAIL.PRICE_HISTORY` in ar/en/de.

Tests run locally (GitHub Actions not used):
- API: build Release 0 warnings; Application 1864/1864; Architecture 206/206; Infrastructure 600/600;
  migration risk gate passed (88 migrations); Migrator applied all migrations to a fresh PostgreSQL 16 + PostGIS DB.
  Integration suite: see the PR description for the final count.
- App: unit 1437/1437 (karma, headless Chromium), `check:i18n` parity, `typecheck:strict` clean.
- Not run: Android/iOS builds (no native change in P1), real-device check, Production.

Known issues / blockers: none new for P1. iOS push still waits on the Apple account (unchanged).

## P2 — Property search requests + matching engine v1

API files:
- `HudhudNestApi.Domain/Demand/` — `PropertySearchRequest` (status Active/Paused/Closed/Expired, expiry 60 days
  default, 90 max, renew), `SearchRequestMatch` (score 0–100, reason codes, source automatic/proposed,
  notification timestamps).
- `HudhudNestApi.Application/Demand/Matching/SearchRequestMatching.cs` — `SearchRequestMatcher`: hard rules
  (listing type, governorate, property type, converted price above budget + 5 %, rooms/area known and out of
  range) and weighted score (location 30, budget 25, size 25, features 20; configurable under
  `SearchRequests:Matching`). Unknown values earn no points and are listed as `*_unknown` reasons (D-05).
- Commands/queries/validators in `HudhudNestApi.Application/Demand/` — 5 open requests per user, contact data
  (phone numbers, e-mail, links, messenger names) rejected in free text, 20 proposals per advertiser per day.
- `HudhudNestApi.Infrastructure/Demand/SearchRequestMatchHostedService.cs` — every 15 minutes, advisory lock
  `SearchRequestMatch`; expires due requests; first sweep of a new request is silent; at most 5 requester alerts
  per request per sweep; one demand notice per listing per day to the owner; listings that no longer fit get
  score 0 (hidden), never deleted.
- `HudhudNestApi/Controllers/SearchRequestsController.cs` — `api/search-requests`: mine, create, update,
  pause/resume/close/renew, matches, board (advertisers with a live listing only), for-listing/{id}, proposals.
  All authenticated; no endpoint returns the requester's id, name, phone or e-mail to anyone but the requester.
- Notifications 65 `SearchRequestMatch`, 66 `SearchRequestProposal`, 67 `ListingDemandMatch`, texts ar/en/de.

Migration: `20261101174551_AddPropertySearchRequests` (two new tables, FKs, checks, filtered index). Additive.

App files: `src/app/core/api/search-request-api.service.ts`, `src/app/features/search-requests/` (my requests
page with form and matches, board page for advertisers and per-listing demand, summary component, specs),
routes behind `AuthGuard`, account-menu and mobile-drawer links, notification routing for `listing-demand`,
i18n `SEARCH_REQUESTS` in ar/en/de.

Tests run locally (GitHub Actions not used) with P1 + P2 together:
- API: see the P3 section (one run covers P1–P3).
- App: unit 1442/1442, `check:i18n` parity (2641 keys), `typecheck:strict` clean, production `ng build` OK.
- Not run: Android/iOS native builds (no native change), real device, Production.

Known limits: the board shows at most 50 per page and only to advertisers with a live listing (so demand
cannot be scraped by non-advertisers). A new request is first checked against the 100 most recently updated
live listings in its governorate; those matches appear on the request page without a push. Later sweeps only
look at listings changed since the previous sweep. Above 5 new matches in one sweep, the extra ones are listed
on the page but not pushed.

## P3 — Saved-search alerts v2

API: `PUT /api/savedsearch/{id}`, `POST /api/savedsearch/{id}/pause|resume`, frequency Instant/Daily (daily =
one `SavedSearchDigest` notification per 24 h with the count), balcony/elevator/parking filters, delivery log
(`LastNotifiedAt`, `NotifiedCount`). Decisions D-07 to D-09. Migration `20261101181305_AddSavedSearchAlertsV2`
(additive). The integration run also found and fixed a P2 bug: the per-listing facts query could not be
translated by EF (500 on `for-listing` and `proposals`).

App: saved-searches page with inline edit, pause/resume, chips, delivery line, localized summary; digest
notifications open the page.

Tests run locally with P1 + P2 + P3 (GitHub Actions not used):
- API: build Release 0 errors; Application 1888/1888; Architecture 207/207; Infrastructure 612/612;
  Integration 986/986 on a fresh PostgreSQL 16 + PostGIS database built by the Migrator (90 migrations);
  migration risk gate passed; `dotnet format --verify-no-changes` clean.
- App: unit 1447/1447, `check:i18n` parity (2666 keys), `typecheck:strict` clean, production `ng build` OK.
- One earlier run had a failure in `ClosedDealLifecyclePostgresTests` (existing test, capped batch ordered by
  `UpdatedAt` on a shared database); it passed in the clean run. Flaky, not caused by these packages.

## P4 — Growth event log

API: `GrowthEvents` table (append-only, unique idempotency key, no personal data, `UserId` FK set null),
`IGrowthEventRecorder` (one `INSERT ... ON CONFLICT DO NOTHING`, never throws, called after the save).
Writers: register (with first-touch UTM tags), first listing publication (MediatR handler on
`PropertyPublishedEvent`), search request created, saved search created, favorite added, first contact,
listing proposed, referral qualified. Decision D-10. Migration `AddGrowthEvents` (new table).
App: the existing attribution store sends the first UTM touch with sign-up.

## P5 — Referral system

API: `ReferralCodes` + `Referrals` tables, `GET /api/referrals/me` (own code, counts and statuses only),
`GET /api/admin/referrals`, `POST /api/admin/referrals/{id}/reject|rewarded` (admin, audited), code accepted at
sign-up (`referralCode`, unknown codes ignored), hourly `ReferralQualificationHostedService` (advisory lock
`ReferralQualification`), notification 69 `ReferralQualified`. Decision D-11. Migration `AddReferrals`.
App: `?ref=` captured for 30 days and sent with sign-up, "Invite friends" page `/referrals` (code, link, copy,
share, conditions, statuses), admin page `/admin/referrals`, menu links, i18n ar/en/de.

Tests (local): API Application 1907, Architecture 208, Infrastructure 615, Auth 447, Integration see the PR;
app unit 1456/1456, i18n parity (2703 keys), strict typecheck clean.

Fixed on the way: `ClosedDealLifecyclePostgresTests` was flaky on the shared test database (the sweep takes
the 20 oldest candidates; leftovers from other classes could keep the test's row out of reach). The test now
runs the sweep until its row is reached; assertions unchanged.

## Blockers carried (see IMPLEMENTATION-ROADMAP.md)

- P9 verification documents: private storage decision (DECISIONS.md D-03).
- P12 social publishing: platform accounts.
- P13 AI assistant: provider and budget.
