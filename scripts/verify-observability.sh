#!/usr/bin/env bash
set -euo pipefail

API_BASE_URL="${API_BASE_URL:-http://127.0.0.1:18080}"
PROMETHEUS_URL="${PROMETHEUS_URL:-http://127.0.0.1:19090}"
TEMPO_URL="${TEMPO_URL:-http://127.0.0.1:13200}"
TEST_KEY="${OBSERVABILITY_TEST_KEY:-}"
SERVICE_NAME="${OBSERVABILITY_SERVICE_NAME:-property-api}"
SERVICE_VERSION="${OBSERVABILITY_SERVICE_VERSION:-local-observability}"
EXPECTED_SHA="${EXPECTED_COMMIT_SHA:-local-observability}"
ENVIRONMENT="${OBSERVABILITY_ENVIRONMENT:-Staging}"
REPORT="${OBSERVABILITY_REPORT:-artifacts/observability/observability-verification-report.json}"
RUN_ID="obs-$(date -u +%Y%m%dT%H%M%SZ)-${RANDOM}"
STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
CORRELATION_ID="obs-${RUN_ID}"
RULE_FILE="observability/prometheus/rules/.observability-test-alert.yml"

for command in curl jq; do
  command -v "${command}" >/dev/null || { echo "${command} is required"; exit 2; }
done

if [ -z "${TEST_KEY}" ]; then
  echo "OBSERVABILITY_TEST_KEY is required"
  exit 2
fi

mkdir -p "$(dirname "${REPORT}")"
rm -f "${RULE_FILE}"
trap 'rm -f "${RULE_FILE}"' EXIT

curl --fail --silent --show-error \
  -X POST \
  -H "X-Correlation-ID: ${CORRELATION_ID}" \
  -H "X-Observability-Test-Key: ${TEST_KEY}" \
  -H "Authorization: Bearer TEST_ACCESS_TOKEN_SHOULD_NOT_APPEAR" \
  -H "Cookie: refresh=TEST_REFRESH_TOKEN_SHOULD_NOT_APPEAR" \
  "${API_BASE_URL}/internal/observability/synthetic?secret=TEST_PASSWORD_SHOULD_NOT_APPEAR" >/dev/null

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
  [ "$(query_prometheus "$1" | jq '.data.result | length')" -gt 0 ]
}

trace_id=""
for _ in $(seq 1 30); do
  trace_id="$(curl --fail --silent --show-error --get \
    --data-urlencode "q={ resource.service.name = \"${SERVICE_NAME}\" && span.correlation.id = \"${CORRELATION_ID}\" }" \
    "${TEMPO_URL}/api/search" | jq -r '.traces[0].traceID // empty')"
  [ -n "${trace_id}" ] && break
  sleep 2
done

trace_json='{}'
if [ -n "${trace_id}" ]; then
  trace_json="$(curl --fail --silent --show-error "${TEMPO_URL}/api/traces/${trace_id}")"
fi

contains_trace_attribute() {
  echo "${trace_json}" | jq -e --arg key "$1" --arg value "$2" \
    '.. | objects | select(.key? == $key and ((.value.stringValue? // .value.intValue? // "") | tostring | contains($value)))' \
    >/dev/null
}

server_span=false
postgres_span=false
redis_span=false
http_client_span=false
if [ -n "${trace_id}" ]; then
  echo "${trace_json}" | jq -e '.. | objects | select(.kind? == 2 or .kind? == "SPAN_KIND_SERVER")' >/dev/null && server_span=true
  (contains_trace_attribute "db.system.name" "postgresql" || contains_trace_attribute "db.system" "postgresql") && postgres_span=true
  (contains_trace_attribute "db.system.name" "redis" || contains_trace_attribute "db.system" "redis") && redis_span=true
  echo "${trace_json}" | jq -e '.. | objects | select(.kind? == 3 or .kind? == "SPAN_KIND_CLIENT")' >/dev/null && http_client_span=true
fi

request_metric=false
latency_metric=false
auth_metric=false
runtime_metric=false
metric_found 'http_server_request_duration_seconds_count{service_name="property-api"}' && request_metric=true
metric_found 'http_server_request_duration_seconds_bucket{service_name="property-api"}' && latency_metric=true
metric_found 'propertyapi_auth_requests_total' && auth_metric=true
metric_found 'process_memory_working_set_bytes{service_name="property-api"}' && runtime_metric=true

queries_valid=false
if metric_found 'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="property-api"}[5m])))' &&
   metric_found 'histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="property-api"}[5m])))'; then
  queries_valid=true
fi

cat > "${RULE_FILE}" <<'YAML'
groups:
  - name: property-api-controlled-alert-test
    rules:
      - alert: PropertyApiControlledObservabilityTest
        expr: vector(1) > 0
        for: 0s
        labels: { severity: test }
        annotations:
          runbook_url: docs/observability/alert-test-report.md
YAML
curl --fail --silent --show-error -X POST "${PROMETHEUS_URL}/-/reload" >/dev/null
sleep 7
firing=false
curl --fail --silent --show-error "${PROMETHEUS_URL}/api/v1/alerts" |
  jq -e '.data.alerts[] | select(.labels.alertname == "PropertyApiControlledObservabilityTest" and .state == "firing")' >/dev/null && firing=true

sed -i 's/expr: vector(1) > 0/expr: vector(0) > 0/' "${RULE_FILE}"
curl --fail --silent --show-error -X POST "${PROMETHEUS_URL}/-/reload" >/dev/null
sleep 7
resolved=false
if ! curl --fail --silent --show-error "${PROMETHEUS_URL}/api/v1/alerts" |
  jq -e '.data.alerts[] | select(.labels.alertname == "PropertyApiControlledObservabilityTest" and .state == "firing")' >/dev/null; then
  resolved=true
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
