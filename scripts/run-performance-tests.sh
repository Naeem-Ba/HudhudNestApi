#!/usr/bin/env bash
set -Eeuo pipefail

profile="${PERF_PROFILE:-pr}"
artifacts="${PERF_ARTIFACTS_DIR:-artifacts/performance}"
compose_file="performance/docker-compose.performance.yml"
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
test_failed=0
services_started=false
resource_sampler_pid=""

mkdir -p "${artifacts}" "${artifacts}/k6" "${artifacts}/concurrency" "${artifacts}/postgresql"
chmod 0777 "${artifacts}/k6"

cleanup() {
  local original_status=$?
  if [ -n "${resource_sampler_pid}" ]; then
    # BUG-30b investigation: stop the once-per-second resource sampler started
    # after the two-instance environment came up (see start_resource_sampler
    # below). The pre-existing single docker-stats snapshot taken here at exit
    # only ever captured an idle moment (all containers back near 0% CPU after
    # every workload had already finished) -- it could neither confirm nor
    # refute CI-runner resource contention as a cause of browse-cold's p95
    # miss. container-resources-timeseries.jsonl gives one sample per second
    # for the whole test run, each tagged with a UTC timestamp, so it can be
    # correlated against the k6 log's own timestamps for the cold-cache browse
    # workload window specifically.
    kill "${resource_sampler_pid}" 2>/dev/null || true
    wait "${resource_sampler_pid}" 2>/dev/null || true
  fi
  if [ "${services_started}" = "true" ]; then
    touch "${artifacts}/container-resources-timeseries.jsonl"
    docker compose -f "${compose_file}" logs --no-color --tail 5000 \
      api1 api2 load-balancer > "${artifacts}/service-logs.txt" 2>&1 || true
    docker stats --no-stream --format '{{json .}}' \
      > "${artifacts}/container-resources.jsonl" 2>&1 || true
    docker compose -f "${compose_file}" exec -T redis redis-cli INFO memory \
      > "${artifacts}/redis-memory.txt" 2>&1 || true
    docker compose -f "${compose_file}" down --volumes --remove-orphans \
      > "${artifacts}/environment-shutdown.log" 2>&1 || true
  fi
  if [ "${original_status}" -ne 0 ]; then
    exit "${original_status}"
  fi
}
trap cleanup EXIT

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

start_resource_sampler() {
  # BUG-30b investigation: sample every container's CPU/memory once per
  # second, each line tagged with a UTC timestamp, for the rest of the run.
  # `|| true` on every step keeps a single failed sample (e.g. during the
  # rate-limit loop's --force-recreate restart) from killing the sampler.
  (
    while true; do
      sample_ts="$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)"
      docker stats --no-stream --format '{{json .}}' 2>/dev/null |
        jq -c --arg ts "${sample_ts}" '. + {sampledAtUtc: $ts}' \
        >> "${artifacts}/container-resources-timeseries.jsonl" || true
      sleep 1
    done
  ) &
  resource_sampler_pid=$!
}

run_and_record() {
  local label="$1"
  shift
  if "$@"; then
    echo "PASS: ${label}"
  else
    echo "FAIL: ${label}" >&2
    test_failed=1
  fi
}

export PYTHON_BIN="${PYTHON_BIN:-python3}"
for command_name in docker dotnet curl psql openssl; do
  command -v "${command_name}" >/dev/null || fail "${command_name} is required."
done
"${PYTHON_BIN}" --version >/dev/null 2>&1 || fail "PYTHON_BIN must reference Python 3."
docker info >/dev/null 2>&1 || fail "Docker daemon is unavailable."

case "${PERF_ENVIRONMENT:-}" in
  Local|CI|Performance|Staging) ;;
  *) fail "PERF_ENVIRONMENT must be Local, CI, Performance, or Staging." ;;
esac

base_url="${PERF_BASE_URL:-http://localhost:58080}"
production_url="${PRODUCTION_BASE_URL:-https://production.invalid}"
"${PYTHON_BIN}" - "${base_url}" "${production_url}" <<'PY'
import sys, urllib.parse
staging = urllib.parse.urlparse(sys.argv[1])
production = urllib.parse.urlparse(sys.argv[2])
if staging.scheme not in ("http", "https") or not staging.hostname:
    raise SystemExit("PERF_BASE_URL must be an absolute HTTP(S) URL.")
if staging.hostname == production.hostname:
    raise SystemExit("Refusing to run performance tests against the Production hostname.")
if any(marker in staging.hostname.lower() for marker in ("prod.", "production.")):
    raise SystemExit("PERF_BASE_URL appears to be a Production hostname.")
PY

