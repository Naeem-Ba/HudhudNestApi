# Recovery objectives and measurement

## Approved initial objectives

- RPO: **60 minutes maximum**. No qualifying disaster may lose more than one hour of committed production data.
- RTO: **120 minutes maximum** from incident declaration to an operational, validated service with controlled traffic restoration.

The hourly backup workflow runs at minute 17. A successful dump that has not uploaded, verified, and produced a valid manifest does not count as a recovery point. A delayed or missed GitHub schedule does not silently relax the objective.

## RTO clock

The clock includes incident confirmation, backup selection/download, isolated database creation, restore, structural and data validation, application verification/startup, health/API smoke checks, application rollback or deployment, and controlled traffic restoration. DNS/provider propagation outside the automated drill must be added to real-incident measurements.

The restore workflow writes `artifacts/database-recovery/restore-drill-evidence.json` with:

- backup and drill UTC timestamps;
- backup age in minutes;
- download, restore, validation, and application smoke durations;
- total recovery duration;
- 60/120-minute targets and explicit booleans;
- restore, schema, data, and smoke results.

The workflow exits non-zero when either objective is missed. Production eligibility depends on that job, so a failed objective blocks release.

## Operational monitoring

Monitor the scheduled workflow and stored manifests for last success, backup age, size, duration, checksum, upload result, retention result, last drill, restore/validation duration, consecutive failures, RPO, and RTO. GitHub failure notifications and the optional `RECOVERY_ALERT_WEBHOOK_URL` cover executed jobs. Repository administrators must additionally configure an external dead-man monitor for the expected hourly schedule; otherwise a workflow that never starts cannot alert and the one-hour RPO is not fully guaranteed.

Any missed hourly recovery point, unreadable archive, failed upload, drill older than seven days, engine/tool version change without a new drill, or failed cleanup is a production-blocking incident.
