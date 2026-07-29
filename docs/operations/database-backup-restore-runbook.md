# PostgreSQL backup, restore, and disaster recovery runbook

## Scope and success criteria

This runbook covers PropertyApi PostgreSQL 16/PostGIS 3.4. Success requires a non-empty encrypted custom archive in durable storage, matching checksum, readable TOC, restore to a separate disposable host, exact critical-table counts, valid constraints/indexes/sequences, matching EF migration and PostGIS version, EF/application verification, API readiness/read smoke, RPO/RTO PASS, and successful target cleanup.

Never point a restore command at production. Never upload `.dump`, `.dump.gpg`, decrypted data, connection strings, or secret-bearing logs as GitHub artifacts.

## Architecture

`database-backup.yml` runs hourly, invokes a PostgreSQL 16 toolbox, creates a custom archive and separate globals file, validates it, encrypts both with GPG AES-256, uploads to private S3-compatible storage with SSE/KMS, and applies tiered retention. `database-restore-drill.yml` downloads or creates a valid backup, restores it into a disposable PostgreSQL 16/PostGIS 3.4 service, validates SQL and the real `AppDbContext`, starts the API in `RecoveryDrill`, performs read-only checks, measures objectives, and destroys the environment.

Globals are separate and omit role passwords. Managed PostgreSQL often forbids role creation; restore them only with a reviewed privileged procedure. Database restore does not depend on globals in the drill.

## Required tools and secrets

Local tools: Docker with Compose v2, .NET SDK 8, curl, and jq. The recovery toolbox supplies PostgreSQL 16 clients, Python, GPG, and AWS CLI. See `required-recovery-secrets.md` for GitHub configuration.

Storage must use HTTPS/TLS, private access, least-privilege credentials limited to the backup prefix, access logging, public-access block, versioning, and preferably object lock. The scripts cannot provision those provider controls.

## Manual production backup

Use a secure host. Values below are placeholders and must not be pasted into tickets or logs.

```bash
export DATABASE_URL='<PRODUCTION_DATABASE_URL>'
export BACKUP_ENCRYPTION_KEY='<BACKUP_ENCRYPTION_KEY>'
export BACKUP_STORAGE_URI='s3://<PRIVATE_BUCKET>/<PREFIX>'
export AWS_ACCESS_KEY_ID='<BACKUP_ACCESS_KEY>'
export AWS_SECRET_ACCESS_KEY='<BACKUP_SECRET_KEY>'
export BACKUP_AWS_REGION='<REGION>'
export BACKUP_REQUIRE_REMOTE_UPLOAD=true
export APPLICATION_COMMIT_SHA='<GIT_COMMIT_SHA>'
bash scripts/database/backup-postgres.sh
```

A successful run ends with a PASS result and three remote objects: `.dump.gpg`, `.globals.sql.gpg`, and `.manifest.json`. The raw archive is removed by the exit trap.

## Verify or download a backup

```bash
export BACKUP_STORAGE_URI='s3://<PRIVATE_BUCKET>/<PREFIX>'
export BACKUP_OUTPUT_DIR="$PWD/artifacts/database-backup-download"
bash scripts/database/download-latest-backup.sh

export BACKUP_FILE="$PWD/artifacts/database-backup-download/backup.dump.gpg"
export MANIFEST_FILE="$PWD/artifacts/database-backup-download/manifest.json"
export BACKUP_ENCRYPTION_KEY='<BACKUP_ENCRYPTION_KEY>'
bash scripts/database/verify-backup.sh
```

Verification checks checksum, metadata, age, decryption, TOC, every captured critical table, migration history, and PostGIS objects. Do not override `ENFORCE_BACKUP_AGE=true` for a qualifying recovery point.

## Isolated restore

Use a target on a different host from production. Both URIs must be PostgreSQL URIs; the admin URI connects to a maintenance database.

