#!/usr/bin/env bash
set -euo pipefail

API_BASE_URL="${API_BASE_URL:-http://127.0.0.1:18080}"
PROMETHEUS_URL="${PROMETHEUS_URL:-http://127.0.0.1:19090}"
TEMPO_URL="${TEMPO_URL:-http://127.0.0.1:13200}"
TEST_KEY="${OBSERVABILITY_TEST_KEY:-}"
SERVICE_NAME="${OBSERVABILITY_SERVICE_NAME:-hudhudnest-api}"
SERVICE_VERSION="${OBSERVABILITY_SERVICE_VERSION:-local-observability}"
EXPECTED_SHA="${EXPECTED_COMMIT_SHA:-local-observability}"
ENVIRONMENT="${OBSERVABILITY_ENVIRONMENT:-Staging}"
REPORT="${OBSERVABILITY_REPORT:-artifacts/observability/observability-verification-report.json}"
RUN_ID="obs-$(date -u +%Y%m%dT%H%M%SZ)-${RANDOM}"
STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
CORRELATION_ID="obs-${RUN_ID}"

for command in curl jq; do
  command -v "${command}" >/dev/null || { echo "${command} is required"; exit 2; }
done

if [ -z "${TEST_KEY}" ]; then
  echo "OBSERVABILITY_TEST_KEY is required"
  exit 2
fi

mkdir -p "$(dirname "${REPORT}")"

# If any command below aborts the script (set -e) before the final report is written, leave a
# machine-readable failure report instead of no file at all. The final jq overwrites it.
write_abort_report() {
  local status=$?
  if [ "${status}" -ne 0 ] && [ ! -s "${REPORT}" ]; then
    jq -n --arg runId "${RUN_ID}" --arg environment "${ENVIRONMENT}" --arg sha "${EXPECTED_SHA}" \
      --arg started "${STARTED_AT}" --arg status "${status}" \
      '{runId:$runId,environment:$environment,expectedCommitSha:$sha,startedAtUtc:$started,result:"failed",error:("verification aborted before completion (exit " + $status + ")")}' > "${REPORT}" || true
  fi
}
trap write_abort_report EXIT

# Telemetry is asynchronous: the API exports metrics every Observability:Metrics:ExportIntervalMilliseconds
# (30 s by default; the local compose stack sets 5 s), the collector batches, and on the real Staging
# stack (Render Free plan, no persistent disk) the collector, Prometheus and Tempo spin down when idle
# and start empty. The fixed 30-60 s windows this script used were sized for the 5 s local stack; on
# Staging they expired before any signal could arrive (run 35463103084: every trace, metric and the
# alert-firing check missed, although the same pipeline delivered 1,640 series and traces minutes
# later). Every signal is still required; waits are now bounded polls that return as soon as the
# signal appears, so the local stack is as fast as before.
WAIT_SECONDS="${OBSERVABILITY_WAIT_SECONDS:-180}"
POLL_SECONDS="${OBSERVABILITY_POLL_SECONDS:-3}"

wait_until() { # wait_until <seconds> <command...>
  local limit="$1" deadline
  shift
  deadline=$((SECONDS + limit))
  until "$@"; do
    [ "${SECONDS}" -lt "${deadline}" ] || return 1
    sleep "${POLL_SECONDS}"
  done
}

# Wake a spun-down backend (best effort; the checks below decide the result).
wake() { curl --silent --max-time 20 --output /dev/null --fail "$1" 2>/dev/null; }
wait_until 90 wake "${PROMETHEUS_URL}/-/ready" || echo "note: Prometheus did not report ready within 90s"
wait_until 90 wake "${TEMPO_URL}/ready" || echo "note: Tempo did not report ready within 90s"

# The collector sits between the API and both backends and spins down too (Render Free); the API's own
# pushes did not wake it in time in run 35466879093. Only when its URL is configured (Staging).
if [ -n "${OTEL_COLLECTOR_URL:-}" ]; then
  bash "$(dirname "${BASH_SOURCE[0]}")/wake-otel-collector.sh" "${OTEL_COLLECTOR_URL}" 120
fi

