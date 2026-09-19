# Current performance assessment

## Scope and evidence state

The original `scripts/perf-baseline.ps1` remains a developer diagnostic. It performs warm-up calls and then a
PowerShell `for` loop that waits for each HTTP response before sending the next request. It reports p50, p95, p99,
and derived requests/second, but that throughput is single-request throughput and is not a concurrent capacity
measurement. Authentication scenarios can also be skipped when credentials are absent, and the script has no
release-blocking thresholds.

No existing repository artifact demonstrated two API replicas, coordinated refresh/OTP races, distributed Redis
rate limits, a declared large dataset, `EXPLAIN ANALYZE`, `BUFFERS`, or `pg_stat_statements`. Average latency and a
successful HTTP status were therefore insufficient evidence of production performance or correctness.

## Critical paths discovered

- Public list: `GET /api/properties` -> `GetPropertiesListQuery` -> `PropertyRepository.GetPagedAsync`.
- Detail: `GET /api/properties/{id}` -> `GetPropertyByIdQuery` -> split no-tracking detail query.
- Geographic search: `GET /api/properties/geo-search` -> `PropertyGeoSearchRepository.SearchNearbyAsync`.
- Refresh: `POST /api/auth/refresh` performs a conditional atomic token revoke and replacement in a transaction.
- OTP registration: `/api/auth/phone/registration/send-otp` and `/verify` use a database reservation before
  consuming a challenge.
- Rate limiting: security endpoints use an atomic Redis Lua fixed-window counter when Redis limiting is enabled.

## Query risks

- Property listing runs a count query plus an offset-paginated data query. Deep pages therefore scan/discard more
  index entries as the offset grows.
- City and region filters use leading-wildcard `ILIKE '%value%'`; ordinary B-tree indexes cannot support these
  predicates. No trigram index will be added without measured plan evidence.
- Sorting supports created date, purchase price, cold rent, and area. Existing indexes are mainly individual
  indexes plus published/status/location composites; actual plan evidence is required before adding overlapping
  composites.
- Detail loading uses `AsNoTracking` and `AsSplitQuery`, which controls cartesian amplification but creates a bounded
  set of SQL commands. The performance gate enforces the approved command budget.
- Geographic search correctly uses `ST_DWithin` against a stored `geography(Point,4326)` column with
  `IX_Properties_GeoLocation` GiST. The gate fails if the representative radius plan stops using that index.
- Geographic and ordinary pagination remain offset based. No compatibility-changing cursor contract is introduced
  by this work.

## Existing indexes

The model has indexes for publication, deletion, status, listing type, owner, city, country, created date, key
foreign keys, one-main-image enforcement, refresh-token hash and active user tokens. A raw migration creates the
GiST geography index. Index changes are deliberately prohibited until before/after plans and write/storage costs
are measured on the same dataset.

## Concurrency and security risks

- Refresh rotation already uses a conditional `UPDATE ... WHERE IsRevoked = false`; the new race suite verifies the
  invariant at 2, 5, and 10 concurrent callers and queries the authoritative tables afterward.
- Correct OTP consumption uses a reservation, but invalid attempts previously incremented without an atomic upper
  predicate. Concurrent attempts could advance past the documented limit. The update is now bounded by
  `AttemptCount < 3` and the database integrity report enforces that maximum.
- Redis limiting is global across replicas and fails closed with 503 if Redis is unavailable. The new suite measures
  exact allowance/rejection counts and window recovery through the load balancer.
- Public property and geographic searches previously had no rate-limit metadata. Dedicated distributed policies are
  now applied.

## Missing historical evidence

Since 2026-09-19 `performance/baselines/approved-baseline.json` exists. It is the median of three real,
comparable release-profile runs from the Production Gate (2 API instances, 25000 properties, identical dataset
hash); its `provenance` section lists the source runs. `performance/performance-budgets.json` is `approved: true`
on the repository owner's request. The policy's separate performance/SRE review is still pending, and the release
profile continues to fail closed if either file is missing. Nothing in the baseline is fabricated.
