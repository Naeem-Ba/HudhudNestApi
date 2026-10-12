#!/usr/bin/env bash
# Proves a production deploy actually happened: the Render deploy for EXPECTED_COMMIT_SHA reached `live`, and the
# API answers /health/ready. The deploy hook returns as soon as Render accepts the request, so without this a
# build that fails or hangs on Render still leaves the workflow green.
#
# Inputs (environment): RENDER_API_KEY, RENDER_SERVICE_ID (srv-...), EXPECTED_COMMIT_SHA (40 hex),
# PRODUCTION_BASE_URL (https://...). Optional: VERIFY_TIMEOUT_SECONDS (default 1500), VERIFY_INTERVAL_SECONDS
# (default 20), READY_ATTEMPTS (default 10). Writes artifacts/production-release/post-deploy-verification.json.
# Nothing here changes Render or the database; it only reads.
set -euo pipefail

fail() { echo "::error::$*" >&2; exit 1; }
log() { echo "[verify-production-deploy] $*"; }
require_env() { [ -n "${!1:-}" ] || fail "$1 is required."; }

require_env RENDER_API_KEY
require_env RENDER_SERVICE_ID
require_env EXPECTED_COMMIT_SHA
require_env PRODUCTION_BASE_URL

[[ "${RENDER_SERVICE_ID}" =~ ^srv-[A-Za-z0-9]+$ ]] || fail "RENDER_SERVICE_ID is not in the expected 'srv-...' format."
[[ "${EXPECTED_COMMIT_SHA}" =~ ^[0-9a-f]{40}$ ]] || fail "EXPECTED_COMMIT_SHA must be a full 40-character commit id."
[[ "${PRODUCTION_BASE_URL}" == https://* ]] || fail "PRODUCTION_BASE_URL must be an https:// URL."
BASE_URL="${PRODUCTION_BASE_URL%/}"

TIMEOUT="${VERIFY_TIMEOUT_SECONDS:-1500}"
INTERVAL="${VERIFY_INTERVAL_SECONDS:-20}"
READY_ATTEMPTS="${READY_ATTEMPTS:-10}"

umask 077
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
auth="${work}/auth"
printf 'header = "Authorization: Bearer %s"\nheader = "Accept: application/json"\n' "${RENDER_API_KEY}" > "${auth}"

started="$(date +%s)"
deploy_id=""
status="not-found"
while :; do
  curl --fail --silent --show-error --max-time 30 --config "${auth}" \
    "https://api.render.com/v1/services/${RENDER_SERVICE_ID}/deploys?limit=10" > "${work}/deploys.json" \
    || fail "Could not list deploys for Render service ${RENDER_SERVICE_ID}."

  # Newest first. Accepts both [{deploy:{...}}] and [{...}] shapes.
  line="$(jq -r --arg sha "${EXPECTED_COMMIT_SHA}" '
    [.. | objects | select((.commit? | type) == "object" and .status? != null and .id? != null)
        | select(.commit.id == $sha)] | first // empty | "\(.id) \(.status)"' "${work}/deploys.json")"
  if [ -n "${line}" ]; then
    deploy_id="${line%% *}"
    status="${line#* }"
  else
    deploy_id=""
    status="not-found"
  fi
  log "deploy for ${EXPECTED_COMMIT_SHA:0:7}: ${deploy_id:-none} (${status})"

  case "${status}" in
    live) break ;;
    build_failed|update_failed|pre_deploy_failed|canceled)
      fail "Render deploy ${deploy_id} of ${EXPECTED_COMMIT_SHA:0:7} ended as ${status}. Production is NOT running this commit." ;;
    deactivated)
      fail "Render deploy ${deploy_id} of ${EXPECTED_COMMIT_SHA:0:7} was replaced by another deploy before it could be verified. Check what is live in Render." ;;
  esac

  if [ $(( $(date +%s) - started )) -ge "${TIMEOUT}" ]; then
    fail "Render deploy of ${EXPECTED_COMMIT_SHA:0:7} did not become live within ${TIMEOUT}s (last status: ${status})."
  fi
  sleep "${INTERVAL}"
done

ready="false"
for attempt in $(seq 1 "${READY_ATTEMPTS}"); do
  if curl --fail --silent --show-error --max-time 15 "${BASE_URL}/health/ready" > /dev/null; then
    ready="true"
    break
  fi
  log "readiness attempt ${attempt}/${READY_ATTEMPTS} failed"
  sleep "${INTERVAL}"
done
[ "${ready}" = "true" ] || fail "Deploy ${deploy_id} is live but ${BASE_URL}/health/ready did not answer successfully."

mkdir -p artifacts/production-release
jq -n --arg commitSha "${EXPECTED_COMMIT_SHA}" --arg deployId "${deploy_id}" \
  --arg verifiedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{commitSha:$commitSha,renderDeployId:$deployId,renderStatus:"live",readiness:"ok",verifiedAtUtc:$verifiedAtUtc}' \
  > artifacts/production-release/post-deploy-verification.json
log "Verified: Render deploy ${deploy_id} of ${EXPECTED_COMMIT_SHA:0:7} is live and ${BASE_URL}/health/ready answers."
