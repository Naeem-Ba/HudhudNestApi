# Recovery drill evidence - 2026-07-28

## Scope and decision

An end-to-end recovery drill ran on 2026-07-28 against an isolated local Docker environment using the repository-pinned `postgis/postgis:16-3.4` image. It exercised migration, seed, native backup, encryption, integrity verification, restore, database validation, application validation, API smoke tests, and destructive cleanup.

This is valid local/CI-equivalent evidence. It is not evidence that the production backup bucket, GitHub environments, scheduler, alerting, or Render rollback are configured or operational.

## Command

```bash
SKIP_RECOVERY_IMAGE_BUILD=true bash scripts/database/run-local-restore-drill.sh
```

The recovery toolbox, migrator, and restored API images had already been built from the current working tree before this run.

## Immutable identifiers

- Backup ID: `local-recovery-drill-20260728T192732Z`
- Backup creation: `2026-07-28T19:30:24Z`
- Drill window: `2026-07-28T19:27:33Z` to `2026-07-28T19:32:07Z`
- Archive SHA-256: `8def7360f4f1939b065e6c19a4716e060d94cd0e2539a83aa5b421361ec47692`
- Latest EF migration: `20260716192200_AddPhonePasswordAuthenticationAndReverification`
- Target database: `propertyapi_restore_drill`
- Source commit metadata: `unknown` because this was an elevated local run; production workflows inject `GITHUB_SHA`.

## Measured results

| Measurement | Actual | Target | Result |
|---|---:|---:|---|
| Backup age at recovery evidence | 1.72 minutes | <= 60 minutes | PASS |
| Native backup duration | 16 seconds | Observed | PASS |
| Restore duration | 28 seconds | Observed | PASS |
| Database validation duration | 4 seconds | Observed | PASS |
| Application smoke duration | 13 seconds | Observed | PASS |
| Total recovery duration | 274 seconds (4.57 minutes) | <= 120 minutes | PASS |

The local download duration was zero because the encrypted archive was produced locally. A production-storage drill must measure object-store discovery and download time.

## Data, schema, and application proof

- Encrypted PostgreSQL custom archive decrypted successfully and `pg_restore --list` returned 264 TOC entries.
- The encrypted archive checksum matched before restore.
- Exact source-versus-target row counts matched for all 20 required tables. Non-empty representative rows included 5 currencies, 14 governorates, 8 districts, 13 property types, and 17 EF migration records.
- Schema validation found 33 primary keys, 34 foreign keys, 14 valid non-primary unique indexes, no invalid/unready indexes, no unvalidated check/foreign-key constraints, no foreign-key orphans, and no sequences behind their owning columns.
- PostGIS 3.4.3 loaded successfully and the spatial distance probe returned 130.35 metres.
- The application verifier constructed the real `AppDbContext`, reported no pending migrations, materialized critical entities without sensitive plaintext output, and returned PASS.
- The restored API reached `/health/ready`; a read-only `GET /api/properties?page=1&pageSize=1` smoke request succeeded.
- Compose volumes, containers, and network were removed; `cleanup-result.json` reported PASS.

## Evidence files

Machine-readable files are produced under the gitignored `artifacts/database-recovery/` directory:

- `restore-drill-evidence.json`
- `database-verification.json`
- `application-verification.json`
- `backup-verification.json`
- `restore-result.json`
- `cleanup-result.json`
- `local-recovery-drill-20260728T192732Z.manifest.json`

The encrypted archive is deliberately excluded from GitHub artifact publication. Production backup storage must retain the archive and manifest under the documented lifecycle policy.

## Remaining production proof

Production readiness remains blocked until a protected GitHub run restores an encrypted archive downloaded from the configured private object store, production schedule/monitoring is proven, and the protected Render rollback is exercised and reviewed.
