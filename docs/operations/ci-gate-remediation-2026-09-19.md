# CI gate remediation — Production Gate run 35431950472 (commit 7908279)

Scope: diagnosis and repair of the failing Performance, Database Recovery and Staging gates.
Every claim below comes from that run's logs or from a local reproduction.

## Diagnosis

| Gate | Failing step | Exit | Root cause | Evidence | Class |
|---|---|---|---|---|---|
| Concurrent Performance | `run-performance-tests.sh` | 1 | The first `aggregate.py` pass ran before `baseline-comparison.json` existed. With `PERF_REQUIRE_BASELINE=true` it always failed and set `test_failed=1`, even though the final pass succeeded. | All 17 k6 scenarios, 12 integrity checks and the query-plan gate were `PASS`; then `FAIL: aggregate performance evidence`, `PASS: approved baseline comparison`, `PASS: final performance evidence evaluation`. browse-cold/warm were ~30% faster than baseline. | Orchestration bug |
| Database Recovery | application-level verification | 1 | The backup is a *pre-deploy* snapshot and lacks the release's new migration `20260918114442_AddAppReleases`. The verifier required zero pending migrations, so any release with a migration failed. | Restore and PostGIS checks passed; verifier reported `pendingMigrations:[AddAppReleases]`. | Wrong evaluation logic |
| Staging Deploy + Smoke | wait for intended release | 1 | **Not a wrong branch.** The deployed SHA matched in 149 of 164 attempts. `migration.complete` stayed `false` because nothing applied migrations to the Staging database (the API never migrates on startup; Render Free has no pre-deploy command). | `commitSha=7908279… (expected 7908279…), migrationsComplete=false`; `OperationalController.cs` `complete = pending.Length == 0`. | Deployment infrastructure |
| Staging evidence upload | upload | 1 | Follow-on: smoke and observability steps were skipped, so no report files existed and `if-no-files-found: error` fired. | `No files were found with the provided path`. | Artifact path / cascade |
| Production Deployment Gate | enforce recovery validation | 1 | Follow-on from Staging and Recovery through `needs`. Behaviour is correct and unchanged. | `Staging gate: failure / Recovery gate: failure`. | Downstream |

Not causes: Build/Test/Container passed (coverage gates are staged minimums and passed; the 48.2% / 50.3% figures are
informational); Observability Export passed in its own job; `REDIS_HA_EVIDENCE_JSON` unset is a documented non-failing
skip; the Ubuntu runner migration warning is unrelated.

Auth / rate-limit error classification: the scenarios gate on dedicated counters (`server_errors rate==0`, exact
success/rejection counts, `security_invariant_failures==0`). 400/401/409/429 are expected rejections; only 5xx,
timeouts and transport errors are real. No 5xx or timeout occurred. The misleading part was reporting: k6's
`http_req_failed` counts every 4xx as a failure.

## Changes

| Area | File | Change |
|---|---|---|
| Performance | `scripts/run-performance-tests.sh` | First aggregate pass runs with `PERF_REQUIRE_BASELINE=false` (every other check still enforced); the final pass is unchanged and strict. |
| Performance reporting | `performance/load-tests/lib/reporting.js`, `performance/reporting/aggregate.py` | Report `unexpectedErrorRate` (5xx/transport) next to the non-2xx rate in markdown and CSV. Thresholds untouched. |
| Recovery | `tools/PropertyApi.DatabaseRecoveryVerifier/Program.cs`, `database-restore-drill.yml`, `run-local-restore-drill.sh` | Fail on unknown applied migrations; rehearse pending migrations on the disposable copy (opt-in `RECOVERY_APPLY_PENDING_MIGRATIONS=true`, loopback host only); then require zero pending. Report records before/after. |
| Staging | `production-gate.yml` | New step applies migrations to Staging before the deploy hook (secret `STAGING_DATABASE_URL`, refuses DB names without `staging`, masks the value). |
| Staging evidence | `production-gate.yml` | `staging-release-verification.json` (expected vs deployed SHA, branch, migration state, readiness, timestamps). Failure message now separates SHA mismatch from unapplied migrations. |
| Artifacts | `production-gate.yml`, `scripts/verify-observability.sh` | `if: always()` step writes failure-status smoke and observability reports when missing; observability script leaves a failure report on early abort. |
| Docs | runbook, coverage plan | `STAGING_DATABASE_URL` and evidence documented; coverage figures documented as informational. |

Thresholds, scenarios, `continue-on-error` and exit-code handling were not weakened anywhere.

## Verification performed

- Recovery verifier on a local PostgreSQL 18 scratch database (AppReleases dropped to mimic a pre-deploy backup):
  without the opt-in, `FAIL`; with it, `PASS` (`pendingMigrationsBeforeRehearsal=[AddAppReleases]`, `ran=true`, pending `[]`).
- Staging database was at 57 migrations (latest `20260912130714_…`); `tools/PropertyApi.Migrator` applied exactly
  `20260918114442_AddAppReleases` (now 58). `GET /health/ready` on Staging returned 200.
- Evidence step, verification-JSON writer and the observability abort trap were run locally.
- `bash -n` on every `run:` block of the `staging-smoke` job; `PropertyApi.Architecture.Tests` (164) and
  `PropertyApi.Performance.Tests` (10) pass.

## Not yet proven

- A full Production Gate run on this branch (the k6 stack could not run locally: Docker was unavailable).
- The new Staging migration step in CI: requires the `STAGING_DATABASE_URL` secret on the `staging` environment.
- Production was not touched. Deploying it remains a separate, explicit decision after Staging and Recovery pass.

## Rollback

All changes are CI/tooling only. Revert the commit to restore the previous behaviour. The applied Staging migration
(`AppReleases`, additive, empty table) can be reverted with `dotnet ef database update 20260912130714_AddValuationNotificationIdempotencyAndReminderStamps`
against the Staging database if ever required.