case "${profile}" in
  pr)
    : "${PERF_DATASET_SIZE:=10000}"
    : "${PERF_VIRTUAL_USERS:=8}"
    : "${PERF_TEST_DURATION:=20s}"
    : "${PERF_REQUIRE_APPROVED_BUDGETS:=false}"
    : "${PERF_REQUIRE_BASELINE:=false}"
    ;;
  release)
    : "${PERF_DATASET_SIZE:=25000}"
    : "${PERF_VIRTUAL_USERS:=15}"
    : "${PERF_TEST_DURATION:=60s}"
    : "${PERF_REQUIRE_APPROVED_BUDGETS:=true}"
    : "${PERF_REQUIRE_BASELINE:=true}"
    ;;
  staging)
    : "${PERF_DATASET_SIZE:=100000}"
    : "${PERF_VIRTUAL_USERS:=30}"
    : "${PERF_TEST_DURATION:=5m}"
    : "${PERF_REQUIRE_APPROVED_BUDGETS:=true}"
    : "${PERF_REQUIRE_BASELINE:=true}"
    ;;
  *) fail "PERF_PROFILE must be pr, release, or staging." ;;
esac

export PERF_PROFILE="${profile}"
export PERF_BASE_URL="${base_url}"
export PERF_COMMIT_SHA="${PERF_COMMIT_SHA:-$(git rev-parse HEAD 2>/dev/null || echo local-performance)}"
export PERF_RUN_ID="${PERF_RUN_ID:-$(date -u +%Y%m%d%H%M%S)-$(printf '%s' "${PERF_COMMIT_SHA}" | cut -c1-8)}"
compose_run_id="$(printf '%s' "${PERF_RUN_ID}" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9_-')"
[ -n "${compose_run_id}" ] || fail "PERF_RUN_ID must contain at least one Compose-safe character."
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-propertyapi-performance-${compose_run_id}}"
export PERF_POSTGRES_PASSWORD="${PERF_POSTGRES_PASSWORD:-$(openssl rand -hex 24)}"
export PERF_JWT_KEY="${PERF_JWT_KEY:-$(openssl rand -base64 48 | tr -d '\r\n')}"
export PERF_OTP_SECRET_KEY="${PERF_OTP_SECRET_KEY:-$(openssl rand -base64 48 | tr -d '\r\n')}"
export PERF_PHONE_HMAC_KEY="${PERF_PHONE_HMAC_KEY:-$(openssl rand -base64 32 | tr -d '\r\n')}"
export PERF_FIXED_OTP="${PERF_FIXED_OTP:-$(printf '%06d' $((RANDOM % 1000000)))}"
export PERF_TEST_PHONE_PREFIX="${PERF_TEST_PHONE_PREFIX:-+155590}"
export PERF_TEST_PASSWORD="${PERF_TEST_PASSWORD:-Performance-$(openssl rand -hex 12)!}"
export PERF_CLEANUP_SECRET="${PERF_CLEANUP_SECRET:-$(openssl rand -base64 48 | tr -d '\r\n')}"
export PERF_DATABASE_CONNECTION_STRING="Host=localhost;Port=55432;Database=propertyapi_performance;Username=postgres;Password=${PERF_POSTGRES_PASSWORD};Pooling=true;Maximum Pool Size=20;Timeout=15;Command Timeout=60"
export PERF_DATABASE_URL="postgresql://postgres:${PERF_POSTGRES_PASSWORD}@localhost:55432/propertyapi_performance"
export PERF_DATASET_MANIFEST="${artifacts}/dataset-manifest.json"
export PERF_ARTIFACTS_DIR="${artifacts}"
export PERF_EXPECTED_SCENARIO_REPORTS=17
export PERF_REQUIRE_APPROVED_BUDGETS PERF_REQUIRE_BASELINE PERF_DATASET_SIZE PERF_VIRTUAL_USERS PERF_TEST_DURATION

cat > "${artifacts}/environment.json" <<JSON
{
  "runId": "${PERF_RUN_ID}",
  "commitSha": "${PERF_COMMIT_SHA}",
  "environment": "${PERF_ENVIRONMENT}",
  "profile": "${PERF_PROFILE}",
  "startedAtUtc": "${started_at}",
  "expectedInstanceCount": 2,
  "observedInstanceCount": 0,
  "datasetSizeRequested": ${PERF_DATASET_SIZE}
}
JSON

docker compose -f "${compose_file}" config --quiet
docker compose -f "${compose_file}" up -d postgres redis
services_started=true

