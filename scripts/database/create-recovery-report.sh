#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env BACKUP_ID
require_env DRILL_STARTED_EPOCH
require_env APPLICATION_SMOKE_DURATION_SECONDS
require_command jq
RESULT_DIR="${RESTORE_RESULT_DIR:-${REPOSITORY_ROOT}/artifacts/database-recovery}"
MANIFEST_FILE="${RESULT_DIR}/${BACKUP_ID}.manifest.json"
for result_file in backup-result.json backup-verification.json restore-result.json database-verification.json application-verification.json; do
  [ -s "${RESULT_DIR}/${result_file}" ] || fail "Required recovery evidence is missing: ${result_file}"
  [ "$(jq -r '.status' "${RESULT_DIR}/${result_file}")" = "PASS" ] || fail \
    "Required recovery evidence did not pass: ${result_file}"
done
DRILL_FINISHED_EPOCH="$(date -u +%s)"
BACKUP_CREATED_AT="$(jq -r '.createdAtUtc' "${MANIFEST_FILE}")"
BACKUP_CREATED_EPOCH="$(date -u -d "${BACKUP_CREATED_AT}" +%s)"
BACKUP_AGE_SECONDS="$((DRILL_FINISHED_EPOCH - BACKUP_CREATED_EPOCH))"
RECOVERY_SECONDS="$((DRILL_FINISHED_EPOCH - DRILL_STARTED_EPOCH))"
RPO_TARGET_SECONDS="${RPO_TARGET_SECONDS:-3600}"
RTO_TARGET_SECONDS="${RTO_TARGET_SECONDS:-7200}"
[ "${BACKUP_AGE_SECONDS}" -le "${RPO_TARGET_SECONDS}" ] || fail "RPO target failed."
[ "${RECOVERY_SECONDS}" -le "${RTO_TARGET_SECONDS}" ] || fail "RTO target failed."
RESTORE_SECONDS="$(jq -r '.durationSeconds' "${RESULT_DIR}/restore-result.json")"
VALIDATION_SECONDS="$(jq -r '.durationSeconds' "${RESULT_DIR}/database-verification.json")"
jq -n \
  --arg result passed \
  --arg backupId "${BACKUP_ID}" \
  --arg restoreTarget hudhudnest_restore_drill \
  --arg backupCreatedAtUtc "${BACKUP_CREATED_AT}" \
  --arg drillStartedAtUtc "$(date -u -d "@${DRILL_STARTED_EPOCH}" +%Y-%m-%dT%H:%M:%SZ)" \
  --arg drillCompletedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --argjson backupAgeSeconds "${BACKUP_AGE_SECONDS}" \
  --argjson downloadDurationSeconds "${DOWNLOAD_DURATION_SECONDS:-0}" \
  --argjson restoreDurationSeconds "${RESTORE_SECONDS}" \
  --argjson validationDurationSeconds "${VALIDATION_SECONDS}" \
  --argjson applicationSmokeTestDurationSeconds "${APPLICATION_SMOKE_DURATION_SECONDS}" \
  --argjson totalRecoveryDurationSeconds "${RECOVERY_SECONDS}" \
  --argjson rpoTargetSeconds "${RPO_TARGET_SECONDS}" \
  --argjson rtoTargetSeconds "${RTO_TARGET_SECONDS}" \
  '{result:$result,backupId:$backupId,restoreTarget:$restoreTarget,backupCreatedAtUtc:$backupCreatedAtUtc,drillStartedAtUtc:$drillStartedAtUtc,drillCompletedAtUtc:$drillCompletedAtUtc,backupAgeMinutes:($backupAgeSeconds/60),downloadDurationSeconds:$downloadDurationSeconds,restoreDurationSeconds:$restoreDurationSeconds,validationDurationSeconds:$validationDurationSeconds,applicationSmokeTestDurationSeconds:$applicationSmokeTestDurationSeconds,totalRecoveryDurationSeconds:$totalRecoveryDurationSeconds,rpoTargetMinutes:($rpoTargetSeconds/60),rtoTargetMinutes:($rtoTargetSeconds/60),rpoMet:true,rtoMet:true,restoreResult:"PASS",schemaResult:"PASS",dataValidationResult:"PASS",applicationVerificationResult:"PASS",applicationSmokeResult:"PASS",cleanupScheduled:true}' \
  > "${RESULT_DIR}/restore-drill-evidence.json"
log "Recovery report passed: backup age ${BACKUP_AGE_SECONDS}s, total recovery ${RECOVERY_SECONDS}s."
