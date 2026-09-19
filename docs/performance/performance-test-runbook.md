# Performance validation runbook

## Purpose and safety

The suite runs concurrent traffic only against disposable Local/CI/Performance or dedicated Staging environments.
It refuses unknown environments and a URL whose hostname matches Production. Never point it at Production or reuse
Production users, databases, Redis, storage, or provider credentials.

## Prerequisites

- Docker with Compose and a running daemon.
- .NET 8 SDK.
- PostgreSQL client (`psql`), `curl`, `jq`, Python 3, OpenSSL, and Bash.
- Ports 58080, 55432, and 56379 available.

The runner generates local secrets in memory. Optional overrides use names only:

- `PERF_POSTGRES_PASSWORD`, `PERF_JWT_KEY`, `PERF_OTP_SECRET_KEY`, `PERF_PHONE_HMAC_KEY`
- `PERF_FIXED_OTP`, `PERF_TEST_PHONE_PREFIX`, `PERF_TEST_PASSWORD`, `PERF_CLEANUP_SECRET`
- `PERF_RUN_ID`, `PERF_COMMIT_SHA`, `PERF_DATASET_SIZE`, `PERF_VIRTUAL_USERS`
- `PERF_TEST_DURATION`, `PERF_PROFILE`, `PERF_BASE_URL`, `PRODUCTION_BASE_URL`

Do not enable shell tracing while secrets are present.

## Local PR-equivalent execution

```bash
export PERF_ENVIRONMENT=Local
export PERF_PROFILE=pr
export PERF_BASE_URL=http://localhost:58080
export PRODUCTION_BASE_URL=https://api.example.invalid
bash scripts/run-performance-tests.sh
```

Release characterization, which fails closed if budgets or the baseline are not approved (both are present since 2026-09-19):

```bash
export PERF_ENVIRONMENT=Performance
export PERF_PROFILE=release
bash scripts/run-performance-tests.sh
```

## CI and scheduled execution

`.github/workflows/performance-validation.yml` runs the PR, release, and scheduled profiles. The production workflow
calls it as a mandatory reusable release job. No mandatory step uses `continue-on-error`; missing evidence causes
artifact upload or the aggregator to fail.

## Test phases

1. Validate target safety and required tools.
2. Generate ephemeral secrets and environment evidence.
3. Start PostgreSQL/PostGIS with `pg_stat_statements` and Redis.
4. Apply the real EF migration path.
5. Generate deterministic skewed data and run `ANALYZE`.
6. Build/start two API replicas and nginx; prove both response identifiers.
7. Reset PostgreSQL workload statistics.
8. Run cold and warm browse workloads.
9. Run coordinated refresh, OTP, invalid-attempt, and registration races; query database integrity after each.
10. Run seven distributed rate-limit races and window-recovery checks.
11. Capture workload statistics and representative query plans.
12. Evaluate absolute and relative thresholds, create reports, collect diagnostics, and remove all containers/volumes.

## Data and cleanup

Every property title and test profile carries the exact `PERF_RUN_ID`. Phone numbers use a dedicated prefix and
numeric suffix. Dataset distribution is deterministic from seed 20260728 and includes dense Syrian city clusters,
a sparse Berlin cluster, varied listing types/prices/areas/rooms/statuses, and main images for half the properties.
Parallel workflows are serialized by workflow concurrency and use separate Compose projects/runs. Teardown removes
the disposable PostgreSQL volume; it never runs a broad cleanup against an external database.

## Failure diagnosis

Inspect `artifacts/performance/summary.md` first, followed by scenario k6 reports, concurrency database reports,
`query-plan-analysis.json`, `postgresql/slow-queries.md`, API/nginx logs, Redis memory, and container resources.
Typical classifications are API/GC saturation, PostgreSQL plan or pool saturation, Redis/rate-limit failure,
serialization cost, lock contention, or a correctness invariant.

A failed threshold, missing report, skipped mandatory scenario, one-instance distribution, database invariant,
unapproved baseline, or cleanup failure blocks release.
