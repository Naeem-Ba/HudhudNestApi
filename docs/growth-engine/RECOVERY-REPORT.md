# Recovery Report — where the growth work actually stopped

Date: 2026-11-01. Repositories: `Naeem-Ba/HudhudNestApi` (.NET 10 LTS, Clean Architecture + MediatR,
PostgreSQL/PostGIS, Redis, SignalR, Render) and `Naeem-Ba/HudhudNest` (Angular 20 standalone, Capacitor 8,
Netlify). Both cloned fresh at `master` 4ab1e7a and `main` b0f8638.

## Git state

- No uncommitted work exists (fresh clones). Both default branches are clean.
- Remote branches: 34 (API) and 47 (app), almost all `claude/project-thread-*` branches whose PRs are merged.
  No open, unmerged growth work was found on them.
- The last ~40 API merges are QA-plan fixes (phases 4–14), the launch audit fixes (#372), DB normalization
  (#362) and four feature PRs from 2026-10-29: listing reports (#357), auto-translation (#358, #360), view
  counter (#359). App merges mirror them up to the Phase 15 mobile fixes (#189).
- The stack note "do not replace .NET 8" is satisfied in spirit: the API was moved to .NET 10 LTS on the
  owner's approval on 2026-10-10 (#312) because .NET 8 support ends 2026-11-10. Not reverted.

## The real stopping point

There never was a single "growth engine" plan in the repositories. Growth-related work stopped in three places:

1. **Phase-0 "freshness" (2025)**: price history writing, saved-search alerts and availability confirmation
   were built. Price history stopped at the write side ("no query methods yet"), so it collects data nobody sees.
2. **Social distribution**: the full engine exists and is admin-only; it stops at external platform accounts
   (tokens, app review), which only the owner can create.
3. **Marketing offers/leads**: built for the landing page and stopped at manual fulfilment ("the sales team
   honors it manually").

Since mid-October all effort went to the QA plan (phases 1–16) and launch readiness, which is why nothing
new was added on the growth side.

## Remaining work (summary, details in IMPLEMENTATION-ROADMAP.md)

Not started: search requests, matching engine with score/reasons, statistical price and rent estimation,
referrals, growth event log, growth dashboard, AI listing assistant.
Partial or broken: price history (G7.1–G7.3), saved-search alerts (G3.1, no edit/pause/frequency), area price
statistics (G15.1), map (no clustering, no location privacy), verification (no document review), campaigns
(manual), expat journey (only the generic services marketplace).
Blocked externally: social publishing (platform accounts), iOS push/build (Apple account), AI assistant
(provider and budget decision).

## Environment constraints that shape execution

- GitHub Actions is often blocked by the budget on these private repos; checks are run locally and reported
  honestly in each PR.
- Nothing is deployed by Claude. Each package ends with the owner's manual steps: merge API PR, run the
  Migrator (only when a migration exists), manual Render deploy, then merge app PR, PR main → release,
  manual Netlify Publish (15 credits per build, so app changes are batched).
- No Mac and no iOS device: iOS is checked statically only.
