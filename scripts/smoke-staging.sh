#!/usr/bin/env bash
set -euo pipefail

base_url="${STAGING_BASE_URL:-${BASE_URL:-}}"

if [ -z "${base_url}" ]; then
  echo "STAGING_BASE_URL or BASE_URL is required."
  exit 1
fi

base_url="${base_url%/}"

request_smoke_endpoint() {
  local name="$1"
  local path="$2"
  local attempts="${SMOKE_RETRY_ATTEMPTS:-5}"
  local delay_seconds="${SMOKE_RETRY_DELAY_SECONDS:-3}"
  local timeout_seconds="${SMOKE_REQUEST_TIMEOUT_SECONDS:-10}"
  local response_file
  local status
  local attempt

  response_file="$(mktemp)"

  for attempt in $(seq 1 "${attempts}"); do
    status="$(
      curl \
        --location \
        --silent \
        --show-error \
        --max-time "${timeout_seconds}" \
        --output "${response_file}" \
        --write-out "%{http_code}" \
        "${base_url}${path}" \
        || true
    )"

    if [ "${status}" = "200" ]; then
      rm -f "${response_file}"
      echo "${name} passed (${path})."
      return 0
    fi

    echo "${name} attempt ${attempt}/${attempts} returned HTTP ${status:-000} for ${path}."

    if [ -s "${response_file}" ]; then
      echo "Response preview:"
      head -c 500 "${response_file}"
      echo
    fi

    if [ "${attempt}" != "${attempts}" ]; then
      sleep "${delay_seconds}"
    fi
  done

  echo "::error::Staging smoke check failed: ${name} expected HTTP 200 from ${path}, got HTTP ${status:-000}."
  rm -f "${response_file}"
  return 1
}

request_smoke_endpoint "Liveness" "/health/live"
request_smoke_endpoint "Readiness" "/health/ready"
request_smoke_endpoint "Public enum endpoint" "/api/enums/PropertyStatus"

echo "Staging smoke checks passed."
