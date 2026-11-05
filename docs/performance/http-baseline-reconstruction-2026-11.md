# HTTP performance baseline reconstruction (2026-11)

Status: **advisory tooling and findings only.** Nothing here changes `approved-baseline.json`,
`performance-budgets.json`, `compare.py` or any gate. Replacing the approved baseline or the
thresholds is a policy change (see `performance-baseline-policy.md`) and needs the owner and a
performance/SRE reviewer.

## What was found

1. **The approved `browse-cold` p95 (418.59 ms) and today's p95 (3-5 ms) are not the same measurement.**
   Same k6 metric (`http_req_duration` p95), same unit, same script and load shape, but the server
   changed: since #254 (2026-10-02) and #273 (2026-10-03) `GET /api/properties`, `/{id}` and
   `/geo-search` are served from the output cache. `browse.js` repeats about ten URLs for the whole
   run, so 99.7% of its requests are cache hits that execute no SQL (`X-Database-Command-Count: 0`).
   Across 176 master runs the median `browse-cold` p95 went 406.6 ms (no cache) -> 27.6 ms
   (partial) -> 3.6 ms (cached); the .NET 10 upgrade did not move it (3.58 -> 3.70 ms).
2. **The relative gate cannot see regressions against that baseline.** It fails only on increases,
   and the limit is 418.59 x 1.15 = 481 ms against a real value of about 4 ms, i.e. a regression of
   about 115x. A 99% improvement passes silently, so nothing flagged the stale baseline.
3. **Re-baselining the cached path with the same 15% limit would flap.** Across 60 .NET 10 runs on
   GitHub-hosted runners the cached p95 has a CV of 17%; simulated, a 15% single-run rule fails
   14-25% of runs with no change.
4. **The database path on a hosted runner is unstable between jobs but stable inside one job.**
   Seven `browse-diverse` runs in different jobs: p95 100-242 ms (CV 34%). Three back-to-back runs in
   one job: CV 0.9% and 5.1%. Within-job sigma (log) 0.037 vs between-job 0.339. Comparing a job
   against a stored value therefore cannot detect anything below roughly 60-80%.

Raw data, the report in Arabic and the candidate files (all `pending-approval`) were delivered with the
thread; the per-run summaries are reproducible with the tools below.

## Tools added (all opt-in)

- `performance/load-tests/scenarios/browse-diverse.js` - the browse mix with unique cache keys
  (`minArea=<unique>` no-op filter for lists, random id pool for details, jittered centre for geo).
  Measures the PostgreSQL path. Writes `artifacts/performance/study/browse-diverse-<run>.json`
  (outside `k6/`, so the gate never reads it) and prints a `STUDY_SUMMARY` line.
- `scripts/run-performance-tests.sh`: with `PERF_ADVISORY_DIVERSE_REPEATS=N` (default 0 = off) runs that
  scenario N times after the gate's browse runs. A failure there cannot fail the gate.
- `performance/reporting/baseline_study.py` (standard library only):
  - `summarize` - steady-state-only percentiles from a k6 `--out json` file (warm-up excluded);
  - `build-candidate` - N runs -> candidate baseline with spread, bootstrap CI, runs needed for +/-5%,
    `valid` / `invalidReasons`, status `pending-approval`;
  - `compare` - run-level (log-scale) one-sided t test with a minimum practical effect; returns
    `REGRESSION`, `IMPROVEMENT`, `NO_SIGNIFICANT_CHANGE` or `INCONCLUSIVE` with the smallest
    regression the data could have detected, instead of guessing;
  - `paired` - same-job A/B (baseline commit = arm A, candidate = arm B, order ABBA) with a drift check;
  - `sensitivity` - Monte-Carlo detection rates for synthetic regressions of 0/3/5/8/10/15/25%.
- Tests: `python3 -m unittest discover -s performance/reporting/tests` (23 tests).

## Recommended next step (not implemented here)

Compare a PR against `master` **inside one job**: build both commits, run the diverse workload
ABBA with 5 repetitions per arm on the same runner, and judge the ratio with `paired`. From the
measured within-job variance the smallest detectable regression is about 9.7% with 3 repetitions per
arm and 6.6% with 5; 3-5% stays below what hosted runners can resolve. This needs a second API image
per job and a decision on whether it may block a merge, so it is left for review.
