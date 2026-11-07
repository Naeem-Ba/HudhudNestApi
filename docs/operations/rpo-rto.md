# Recovery objectives and measurement

## Approved initial objectives

- RPO: **24 hours maximum** (relaxed from 60 minutes on 2026-09-28 at the owner's decision: the backup now runs once a day). A qualifying disaster may lose up to one day of committed production data.
- RTO: **120 minutes maximum** from incident declaration to an operational, validated service with controlled traffic restoration.

The daily backup workflow runs at 03:17 UTC. A successful dump that has not uploaded, verified, and produced a valid manifest does not count as a recovery point. A delayed or missed GitHub schedule does not silently relax the objective.

## One decision, three thresholds (BKP-001)

This file is the only source for the objective; every other document links here and must not restate a different number.

| Level | Value | Where it is enforced |
|---|---|---|
| Objective (RPO) | 24 hours | Daily backup at 03:17 UTC (`database-backup.yml`) |
| Machine threshold | 25 hours (`RPO_TARGET_SECONDS=90000`) | `verify-backup.sh`, drill report; the 1 h margin absorbs GitHub's late start of scheduled runs (observed 5-8 h, 2026-09 to 2026-11) |
| External alert | 28 hours without a success heartbeat (period 24 h + grace 4 h; the grace covers the day-to-day jitter of GitHub's start time) | Healthchecks.io check (see runbook), independent of GitHub |
| RTO | 120 minutes | Drill report |

History that justifies the external alert: between 2026-10-18 and 2026-10-24 no backup succeeded for 144 hours and nothing alerted (QA phase 19).

## RTO clock

The clock includes incident confirmation, backup selection/download, isolated database creation, restore, structural and data validation, application verification/startup, health/API smoke checks, application rollback or deployment, and controlled traffic restoration. DNS/provider propagation outside the automated drill must be added to real-incident measurements.

The restore workflow writes `artifacts/database-recovery/restore-drill-evidence.json` with:

- backup and drill UTC timestamps;
- backup age in minutes;
- download, restore, validation, and application smoke durations;
- total recovery duration;
- the RPO (25 h machine threshold) and RTO (120 min) targets and explicit booleans;
- restore, schema, data, and smoke results.

The workflow exits non-zero when either objective is missed. Production eligibility depends on that job, so a failed objective blocks release.

## Operational monitoring

Monitor the scheduled workflow and stored manifests for last success, backup age, size, duration, checksum, upload result, retention result, last drill, restore/validation duration, consecutive failures, RPO, and RTO. GitHub failure notifications and the optional `RECOVERY_ALERT_WEBHOOK_URL` cover executed jobs. Repository administrators must additionally configure an external dead-man monitor for the expected daily schedule; otherwise a workflow that never starts cannot alert and the 24-hour RPO is not fully guaranteed.

Any missed daily recovery point, unreadable archive, failed upload, drill older than seven days, engine/tool version change without a new drill, or failed cleanup is a production-blocking incident.

## Images and files (BKP-005)

The database backup does not contain photos or documents: they live in Cloudinary, and the database holds only their URLs. Decision 2026-11-07 (owner): there is no real user data in the program yet, so the risk is **accepted for now** and nothing is exported.

What this means in a disaster: restoring the database brings back every listing record, but a photo is lost if the Cloudinary account itself is lost or the file is deleted there. Cloudinary's own durability protects against disk failure, not against account loss, mistaken deletion or a bad bulk operation.

Before the first real photo is accepted from a public user, this entry must be revisited and one of these chosen: (a) a weekly job that lists the assets through the Admin API and copies new ones to the backup bucket, or (b) a written acceptance of the risk by the owner at that point. Staging and Production share one Cloudinary account (folder isolation only), so a Staging clean-up script can touch Production files; keep any bulk delete folder-scoped.
