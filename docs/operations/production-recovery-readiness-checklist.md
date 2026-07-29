# Production recovery readiness checklist

A checked repository-control item is not proof that its external configuration or drill passed. Attach evidence and reviewer/date to every operational item.

## Backup and storage

- [x] Native PostgreSQL custom-format backup implemented.
- [x] Hourly schedule declared for the 60-minute RPO.
- [ ] Hourly schedule and independent missed-run monitor proven in production.
- [ ] Private durable object storage provisioned and upload proven.
- [x] Client-side encryption and storage SSE/KMS support implemented.
- [x] SHA-256 and archive TOC verification implemented.
- [x] 48-hour/30-day/12-week/12-month retention implemented.
- [ ] Bucket versioning, immutability, public-access block, TLS-only policy, and access logging audited.

## Restore and validation

- [x] Isolated PostgreSQL 16/PostGIS 3.4 restore environment implemented.
- [x] Production/source host, identity, disposable target, name, and explicit-action guards implemented.
- [x] Local end-to-end restore drill evidence attached (2026-07-28); production-storage drill remains required.
- [x] PostGIS restore/version and spatial query passed in the local drill.
- [x] Schema, PK/FK/unique/index validation passed in the local drill.
- [x] Exact counts for 20 critical tables, orphan checks, and sequence checks passed in the local drill.
- [x] EF pending-migration/application DbContext verification passed in the local drill.
- [x] API readiness and read-only property smoke passed in the local drill.
- [x] Disposable target cleanup passed in the local drill.
- [x] Measured local RPO <= 60 minutes (1.72 minutes).
- [x] Measured local total RTO <= 120 minutes (4.57 minutes).

## Deployment and migration recovery

- [x] Historical migrations classified and new risk gate implemented.
- [x] Destructive migration restore/forward-fix decision documented.
- [x] Fresh pre-deployment backup and full restore are production gate dependencies.
- [x] Gate fails when recovery is failed or unexpectedly skipped.
- [ ] Render configured to wait for `Production Deployment Gate`.
- [ ] Previous immutable Render deploy retention confirmed.
- [x] Protected executable application rollback workflow implemented without DB downgrade.
- [ ] Rollback exercised on Render and evidence attached.
- [ ] Protected GitHub environment approvals configured.
- [ ] Break-glass process, if organization adopts one, separately approved and audited; no bypass exists by default.

## Operations

- [x] Backup/restore, migration, rollback, RPO/RTO, assessment, and secrets runbooks present in the recovery change set.
- [ ] GitHub failure notifications enabled for operators.
- [ ] Optional incident webhook tested, or documented decision to rely on GitHub plus external monitor.
- [ ] Runbooks peer reviewed through a tabletop exercise.
- [ ] All required secrets configured and rotation/ownership recorded.
- [ ] P0 M-10 evidence reviewed and formally closed.

Final approval requires every unchecked production item above either completed or covered by a time-bounded, formally accepted production-blocking decision. No silent exception is permitted.
