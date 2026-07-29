#!/usr/bin/env bash
set -Eeuo pipefail

current="${1:-artifacts/performance/summary.json}"
baseline="${2:-performance/baselines/approved-baseline.json}"
output="${3:-artifacts/performance/baseline-comparison.json}"

if [ ! -f "${baseline}" ]; then
  echo "ERROR: approved comparable performance baseline is missing: ${baseline}" >&2
  exit 1
fi

"${PYTHON_BIN:-python3}" performance/reporting/compare.py \
  "${current}" \
  "${baseline}" \
  performance/performance-budgets.json \
  "${output}"
