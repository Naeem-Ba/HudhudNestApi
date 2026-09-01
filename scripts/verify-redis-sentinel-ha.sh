#!/usr/bin/env bash
# Real, CI-native Redis Sentinel HA failover drill.
#
# Unlike scripts/verify-redis-ha.sh (which validates a *managed provider's*
# self-reported HA evidence JSON) and scripts/test-redis-failover.sh (which
# triggers a *managed provider's* failover command against a live Staging
# deployment), this script proves automatic failover against a real,
# self-hosted Redis OSS + Replication + Sentinel topology that this CI job
# itself brings up via ci/docker-compose.production-gate.yml. It does not
# depend on any external provider, secret, or live deployment.
#
# It is intentionally strict: every stage is a real command against a real
# Redis/Sentinel process, not a "container exited" or "ping succeeded"
# shortcut (see docs/REDIS-HA.md and section 31/42 of the task this
# implements). Any stage that fails prints diagnostics and exits non-zero --
# no `|| true`, no swallowed failures.
#
# See docs/REDIS-HA.md for the full architecture, what this does and does
# NOT prove, and how it relates to Production Gate's mandatory checks.
set -euo pipefail

compose_file="${COMPOSE_FILE:-ci/docker-compose.production-gate.yml}"
output_dir="${REDIS_SENTINEL_HA_OUTPUT_DIR:-artifacts/redis-sentinel-ha}"
replication_timeout_seconds="${REDIS_SENTINEL_HA_REPLICATION_TIMEOUT_SECONDS:-30}"
quorum_timeout_seconds="${REDIS_SENTINEL_HA_QUORUM_TIMEOUT_SECONDS:-30}"
failover_timeout_seconds="${REDIS_SENTINEL_HA_FAILOVER_TIMEOUT_SECONDS:-90}"

# Static IPs assigned in ci/docker-compose.production-gate.yml's redis-ha
# network. Sentinel is configured to address the primary by IP, not by
# Docker DNS hostname -- see ci/redis/sentinel/sentinel.conf for why
# (a real drill run showed Docker's embedded DNS stops resolving a stopped
# container's hostname, which stalls Sentinel in TILT mode forever).
primary_ip="172.28.0.10"
replica_ip="172.28.0.11"

mkdir -p "${output_dir}"
report_json="${output_dir}/redis-sentinel-ha-report.json"
summary_md="${output_dir}/redis-sentinel-ha-summary.md"

dc() {
  docker compose -f "${compose_file}" "$@"
}

step=0
total_steps=9
mark() {
  step=$((step + 1))
  echo ""
  echo "[${step}/${total_steps}] $*"
}

pass() { echo "PASS"; }

fail_marker="========================================"

print_diagnostics() {
  echo ""
  echo "${fail_marker}"
  echo "REDIS SENTINEL HA FAILOVER TEST: FAIL -- diagnostics follow"
  echo "${fail_marker}"

  echo "--- docker compose ps ---"
  dc ps || true

  for svc in redis-primary redis-replica redis-sentinel-1 redis-sentinel-2 redis-sentinel-3; do
    echo "--- docker compose logs ${svc} (last 100 lines) ---"
    dc logs --no-color --tail=100 "${svc}" || true
  done

  echo "--- SENTINEL masters (via redis-sentinel-1, if reachable) ---"
  dc exec -T redis-sentinel-1 redis-cli -p 26379 sentinel masters || true

  echo "--- SENTINEL replicas mymaster (via redis-sentinel-1, if reachable) ---"
  dc exec -T redis-sentinel-1 redis-cli -p 26379 sentinel replicas mymaster || true

  echo "--- SENTINEL sentinels mymaster (via redis-sentinel-1, if reachable) ---"
  dc exec -T redis-sentinel-1 redis-cli -p 26379 sentinel sentinels mymaster || true

  echo "--- redis-replica INFO replication (if reachable) ---"
  dc exec -T redis-replica redis-cli info replication || true
}

on_failure() {
  local exit_code=$?
  if [ "${exit_code}" -ne 0 ]; then
    print_diagnostics
  fi
  exit "${exit_code}"
}
trap on_failure EXIT

echo "${fail_marker}"
echo "REDIS SENTINEL HA VERIFICATION"
echo "${fail_marker}"

# --- [1/9] Connectivity ------------------------------------------------
mark "Checking connectivity: redis-primary, redis-replica, sentinel-1/2/3"
dc exec -T redis-primary redis-cli ping | grep -q PONG
dc exec -T redis-replica redis-cli ping | grep -q PONG
dc exec -T redis-sentinel-1 redis-cli -p 26379 ping | grep -q PONG
dc exec -T redis-sentinel-2 redis-cli -p 26379 ping | grep -q PONG
dc exec -T redis-sentinel-3 redis-cli -p 26379 ping | grep -q PONG
pass