for attempt in $(seq 1 60); do
  if psql "${PERF_DATABASE_URL}" -X -qAt -c 'SELECT 1' >/dev/null 2>&1 &&
     docker compose -f "${compose_file}" exec -T redis redis-cli ping 2>/dev/null | grep -q PONG; then
    break
  fi
  if [ "${attempt}" -eq 60 ]; then fail "PostgreSQL/Redis did not become ready."; fi
  sleep 2
done

psql "${PERF_DATABASE_URL}" -X -v ON_ERROR_STOP=1 \
  -f performance/sql/enable-pg-stat-statements.sql >/dev/null

export ASPNETCORE_ENVIRONMENT=Staging
export DOTNET_ENVIRONMENT=Staging
export ConnectionStrings__DefaultConnection="${PERF_DATABASE_CONNECTION_STRING}"
export ConnectionStrings__Redis="localhost:56379,abortConnect=false"
export Redis__ConnectionString="localhost:56379,abortConnect=false"
export RateLimiting__Redis__Enabled=true
export DataProtection__PersistKeysToDatabase=true
dotnet run --project tools/PropertyApi.Migrator/PropertyApi.Migrator.csproj \
  --configuration Release

dotnet run --project tools/PropertyApi.PerformanceDataGenerator/PropertyApi.PerformanceDataGenerator.csproj \
  --configuration Release -- \
  --environment "${PERF_ENVIRONMENT}" \
  --connection "${PERF_DATABASE_CONNECTION_STRING}" \
  --run-id "${PERF_RUN_ID}" \
  --properties "${PERF_DATASET_SIZE}" \
  --manifest "${PERF_DATASET_MANIFEST}"

docker compose -f "${compose_file}" up -d --build api1 api2 load-balancer

for attempt in $(seq 1 90); do
  if curl --fail --silent --show-error --max-time 5 "${PERF_BASE_URL}/health/ready" >/dev/null 2>&1; then
    break
  fi
  if [ "${attempt}" -eq 90 ]; then fail "The two-instance API environment did not become ready."; fi
  sleep 2
done

instance_file="$(mktemp)"
for _ in $(seq 1 30); do
  curl --silent --show-error --max-time 5 -D - -o /dev/null "${PERF_BASE_URL}/health/live" |
    awk 'BEGIN { IGNORECASE=1 } /^X-Instance-Id:/ { gsub("\r", "", $2); print $2 }' >> "${instance_file}"
done
sort -u "${instance_file}" > "${artifacts}/observed-instances.txt"
observed_instances="$(wc -l < "${artifacts}/observed-instances.txt" | tr -d ' ')"
rm -f "${instance_file}"
if [ "${observed_instances}" -lt 2 ]; then fail "Load balancer did not reach two API instances."; fi

"${PYTHON_BIN}" - "${artifacts}/environment.json" "${observed_instances}" <<'PY'
import json, pathlib, subprocess, sys
path = pathlib.Path(sys.argv[1])
data = json.loads(path.read_text(encoding="utf-8"))
data["observedInstanceCount"] = int(sys.argv[2])
data["dockerVersion"] = subprocess.check_output(["docker", "version", "--format", "{{.Server.Version}}"], text=True).strip()
path.write_text(json.dumps(data, indent=2), encoding="utf-8")
PY

psql "${PERF_DATABASE_URL}" -X -qAt -c 'SELECT pg_stat_statements_reset()' >/dev/null

start_resource_sampler

docker compose -f "${compose_file}" exec -T redis redis-cli FLUSHDB >/dev/null
run_and_record "cold-cache browse workload" \
  docker compose -f "${compose_file}" --profile load run --rm \
    -e PERF_CACHE_MODE=cold k6 run scenarios/browse.js

run_and_record "warm-cache browse workload" \
  docker compose -f "${compose_file}" --profile load run --rm \
    -e PERF_CACHE_MODE=warm k6 run scenarios/browse.js

race_counter=0
for race_kind in refresh otp; do
  for concurrency in 2 5 10; do
    race_counter=$((race_counter + 1))
    suffix="$(printf '%06d' $((race_counter * 100 + concurrency)))"
    phone="${PERF_TEST_PHONE_PREFIX}${suffix}"
    marker="PERF-RACE-${PERF_RUN_ID}-${race_kind}-${concurrency}"
    docker compose -f "${compose_file}" exec -T redis redis-cli FLUSHDB >/dev/null
    run_and_record "${race_kind} race at concurrency ${concurrency}" \
      docker compose -f "${compose_file}" --profile load run --rm \
        -e PERF_RACE_KIND="${race_kind}" \
        -e PERF_RACE_CONCURRENCY="${concurrency}" \
        -e PERF_RACE_PHONE_SUFFIX="${suffix}" \
        k6 run scenarios/auth-races.js
    export PERF_RACE_KIND="${race_kind}" PERF_RACE_CONCURRENCY="${concurrency}"
    export PERF_RACE_MARKER="${marker}" PERF_RACE_PHONE="${phone}"
    run_and_record "${race_kind} database integrity at concurrency ${concurrency}" \
      bash scripts/capture-auth-race-integrity.sh
  done
