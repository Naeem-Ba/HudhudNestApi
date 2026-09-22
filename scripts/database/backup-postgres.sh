#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env DATABASE_URL
require_env BACKUP_ENCRYPTION_KEY
for command in pg_dump pg_dumpall pg_restore psql gpg jq python3; do
  require_command "${command}"
done

OUTPUT_DIR="${BACKUP_OUTPUT_DIR:-${REPOSITORY_ROOT}/artifacts/database-backup}"
mkdir -p "${OUTPUT_DIR}"

WORK_DIR="$(create_private_temp_dir)"
cleanup() {
  rm -rf -- "${WORK_DIR}"
}
trap cleanup EXIT INT TERM
configure_gpg_home "${WORK_DIR}"

configure_pg_environment "${DATABASE_URL}"
assert_postgres_client_matches_server
log "Source identity: host=${PGHOST}, port=${PGPORT}, database=${PGDATABASE}."

BACKUP_STARTED_EPOCH="$(date -u +%s)"
BACKUP_CREATED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
BACKUP_ID="${BACKUP_ID:-propertyapi-$(date -u +%Y%m%dT%H%M%SZ)}"
RAW_DUMP="${WORK_DIR}/${BACKUP_ID}.dump"
RAW_GLOBALS="${WORK_DIR}/${BACKUP_ID}.globals.sql"
ARCHIVE_FILE="${OUTPUT_DIR}/${BACKUP_ID}.dump.gpg"
GLOBALS_FILE="${OUTPUT_DIR}/${BACKUP_ID}.globals.sql.gpg"
MANIFEST_FILE="${OUTPUT_DIR}/${BACKUP_ID}.manifest.json"
PASSPHRASE_FILE="${WORK_DIR}/encryption-passphrase"
printf '%s' "${BACKUP_ENCRYPTION_KEY}" > "${PASSPHRASE_FILE}"

DATABASE_NAME="$(psql -X -v ON_ERROR_STOP=1 -tAc 'SELECT current_database();' | tr -d '[:space:]')"
POSTGRES_VERSION="$(psql -X -v ON_ERROR_STOP=1 -tAc 'SHOW server_version;' | xargs)"
POSTGRES_CLIENT_VERSION="$(pg_dump --version | xargs)"
POSTGIS_VERSION="$(psql -X -v ON_ERROR_STOP=1 -tAc 'SELECT PostGIS_Lib_Version();' | xargs)"
LATEST_MIGRATION="$(psql -X -v ON_ERROR_STOP=1 -tAc 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;' | xargs)"
APPLICATION_COMMIT_SHA="${APPLICATION_COMMIT_SHA:-${GITHUB_SHA:-unknown}}"

# The production database is Supabase-hosted. Supabase auto-provisions a
# "supabase_vault" extension (schema "vault") on every project that
# HudhudNestApi's application code never uses. Confirmed via two separate
# real Recovery Gate runs against production (2026-09-02, runs
# 33652899781 and 33659828056): the dump's `CREATE EXTENSION IF NOT
# EXISTS supabase_vault WITH SCHEMA vault;` statement makes pg_restore
# fail closed, because that extension has no control file outside
# Supabase's own managed PostgreSQL fork and can never be installed on
# the drill's disposable, non-Supabase PostgreSQL instance.
#
# Two earlier attempts at a fix were each tried and disproved by a real
# CI run before this one:
#   - `--exclude-schema=vault` (run 33659828056) did NOT work: pg_dump's
#     --exclude-schema filters regular schema-owned relations, not
#     extension objects, so supabase_vault's CREATE EXTENSION statement
#     was dumped regardless and pg_restore failed on it again, identically.
#   - `--schema=public` (run 33657896745) was too broad: production's
#     PostGIS extension is itself installed in a schema other than
#     "public" (most likely Supabase's default "extensions" schema), so
#     that allow-list silently dropped PostGIS's own CREATE EXTENSION
#     entry too. verify-backup.sh's existing `EXTENSION .*postgis` TOC
#     check correctly caught this during backup creation, before any
#     upload or restore was attempted.
#
# `--exclude-extension=<pattern>` is the option actually designed for
# this: unlike --exclude-schema, it targets the extension itself
# regardless of which schema it lives in, so it cannot collide with
# wherever PostGIS happens to be installed. Confirmed present in
# PostgreSQL 17's pg_dump (production's server major version, and the
# recovery-toolbox's pg_dump version -- see ci/Dockerfile.database-recovery)
# via the official PostgreSQL 17 documentation for pg_dump.
log "Creating PostgreSQL custom-format backup ${BACKUP_ID}."
pg_dump \
  --format=custom \
  --compress=9 \
  --no-owner \
  --no-privileges \
  --exclude-extension=supabase_vault \
  --file="${RAW_DUMP}"