# --- [2/9] Replication ---------------------------------------------------
mark "Checking replication (primary=master, replica=slave, link up)"

primary_role="$(dc exec -T redis-primary redis-cli role | head -n1 | tr -d '\r')"
if [ "${primary_role}" != "master" ]; then
  echo "::error::redis-primary ROLE is '${primary_role}', expected 'master'."
  exit 1
fi

replica_role="$(dc exec -T redis-replica redis-cli role | head -n1 | tr -d '\r')"
if [ "${replica_role}" != "slave" ]; then
  echo "::error::redis-replica ROLE is '${replica_role}', expected 'slave'."
  exit 1
fi

deadline=$((SECONDS + replication_timeout_seconds))
replication_ready=false
while [ "${SECONDS}" -lt "${deadline}" ]; do
  connected_slaves="$(dc exec -T redis-primary redis-cli info replication | tr -d '\r' | awk -F: '/^connected_slaves:/{print $2}')"
  link_status="$(dc exec -T redis-replica redis-cli info replication | tr -d '\r' | awk -F: '/^master_link_status:/{print $2}')"
  echo "  waiting for replication: connected_slaves=${connected_slaves:-0} master_link_status=${link_status:-unknown} (elapsed ${SECONDS}s)"
  if [ "${connected_slaves:-0}" -ge 1 ] 2>/dev/null && [ "${link_status:-}" = "up" ]; then
    replication_ready=true
    break
  fi
  sleep 2
done

if [ "${replication_ready}" != "true" ]; then
  echo "::error::Replication did not become healthy within ${replication_timeout_seconds}s (connected_slaves>=1 and master_link_status=up required)."
  exit 1
fi
echo "  connected_slaves=${connected_slaves} master_link_status=${link_status}"
pass

# --- [3/9] Sentinel quorum ------------------------------------------------
mark "Checking Sentinel quorum (mutual discovery of all 3 sentinels)"

deadline=$((SECONDS + quorum_timeout_seconds))
quorum_ready=false
while [ "${SECONDS}" -lt "${deadline}" ]; do
  ok=true
  for sentinel in redis-sentinel-1 redis-sentinel-2 redis-sentinel-3; do
    known_master="$(dc exec -T "${sentinel}" redis-cli -p 26379 sentinel masters | tr -d '\r' | awk '/^ip$/{getline; print; exit}')"
    other_sentinels="$(dc exec -T "${sentinel}" redis-cli -p 26379 sentinel masters | tr -d '\r' | awk '/^num-other-sentinels$/{getline; print; exit}')"
    echo "  ${sentinel}: sees master ip=${known_master:-?} num-other-sentinels=${other_sentinels:-?}"
    if [ "${known_master:-}" != "${primary_ip}" ] || [ "${other_sentinels:-0}" -lt 2 ] 2>/dev/null; then
      ok=false
    fi
  done
  if [ "${ok}" = "true" ]; then
    quorum_ready=true
    break
  fi
  sleep 2
done

if [ "${quorum_ready}" != "true" ]; then
  echo "::error::Sentinel quorum was not established within ${quorum_timeout_seconds}s (each of the 3 sentinels must see the master and the other 2 sentinels)."
  exit 1
fi
pass

# --- [4/9] Sentinel discovery (pre-failover) ------------------------------
mark "Verifying current master via Sentinel discovery"
discovered_master="$(dc exec -T redis-sentinel-1 redis-cli -p 26379 sentinel get-master-addr-by-name mymaster | tr -d '\r' | head -n1)"
echo "  SENTINEL get-master-addr-by-name mymaster -> ${discovered_master}"
if [ "${discovered_master}" != "${primary_ip}" ]; then
  echo "::error::Sentinel-discovered master (${discovered_master}) does not match the expected primary (${primary_ip})."
  exit 1
fi
pass

