#!/usr/bin/env bash
set -euo pipefail

report_path="${REDIS_FAILOVER_REPORT_PATH:-artifacts/redis-failover/redis-failover-report.json}"
output_dir="${REDIS_FAILOVER_OUTPUT_DIR:-artifacts/redis-failover}"

mkdir -p "${output_dir}"

python3 - "$report_path" "$output_dir" <<'PY'
import json
import pathlib
import sys

report_path, output_dir = sys.argv[1:3]
output = pathlib.Path(output_dir)

try:
    report = json.loads(pathlib.Path(report_path).read_text(encoding="utf-8"))
except OSError as exc:
    raise SystemExit(f"Could not read Redis failover report: {exc}")
except json.JSONDecodeError as exc:
    raise SystemExit(f"Redis failover report is not valid JSON: {exc}")

failures = []

def require(path, predicate, message):
    value = report
    for part in path.split("."):
        if not isinstance(value, dict) or part not in value:
            failures.append(message)
            return
        value = value[part]
    if not predicate(value):
        failures.append(message)

require("environment", lambda value: value == "Staging", "environment must be Staging.")
require("topology.primaryCount", lambda value: value == 1, "topology.primaryCount must equal 1.")
require("topology.replicaCount", lambda value: isinstance(value, int) and value >= 1, "topology.replicaCount must be at least 1.")
require("topology.automaticFailover", lambda value: value is True, "topology.automaticFailover must be true.")
require("failover.replicaPromotedAtUtc", lambda value: isinstance(value, str) and value.strip(), "replica promotion timestamp is required.")
require("failover.clientRecoveredAtUtc", lambda value: isinstance(value, str) and value.strip(), "client recovery timestamp is required.")
require("failover.fullRecoveryDurationSeconds", lambda value: isinstance(value, (int, float)) and value >= 0, "full recovery duration is required.")
require("traffic.totalRequests", lambda value: isinstance(value, int) and value > 0, "traffic.totalRequests must be greater than 0.")
require("traffic.successRate", lambda value: isinstance(value, (int, float)) and value >= 0.95, "traffic.successRate must be at least 0.95.")
require("applicationRestartRequired", lambda value: value is False, "applicationRestartRequired must be false.")
require("securityInvariantViolation", lambda value: value is False, "securityInvariantViolation must be false.")
require("features.outputCache", lambda value: value in ("passed", "degraded", "recovered"), "output cache behavior must be verified.")
require("features.rateLimiting", lambda value: value in ("protected", "passed", "recovered"), "rate limiting behavior must be protected.")
require("features.authentication", lambda value: value in ("passed", "protected"), "authentication behavior must be verified.")
require("features.signalR", lambda value: value in ("passed", "degraded", "recovered", "not-applicable"), "SignalR behavior must be measured or explicitly not applicable.")
require("monitoring.alertsFired", lambda value: value is True, "monitoring.alertsFired must be true.")
require("monitoring.alertsResolved", lambda value: value is True, "monitoring.alertsResolved must be true.")
require("result", lambda value: value == "passed", "result must be passed.")

summary = [
    "# Redis Recovery Verification",
    "",
    "Status: " + ("failed" if failures else "passed"),
]

if failures:
    summary += ["", "Failures:"]
    summary += [f"- {failure}" for failure in failures]

(output / "redis-recovery-verification-summary.md").write_text("\n".join(summary) + "\n", encoding="utf-8")
(output / "redis-recovery-verification.json").write_text(json.dumps({
    "status": "failed" if failures else "passed",
    "failures": failures
}, indent=2), encoding="utf-8")

print("\n".join(summary))

if failures:
    raise SystemExit(1)
PY
