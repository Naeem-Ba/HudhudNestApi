# Application rollback runbook (Render)

## Model

Database and application rollback are independent. PropertyApi uses Render externally and the repository had no container registry/publish pipeline. The implemented rollback therefore targets an immutable Render deploy ID (`dep-...`) whose retained build artifact and commit are known. It never executes EF migration downgrade.

Render must be configured to wait for the GitHub `Production Deployment Gate` check on `main`/`master`. The protected `production-release`, `production-recovery`, and `production-rollback` GitHub environments require reviewers. Auto-deploy must be paused during an incident so the bad commit is not immediately redeployed.

## Prerequisites

- Incident ID, named approver, reason in the incident record, target known-good deploy ID, and database compatibility decision.
- `RENDER_API_KEY`, `RENDER_SERVICE_ID`, and `PRODUCTION_BASE_URL` secrets.
- The previous deploy is `live` or `deactivated`, retained by Render, and compatible with the current schema.
- If a destructive migration completed, first execute the new-database recovery procedure; do not start with application rollback against an incompatible schema.

## Execute

1. Stop/hold the failed rollout and disable Render auto-deploy.
2. Preserve failed deploy logs, current deploy ID/commit, health results, database migration ID, and backup ID.
3. Open **Actions â†’ Rollback Production Application â†’ Run workflow**.
4. Enter the target `dep-...`, incident ID, approver, and confirm auto-deploy is disabled.
5. Obtain approval from the protected `production-rollback` environment.
6. The workflow verifies the target through Render, calls the official rollback endpoint, polls the new deploy to `live`, checks readiness and a read-only properties request, and uploads evidence.
7. Review logs and database connectivity, then restore traffic and re-enable auto-deploy only after the bad commit is removed/reverted.

Equivalent secured-host command:

```bash
export RENDER_API_KEY='<RENDER_API_KEY>'
export RENDER_SERVICE_ID='srv-<SERVICE_ID>'
export TARGET_DEPLOY_ID='dep-<KNOWN_GOOD_DEPLOY>'
export PRODUCTION_BASE_URL='https://<PRODUCTION_API_HOST>'
export INCIDENT_ID='<INCIDENT_ID>'
export APPROVED_BY='<APPROVER>'
export CONFIRM_AUTODEPLOY_DISABLED=true
bash scripts/deployment/rollback-application.sh
```

## Automatic rollback policy

No unattended rollback is enabled because the existing production deploy is provider-controlled and schema compatibility cannot be inferred safely. Failed health, readiness, critical smoke, or migration verification blocks the eligibility gate; an operator then runs the protected rollback. This is fail-closed and prevents an automatic unsafe database downgrade.

Success means the rollback deploy is `live`, readiness and public read smoke pass, logs show stable database connectivity, traffic is controlled, evidence records both deploy IDs, and the incident record is updated. A failed rollback stays in maintenance/traffic-off mode and escalates to Render support plus the database/application owners.
