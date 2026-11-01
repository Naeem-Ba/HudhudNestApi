# Growth Engine — Implementation Roadmap

Ordered by value for a young marketplace and by dependency. One PR per package and repository.
Complexity: S (≤1 day), M (2–3 days), L (a week or more). "Brief §" refers to the owner's brief of 2026-11-01.

| Pkg | Scope (brief §) | Depends on | Complexity | Status |
|---|---|---|---|---|
| P1 | Price history: fix recording (G7.2 currency, G7.3 drafts), public read API, timeline on detail page, notify users who saved the listing in a background job (G7.1); saved-search alert misses listings published after their draft was created (G3.1) (§9, §10, §14) | — | M | IMPLEMENTED_UNTESTED (PR open, not deployed) |
| P2 | Property search requests + matching engine v1: demand posts with expiry/pause/close, anonymous board for advertisers, explainable score, background matching, buyer + advertiser notifications, "propose my listing" without exposing the buyer (§11, §12) | — | L | IMPLEMENTED_UNTESTED (PR open, not deployed) |
| P3 | Search alerts v2: edit, pause/resume, frequency (instant/daily), amenity filters, delivery log; reuse the P2 matcher where rules overlap (§10) | P2 | M | NOT_STARTED |
| P4 | Growth event log: one append-only `GrowthEvents` table with stable names, idempotency key, correlation id, no PII; writers in existing handlers; acquisition source on register (UTM / referral code) (§25) | — | M | NOT_STARTED |
| P5 | Referral system: codes, attribution at register, qualification on real activity (e.g. first published listing that stays live N days, or confirmed phone + first contact), configurable reward rules, fraud checks, user + admin views (§16) | P4 | L | NOT_STARTED |
| P6 | Area price statistics + statistical sale and rent estimator: asking-price based, sale and rent separate, per-m² median and IQR band, duplicate and outlier exclusion, minimum sample before showing anything, sample count + data date + confidence, methodology page; replaces the current per-city average (G15.1); short-stay nightly rates separate (§13, §15, §22) | — | L | NOT_STARTED |
| P7 | Launch campaigns: campaign entity on top of `Offer` (seats, window, governorate/role targeting), automatic grant tied to P4 events, anti-abuse, stats (§17) | P4, P5 | M | NOT_STARTED |
| P8 | Map: bounding-box endpoint with server-side clustering, price markers, filters, approximate location when the owner hides the exact spot (§21) | — | M | NOT_STARTED |
| P9 | Verification & trust: levels with clear wording, advertiser/office/property document review queue, reasons, info requests, audit trail. Needs private document storage (§18) | owner decision D-03 | L | BLOCKED (storage) |
| P10 | Growth analytics dashboard: supply, demand, growth, retention (D1/D7/D30), marketplace funnels — only from P4 events and existing tables, every metric defined (§24) | P4 | L | NOT_STARTED |
| P11 | Expat journey: landing + guided request flow on top of the approved-provider services marketplace; only categories with at least one active provider are offered (§23) | — | M | NOT_STARTED |
| P12 | Social publishing: connect real accounts, per-listing post review, verify publish results from the platform (§19) | owner: platform accounts | M | BLOCKED (external) |
| P13 | AI listing assistant: suggestions only, never invents area/rooms/price/location/ownership, user confirms each field, cost cap (§20) | owner: provider + budget | M | BLOCKED (decision) |

## Acceptance per package (common)

- API: build, unit, integration (real PostgreSQL) and authorization tests for every new endpoint; migration gate
  passes (`scripts/database/analyze-migrations.sh`, baseline entry for each migration); `dotnet format` clean.
- App: unit tests for services and components, i18n keys in ar/en/de with parity, RTL/LTR checked, responsive
  check at 360 / 768 / 1366 px; Android build when native code changes.
- No heavy work inside an HTTP request: fan-out notifications and matching run in hosted services guarded
  by a Postgres advisory lock (existing `BackgroundJobLock`).
- No personal data in public responses; new public endpoints are rate limited and output-cached where safe.

## P1 acceptance

1. A price change on a published listing creates exactly one history row per changed field; a draft edit
   creates none; a currency change records the old currency.
2. `GET /api/properties/{id}/price-history` is anonymous for publicly viewable listings, 404 otherwise
   (owner/admin can read their own unpublished listing), never returns who made the change.
3. Users who saved the listing (not the owner) get one in-app/push notification per change, sent from a
   background sweep, deduplicated, respecting notification preferences.
4. The detail page shows the timeline with the percent change only where both values share a currency.
5. A listing created as a draft before a saved search's cursor and published later is alerted.

## P2 acceptance

The brief's end-to-end path: a user creates a request → an advertiser publishes a matching listing → the
user gets a notification → opens it → saves it → contacts the advertiser. The advertiser sees that N
anonymous requests match their listing and can propose it; the requester's identity is never shown to the
advertiser unless the requester writes first.

## Risks

- Sparse data: estimators and area statistics must say "not enough data" for most areas at launch.
- Notification fatigue: every new notification kind gets a per-user cap per sweep and a preference switch.
- Actions budget: CI may not run; local runs replace it and are reported.
- Netlify credits: app packages are batched into one release.
