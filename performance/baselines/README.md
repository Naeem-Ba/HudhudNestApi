# Approved performance baselines

`approved-baseline.json` is the median of three comparable measured runs of the `release` profile
(Production Gate, 2 API instances, 25000 properties, identical dataset manifest hash). Its `provenance`
section lists the source runs and commits.

The `staging` profile (100000 properties) is a different, non-comparable dataset -- see
`docs/performance/performance-baseline-policy.md`'s comparability rules -- so it is never compared
against this file. `scripts/run-performance-tests.sh` points it at `approved-staging-baseline.json`
instead, which does not exist yet (BUG-35): no staging-profile baseline has ever been built, so the
comparison step fails closed with a clear "baseline is missing" error until one is. Follow the same
"Updating the baseline" procedure below with three successful `staging`-profile runs to create it.

It was generated from real CI measurements and approved by the repository owner. Per
`docs/performance/performance-baseline-policy.md`, a separate performance/SRE review is still required.
Never edit it to make a failing run pass; replace it only through the update procedure in that policy.

Relative comparison (`performance/reporting/compare.py`) skips scenarios with fewer than
`minimumSampleRequests` requests and ignores database-time deltas below
`minimumDatabaseTimeDeltaMilliseconds` (both in `performance-budgets.json`), because such samples are
noise-dominated. Security invariants, race checks and k6 thresholds are unaffected.
