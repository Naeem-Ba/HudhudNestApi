#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env RESTORE_TARGET_DATABASE_URL
require_env RESTORE_ADMIN_DATABASE_URL
require_env RESTORE_TARGET_ENVIRONMENT
require_boolean_true ALLOW_DESTRUCTIVE_RESTORE
require_boolean_true RESTORE_TARGET_DISPOSABLE
[ "${RESTORE_TARGET_ENVIRONMENT}" = "recovery-drill" ] || fail "Cleanup is restricted to recovery-drill."
TARGET_IDENTITY="$(connection_identity_json "${RESTORE_TARGET_DATABASE_URL}")"
TARGET_DATABASE="$(jq -r '.database' <<<"${TARGET_IDENTITY}")"
[[ "${TARGET_DATABASE}" =~ restore_drill ]] || fail "Target name must contain restore_drill."
if [ -n "${PRODUCTION_DATABASE_URL:-}" ]; then
  PROD_IDENTITY="$(connection_identity_json "${PRODUCTION_DATABASE_URL}")"
  [ "${TARGET_IDENTITY}" != "${PROD_IDENTITY}" ] || fail "Refusing to clean production."
fi
configure_pg_environment "${RESTORE_ADMIN_DATABASE_URL}"
psql -X -v ON_ERROR_STOP=1 -v target_database="${TARGET_DATABASE}" <<'SQL'
SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', :'target_database') \gexec
SQL
log "Disposable restore target ${TARGET_DATABASE} removed."