done

for race_kind in otp-invalid registration; do
  concurrency=10
  race_counter=$((race_counter + 1))
  suffix="$(printf '%06d' $((race_counter * 100 + concurrency)))"
  phone="${PERF_TEST_PHONE_PREFIX}${suffix}"
  marker="PERF-RACE-${PERF_RUN_ID}-${race_kind}-${concurrency}"
  docker compose -f "${compose_file}" exec -T redis redis-cli FLUSHDB >/dev/null
  run_and_record "${race_kind} race at concurrency ${concurrency}" \
    docker compose -f "${compose_file}" --profile load run --rm \
      -e PERF_RACE_KIND="${race_kind}" \
      -e PERF_RACE_CONCURRENCY="${concurrency}" \
      -e PERF_RACE_PHONE_SUFFIX="${suffix}" \
      k6 run scenarios/auth-races.js
  export PERF_RACE_KIND="${race_kind}" PERF_RACE_CONCURRENCY="${concurrency}"
  export PERF_RACE_MARKER="${marker}" PERF_RACE_PHONE="${phone}"
  run_and_record "${race_kind} database integrity" bash scripts/capture-auth-race-integrity.sh
done

export PERF_LIMIT_AUTH_LOGIN=10
export PERF_LIMIT_AUTH_REGISTER=5
export PERF_LIMIT_SEND_OTP=3
export PERF_LIMIT_VERIFY_OTP=5
export PERF_LIMIT_AUTH_REFRESH=20
export PERF_LIMIT_PUBLIC_SEARCH=20
export PERF_LIMIT_GEO_SEARCH=15
docker compose -f "${compose_file}" up -d --force-recreate api1 api2 load-balancer
rate_instance_file="$(mktemp)"
for attempt in $(seq 1 90); do
  curl --silent --show-error --max-time 5 -D - -o /dev/null "${PERF_BASE_URL}/health/ready" 2>/dev/null |
    awk 'BEGIN { IGNORECASE=1 } /^X-Instance-Id:/ { gsub("\r", "", $2); print $2 }' >> "${rate_instance_file}" || true
  if [ "$(sort -u "${rate_instance_file}" | wc -l | tr -d ' ')" -ge 2 ]; then
    break
  fi
  if [ "${attempt}" -eq 90 ]; then fail "Both rate-limit API instances did not become ready."; fi
  sleep 2
done
rm -f "${rate_instance_file}"

for rate_case in \
  'login:10:20' \
  'registration:5:12' \
  'otp-request:3:8' \
  'otp-verify:5:12' \
  'refresh:20:30' \
  'public-search:20:30' \
  'geo-search:15:25'; do
  IFS=':' read -r rate_target rate_limit rate_requests <<< "${rate_case}"
  docker compose -f "${compose_file}" exec -T redis redis-cli FLUSHDB >/dev/null
  run_and_record "distributed ${rate_target} rate-limit race" \
    docker compose -f "${compose_file}" --profile load run --rm \
      -e PERF_RATE_TARGET="${rate_target}" \
      -e PERF_RATE_REQUEST_COUNT="${rate_requests}" \
      -e PERF_RATE_CONFIGURED_LIMIT="${rate_limit}" \
      -e PERF_RATE_WINDOW_SECONDS=5 \
      k6 run scenarios/rate-limit-race.js
done

run_and_record "pg_stat_statements capture" bash scripts/capture-pg-stat-statements.sh
run_and_record "EXPLAIN ANALYZE query-plan gate" bash scripts/capture-query-plans.sh

run_and_record "aggregate performance evidence" \
  "${PYTHON_BIN}" performance/reporting/aggregate.py "${artifacts}"

if [ "${PERF_REQUIRE_BASELINE}" = "true" ]; then
  run_and_record "approved baseline comparison" \
    bash scripts/compare-performance-baseline.sh \
      "${artifacts}/summary.json" \
      performance/baselines/approved-baseline.json \
      "${artifacts}/baseline-comparison.json"
fi

run_and_record "final performance evidence evaluation" \
  "${PYTHON_BIN}" performance/reporting/aggregate.py "${artifacts}"

if [ "${test_failed}" -ne 0 ]; then
  fail "One or more mandatory performance checks failed; inspect ${artifacts}."
fi

echo "Concurrent performance validation passed."