pg_restore --list "${RAW_DUMP}" >/dev/null
[ -s "${RAW_DUMP}" ] || fail "pg_dump produced an empty archive."

# Cluster-wide roles are separate because managed providers often forbid their restore.
pg_dumpall --globals-only --no-role-passwords > "${RAW_GLOBALS}"

COUNTS_JSON='{}'
EXPECTED_TABLE_COUNT=0
while IFS= read -r table || [ -n "${table}" ]; do
  [ -n "${table}" ] || continue
  EXPECTED_TABLE_COUNT="$((EXPECTED_TABLE_COUNT + 1))"
  [[ "${table}" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || fail "Unsafe critical table name: ${table}"
  exists="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='${table}' AND c.relkind IN ('r','p'));" | xargs)"
  [ "${exists}" = "t" ] || fail "Required critical table is missing from the source database: ${table}"
  quoted_table="$(quote_identifier "${table}")"
  count="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT COUNT(*) FROM ${quoted_table};" | xargs)"
  COUNTS_JSON="$(jq --arg table "${table}" --argjson count "${count}" '. + {($table): $count}' <<<"${COUNTS_JSON}")"
done < "${DATABASE_SCRIPTS_DIR}/critical-tables.txt"
[ "$(jq 'length' <<<"${COUNTS_JSON}")" -eq "${EXPECTED_TABLE_COUNT}" ] || fail \
  "Critical table row-count manifest is incomplete."

gpg \
  --homedir "${GNUPGHOME}" \
  --batch \
  --yes \
  --pinentry-mode loopback \
  --passphrase-file "${PASSPHRASE_FILE}" \
  --symmetric --cipher-algo AES256 \
  --output "${ARCHIVE_FILE}" "${RAW_DUMP}"
gpg \
  --homedir "${GNUPGHOME}" \
  --batch \
  --yes \
  --pinentry-mode loopback \
  --passphrase-file "${PASSPHRASE_FILE}" \
  --symmetric --cipher-algo AES256 \
  --output "${GLOBALS_FILE}" "${RAW_GLOBALS}"

ARCHIVE_SHA256="$(sha256_file "${ARCHIVE_FILE}")"
ARCHIVE_BYTES="$(wc -c < "${ARCHIVE_FILE}" | tr -d '[:space:]')"
BACKUP_FINISHED_EPOCH="$(date -u +%s)"
DURATION_SECONDS="$((BACKUP_FINISHED_EPOCH - BACKUP_STARTED_EPOCH))"

REMOTE_PREFIX=""
ARCHIVE_OBJECT_KEY=""
GLOBALS_OBJECT_KEY=""
MANIFEST_OBJECT_KEY=""
if [ -n "${BACKUP_STORAGE_URI:-}" ]; then
  parse_s3_uri "${BACKUP_STORAGE_URI}"
  REMOTE_PREFIX="${S3_PREFIX:+${S3_PREFIX}/}backups/hourly/$(date -u +%Y/%m/%d)"
  ARCHIVE_OBJECT_KEY="${REMOTE_PREFIX}/${BACKUP_ID}.dump.gpg"
  GLOBALS_OBJECT_KEY="${REMOTE_PREFIX}/${BACKUP_ID}.globals.sql.gpg"
  MANIFEST_OBJECT_KEY="${REMOTE_PREFIX}/${BACKUP_ID}.manifest.json"
fi