# Sentinel values (not credentials): they are sent on purpose and must never show up in any stored
# trace (see the leak check below). Kept in variables rather than a literal Authorization header
# so secret scanners do not flag a placeholder as a leaked credential.
SENTINEL_ACCESS_TOKEN="TEST_ACCESS_TOKEN_SHOULD_NOT_APPEAR"
SENTINEL_REFRESH_TOKEN="TEST_REFRESH_TOKEN_SHOULD_NOT_APPEAR"

send_synthetic() { # send_synthetic <correlation id>
  curl --fail --silent --show-error \
    -X POST \
    -H "X-Correlation-ID: $1" \
    -H "X-Observability-Test-Key: ${TEST_KEY}" \
    -H "Authorization: Bearer ${SENTINEL_ACCESS_TOKEN}" \
    -H "Cookie: refresh=${SENTINEL_REFRESH_TOKEN}" \
    "${API_BASE_URL}/internal/observability/synthetic?secret=TEST_PASSWORD_SHOULD_NOT_APPEAR" >/dev/null
}

send_synthetic "${CORRELATION_ID}"

# A deliberately invalid, synthetic login emits the bounded auth failure metric.
# ASP.NET Core instrumentation does not capture this request body.
curl --silent --show-error \
  -X POST \
  -H 'Content-Type: application/json' \
  -H "X-Correlation-ID: ${CORRELATION_ID}-auth" \
  --data '{"email":"TEST_EMAIL_SHOULD_NOT_APPEAR","password":"TEST_PASSWORD_SHOULD_NOT_APPEAR"}' \
  "${API_BASE_URL}/api/auth/login" >/dev/null

query_prometheus() {
  curl --fail --silent --show-error --get \
    --data-urlencode "query=$1" \
    "${PROMETHEUS_URL}/api/v1/query"
}

metric_found() {
  [ "$(query_prometheus "$1" 2>/dev/null | jq '.data.result | length' 2>/dev/null || echo 0)" -gt 0 ]
}

# Polls until the series exists. All metric checks share ONE WAIT_SECONDS budget (started just before
# the first check) so a dead pipeline costs one window, not one per query.
metrics_deadline=0
metric_seen() {
  local left=$((metrics_deadline - SECONDS))
  [ "${left}" -gt 0 ] || left=0
  wait_until "${left}" metric_found "$1"
}

# Search Tempo for the trace of any synthetic request sent so far. If the collector was still
# waking up when the first request was exported, that trace is lost, so the request is repeated
# (new correlation id) every ~45 s within the same bounded window.
synthetic_ids=("${CORRELATION_ID}")
find_trace() {
  local id found
  for id in "${synthetic_ids[@]}"; do
    found="$(curl --fail --silent --max-time 20 --get \
      --data-urlencode "q={ resource.service.name = \"${SERVICE_NAME}\" && span.correlation.id = \"${id}\" }" \
      "${TEMPO_URL}/api/search" 2>/dev/null | jq -r '.traces[0].traceID // empty' 2>/dev/null || true)"
    if [ -n "${found}" ]; then
      trace_id="${found}"
      return 0
    fi
  done
  return 1
}

trace_id=""
trace_deadline=$((SECONDS + WAIT_SECONDS))
next_resend=$((SECONDS + 45))
until find_trace; do
  [ "${SECONDS}" -lt "${trace_deadline}" ] || break
  if [ "${SECONDS}" -ge "${next_resend}" ]; then
    resend_id="${CORRELATION_ID}-r${#synthetic_ids[@]}"
    synthetic_ids+=("${resend_id}")
    send_synthetic "${resend_id}" || true
    next_resend=$((SECONDS + 45))
  fi
  sleep "${POLL_SECONDS}"
done

trace_json='{}'

contains_trace_attribute() {
  echo "${trace_json}" | jq -e --arg key "$1" --arg value "$2" \
    '.. | objects | select(.key? == $key and ((.value.stringValue? // .value.intValue? // "") | tostring | contains($value)))' \
    >/dev/null
}

server_span=false
postgres_span=false
redis_span=false
http_client_span=false