# --- [5/9] Sentinel replica discovery (pre-failover) -----------------------
mark "Verifying Sentinel has independently discovered redis-replica (not just direct replication)"
# Sentinel learns a monitored master's replicas by periodically polling the
# *master's own* `INFO replication` output (default cadence: every 10s) --
# entirely separate from, and not driven by, the replica's own self-reported
# link status already confirmed in step 2. If redis-primary is killed before
# every Sentinel's internal replica table has actually been populated,
# `sentinelSelectSlave()` finds zero promotion candidates at failover time
# and every attempt aborts with `-failover-abort-no-good-slave` -- Sentinel
# retries the *failover*, not the missed *discovery*, so once this window is
# missed it stays missed for the rest of the run; the drill hangs until
# failover_timeout_seconds and then fails deterministically, not flakily.
# See docs/REDIS-HA.md.
replica_discovery_timeout_seconds="${REDIS_SENTINEL_HA_REPLICA_DISCOVERY_TIMEOUT_SECONDS:-30}"
deadline=$((SECONDS + replica_discovery_timeout_seconds))
replica_discovered=false
while [ "${SECONDS}" -lt "${deadline}" ]; do
  ok=true
  for sentinel in redis-sentinel-1 redis-sentinel-2 redis-sentinel-3; do
    replica_ip_seen="$(dc exec -T "${sentinel}" redis-cli -p 26379 sentinel replicas mymaster | tr -d '\r' | awk '/^ip$/{getline; print; exit}')"
    echo "  ${sentinel}: sees replica ip=${replica_ip_seen:-none}"
    if [ "${replica_ip_seen:-}" != "${replica_ip}" ]; then
      ok=false
    fi
  done
  if [ "${ok}" = "true" ]; then
    replica_discovered=true
    break
  fi
  sleep 2
done

if [ "${replica_discovered}" != "true" ]; then
  echo "::error::Sentinel had not discovered redis-replica (${replica_ip}) as mymaster's replica within ${replica_discovery_timeout_seconds}s (checked SENTINEL REPLICAS on all 3 sentinels). Killing the primary now would make failover unwinnable -- see docs/REDIS-HA.md."
  exit 1
fi
pass

# --- Pre-failover: write test data ---------------------------------------
echo ""
echo "Writing pre-failover test data..."
test_value="ha-drill-$(date -u +%Y%m%dT%H%M%SZ)-${RANDOM}"
dc exec -T redis-primary redis-cli set production_gate_redis_ha_test "${test_value}" > /dev/null
read_back="$(dc exec -T redis-primary redis-cli get production_gate_redis_ha_test | tr -d '\r')"
if [ "${read_back}" != "${test_value}" ]; then
  echo "::error::Pre-failover write/read mismatch on redis-primary (wrote '${test_value}', read '${read_back}')."
  exit 1
fi
echo "  wrote production_gate_redis_ha_test=${test_value}, confirmed readable on redis-primary."

old_primary="${primary_ip}"

# --- [6/9] Kill the primary ------------------------------------------------
mark "Stopping redis-primary (real container stop, not a mock)"
failover_started_epoch="$(date -u +%s)"
failover_started_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
dc stop redis-primary
echo "  redis-primary stopped at ${failover_started_utc}."
pass

# --- [7/9] Wait for automatic failover -------------------------------------
mark "Waiting for Sentinel to detect the failure and promote redis-replica (timeout ${failover_timeout_seconds}s)"
deadline=$((SECONDS + failover_timeout_seconds))
failover_detected=false
while [ "${SECONDS}" -lt "${deadline}" ]; do
  current_master="$(dc exec -T redis-sentinel-1 redis-cli -p 26379 sentinel get-master-addr-by-name mymaster 2>/dev/null | tr -d '\r' | head -n1 || true)"
  echo "  elapsed=${SECONDS}s sentinel-reported master=${current_master:-unknown}"
  if [ "${current_master}" = "${replica_ip}" ]; then
    failover_detected=true
    break
  fi
  sleep 2
done

if [ "${failover_detected}" != "true" ]; then
  echo "::error::Sentinel did not promote redis-replica (${replica_ip}) within ${failover_timeout_seconds}s."
  exit 1
fi

failover_detected_epoch="$(date -u +%s)"
failover_detected_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
failover_duration_seconds=$((failover_detected_epoch - failover_started_epoch))
echo "  Sentinel reports new master=${replica_ip} at ${failover_detected_utc} (${failover_duration_seconds}s after stop)."

# A majority of the surviving Sentinels must agree, not just the one we polled.
agreeing=0
for sentinel in redis-sentinel-1 redis-sentinel-2 redis-sentinel-3; do
  addr="$(dc exec -T "${sentinel}" redis-cli -p 26379 sentinel get-master-addr-by-name mymaster 2>/dev/null | tr -d '\r' | head -n1 || true)"
  echo "  ${sentinel} reports master=${addr:-unreachable}"
  if [ "${addr}" = "${replica_ip}" ]; then
    agreeing=$((agreeing + 1))
  fi
done
if [ "${agreeing}" -lt 2 ]; then
  echo "::error::Only ${agreeing}/3 Sentinels agree on the new master (need a quorum majority of 2)."
  exit 1
fi
pass

