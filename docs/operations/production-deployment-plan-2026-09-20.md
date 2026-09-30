# Production deployment plan (2026-09-20)

Status: approved by the owner for execution step by step. No credentials are stored in this file;
connection strings are supplied only through process environment variables for the duration of a step.

## Baseline

- Gate: Production Gate run `35490886709` on commit `b98c243` passed every job (Staging smoke,
  observability, Redis HA, performance, database recovery). `deploy-production` is skipped on push and
  only runs from `workflow_dispatch`.
- Staging: 59 migrations applied, 0 pending, latest `20260919145936_AddShortStayListingCurrencyCode`.

## Gaps in the current path and how this plan closes them

| # | Gap | Impact | Mitigation |
|---|---|---|---|
| 1 | `deploy-production` only calls the Render Deploy Hook; the API never migrates on startup. | Code deployed before the schema fails on `ShortStayListing.CurrencyCode` and `AppReleases`. | Apply migrations **before** the deploy (step 3). |
| 2 | The Deploy Hook deploys the branch head, not necessarily the commit that passed the gate. | A merge between gate and deploy ships untested code. | Freeze merges to `master`; verify the deployed SHA afterwards (steps 1, 5). |
| 3 | Production `__EFMigrationsHistory` state is unknown. | More than the expected migrations may be pending. | Read-only inspection first (step 2). |
| 4 | The Migrator in Production mode needs Redis and JWT/OTP settings. | Migrator fails to start. | Supply them as environment variables only (step 3). |

## Steps

1. **Prepare (no change).** Confirm the latest Gate on `master` is green and record the target SHA.
   Freeze merges. Record the last known-good Render deploy id (`dep-...`) and `/health/ready`.
2. **Inspect Production (read-only).** Query `__EFMigrationsHistory`. Expected pending:
   `AddAppReleases` and `AddShortStayListingCurrencyCode`. Anything more stops the run for review
   against `ci/migration-risk-baseline.json`. Review the generated SQL: additive only, no drops, no long locks.
3. **Backup, then apply migrations (expand only).** Take and verify a fresh pre-deployment backup
   (`scripts/database/backup-postgres.sh`, `verify-backup.sh`) before any schema change. Then run
   `tools/HudhudNestApi.Migrator` with `ASPNETCORE_ENVIRONMENT=Production` and settings from environment
   variables. Verify pending = 0 and the new column/table exist. Additive migrations stay compatible with
   the running application.
4. **Run Production Gate via `workflow_dispatch` on `master`.** It performs the recovery gate with a fresh
   backup and, when every gate passes, triggers the Render Deploy Hook. Run it only after step 3.
5. **Verify.** `/health/ready` is green, deployed SHA equals the target SHA, read-only smoke of the
   properties list, short-stay endpoints and app releases, then 15 minutes of monitoring of 5xx rate,
   latency, Redis and database connectivity. No write tests that create data on Production.
6. **Rollback.**

| Situation | Action |
|---|---|
| Application defect after deploy | Actions -> *Rollback Production Application* with the recorded `dep-...`, incident id and approver; disable auto-deploy first. Migrations are additive, so no database restore is needed. |
| Migrator fails partway | Stop traffic, keep the database and logs, fix forward on a clone. Restore to a **new** database only if data is corrupted. |
| Data problem | Restore the step-3 backup to a new database; never over the live one. |

See also `application-rollback-runbook.md` and `database-migration-recovery-runbook.md`.