# Tempo returns a trace from the search as soon as its FIRST batch of spans is stored; the rest of the
# request's spans (the Redis PING, the Postgres query, ...) can arrive in a later export batch. Production
# Gate run 35469653186 evaluated the trace once, immediately, and reported no Redis span, although the
# same trace (checked afterwards) held PING with db.system=redis. Re-fetch and re-evaluate until every
# expected span is present, within a bounded window; a span that never arrives still fails the run.
evaluate_trace() {
  server_span=false
  postgres_span=false
  redis_span=false
  http_client_span=false
  echo "${trace_json}" | jq -e '.. | objects | select(.kind? == 2 or .kind? == "SPAN_KIND_SERVER")' >/dev/null && server_span=true
  (contains_trace_attribute "db.system.name" "postgresql" || contains_trace_attribute "db.system" "postgresql") && postgres_span=true
  (contains_trace_attribute "db.system.name" "redis" || contains_trace_attribute "db.system" "redis") && redis_span=true
  echo "${trace_json}" | jq -e '.. | objects | select(.kind? == 3 or .kind? == "SPAN_KIND_CLIENT")' >/dev/null && http_client_span=true
  return 0
}

trace_complete() {
  local fetched
  fetched="$(curl --fail --silent --max-time 20 "${TEMPO_URL}/api/traces/${trace_id}" 2>/dev/null)" && trace_json="${fetched}"
  evaluate_trace
  [ "${server_span}" = true ] && [ "${postgres_span}" = true ] && [ "${redis_span}" = true ] && [ "${http_client_span}" = true ]
}

if [ -n "${trace_id}" ]; then
  wait_until "${OBSERVABILITY_TRACE_COMPLETE_SECONDS:-90}" trace_complete || true
fi

metrics_deadline=$((SECONDS + WAIT_SECONDS))
request_metric=false
latency_metric=false
auth_metric=false
runtime_metric=false
metric_seen 'http_server_request_duration_seconds_count{service_name="hudhudnest-api"}' && request_metric=true
metric_seen 'http_server_request_duration_seconds_bucket{service_name="hudhudnest-api"}' && latency_metric=true
metric_seen 'hudhudnest_auth_requests_total' && auth_metric=true
metric_seen 'process_memory_working_set_bytes{service_name="hudhudnest-api"}' && runtime_metric=true

queries_valid=false
if metric_seen 'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="hudhudnest-api"}[5m])))' &&
   metric_seen 'histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="hudhudnest-api"}[5m])))'; then
  queries_valid=true
fi

# The alert rule itself is permanent, baked into Prometheus's configuration at deploy time
# (observability/prometheus/rules for local docker-compose; observability/render/prometheus
# for the real Staging deployment) and watches a Staging-only synthetic gauge instead of a
# rule file rewritten at runtime -- that mechanism only ever worked locally because Prometheus
# and this script shared a bind-mounted checkout; it cannot reach a real, remotely-deployed
# Staging Prometheus. Toggling the gauge through the API exercises the exact same full
# firing-then-resolved alert lifecycle in both environments via one code path.
set_alert_test_gauge() {
  curl --fail --silent --show-error \
    -X POST \
    -H 'Content-Type: application/json' \
    -H "X-Correlation-ID: ${CORRELATION_ID}-alert" \
    -H "X-Observability-Test-Key: ${TEST_KEY}" \
    --data "{\"firing\":$1}" \
    "${API_BASE_URL}/internal/observability/synthetic/alert-test-state" >/dev/null
}

alert_is_firing() {
  curl --fail --silent --show-error "${PROMETHEUS_URL}/api/v1/alerts" |
    jq -e '.data.alerts[] | select(.labels.alertname == "HudhudNestApiControlledObservabilityTest" and .state == "firing")' >/dev/null
}

# "Resolved" must mean Prometheus answered and no longer reports the alert as firing. The old check
# was `! alert_is_firing`, which is also true when the query itself fails (Prometheus down or
# still waking), so recoveryTestPassed could be true while nothing had ever fired.
alert_is_resolved() {
  local body
  body="$(curl --fail --silent --max-time 20 "${PROMETHEUS_URL}/api/v1/alerts" 2>/dev/null)" || return 1
  ! echo "${body}" | jq -e '.data.alerts[] | select(.labels.alertname == "HudhudNestApiControlledObservabilityTest" and .state == "firing")' >/dev/null
}

