#!/usr/bin/env bash
set -Eeuo pipefail

: "${PERF_DATABASE_URL:?PERF_DATABASE_URL is required}"
: "${PERF_ENVIRONMENT:?PERF_ENVIRONMENT is required}"

case "${PERF_ENVIRONMENT}" in
  Local|CI|Performance|Staging) ;;
  *) echo "ERROR: query-plan capture is restricted to isolated performance environments." >&2; exit 1 ;;
esac

command -v psql >/dev/null || { echo "ERROR: psql is required." >&2; exit 1; }
PYTHON_BIN="${PYTHON_BIN:-python3}"
"${PYTHON_BIN}" --version >/dev/null 2>&1 || { echo "ERROR: Python 3 is required." >&2; exit 1; }

output="${PERF_ARTIFACTS_DIR:-artifacts/performance}/query-plans"
mkdir -p "${output}"

for sql_file in performance/sql/plans/*.sql; do
  plan_name="$(basename "${sql_file}" .sql)"
  psql "${PERF_DATABASE_URL}" -X -qAt -v ON_ERROR_STOP=1 -f "${sql_file}" \
    > "${output}/${plan_name}.json"
  "${PYTHON_BIN}" -m json.tool "${output}/${plan_name}.json" >/dev/null
done

analysis_path="${PERF_ARTIFACTS_DIR:-artifacts/performance}/query-plan-analysis.json"
"${PYTHON_BIN}" performance/query-analysis/analyze_plans.py \
  "${output}" \
  performance/performance-budgets.json \
  "${analysis_path}"

"${PYTHON_BIN}" - "${analysis_path}" "${output}/query-plans-summary.md" <<'PY'
import json, pathlib, sys
data = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
lines = ["# Query-plan analysis", "", "| Plan | Execution ms | Rows | Indexes | Seq scans | Temp blocks | Result |", "|---|---:|---:|---|---:|---:|---:|"]
for plan in data["plans"]:
    indexes = ", ".join(plan["indexes"]) or "none"
    lines.append(f'| {plan["planFile"]} | {plan["executionTimeMilliseconds"]} | {plan["actualRows"]} | {indexes} | {plan["sequentialScanCount"]} | {plan["temporaryBlocks"]} | {"Passed" if plan["passed"] else "Failed"} |')
pathlib.Path(sys.argv[2]).write_text("\n".join(lines) + "\n", encoding="utf-8")
PY
