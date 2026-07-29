#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

artifacts_dir="${SMOKE_ARTIFACTS_DIR:-artifacts/staging-smoke}"
mkdir -p "${artifacts_dir}"
temp_dir="$(mktemp -d)"
failure_message="Staging smoke bootstrap failed."

write_bootstrap_failure() {
  local exit_code="$1"
  if [ "${exit_code}" -eq 0 ] || [ -f "${artifacts_dir}/staging-smoke-report.json" ]; then
    return
  fi

  jq -n \
    --arg runId "${SMOKE_RUN_ID:-not-started}" \
    --arg expected "${EXPECTED_COMMIT_SHA:-unknown}" \
    --arg message "${failure_message}" \
    '{runId:$runId,environment:"Staging",expectedCommitSha:$expected,deployedCommitSha:"unknown",journeys:[],cleanup:{status:"skipped"},result:"failed",error:$message}' \
    > "${artifacts_dir}/staging-smoke-report.json"

  cat > "${artifacts_dir}/staging-smoke-junit.xml" <<XML
<?xml version="1.0" encoding="utf-8"?>
<testsuites><testsuite name="PropertyApi.StagingSmoke.Bootstrap" tests="1" failures="1"><testcase name="configuration"><failure>Staging smoke bootstrap failed.</failure></testcase></testsuite></testsuites>
XML

  {
    echo "# Staging E2E smoke report"
    echo
    echo "- Result: **Failed**"
    echo "- Reason: ${failure_message}"
  } > "${artifacts_dir}/staging-smoke-summary.md"
}

cleanup() {
  local exit_code=$?
  rm -rf "${temp_dir}"
  write_bootstrap_failure "${exit_code}"
}
trap cleanup EXIT

require_value() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    failure_message="${name} is required."
    echo "::error::${failure_message}"
    exit 1
  fi
}

for name in \
  STAGING_BASE_URL \
  PRODUCTION_BASE_URL \
  EXPECTED_COMMIT_SHA \
  STAGING_SMOKE_PHONE_PREFIX \
  STAGING_SMOKE_PASSWORD \
  STAGING_SMOKE_FIXED_OTP \
  STAGING_SMOKE_CLEANUP_SECRET
do
  require_value "${name}"
done

staging_url="${STAGING_BASE_URL%/}"
production_url="${PRODUCTION_BASE_URL%/}"

if [[ ! "${staging_url}" =~ ^https://[^/?#]+([/:][^?#]*)?$ ]]; then
  if [ "${SMOKE_ALLOW_HTTP_LOCAL:-false}" != "true" ] || [[ ! "${staging_url}" =~ ^http://(127\.0\.0\.1|localhost)(:[0-9]+)?$ ]]; then
    failure_message="STAGING_BASE_URL must be an absolute HTTPS URL without query parameters."
    echo "::error::${failure_message}"
    exit 1
  fi
fi

staging_host="$(printf '%s' "${staging_url}" | sed -E 's#^https?://([^/:]+).*$#\1#' | tr '[:upper:]' '[:lower:]')"
production_host="$(printf '%s' "${production_url}" | sed -E 's#^https?://([^/:]+).*$#\1#' | tr '[:upper:]' '[:lower:]')"

case "${staging_host}" in
  example.com|*.example.com|example.net|*.example.net|example.org|*.example.org|invalid|*.invalid|0.0.0.0)
    failure_message="STAGING_BASE_URL points to a placeholder or unsafe host."
    echo "::error::${failure_message}"
    exit 1
    ;;
esac

if [ "${staging_host}" = "${production_host}" ]; then
  failure_message="STAGING_BASE_URL resolves to the configured Production hostname."
  echo "::error::${failure_message}"
  exit 1
fi

if [[ ! "${STAGING_SMOKE_FIXED_OTP}" =~ ^[0-9]{6}$ ]]; then
  failure_message="STAGING_SMOKE_FIXED_OTP must contain exactly six digits."
  echo "::error::${failure_message}"
  exit 1
fi

if [ "${#STAGING_SMOKE_CLEANUP_SECRET}" -lt 32 ]; then
  failure_message="STAGING_SMOKE_CLEANUP_SECRET must contain at least 32 characters."
  echo "::error::${failure_message}"
  exit 1
fi

if [[ ! "${STAGING_SMOKE_PHONE_PREFIX}" =~ ^\+[0-9]{5,9}$ ]]; then
  failure_message="STAGING_SMOKE_PHONE_PREFIX must be an E.164-style dedicated test prefix."
  echo "::error::${failure_message}"
  exit 1
fi

if [ "${1:-}" = "--validate-only" ]; then
  echo "Staging smoke configuration validation passed for ${staging_host}."
  exit 0
fi

export STAGING_BASE_URL="${staging_url}"
export PRODUCTION_BASE_URL="${production_url}"
export SMOKE_ARTIFACTS_DIR="${artifacts_dir}"
export SMOKE_RUN_ID="${SMOKE_RUN_ID:-$(date -u +%Y%m%d%H%M%S)-${EXPECTED_COMMIT_SHA:0:7}-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}}"

echo "Running mandatory Staging E2E smoke suite against ${staging_host}."

dotnet restore \
  tests/PropertyApi.StagingSmokeTests/PropertyApi.StagingSmokeTests.csproj

set +e
dotnet test \
  tests/PropertyApi.StagingSmokeTests/PropertyApi.StagingSmokeTests.csproj \
  --configuration Release \
  --no-restore \
  --results-directory "${artifacts_dir}/test-results" \
  --logger "trx;LogFileName=staging-smoke.trx"
test_exit=$?
set -e

for report in staging-smoke-report.json staging-smoke-junit.xml staging-smoke-summary.md; do
  if [ ! -s "${artifacts_dir}/${report}" ]; then
    failure_message="Mandatory smoke report ${report} is missing."
    echo "::error::${failure_message}"
    exit 1
  fi
done

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  cat "${artifacts_dir}/staging-smoke-summary.md" >> "${GITHUB_STEP_SUMMARY}"
fi

if [ "${test_exit}" -ne 0 ]; then
  failure_message="One or more mandatory Staging journeys failed."
  echo "::error::${failure_message}"
  exit "${test_exit}"
fi

if [ "$(jq -r '.result' "${artifacts_dir}/staging-smoke-report.json")" != "passed" ]; then
  failure_message="The machine-readable smoke report is not passed."
  echo "::error::${failure_message}"
  exit 1
fi

if jq -e '.journeys[] | select(.status != "passed")' \
  "${artifacts_dir}/staging-smoke-report.json" > /dev/null; then
  failure_message="A mandatory journey is failed or skipped."
  echo "::error::${failure_message}"
  exit 1
fi

echo "Mandatory Staging E2E smoke suite passed."