jq -n \
  --arg schemaVersion "1" \
  --arg backupId "${BACKUP_ID}" \
  --arg createdAtUtc "${BACKUP_CREATED_AT}" \
  --arg databaseName "${DATABASE_NAME}" \
  --arg databaseHostSanitized "${PGHOST}:${PGPORT}" \
  --arg postgresVersion "${POSTGRES_VERSION}" \
  --arg postgresClientVersion "${POSTGRES_CLIENT_VERSION}" \
  --arg postgisVersion "${POSTGIS_VERSION}" \
  --arg applicationCommitSha "${APPLICATION_COMMIT_SHA}" \
  --arg latestEfMigration "${LATEST_MIGRATION}" \
  --arg archiveFile "$(basename "${ARCHIVE_FILE}")" \
  --arg globalsFile "$(basename "${GLOBALS_FILE}")" \
  --arg archiveSha256 "${ARCHIVE_SHA256}" \
  --arg archiveObjectKey "${ARCHIVE_OBJECT_KEY}" \
  --arg globalsObjectKey "${GLOBALS_OBJECT_KEY}" \
  --arg manifestObjectKey "${MANIFEST_OBJECT_KEY}" \
  --arg storageLocationSanitized "${BACKUP_STORAGE_URI:-local-only}" \
  --argjson archiveBytes "${ARCHIVE_BYTES}" \
  --argjson durationSeconds "${DURATION_SECONDS}" \
  --argjson tableRowCounts "${COUNTS_JSON}" \
  '{
    schemaVersion: ($schemaVersion | tonumber),
    backupId: $backupId,
    createdAtUtc: $createdAtUtc,
    databaseName: $databaseName,
    databaseHostSanitized: $databaseHostSanitized,
    postgresVersion: $postgresVersion,
    postgresServerVersion: $postgresVersion,
    postgresClientVersion: $postgresClientVersion,
    postgisVersion: $postgisVersion,
    applicationCommitSha: $applicationCommitSha,
    latestEfMigration: $latestEfMigration,
    efCoreMigration: $latestEfMigration,
    format: "PostgreSQL custom archive",
    backupFormat: "PostgreSQL custom archive",
    compression: "gzip-level-9",
    encryption: "GPG symmetric AES256",
    archiveFile: $archiveFile,
    globalsFile: $globalsFile,
    archiveSha256: $archiveSha256,
    archiveBytes: $archiveBytes,
    backupSizeBytes: $archiveBytes,
    durationSeconds: $durationSeconds,
    archiveObjectKey: $archiveObjectKey,
    globalsObjectKey: $globalsObjectKey,
    manifestObjectKey: $manifestObjectKey,
    storageLocationSanitized: $storageLocationSanitized,
    sha256: $archiveSha256,
    encrypted: true,
    status: "verified",
    tableRowCounts: $tableRowCounts
  }' > "${MANIFEST_FILE}"

BACKUP_FILE="${ARCHIVE_FILE}" MANIFEST_FILE="${MANIFEST_FILE}" \
  bash "${DATABASE_SCRIPTS_DIR}/verify-backup.sh"

if [ -n "${BACKUP_STORAGE_URI:-}" ]; then
  require_command aws
  UPLOAD_STARTED_EPOCH="$(date -u +%s)"
  upload_file "${ARCHIVE_FILE}" "${ARCHIVE_OBJECT_KEY}"
  upload_file "${GLOBALS_FILE}" "${GLOBALS_OBJECT_KEY}"
  UPLOAD_DURATION_SECONDS="$(( $(date -u +%s) - UPLOAD_STARTED_EPOCH ))"
  log "Encrypted archive and globals uploaded to durable object storage."
elif [ "${BACKUP_REQUIRE_REMOTE_UPLOAD:-false}" = "true" ]; then
  fail "Remote upload is required but BACKUP_STORAGE_URI is not configured."
else
  UPLOAD_DURATION_SECONDS=0
fi

MANIFEST_TEMP="${WORK_DIR}/manifest.final.json"
jq --arg status success --argjson uploadDurationSeconds "${UPLOAD_DURATION_SECONDS}" \
  '.status=$status | .uploadDurationSeconds=$uploadDurationSeconds' \
  "${MANIFEST_FILE}" > "${MANIFEST_TEMP}"
mv "${MANIFEST_TEMP}" "${MANIFEST_FILE}"
if [ -n "${BACKUP_STORAGE_URI:-}" ]; then
  upload_file "${MANIFEST_FILE}" "${MANIFEST_OBJECT_KEY}"
fi

jq -n \
  --arg status PASS \
  --arg backupId "${BACKUP_ID}" \
  --arg createdAtUtc "${BACKUP_CREATED_AT}" \
  --arg manifest "${MANIFEST_FILE}" \
  --arg archive "${ARCHIVE_FILE}" \
  --argjson durationSeconds "${DURATION_SECONDS}" \
  '{status:$status,backupId:$backupId,createdAtUtc:$createdAtUtc,durationSeconds:$durationSeconds,manifest:$manifest,archive:$archive}' \
  > "${OUTPUT_DIR}/backup-result.json"
log "Backup ${BACKUP_ID} completed and verified in ${DURATION_SECONDS}s."
