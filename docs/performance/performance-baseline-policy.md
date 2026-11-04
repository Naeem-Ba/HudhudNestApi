# Performance baseline policy

## Approval

An approved baseline must be built from at least three successful repetitions of the same profile. The median run is
stored as `performance/baselines/approved-baseline.json`. Approval requires review by a service owner and a
performance/SRE reviewer. A baseline replacement in the same change as an observed regression must not be approved
without an explicit explanation and evidence.

`performance/performance-budgets.json` remains `approved: false` until measured evidence exists. Release and
scheduled profiles require both approved budgets and an approved baseline; missing files fail closed.

## Comparability

Comparisons require the same:

- dataset manifest hash and deterministic seed;
- API instance count and instance resource limits;
- PostgreSQL/PostGIS and Redis major versions;
- performance profile and load shape;
- connection-pool configuration;
- cold/warm cache policy.

Runs with materially different metadata are informative but not release-comparable.

## Blocking metrics

p95, p99, minimum throughput, HTTP errors, 5xx errors, failed checks, security invariants, rate-limit overshoot,
query-count budgets, database duration, spatial-index use, temporary blocks, and large-table sequential scans are
release blocking. CPU, memory, cache ratios, and capacity saturation are advisory until stable environment-specific
budgets are approved.

Relative gates fail when p95 regresses more than 15%, p99 more than 20%, throughput falls more than 15%, database
time grows more than 20%, or buffer reads grow more than 25%. These initial policies cannot be loosened merely to
make a failing release pass.

## Noise floor for relative comparison

`performance/reporting/compare.py` applies the relative gates only where they carry signal, using two values in
`performance-budgets.json` (`relativeRegression`):

- `minimumSampleRequests` (100): scenarios with fewer requests, in the current run or in the baseline, are skipped
  by the latency/throughput comparison. Race and rate-limit scenarios issue 3-31 requests; across three identical
  release-profile runs their p95/p99 drifted by up to 114%, while `browse-cold/warm` (2000+ requests) drifted by at
  most 5.8%. They remain gated by their own k6 thresholds, database invariants and security invariants.
- `minimumDatabaseTimeDeltaMilliseconds` (1): a query-plan execution-time increase below this absolute delta is not a
  regression (sub-millisecond plans varied by 40% between identical runs). Buffer-read, sequential-scan,
  temporary-block and spatial-index checks are unchanged.

Raising either value, or lowering the percentage limits, is a policy change and needs the same approvals as a baseline.

## Variance and reruns

Warm-up is excluded from custom steady-state endpoint metrics. A suspected noisy result is rerun three times in the
same environment; the median determines the result. A security invariant, 5xx response, missing instance, missing
artifact, or database-plan regression is never dismissed as an outlier.

The query-plan capture applies that rule itself: `scripts/capture-query-plans.sh` executes every plan 9 times
(`PERF_QUERY_PLAN_SAMPLES`), in rounds over all plans with a 2 s pause between rounds
(`PERF_QUERY_PLAN_SAMPLE_PAUSE_SECONDS`), and keeps the median-execution-time run, so its plan, node types and buffer counts
are what the comparison sees. Two release runs failed only on query-plan execution time with an identical plan (same index
scan, same buffer hits, 0 reads) while the same commit tree had passed minutes earlier:

- run 35448089286: `property-list-deep-page` 5.249 ms -> 6.938 ms (+32%, delta 1.7 ms), 1 sample per plan;
- manual Production Gate 41896748419 (2026-11-04, commit fd3a25c): `property-geo-radius` 8.344 -> 10.66 ms (+28%, delta 2.3 ms)
  and `property-list-deep-page` 5.249 -> 6.905 ms (+32%, delta 1.7 ms), buffer-read regression 0% on all plans, and all five
  plans slower by 24-54% at the same moment, i.e. most likely one burst of runner slowness (inferred: only the final medians are logged). The 3
  back-to-back samples used until then run within a fraction of a second of each other, so one burst covers all of them
  and the median cannot help. The same commit had passed on the push run 30 minutes earlier.

A single sample of a ~5 ms plan on a shared 2-vCPU runner is that noisy, and back-to-back samples share the noise, hence
the spread-out rounds. Percentage limits and `minimumDatabaseTimeDeltaMilliseconds` are unchanged.

## Updating the baseline

1. Run the release or Staging profile three times without changing infrastructure.
2. Verify dataset and environment fingerprints are identical.
3. Review root summaries, PostgreSQL statistics, plans, resource diagnostics, and correctness checks.
4. Select the median comparable run.
5. Record commit, application version, environment versions, dataset hash, profile, instance count, and timestamp.
6. Obtain the two required approvals.
7. Change `approved` metadata and add the baseline in a dedicated reviewed commit.

The automation never overwrites an approved baseline.
