#!/usr/bin/env bash
set -Eeuo pipefail

: "${PERF_DATABASE_URL:?PERF_DATABASE_URL is required}"
: "${PERF_RACE_KIND:?PERF_RACE_KIND is required}"
: "${PERF_RACE_CONCURRENCY:?PERF_RACE_CONCURRENCY is required}"
: "${PERF_RACE_MARKER:?PERF_RACE_MARKER is required}"
: "${PERF_RACE_PHONE:?PERF_RACE_PHONE is required}"

output="${PERF_ARTIFACTS_DIR:-artifacts/performance}/concurrency"
mkdir -p "${output}"
path="${output}/${PERF_RACE_KIND}-${PERF_RACE_CONCURRENCY}-integrity.json"

psql "${PERF_DATABASE_URL}" -X -qAt -v ON_ERROR_STOP=1 \
  -v race_kind="${PERF_RACE_KIND}" \
  -v concurrency="${PERF_RACE_CONCURRENCY}" \
  -v marker="${PERF_RACE_MARKER}" \
  -v phone="${PERF_RACE_PHONE}" \
  -f performance/sql/capture-auth-integrity.sql > "${path}"

"${PYTHON_BIN:-python3}" performance/reporting/json_tools.py assert-invariant "${path}"
echo "Captured ${PERF_RACE_KIND}/${PERF_RACE_CONCURRENCY} database integrity evidence."
