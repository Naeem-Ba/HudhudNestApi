# Growth Engine — Final report (2026-11-01)

**Status: built and tested locally, not deployed.** Everything is on branch `claude/project-thread-1oghdc`:
API PR #378 and app PR #193 (both drafts). GitHub Actions is still blocked by the budget, so CI did not run;
all results below come from local runs in the session (real PostgreSQL 16 + PostGIS, Redis, headless Chromium).

## What was built

| Pkg | Result | Main pieces |
|---|---|---|
| P1 | Done | Price history fixed (currency, drafts), public timeline, followers notified by a background sweep |
| P2 | Done | Search requests (demand posts), anonymous board for advertisers, explainable matching score, "propose my listing" |
| P3 | Done | Saved searches: edit, pause/resume, instant or daily summary, balcony/elevator/parking filters, delivery counters |
| P4 | Done | `GrowthEvents` log (no personal data, idempotent), UTM first touch sent with e-mail sign-up |
| P5 | Done | Referrals: codes, attribution, qualification on real activity, caps and fraud checks, user page, admin page. **No automatic reward** (D-11) |
| P6 | Done | Area asking-price statistics (P25/median/P75 per m², minimum 8 listings from 3 owners), public estimator with sample count, level, confidence, dates; "not enough data" is an answer |
| P7 | Partial | Campaign tracking + UTM link builder (inside P10). Automatic campaign grants **not built** (commercial decision, D-13) |
| P8 | Done | `/map` with server-side clusters; owner option "hide exact location" (approximate ~550 m cell, no street, rounded distances) |
| P9 | Blocked | Verification needs private document storage (decision D-03) |
| P10 | Done | `/admin/growth`: totals, daily table, 30-day funnel, UTM campaigns, D1/D7/D30 retention (definitions in GROWTH-METRICS.md) |
| P11 | Done | `/expats`: three goals, steps to existing pages, only service categories with a real approved provider |
| P12 | Blocked | Social publishing needs real platform accounts |
| P13 | Blocked | AI listing assistant needs a provider and a budget decision |

## Tests (local)

- API: build Release with 0 warnings; Application 1933/1933; Architecture 210/210; Infrastructure 615/615;
  Auth 447/447; full Integration suite: see the PR description (last full run before P6–P8: 992/992); migration
  risk gate passed for 93 migrations; `dotnet format` clean.
- App: unit 1470/1470; i18n parity ar/en/de (2803 keys); `typecheck:strict` clean; frontend audit passed;
  production build: see the PR description.
- **Not tested:** Android and iOS builds (no native code changed), any real device, iOS on a Mac, Production,
  real e-mail/push delivery, load with real data volumes.

## New migrations (all additive, apply in this order; the Migrator does it)

1. `20261101172158_AddPriceHistoryCurrencyAndFollowerSweep` (also stamps old rows so nothing old is announced)
2. `20261101174551_AddPropertySearchRequests`
3. `20261101181305_AddSavedSearchAlertsV2`
4. `20261101211830_AddGrowthEvents`
5. `20261101213229_AddReferrals`
6. `20261101215020_AddAreaPriceStats`
7. `20261101222425_AddListingLocationPrivacy`

## Your manual steps, in order

1. Read PR #378 and PR #193. Mark them ready and merge **API #378 first**.
2. Take a Production database backup.
3. Run the Migrator against Production (applies the 7 migrations above).
4. Manual Render deploy of the API. Check `/health/ready`.
5. Merge app #193 into `main`.
6. Open PR `main → release`, merge it, then one manual Netlify Publish (15 credits). Run `npm run check:live`.
7. After one hour: `/admin/growth` should show activity data starting; after 6 hours the first area price
   statistics exist (most areas will say "not enough data" at first, which is expected).

The app must never go out before the API: the new pages call endpoints that only exist in the new API.

## Decisions waiting for you (defaults are already in place)

- D-11 Referral reward: what is granted (free featured days, a plan month, money) and its text. Until then the
  invite page says no reward is announced and admins mark rewards by hand.
- D-13 Campaign grants ("first N agencies get X"): only after the reward is decided and there is billing.
- D-14 Location privacy default: currently off for everyone. Recommendation: switch it on by default for private
  owners.
- D-12 Old `/api/analytics/market` (mixed-currency averages) is unused by the app; remove it in a later release.
- P9 storage, P12 platform accounts, P13 AI provider and monthly budget.

## Known gaps and risks

- Google/Apple/phone sign-ups record no UTM tags (counted in "new accounts", not in the campaign table).
- Retention only measures accounts created after the activity job first runs; small cohorts are noisy.
- Price statistics ignore rooms, floor, condition and age; the range and the confidence label carry that.
- Short-stay listings have no "hide exact location" option yet.
- Map and near-me order by exact distance internally; only the shown values are rounded for hidden spots.
- New pages are linked from the mobile menu and from related pages, not from the desktop top bar (it already
  overflows at some widths, Phase 15 D01).
