#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env BACKUP_FILE
require_env MANIFEST_FILE
require_env BACKUP_ENCRYPTION_KEY
require_env RESTORE_TARGET_DATABASE_URL
require_env RESTORE_ADMIN_DATABASE_URL
require_env RESTORE_TARGET_ENVIRONMENT
require_boolean_true ALLOW_DESTRUCTIVE_RESTORE
require_boolean_true RESTORE_TARGET_DISPOSABLE
[ "${RESTORE_TARGET_ENVIRONMENT}" = "recovery-drill" ] || fail \
  "RESTORE_TARGET_ENVIRONMENT must equal recovery-drill."
for command in psql pg_restore gpg jq python3; do require_command "${command}"; done

TARGET_IDENTITY="$(connection_identity_json "${RESTORE_TARGET_DATABASE_URL}")"
TARGET_DATABASE="$(jq -r '.database' <<<"${TARGET_IDENTITY}")"
[[ "${TARGET_DATABASE}" =~ restore_drill ]] || fail \
  "Disposable target database name must contain restore_drill."

ADMIN_IDENTITY="$(connection_identity_json "${RESTORE_ADMIN_DATABASE_URL}")"
[ "$(jq -r '.database' <<<"${ADMIN_IDENTITY}")" != "${TARGET_DATABASE}" ] || fail \
  "RESTORE_ADMIN_DATABASE_URL must connect to a maintenance database, not the target."

if [ -n "${PRODUCTION_DATABASE_URL:-}" ]; then
  PRODUCTION_IDENTITY="$(connection_identity_json "${PRODUCTION_DATABASE_URL}")"
  target_host_port="$(jq -r '.host + ":" + (.port|tostring)' <<<"${TARGET_IDENTITY}")"
  production_host_port="$(jq -r '.host + ":" + (.port|tostring)' <<<"${PRODUCTION_IDENTITY}")"
  [ "${target_host_port}" != "${production_host_port}" ] || fail \
    "Restore target is on the production PostgreSQL server. This is forbidden."
  [ "${TARGET_IDENTITY}" != "${PRODUCTION_IDENTITY}" ] || fail \
    "Restore target matches production."
fi

BACKUP_FILE="${BACKUP_FILE}" MANIFEST_FILE="${MANIFEST_FILE}" \
  bash "${DATABASE_SCRIPTS_DIR}/verify-backup.sh"

WORK_DIR="$(create_private_temp_dir)"
cleanup() { rm -rf -- "${WORK_DIR}"; }
trap cleanup EXIT INT TERM
PASSPHRASE_FILE="${WORK_DIR}/passphrase"
DECRYPTED_FILE="${WORK_DIR}/restore.dump"
printf '%s' "${BACKUP_ENCRYPTION_KEY}" > "${PASSPHRASE_FILE}"
gpg --batch --yes --pinentry-mode loopback \
  --passphrase-file "${PASSPHRASE_FILE}" \
  --decrypt --output "${DECRYPTED_FILE}" "${BACKUP_FILE}"

RESTORE_STARTED_EPOCH="$(date -u +%s)"
configure_pg_environment "${RESTORE_ADMIN_DATABASE_URL}"
psql -X -v ON_ERROR_STOP=1 -v target_database="${TARGET_DATABASE}" <<'SQL'
SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', :'target_database') \gexec
SELECT format('CREATE DATABASE %I', :'target_database') \gexec
SQL

configure_pg_environment "${RESTORE_TARGET_DATABASE_URL}"
assert_postgres_client_matches_server
psql -X -v ON_ERROR_STOP=1 -c 'CREATE EXTENSION IF NOT EXISTS postgis;' >/dev/null
pg_restore \
  --exit-on-error \
  --clean \
  --if-exists \
  --no-owner \
  --no-privileges \
  --dbname="${PGDATABASE}" \
  "${DECRYPTED_FILE}" \
  2> >(tee "${WORK_DIR}/pg-restore.stderr" >&2)

RESTORE_FINISHED_EPOCH="$(date -u +%s)"
RESTORE_DURATION_SECONDS="$((RESTORE_FINISHED_EPOCH - RESTORE_STARTED_EPOCH))"
RESULT_DIR="${RESTORE_RESULT_DIR:-${REPOSITORY_ROOT}/artifacts/database-restore}"
mkdir -p "${RESULT_DIR}"
jq -n \
  --arg status PASS \
  --arg backupId "$(jq -r '.backupId' "${MANIFEST_FILE}")" \
  --arg restoredAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg targetDatabase "${TARGET_DATABASE}" \
  --argjson durationSeconds "${RESTORE_DURATION_SECONDS}" \
  '{status:$status,backupId:$backupId,restoredAtUtc:$restoredAtUtc,targetDatabase:$targetDatabase,durationSeconds:$durationSeconds}' \
  > "${RESULT_DIR}/restore-result.json"
log "Restore into disposable target ${TARGET_DATABASE} passed in ${RESTORE_DURATION_SECONDS}s."