# --- [8/9] Verify promotion directly against Redis (not just Sentinel's claim) ---
mark "Verifying promotion via ROLE against redis-replica directly"
new_role="$(dc exec -T redis-replica redis-cli role | head -n1 | tr -d '\r')"
echo "  redis-replica ROLE -> ${new_role}"
if [ "${new_role}" != "master" ]; then
  echo "::error::redis-replica reports ROLE='${new_role}' after failover; expected 'master'. Sentinel claiming a new master is not sufficient -- Redis itself must confirm it."
  exit 1
fi
pass

# --- [9/9] Data preserved + post-failover writes accepted + Sentinel stable ---
mark "Verifying data survived failover, new master accepts writes, Sentinel cluster is stable"

preserved_value="$(dc exec -T redis-replica redis-cli get production_gate_redis_ha_test | tr -d '\r')"
echo "  production_gate_redis_ha_test on promoted master = '${preserved_value}' (expected '${test_value}')"
data_preserved=false
if [ "${preserved_value}" = "${test_value}" ]; then
  data_preserved=true
else
  echo "::error::Data written before failover ('${test_value}') was not found on the promoted master (got '${preserved_value}')."
  exit 1
fi

post_failover_value="ha-drill-post-failover-$(date -u +%Y%m%dT%H%M%SZ)-${RANDOM}"
dc exec -T redis-replica redis-cli set production_gate_redis_ha_post_failover_test "${post_failover_value}" > /dev/null
post_failover_readback="$(dc exec -T redis-replica redis-cli get production_gate_redis_ha_post_failover_test | tr -d '\r')"
post_failover_write_ok=false
if [ "${post_failover_readback}" = "${post_failover_value}" ]; then
  post_failover_write_ok=true
  echo "  post-failover write/read OK (production_gate_redis_ha_post_failover_test=${post_failover_value})."
else
  echo "::error::Post-failover write failed: wrote '${post_failover_value}', read back '${post_failover_readback}'."
  exit 1
fi

sentinel_stable=true
for sentinel in redis-sentinel-1 redis-sentinel-2 redis-sentinel-3; do
  flags="$(dc exec -T "${sentinel}" redis-cli -p 26379 sentinel masters | tr -d '\r' | awk '/^flags$/{getline; print; exit}')"
  echo "  ${sentinel} post-failover master flags = ${flags:-unknown}"
  if echo "${flags:-}" | grep -qE 's_down|o_down'; then
    sentinel_stable=false
  fi
done
if [ "${sentinel_stable}" != "true" ]; then
  echo "::error::Sentinel cluster did not return to a stable state (down flags still present) after failover."
  exit 1
fi
pass

result="passed"

echo ""
echo "${fail_marker}"
echo "REDIS SENTINEL HA FAILOVER TEST: PASS"
echo "${fail_marker}"

jq -n \
  --arg oldPrimary "${old_primary}" \
  --arg newPrimary "${replica_ip}" \
  --arg failoverStartedAtUtc "${failover_started_utc}" \
  --arg failoverDetectedAtUtc "${failover_detected_utc}" \
  --argjson failoverDurationSeconds "${failover_duration_seconds}" \
  --argjson dataPreserved "${data_preserved}" \
  --argjson postFailoverWriteAccepted "${post_failover_write_ok}" \
  --argjson sentinelStableAfterFailover "${sentinel_stable}" \
  --argjson sentinelsAgreeingOnNewMaster "${agreeing}" \
  --arg result "${result}" \
  '{
    topology: "self-hosted-redis-sentinel",
    replication: "passed",
    sentinelQuorum: "passed",
    oldPrimary: $oldPrimary,
    newPrimary: $newPrimary,
    failover: {
      startedAtUtc: $failoverStartedAtUtc,
      detectedAtUtc: $failoverDetectedAtUtc,
      durationSeconds: $failoverDurationSeconds,
      sentinelsAgreeingOnNewMaster: $sentinelsAgreeingOnNewMaster
    },
    dataPreserved: $dataPreserved,
    postFailoverWriteAccepted: $postFailoverWriteAccepted,
    sentinelStableAfterFailover: $sentinelStableAfterFailover,
    result: $result
  }' > "${report_json}"

{
  echo "# Redis Sentinel HA Failover Drill"
  echo ""
  echo "Status: passed"
  echo ""
  echo "| Check | Result |"
  echo "|---|---|"
  echo "| Replication | PASS |"
  echo "| Sentinel quorum | PASS |"
  echo "| Automatic failover | PASS (${failover_duration_seconds}s) |"
  echo "| Replica promotion | PASS |"
  echo "| Data preserved | PASS |"
  echo "| Post-failover writes | PASS |"
  echo ""
  echo "Old primary: ${old_primary}"
  echo "New primary: ${replica_ip}"
} > "${summary_md}"

trap - EXIT
exit 0
