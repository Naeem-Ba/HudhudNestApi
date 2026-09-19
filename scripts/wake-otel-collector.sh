#!/usr/bin/env bash
# Best-effort wake-up of the Staging otel-collector (Render Free web service).
#
# The collector spins down when it has had no inbound request for ~15 minutes and takes 10-60 s to
# start. Production Gate run 35466879093 showed that the API's own OTLP pushes do not bring it back
# in time: Prometheus and Tempo held no data for the whole 10-minute verification window, yet data
# flowed within 40 s once the collector was hit directly (a GET answered after 12 s, i.e. it was
# cold). Waking it explicitly, after the new API is up and before the smoke traffic, lets the API's
# pushes land from the start.
#
# Usage: wake-otel-collector.sh <base-url> [timeout-seconds]
# Any HTTP answer below 500 counts as awake (the OTLP endpoint answers 405 to a GET). Never fails the
# job: the observability verification remains the judge.
set -uo pipefail

url="${1:-${OTEL_COLLECTOR_URL:-}}"
limit="${2:-120}"

if [ -z "${url}" ]; then
  echo "::notice::OTEL collector URL is not configured (STAGING_OTEL_COLLECTOR_URL); not pre-warming it."
  exit 0
fi

started=$SECONDS
deadline=$((SECONDS + limit))
until code="$(curl --silent --max-time 30 --output /dev/null --write-out '%{http_code}' "${url%/}/v1/metrics" 2>/dev/null)" &&
  [ "${code}" -ge 200 ] && [ "${code}" -lt 500 ]; do
  if [ "${SECONDS}" -ge "${deadline}" ]; then
    echo "::warning::otel-collector did not answer within ${limit}s (last HTTP ${code:-none})."
    exit 0
  fi
  sleep 3
done
echo "otel-collector is awake (HTTP ${code}) after $((SECONDS - started))s."
