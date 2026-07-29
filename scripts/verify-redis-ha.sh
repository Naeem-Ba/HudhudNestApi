#!/usr/bin/env bash
set -euo pipefail

evidence_path="${REDIS_HA_EVIDENCE_PATH:-}"
evidence_json="${REDIS_HA_EVIDENCE_JSON:-}"
output_dir="${REDIS_HA_OUTPUT_DIR:-artifacts/redis-ha}"

mkdir -p "${output_dir}"

python3 - "$evidence_path" "$evidence_json" "$output_dir" <<'PY'
import json
import pathlib
import sys

evidence_path, evidence_json, output_dir = sys.argv[1:4]
output = pathlib.Path(output_dir)
output.mkdir(parents=True, exist_ok=True)

failures = []

if evidence_path:
    try:
        payload = pathlib.Path(evidence_path).read_text(encoding="utf-8")
    except OSError as exc:
        raise SystemExit(f"Could not read Redis HA evidence file: {exc}")
elif evidence_json:
    payload = evidence_json
else:
    raise SystemExit("REDIS_HA_EVIDENCE_JSON or REDIS_HA_EVIDENCE_PATH is required.")

try:
    evidence = json.loads(payload)
except json.JSONDecodeError as exc:
    raise SystemExit(f"Redis HA evidence is not valid JSON: {exc}")

def require(name, predicate, message):
    if not predicate(evidence.get(name)):
        failures.append(message)

require("environment", lambda value: value == "Staging", "environment must be Staging.")
require("provider", lambda value: isinstance(value, str) and value.strip(), "provider is required.")
require("serviceTier", lambda value: isinstance(value, str) and value.strip(), "serviceTier is required.")
require("primaryCount", lambda value: isinstance(value, int) and value == 1, "primaryCount must equal 1.")
require("replicaCount", lambda value: isinstance(value, int) and value >= 1, "replicaCount must be at least 1.")
require("automaticFailover", lambda value: value is True, "automaticFailover must be true.")
require("tlsRequired", lambda value: value is True, "tlsRequired must be true.")
require("networkRestricted", lambda value: value is True, "networkRestricted must be true.")
require("endpointStrategy", lambda value: isinstance(value, str) and value.strip(), "endpointStrategy is required.")
require("providerSlaReference", lambda value: isinstance(value, str) and value.strip(), "providerSlaReference is required.")
require("monitoringConfigured", lambda value: value is True, "monitoringConfigured must be true.")
require("alertsConfigured", lambda value: value is True, "alertsConfigured must be true.")

for forbidden in ("connectionString", "password", "token", "secret", "credential"):
    if forbidden in json.dumps(evidence, ensure_ascii=False).lower():
        failures.append(f"evidence must not contain sensitive field text: {forbidden}.")

summary = [
    "# Redis HA Validation",
    "",
    f"Provider: {evidence.get('provider', '')}",
    f"Service tier: {evidence.get('serviceTier', '')}",
    f"Primary count: {evidence.get('primaryCount', '')}",
    f"Replica count: {evidence.get('replicaCount', '')}",
    f"Automatic failover: {evidence.get('automaticFailover', '')}",
    f"TLS required: {evidence.get('tlsRequired', '')}",
    f"Network restricted: {evidence.get('networkRestricted', '')}",
    "",
    "Status: " + ("failed" if failures else "passed"),
]

if failures:
    summary += ["", "Failures:"]
    summary += [f"- {failure}" for failure in failures]

(output / "redis-ha-validation-summary.md").write_text("\n".join(summary) + "\n", encoding="utf-8")
(output / "redis-ha-validation.json").write_text(json.dumps({
    "status": "failed" if failures else "passed",
    "failures": failures,
    "provider": evidence.get("provider"),
    "serviceTier": evidence.get("serviceTier"),
    "primaryCount": evidence.get("primaryCount"),
    "replicaCount": evidence.get("replicaCount"),
    "automaticFailover": evidence.get("automaticFailover"),
    "tlsRequired": evidence.get("tlsRequired"),
    "networkRestricted": evidence.get("networkRestricted"),
}, indent=2), encoding="utf-8")

print("\n".join(summary))

if failures:
    raise SystemExit(1)
PY
