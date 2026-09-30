#!/usr/bin/env bash

set -Eeuo pipefail

log() { printf '[application-rollback] %s\n' "$*" >&2; }
fail() { log "ERROR: $*"; exit 1; }
require_env() { [ -n "${!1:-}" ] || fail "Required environment variable is missing: $1"; }
for name in RENDER_API_KEY RENDER_SERVICE_ID TARGET_DEPLOY_ID PRODUCTION_BASE_URL INCIDENT_ID APPROVED_BY; do
  require_env "${name}"
done
[ "${CONFIRM_AUTODEPLOY_DISABLED:-}" = "true" ] || fail \
  "Set CONFIRM_AUTODEPLOY_DISABLED=true after disabling Render auto-deploy for the incident."
for command in curl jq; do command -v "${command}" >/dev/null 2>&1 || fail "Missing command: ${command}"; done
[[ "${RENDER_SERVICE_ID}" =~ ^srv-[A-Za-z0-9]+$ ]] || fail "Invalid Render service ID."
[[ "${TARGET_DEPLOY_ID}" =~ ^dep-[A-Za-z0-9]+$ ]] || fail "Invalid Render deploy ID."
[[ "${PRODUCTION_BASE_URL}" =~ ^https:// ]] || fail "PRODUCTION_BASE_URL must use HTTPS."
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/hudhudnest-render-rollback.XXXXXXXX")"
cleanup() { rm -rf -- "${WORK_DIR}"; }
trap cleanup EXIT INT TERM
CURL_CONFIG="${WORK_DIR}/curl.conf"
printf 'header = "Authorization: Bearer %s"\nheader = "Accept: application/json"\n' "${RENDER_API_KEY}" > "${CURL_CONFIG}"
API_BASE="https://api.render.com/v1/services/${RENDER_SERVICE_ID}"
TARGET_JSON="${WORK_DIR}/target.json"
curl --fail --silent --show-error --config "${CURL_CONFIG}" \
  "${API_BASE}/deploys/${TARGET_DEPLOY_ID}" > "${TARGET_JSON}"
[ "$(jq -r '.id // empty' "${TARGET_JSON}")" = "${TARGET_DEPLOY_ID}" ] || fail "Target deploy was not found."
TARGET_STATUS="$(jq -r '.status // empty' "${TARGET_JSON}")"
case "${TARGET_STATUS}" in live|deactivated) ;; *) fail "Target deploy status ${TARGET_STATUS} is not rollback-capable." ;; esac

ROLLBACK_JSON="${WORK_DIR}/rollback.json"
curl --fail --silent --show-error --request POST --config "${CURL_CONFIG}" \
  --header 'Content-Type: application/json' \
  --data "$(jq -n --arg deployId "${TARGET_DEPLOY_ID}" '{deployId:$deployId}')" \
  "${API_BASE}/rollback" > "${ROLLBACK_JSON}"
ROLLBACK_DEPLOY_ID="$(jq -r '.id // empty' "${ROLLBACK_JSON}")"
[ -n "${ROLLBACK_DEPLOY_ID}" ] || fail "Render rollback response did not contain a deploy ID."

FINAL_STATUS=""
for attempt in $(seq 1 90); do
  DEPLOY_JSON="${WORK_DIR}/deploy-${attempt}.json"
  curl --fail --silent --show-error --config "${CURL_CONFIG}" \
    "${API_BASE}/deploys/${ROLLBACK_DEPLOY_ID}" > "${DEPLOY_JSON}"
  FINAL_STATUS="$(jq -r '.status // empty' "${DEPLOY_JSON}")"
  log "Rollback deploy ${ROLLBACK_DEPLOY_ID}: ${FINAL_STATUS} (${attempt}/90)"
  case "${FINAL_STATUS}" in
    live) break ;;
    build_failed|update_failed|canceled|pre_deploy_failed) fail "Rollback deploy failed with status ${FINAL_STATUS}." ;;
  esac
  sleep 10
done
[ "${FINAL_STATUS}" = "live" ] || fail "Rollback did not become live before timeout."
BASE_URL="${PRODUCTION_BASE_URL%/}"
curl --fail --silent --show-error --max-time 15 "${BASE_URL}/health/ready" >/dev/null
curl --fail --silent --show-error --max-time 20 "${BASE_URL}/api/properties?page=1&pageSize=1" >/dev/null
RESULT_DIR="${ROLLBACK_RESULT_DIR:-artifacts/rollback-production}"
mkdir -p "${RESULT_DIR}"
jq -n \
  --arg status PASS \
  --arg incidentId "${INCIDENT_ID}" \
  --arg approvedBy "${APPROVED_BY}" \
  --arg targetDeployId "${TARGET_DEPLOY_ID}" \
  --arg rollbackDeployId "${ROLLBACK_DEPLOY_ID}" \
  --arg completedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{status:$status,incidentId:$incidentId,approvedBy:$approvedBy,targetDeployId:$targetDeployId,rollbackDeployId:$rollbackDeployId,completedAtUtc:$completedAtUtc,databaseDowngradePerformed:false}' \
  > "${RESULT_DIR}/rollback-result.json"
log "Application rollback, health check, and public read smoke test passed. No database downgrade was run."