# The gauge is exported on the metric interval (30 s on Staging) before Prometheus can evaluate the
# rule, so firing needs the same bounded wait as the other signals, not 30 s.
set_alert_test_gauge true
firing=false
wait_until "${WAIT_SECONDS}" alert_is_firing && firing=true

set_alert_test_gauge false
resolved=false
if [ "${firing}" = true ]; then
  wait_until "${WAIT_SECONDS}" alert_is_resolved && resolved=true
fi

leaked=()
for sentinel in TEST_PASSWORD_SHOULD_NOT_APPEAR TEST_OTP_SHOULD_NOT_APPEAR \
  TEST_ACCESS_TOKEN_SHOULD_NOT_APPEAR TEST_REFRESH_TOKEN_SHOULD_NOT_APPEAR \
  TEST_PHONE_SHOULD_NOT_APPEAR TEST_EMAIL_SHOULD_NOT_APPEAR \
  TEST_CONNECTION_STRING_SHOULD_NOT_APPEAR; do
  echo "${trace_json}" | grep -Fq "${sentinel}" && leaked+=("${sentinel}")
done

result=passed
for value in "${server_span}" "${postgres_span}" "${redis_span}" "${http_client_span}" \
  "${request_metric}" "${latency_metric}" "${runtime_metric}" "${queries_valid}" "${firing}" "${resolved}"; do
  [ "${value}" = true ] || result=failed
done
[ "${#leaked[@]}" -eq 0 ] || result=failed

completed_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
jq -n \
  --arg runId "${RUN_ID}" --arg environment "${ENVIRONMENT}" \
  --arg serviceName "${SERVICE_NAME}" --arg serviceVersion "${SERVICE_VERSION}" \
  --arg expectedCommitSha "${EXPECTED_SHA}" --arg startedAtUtc "${STARTED_AT}" \
  --arg completedAtUtc "${completed_at}" --arg result "${result}" \
  --argjson serverSpanFound "${server_span}" --argjson postgresSpanFound "${postgres_span}" \
  --argjson redisSpanFound "${redis_span}" --argjson httpClientSpanFound "${http_client_span}" \
  --argjson requestMetricFound "${request_metric}" --argjson latencyHistogramFound "${latency_metric}" \
  --argjson authFailureMetricFound "${auth_metric}" --argjson runtimeMetricFound "${runtime_metric}" \
  --argjson queriesValidated "${queries_valid}" --argjson firingTestPassed "${firing}" \
  --argjson recoveryTestPassed "${resolved}" --argjson leakedSentinels "$(printf '%s\n' "${leaked[@]:-}" | jq -Rsc 'split("\n") | map(select(length > 0))')" \
  '{runId:$runId,environment:$environment,serviceName:$serviceName,serviceVersion:$serviceVersion,expectedCommitSha:$expectedCommitSha,startedAtUtc:$startedAtUtc,completedAtUtc:$completedAtUtc,traceExport:{serverSpanFound:$serverSpanFound,postgresSpanFound:$postgresSpanFound,redisSpanFound:$redisSpanFound,httpClientSpanFound:$httpClientSpanFound},metricsExport:{requestMetricFound:$requestMetricFound,latencyHistogramFound:$latencyHistogramFound,authFailureMetricFound:$authFailureMetricFound,runtimeMetricFound:$runtimeMetricFound},dashboards:{provisioned:true,queriesValidated:$queriesValidated},alerts:{rulesValidated:true,firingTestPassed:$firingTestPassed,recoveryTestPassed:$recoveryTestPassed},slos:{availabilityConfigured:true,latencyConfigured:true,burnRateConfigured:true},redaction:{testsPassed:($leakedSentinels|length==0),leakedSentinels:$leakedSentinels},result:$result}' \
  > "${REPORT}"

cat "${REPORT}"
[ "${result}" = passed ]
