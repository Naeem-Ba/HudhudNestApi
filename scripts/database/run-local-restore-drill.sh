#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_command docker
docker info >/dev/null 2>&1 || fail "Docker daemon is not available."
COMPOSE_FILE="${REPOSITORY_ROOT}/ci/docker-compose.database-recovery.yml"
RESULT_DIR="${REPOSITORY_ROOT}/artifacts/database-recovery"
mkdir -p "${RESULT_DIR}"
export BACKUP_ENCRYPTION_KEY="${BACKUP_ENCRYPTION_KEY:-local-recovery-drill-key-not-for-production}"
export BACKUP_ID="${BACKUP_ID:-local-recovery-drill-$(date -u +%Y%m%dT%H%M%SZ)}"
export APPLICATION_COMMIT_SHA="${APPLICATION_COMMIT_SHA:-${GITHUB_SHA:-unknown}}"
cleanup() {
  docker compose -f "${COMPOSE_FILE}" down -v --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM
docker compose -f "${COMPOSE_FILE}" config --quiet
if [ "${SKIP_RECOVERY_IMAGE_BUILD:-false}" != "true" ]; then
  docker compose -f "${COMPOSE_FILE}" build migrator recovery-toolbox api-restored
fi
DRILL_STARTED_EPOCH="$(date -u +%s)"
docker compose -f "${COMPOSE_FILE}" up -d --wait --wait-timeout 120 source-postgres restore-postgres redis
docker compose -f "${COMPOSE_FILE}" run --rm --no-deps \
  recovery-toolbox bash -lc '
    set -Eeuo pipefail
    stable_reads=0
    for attempt in $(seq 1 60); do
      if PGPASSWORD=postgres psql -X -h source-postgres -U postgres -d propertyapi_source -tAc "SELECT 1" | grep -q 1; then
        stable_reads=$((stable_reads + 1))
      else
        stable_reads=0
      fi
      if [ "${stable_reads}" -ge 3 ]; then
        exit 0
      fi
      sleep 2
    done
    exit 1
  '
docker compose -f "${COMPOSE_FILE}" run --rm migrator
docker compose -f "${COMPOSE_FILE}" run --rm \
  -e BACKUP_ENCRYPTION_KEY="${BACKUP_ENCRYPTION_KEY}" \
  -e BACKUP_ID="${BACKUP_ID}" \
  -e APPLICATION_COMMIT_SHA="${APPLICATION_COMMIT_SHA}" \
  recovery-toolbox bash scripts/database/backup-postgres.sh
MANIFEST_FILE="artifacts/database-recovery/${BACKUP_ID}.manifest.json"
BACKUP_FILE="artifacts/database-recovery/${BACKUP_ID}.dump.gpg"
docker compose -f "${COMPOSE_FILE}" run --rm \
  -e BACKUP_ENCRYPTION_KEY="${BACKUP_ENCRYPTION_KEY}" \
  -e MANIFEST_FILE="${MANIFEST_FILE}" \
  -e BACKUP_FILE="${BACKUP_FILE}" \
  recovery-toolbox bash scripts/database/restore-postgres.sh
docker compose -f "${COMPOSE_FILE}" run --rm \
  -e MANIFEST_FILE="${MANIFEST_FILE}" \
  recovery-toolbox bash scripts/database/verify-restored-database.sh

export ConnectionStrings__DefaultConnection='Host=127.0.0.1;Port=55433;Database=propertyapi_restore_drill;Username=postgres;Password=postgres'
dotnet run --project "${REPOSITORY_ROOT}/tools/PropertyApi.DatabaseRecoveryVerifier/PropertyApi.DatabaseRecoveryVerifier.csproj" \
  --configuration Release --no-restore
docker compose -f "${COMPOSE_FILE}" up -d api-restored
SMOKE_STARTED_EPOCH="$(date -u +%s)"
api_ready=false
for attempt in $(seq 1 60); do
  if curl --fail --silent --show-error --max-time 5 http://127.0.0.1:18081/health/ready >/dev/null; then
    api_ready=true
    break
  fi
  sleep 2
done
[ "${api_ready}" = "true" ] || fail "Restored API did not become ready."
curl --fail --silent --show-error --max-time 10 'http://127.0.0.1:18081/api/properties?page=1&pageSize=1' >/dev/null
APPLICATION_SMOKE_DURATION_SECONDS="$(( $(date -u +%s) - SMOKE_STARTED_EPOCH ))"
docker compose -f "${COMPOSE_FILE}" run --rm --no-deps \
  -e BACKUP_ID="${BACKUP_ID}" \
  -e DRILL_STARTED_EPOCH="${DRILL_STARTED_EPOCH}" \
  -e APPLICATION_SMOKE_DURATION_SECONDS="${APPLICATION_SMOKE_DURATION_SECONDS}" \
  -e RPO_TARGET_SECONDS="${RPO_TARGET_SECONDS:-3600}" \
  -e RTO_TARGET_SECONDS="${RTO_TARGET_SECONDS:-7200}" \
  recovery-toolbox bash scripts/database/create-recovery-report.sh
docker compose -f "${COMPOSE_FILE}" down -v --remove-orphans
trap - EXIT INT TERM
printf '{"status":"PASS","disposableEnvironmentRemoved":true,"completedAtUtc":"%s"}\n' \
  "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "${RESULT_DIR}/cleanup-result.json"
log "End-to-end backup, restore, validation, application smoke, and cleanup drill passed."
