#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env BACKUP_FILE
require_env MANIFEST_FILE
require_env BACKUP_ENCRYPTION_KEY
for command in gpg jq pg_restore; do
  require_command "${command}"
done
[ -s "${BACKUP_FILE}" ] || fail "Backup archive does not exist or is empty: ${BACKUP_FILE}"
[ -s "${MANIFEST_FILE}" ] || fail "Backup manifest does not exist or is empty: ${MANIFEST_FILE}"
jq -e '.schemaVersion == 1 and .backupId and .createdAtUtc and .databaseName and .databaseHostSanitized and .postgresServerVersion and .postgresClientVersion and .postgisVersion and .applicationCommitSha and .efCoreMigration and .backupFormat and (.backupSizeBytes > 0) and .sha256 and (.encrypted == true) and .storageLocationSanitized and .durationSeconds >= 0 and (.tableRowCounts | type == "object")' \
  "${MANIFEST_FILE}" >/dev/null || fail "Backup manifest schema validation failed."

EXPECTED_SHA="$(jq -r '.archiveSha256' "${MANIFEST_FILE}")"
ACTUAL_SHA="$(sha256_file "${BACKUP_FILE}")"
[ "${EXPECTED_SHA}" = "${ACTUAL_SHA}" ] || fail "Encrypted archive checksum mismatch."

WORK_DIR="$(create_private_temp_dir)"
cleanup() { rm -rf -- "${WORK_DIR}"; }
trap cleanup EXIT INT TERM
configure_gpg_home "${WORK_DIR}"
PASSPHRASE_FILE="${WORK_DIR}/passphrase"
DECRYPTED_FILE="${WORK_DIR}/verified.dump"
TOC_FILE="${WORK_DIR}/archive.toc"
printf '%s' "${BACKUP_ENCRYPTION_KEY}" > "${PASSPHRASE_FILE}"
gpg --batch --yes --pinentry-mode loopback \
  --passphrase-file "${PASSPHRASE_FILE}" \
  --decrypt --output "${DECRYPTED_FILE}" "${BACKUP_FILE}"
pg_restore --list "${DECRYPTED_FILE}" > "${TOC_FILE}"
grep -q 'DATABASE\|TABLE\|SCHEMA' "${TOC_FILE}" || fail "Archive TOC has no database schema objects."
while IFS= read -r table; do
  [ -n "${table}" ] || continue
  grep -Eq "[[:space:]]TABLE( DATA)? public ${table}([[:space:]]|$)" "${TOC_FILE}" || fail \
    "Critical table is missing from archive TOC: ${table}"
done < <(jq -r '.tableRowCounts | keys[]' "${MANIFEST_FILE}")
grep -Eq '[[:space:]]TABLE( DATA)? public __EFMigrationsHistory([[:space:]]|$)' "${TOC_FILE}" || fail \
  "EF Core migration history is missing from archive TOC."
grep -Eq '[[:space:]]EXTENSION .*postgis([[:space:]]|$)' "${TOC_FILE}" || fail \
  "PostGIS extension is missing from archive TOC."

BACKUP_AGE_SECONDS="$(python3 - "$(jq -r '.createdAtUtc' "${MANIFEST_FILE}")" <<'PY'
import datetime as dt
import sys
created = dt.datetime.fromisoformat(sys.argv[1].replace("Z", "+00:00"))
print(max(0, int((dt.datetime.now(dt.timezone.utc) - created).total_seconds())))
PY
)"
if [ "${ENFORCE_BACKUP_AGE:-true}" = "true" ] && [ "${BACKUP_AGE_SECONDS}" -gt "${RPO_TARGET_SECONDS:-3600}" ]; then
  fail "Backup age ${BACKUP_AGE_SECONDS}s exceeds RPO ${RPO_TARGET_SECONDS:-3600}s."
fi

RESULT_FILE="${BACKUP_VERIFY_RESULT_FILE:-$(dirname "${MANIFEST_FILE}")/backup-verification.json}"
jq -n \
  --arg status PASS \
  --arg backupId "$(jq -r '.backupId' "${MANIFEST_FILE}")" \
  --arg verifiedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg archiveSha256 "${ACTUAL_SHA}" \
  --argjson backupAgeSeconds "${BACKUP_AGE_SECONDS}" \
  --argjson tocEntries "$(grep -c '^[0-9]' "${TOC_FILE}" || true)" \
  '{status:$status,backupId:$backupId,verifiedAtUtc:$verifiedAtUtc,archiveSha256:$archiveSha256,tocEntries:$tocEntries,backupAgeSeconds:$backupAgeSeconds}' \
  > "${RESULT_FILE}"
log "Backup checksum, decryption, and pg_restore TOC verification passed."
