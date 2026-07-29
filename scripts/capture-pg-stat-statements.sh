#!/usr/bin/env bash
set -Eeuo pipefail

: "${PERF_DATABASE_URL:?PERF_DATABASE_URL is required}"
: "${PERF_ENVIRONMENT:?PERF_ENVIRONMENT is required}"

case "${PERF_ENVIRONMENT}" in
  Local|CI|Performance|Staging) ;;
  *) echo "ERROR: pg_stat_statements capture is restricted to isolated performance environments." >&2; exit 1 ;;
esac

command -v psql >/dev/null || { echo "ERROR: psql is required." >&2; exit 1; }
PYTHON_BIN="${PYTHON_BIN:-python3}"
"${PYTHON_BIN}" --version >/dev/null 2>&1 || { echo "ERROR: Python 3 is required." >&2; exit 1; }

root="${PERF_ARTIFACTS_DIR:-artifacts/performance}/postgresql"
mkdir -p "${root}"
export PGCONNECT_TIMEOUT="${PERF_DATABASE_CONNECT_TIMEOUT_SECONDS:-10}"

psql "${PERF_DATABASE_URL}" -X -qAt \
  -f performance/sql/capture-pg-stat-statements.sql \
  > "${root}/pg-stat-statements.json"
"${PYTHON_BIN}" performance/reporting/json_tools.py validate-array \
  "${root}/pg-stat-statements.json"

psql "${PERF_DATABASE_URL}" -X --csv -q \
  -f performance/sql/capture-pg-stat-statements.csv.sql \
  > "${root}/pg-stat-statements.csv"

psql "${PERF_DATABASE_URL}" -X -qAt \
  -f performance/sql/capture-database-diagnostics.sql \
  > "${PERF_ARTIFACTS_DIR:-artifacts/performance}/database-results.json"
"${PYTHON_BIN}" performance/reporting/json_tools.py validate-object \
  "${PERF_ARTIFACTS_DIR:-artifacts/performance}/database-results.json"

"${PYTHON_BIN}" performance/reporting/json_tools.py slow-markdown \
  "${root}/pg-stat-statements.json" "${root}/slow-queries.md"

echo "Captured sanitized PostgreSQL workload statistics."
