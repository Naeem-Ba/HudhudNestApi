# HudhudNest Growth Engine — Current State

Audit date: 2026-11-01. Base commits: API `master` 4ab1e7a (#376), app `main` b0f8638 (#189).
Method: code reading of both repositories, the QA plan phase reports 1–16 (project folder `qa-plan/`),
the launch audit of 2026-10-30 (`launch-audit/`) and the DB normalization audit (`db-normalization/`).
No Production data was read or changed.

Status values: NOT_STARTED, PARTIAL, IMPLEMENTED_UNTESTED, IMPLEMENTED_BROKEN, COMPLETE, BLOCKED.
"COMPLETE" is only given where a QA phase ran the full user flow end to end and its defects were fixed.
Nothing here was tested on an iOS device or a Mac; Android was tested on one phone via Play Internal
testing (build 1.1.x). "Mobile" below means the Capacitor app reuses the same Angular page; native
parts (push, deep links, camera, share) are named explicitly.

## Feature table

| # | Feature | Backend | Database | Frontend | Mobile | Tests | Status |
|---|---|---|---|---|---|---|---|
| 1 | Public property pages (no login, stable URL `/properties/:id`) | `GET /api/properties/{id}` anonymous, output cache | Property + images | property-detail page, share sheet, print | same page; App Links / Universal Links config in `deep-links.config.json` | unit + QA phase 7 | COMPLETE (web/Android) |
| 1a | Social preview (OG tags) | — | — | Netlify edge `social-crawler-meta` renders OG for crawlers; `sitemap.mjs` | n/a | edge-function node tests | IMPLEMENTED_UNTESTED (no crawler run against Production in this audit). No SSR: search engines that do not run JS see only the edge meta |
| 2 | Favorites | `FavoritesController`, unique (user, property) | `Favorites` | heart signal, favorites page | same | QA 12 fixed D04 | COMPLETE |
| 3 | Search alerts (saved searches) | `SavedSearchController` (list/create/delete), `SavedSearchMatchHostedService` every 15 min, cap 10/run, advisory lock | `SavedSearches` | saved-searches page, "save search" button | in-app + FCM push (Android) | QA 12 fixes #374 | PARTIAL — G3.1 fixed in P1; edit/pause/frequency remain (P3) |
| 4 | Property search requests (buyer/renter demand posts) | `SearchRequestsController` (P2): create/edit/pause/resume/close/renew, expiry, 5 open per user, contact-data filter | `PropertySearchRequests` (P2 migration) | my-requests page, anonymous board | web build (Capacitor shell unchanged) | unit + integration (P2) | IMPLEMENTED_UNTESTED (not deployed) |
| 5 | Matching engine (requests ↔ listings, score, reasons) | `SearchRequestMatcher` (hard rules + weighted score, reason codes, unknown ≠ match) + 15-min sweep with advisory lock; propose-my-listing | `SearchRequestMatches` (P2) | matches list with reasons, demand view per listing | web build | unit + integration (P2) | IMPLEMENTED_UNTESTED (not deployed) |
| 6 | "How much is my property worth" estimator | Valuation module is **office-based** (inquiry → invite ≥3 offices → office responses), not statistical | Valuation* tables | valuation-request page | same | QA 11 + fixes #373 | PARTIAL — no statistical range from listing data |
| 7 | Price history | fixed recording (P1: published-only, currency kept), public read endpoint, follower sweep | `PropertyPriceHistories` + 2 columns (P1 migration) | timeline on detail page | web build | unit + integration (P1) | IMPLEMENTED_UNTESTED (not deployed) |
| 8 | Rental estimation | none | — | none | none | none | NOT_STARTED |
| 9 | Referral system | none (no referral code, no source attribution on register) | none | none | none | none | NOT_STARTED |
| 10 | Launch campaigns | `Offer` (quota-bounded, atomic redemption) + `Lead` + `MarketingEvent` for the landing page; honored manually | Offers, Leads, MarketingEvents | admin/marketing page | same | unit | PARTIAL — offers are lead-capture only, no automatic reward/seat logic tied to real activity |
| 11 | Verification & trust | Phone OTP (Syrian delivery unproven), `Property.IsVerified` flag, short-stay `HostVerificationRecord`, service-provider verification level, agency approval | yes | badges in places | same | QA 4, 9, providers | PARTIAL — no document review workflow for advertisers/offices/properties; no clear per-level description |
| 12 | Social publishing engine | full module: channels, accounts, rules, publications, retries, dead letters, asset generation, admin UI | SocialDistribution* tables | admin/social-distribution | n/a | unit + integration | BLOCKED on external accounts (Meta/Telegram tokens, platform review) — no publisher verified against a real platform |
| 13 | AI listing assistant | none | — | none | none | none | NOT_STARTED (needs his decision on an AI provider and budget) |
| 14 | Property map | `GET /api/properties/geo-search` (radius), Leaflet map on list page, map picker | lat/lng on Property | list page map view | same page, touch via Leaflet | QA 7 | PARTIAL — no clustering, no price markers, no bounding-box query, no privacy fuzzing of exact location |
| 15 | Area price intelligence | `GET /api/analytics/market` (avg per city, 5 min cache) | — | analytics page | same | unit | IMPLEMENTED_BROKEN — averages listing asks per city without a minimum sample, without currency separation guarantees, and calls asking prices "prices"; no methodology shown |
| 16 | Expat services | Services marketplace with approved providers, service requests, in-app conversation, outcome tracking | Service* tables | services, my-services, provider pages | same | QA 11 + providers | PARTIAL — no dedicated expat journey (remote inspection/verification packaging) on top of the marketplace |
| 17 | Growth analytics dashboard | listing view counter (#359), share + attribution events, marketing events, owner property stats | ListingView*, PropertyShareEvents, PropertyAttributionEvents, MarketingEvents | analytics page (owner), admin marketing | — | unit | PARTIAL — no admin supply/demand/retention dashboard, no D1/D7/D30 |
| 18 | Growth event system | no central event log; domain-specific event tables exist (views, shares, attribution, marketing, audit log) | per-feature tables | — | — | — | PARTIAL |
| 19 | Notifications (in-app, push, preferences) | `NotificationService`, type catalog, dedupe keys, preferences, retention; FCM push | Notifications, DeviceTokens, NotificationPreferences | notifications page, settings | Android push works (build 1.1.1); iOS push needs Apple account + APNs key (his step) | QA 12, #343 | COMPLETE (web/Android), BLOCKED (iOS) |
| 20 | Sharing | share events, share sheet, Capacitor share | PropertyShareEvents | share button on cards/detail | native share | unit | COMPLETE |

## Specific answers

**What of the earlier growth plan was implemented?** There is no earlier document called a growth plan in either
repository. The pieces that serve growth were built under other names: "Phase-0 freshness" (price history
writing, saved searches, availability confirmation), social distribution, share/attribution events, marketing
offers/leads, listing view counter.

**What was started and not finished?**
- Price history: rows are written but nothing reads them (the repository has only `AddAsync`, comment says "no
  query methods yet"); no endpoint, no UI.
- Saved-search alerts: work, but only list/create/delete (no edit, no pause, no frequency), no amenity filter.
- Valuation: office flow exists, the statistical part never started.
- Offers: lead capture only.

**What needs fixing (found in this audit)?**
- G7.1 Price change notifications go to the **owner only** (the person who just changed the price), not to the
  users who saved the listing. Users following a listing never learn about a price drop.
- G7.2 When the owner changes the currency, the history row stores the old number with the **new** currency
  code; if only the currency changes, no row is written at all.
- G7.3 Edits made while the listing is still a draft are recorded as price changes, which would show changes
  nobody could see.
- G3.1 The saved-search sweep picks listings by `CreatedAt > cursor`. A listing created as a draft and published
  after the cursor moved past its creation time is never alerted.
- G15.1 Market insights show averages without a sample minimum (see row 15).

**Works on web but not on the phone?** iOS: push, Apple Sign-In and the store build all wait on the Apple
developer account (memory: Phase 15 fixes). Android: no gap found for these features.

**Works in the UI without a complete backend?** None found for growth features.

**Backend without UI?** Price history (no read endpoint either), `PropertyAttributionEvents` reporting
(`social-performance` endpoint exists, admin view only partial).
