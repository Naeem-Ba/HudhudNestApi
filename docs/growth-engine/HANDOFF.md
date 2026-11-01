# Growth Engine — Handoff

Read in this order: CURRENT-STATE.md → RECOVERY-REPORT.md → IMPLEMENTATION-ROADMAP.md → DECISIONS.md →
PROGRESS.md → this file. Then check git: the repositories and tests are the source of truth.

## Where the code lives

- API: `Naeem-Ba/HudhudNestApi`, default branch `master`. Clean Architecture: Domain / Application (MediatR
  commands and queries, validators) / Infrastructure (EF Core, repositories, hosted services) / API controllers.
- App: `Naeem-Ba/HudhudNest`, default branch `main`; production builds come from `release` (Netlify).
- Docs for this programme: `docs/growth-engine/` in the API repository (the app repository has a pointer).

## Patterns to reuse (do not invent new ones)

- Background work: `BackgroundService` + `BackgroundJobLock.TryRunAsync` with a new key in
  `BackgroundJobLockKeys` (then update the count and lists in `BackgroundJobLockExclusionTests` and
  `AdvisoryLockConcurrencyTests`).
- Notifications: add a `NotificationType`, a row in `NotificationKindCatalog`, a text in `NotificationCatalog`
  (ar/en/de) and a method on `INotificationService` with a dedupe key. Three test fakes implement the interface
  (`CreateBookingCommandHandlerTests`, `ListingReportTests`, `ServiceProviderOnboardingTests`).
- Public endpoints: add to `PublicEndpointPolicyTests.ApprovedAnonymousEndpoints`, use rate limit `public-read`,
  output-cache policies in `OutputCacheRegistration` (property tag is evicted on listing changes).
- Listing visibility: `Property.OnMarket(now)` for search-like reads, `Property.PubliclyViewable(now)` for
  detail-like reads.
- Search filters and currency conversion: `PropertyRepository.ApplyFilter` + `LoadCurrencyRatesAsync`. The list
  endpoint has a DB command budget of 3 (performance gate); never add a lookup to that path.
- Migrations: `dotnet ef migrations add <Name> -p HudhudNestApi.Infrastructure -s HudhudNestApi --context AppDbContext`,
  then add an entry to `ci/migration-risk-baseline.json` and run `scripts/database/analyze-migrations.sh`.

## Local test recipe (when Actions is blocked)

API: `apt-get install dotnet-sdk-10.0 postgresql-16-postgis-3`, start postgres and redis, export the env block
from `.github/workflows/ci.yml` (job env), create `hudhudnest_testing` with `postgis` + `pg_trgm`, run the
Migrator project, `redis-cli FLUSHALL`, then `dotnet test` per project. Run the whole Integration project, not a
filtered subset: a filtered run can fail on timestamp kinds that the full run does not hit.

App: `npm ci`, then karma with a throw-away config that launches ChromeHeadless with `--no-sandbox`
(`CHROME_BIN=/opt/pw-browsers/chromium-*/chrome-linux64/chrome`), plus `npm run check:i18n` and
`npm run typecheck:strict`.

## Owner's manual steps per package

API PR merge → Migrator against Production (only if the package has a migration) → manual Render deploy →
app PR merge → PR `main → release` → manual Netlify Publish. App before API is never safe.
