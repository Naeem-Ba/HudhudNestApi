#!/usr/bin/env bash
set -euo pipefail

environment="${REDIS_FAILOVER_ENVIRONMENT:-}"
base_url="${STAGING_BASE_URL:-${BASE_URL:-}}"
failover_command="${REDIS_PROVIDER_FAILOVER_COMMAND:-}"
report_path="${REDIS_FAILOVER_REPORT_PATH:-artifacts/redis-failover/redis-failover-report.json}"
summary_path="${REDIS_FAILOVER_SUMMARY_PATH:-artifacts/redis-failover/redis-failover-summary.md}"
junit_path="${REDIS_FAILOVER_JUNIT_PATH:-artifacts/redis-failover/redis-failover-junit.xml}"

mkdir -p "$(dirname "${report_path}")"

if [ "${environment}" != "Staging" ]; then
  echo "::error::REDIS_FAILOVER_ENVIRONMENT must be Staging."
  exit 1
fi

if [ -z "${base_url}" ]; then
  echo "::error::STAGING_BASE_URL or BASE_URL is required."
  exit 1
fi

if [ -z "${failover_command}" ]; then
  echo "::error::REDIS_PROVIDER_FAILOVER_COMMAND is required. Use the provider-supported failover command; do not restart a standalone Redis container."
  exit 1
fi

base_url="${base_url%/}"

start_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

pre_status="$(curl --silent --output /dev/null --write-out "%{http_code}" --max-time 10 "${base_url}/health/live" || true)"
if [ "${pre_status}" != "200" ]; then
  echo "::error::Staging API liveness must be healthy before Redis failover. Status=${pre_status}."
  exit 1
fi

set +e
bash -c "${failover_command}"
failover_exit="$?"
set -e

if [ "${failover_exit}" != "0" ]; then
  echo "::error::Provider Redis failover command failed."
  exit 1
fi

successful=0
failed=0
latencies=()

for _ in $(seq 1 "${REDIS_FAILOVER_TRAFFIC_REQUESTS:-60}"); do
  started_ms="$(date +%s%3N)"
  status="$(curl --silent --output /dev/null --write-out "%{http_code}" --max-time 10 "${base_url}/health/live" || true)"
  ended_ms="$(date +%s%3N)"
  latencies+=("$((ended_ms - started_ms))")
  if [ "${status}" = "200" ]; then
    successful="$((successful + 1))"
  else
    failed="$((failed + 1))"
  fi
  sleep "${REDIS_FAILOVER_TRAFFIC_DELAY_SECONDS:-1}"
done

end_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

python3 - "$report_path" "$summary_path" "$junit_path" "$start_utc" "$end_utc" "$successful" "$failed" "${latencies[@]}" <<'PY'
import json
import pathlib
import statistics
import sys
import xml.etree.ElementTree as ET

report_path, summary_path, junit_path, start_utc, end_utc, successful, failed, *latencies = sys.argv[1:]
successful = int(successful)
failed = int(failed)
latencies = [int(value) for value in latencies]
total = successful + failed

def percentile(values, percent):
    if not values:
        return 0
    values = sorted(values)
    index = min(len(values) - 1, round((percent / 100) * (len(values) - 1)))
    return values[index]

report = {
    "runId": start_utc.replace(":", "").replace("-", ""),
    "environment": "Staging",
    "failover": {
        "startedAtUtc": start_utc,
        "featuresRecoveredAtUtc": end_utc
    },
    "traffic": {
        "totalRequests": total,
        "successfulRequests": successful,
        "failedRequests": failed,
        "successRate": round(successful / total, 4) if total else 0,
        "p95Milliseconds": percentile(latencies, 95),
        "p99Milliseconds": percentile(latencies, 99)
    },
    "applicationRestartRequired": None,
    "securityInvariantViolation": None,
    "signalRVerified": False,
    "result": "failed"
}

pathlib.Path(report_path).write_text(json.dumps(report, indent=2), encoding="utf-8")

summary = [
    "# Redis Failover Drill",
    "",
    "Status: failed",
    "",
    "The provider failover command executed and liveness traffic was sampled.",
    "This generated report is intentionally failed until post-failover topology, security invariants, SignalR behavior, and restart count are verified by `verify-redis-recovery.sh`."
]
pathlib.Path(summary_path).write_text("\n".join(summary) + "\n", encoding="utf-8")

testsuite = ET.Element("testsuite", name="redis-failover", tests="1", failures="1")
testcase = ET.SubElement(testsuite, "testcase", name="redis-failover-evidence")
failure = ET.SubElement(testcase, "failure", message="post-failover verification required")
failure.text = "Run scripts/verify-redis-recovery.sh with a completed failover report."
ET.ElementTree(testsuite).write(junit_path, encoding="utf-8", xml_declaration=True)
PY

echo "Redis failover drill report generated at ${report_path}."
exit 1