```bash
export PRODUCTION_DATABASE_URL='<PRODUCTION_DATABASE_URL>'
export RESTORE_ADMIN_DATABASE_URL='postgresql://<RECOVERY_USER>:<PASSWORD>@<RECOVERY_HOST>:5432/postgres?sslmode=require'
export RESTORE_TARGET_DATABASE_URL='postgresql://<RECOVERY_USER>:<PASSWORD>@<RECOVERY_HOST>:5432/propertyapi_restore_drill_<ID>?sslmode=require'
export RESTORE_TARGET_ENVIRONMENT='recovery-drill'
export RESTORE_TARGET_DISPOSABLE=true
export ALLOW_DESTRUCTIVE_RESTORE=true
export BACKUP_FILE='<ENCRYPTED_ARCHIVE_PATH>'
export MANIFEST_FILE='<MANIFEST_PATH>'
export BACKUP_ENCRYPTION_KEY='<BACKUP_ENCRYPTION_KEY>'
bash scripts/database/restore-postgres.sh
bash scripts/database/verify-restored-database.sh
```

The script refuses a target on the production host, the production identity, a name without `restore_drill`, a non-disposable target, or missing explicit flags. It creates and verifies PostGIS, then uses `--clean --if-exists --exit-on-error` only after these guards.

Run application validation without printing rows:

```bash
export ConnectionStrings__DefaultConnection='<RECOVERY_ADONET_CONNECTION>'
dotnet run --project tools/PropertyApi.DatabaseRecoveryVerifier/PropertyApi.DatabaseRecoveryVerifier.csproj --configuration Release
```

Cleanup is explicit and has the same guards:

```bash
bash scripts/database/cleanup-restore-target.sh
```

## Reproducible local drill

```bash
export BACKUP_ENCRYPTION_KEY='<LOCAL_ONE_TIME_KEY>'
bash scripts/database/run-local-restore-drill.sh
```

This creates a representative migrated/seeded source, performs a real encrypted backup/restore in separate PostGIS containers, validates, starts the API against restored data, writes evidence, and tears down volumes.

## Full disaster recovery

1. Declare the incident, owner, UTC start, and traffic/maintenance decision. Pause Render auto-deploy.
2. Preserve the failed database; do not restore over it. Capture provider/database/application logs and migration state.
3. Select the newest manifest inside RPO and verify checksum/TOC.
4. Provision a new isolated PostgreSQL 16/PostGIS 3.4 database with sufficient capacity and private networking.
5. Restore and run both SQL and .NET validation. Record all timings.
6. Decide forward-fix versus restore using the migration runbook. For destructive data loss, use the verified pre-deployment backup and a new database.
7. Roll back/redeploy the previous known-good Render deploy. Change the secure database configuration to the recovered database only after validation.
8. Run `/health/live`, `/health/ready`, a public property list/read, authentication-schema query via the verifier, PostGIS query, and log review. Do not run write or third-party side-effect tests.
9. Restore traffic gradually; watch errors, latency, saturation, database connections, Redis, and business reads.
10. Record the actual data-loss window/RPO, RTO, backup/deploy IDs, approver, evidence links, and follow-up actions.

## Retention and monitoring

`apply-retention.sh` keeps all recovery points for 48 hours, then the newest daily point for 30 days, weekly point for 12 weeks, and monthly point for 12 months. It is a dry run unless `RETENTION_APPLY=true`. Bucket version lifecycle/object lock can delay physical deletion and must be audited separately.

Respond immediately to missing/empty backup, checksum/TOC failure, upload failure, age over 60 minutes, restore/schema/data/smoke failure, RTO over 120 minutes, or cleanup failure. Preserve sanitized JSON/log evidence and use the optional secure webhook or GitHub notifications. Escalate to the incident commander and database owner on any potential data loss, production-host guard, partial migration, key-loss risk, or target cleanup failure.

## Troubleshooting

- Client/server mismatch: rebuild the pinned toolbox for the confirmed production major; do not force a mismatched `pg_dump`.
- PostGIS create/restore failure: verify the target image/provider extension and privileges before retrying.
- Checksum mismatch: quarantine the object and select another verified manifest; never restore it.
- `pg_restore` error: preserve stderr and the target, create a new target for the next reviewed attempt.
- Row/constraint mismatch: stop; do not route traffic. Compare manifest counts and restore logs.
- GPG failure: verify the secret version and backup key rotation record. Never print the key.
- Object upload/retention failure: keep local encrypted files only on the secured operator host until upload succeeds, then securely remove them.
