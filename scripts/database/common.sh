#!/usr/bin/env bash

set -Eeuo pipefail
umask 077

DATABASE_SCRIPTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPOSITORY_ROOT="$(cd "${DATABASE_SCRIPTS_DIR}/../.." && pwd)"

log() {
  printf '[database-recovery] %s\n' "$*" >&2
}

fail() {
  log "ERROR: $*"
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command is missing: $1"
}

require_env() {
  local name="$1"
  [ -n "${!name:-}" ] || fail "Required environment variable is missing: ${name}"
}

require_boolean_true() {
  local name="$1"
  [ "${!name:-}" = "true" ] || fail "${name}=true is required."
}

create_private_temp_dir() {
  local base="${DATABASE_RECOVERY_TEMP_ROOT:-${TMPDIR:-/tmp}}"
  mkdir -p "${base}"
  mktemp -d "${base%/}/propertyapi-db-recovery.XXXXXXXX"
}

# Points GnuPG at a private directory the current process already owns,
# instead of the container user's home directory. recovery-toolbox
# invocations run as an explicit numeric --user UID (matching the host UID
# that owns the shared artifacts/database-recovery bind mount -- see
# database-restore-drill.yml and BUG-recovery-gate-manifest-uid-mismatch) that
# has no /etc/passwd entry and therefore no resolvable $HOME. Without this,
# gpg falls back to creating "$HOME/.gnupg" (effectively "//.gnupg" when
# $HOME is empty) and fails with:
#   gpg: Fatal: can't create directory '//.gnupg': Permission denied
# Every script that invokes gpg must call this before doing so.
configure_gpg_home() {
  local private_dir="$1"
  export HOME="${private_dir}"
  export GNUPGHOME="${private_dir}/.gnupg"
  mkdir -p "${GNUPGHOME}"
  chmod 700 "${GNUPGHOME}"
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    fail "sha256sum or shasum is required."
  fi
}

# Parses a PostgreSQL URI into libpq environment variables. Secrets stay out of
# process arguments and logs. ADO.NET connection strings are deliberately rejected.
configure_pg_environment() {
  local connection_uri="$1"
  local parsed=()

  require_command python3
  mapfile -d '' -t parsed < <(
    python3 - "${connection_uri}" <<'PY'
import sys
from urllib.parse import parse_qs, unquote, urlparse

uri = urlparse(sys.argv[1])
if uri.scheme not in {"postgres", "postgresql"} or not uri.hostname:
    raise SystemExit("Connection value must be a postgres:// or postgresql:// URI")
query = parse_qs(uri.query)
values = [
    uri.hostname or "",
    str(uri.port or 5432),
    unquote(uri.username or ""),
    unquote(uri.password or ""),
    unquote(uri.path.lstrip("/")),
    query.get("sslmode", [""])[0],
]
sys.stdout.buffer.write(b"\0".join(value.encode("utf-8") for value in values) + b"\0")
PY
  )

  [ "${#parsed[@]}" -ge 6 ] || fail "Unable to parse PostgreSQL connection URI."
  export PGHOST="${parsed[0]}"
  export PGPORT="${parsed[1]}"
  export PGUSER="${parsed[2]}"
  export PGPASSWORD="${parsed[3]}"
  export PGDATABASE="${parsed[4]}"
  [ -n "${PGHOST}" ] && [ -n "${PGUSER}" ] && [ -n "${PGDATABASE}" ] || fail \
    "PostgreSQL URI must identify host, user, and database explicitly."
  export PGCONNECT_TIMEOUT="${PGCONNECT_TIMEOUT:-15}"
  if [ -n "${parsed[5]}" ]; then
    export PGSSLMODE="${parsed[5]}"
  else
    unset PGSSLMODE || true
  fi
}

connection_identity_json() {
  local connection_uri="$1"
  python3 - "${connection_uri}" <<'PY'
import json
import sys
from urllib.parse import unquote, urlparse
uri = urlparse(sys.argv[1])
if uri.scheme not in {"postgres", "postgresql"} or not uri.hostname:
    raise SystemExit("Connection value must be a PostgreSQL URI")
print(json.dumps({
    "host": (uri.hostname or "").lower(),
    "port": uri.port or 5432,
    "database": unquote(uri.path.lstrip("/")),
}))
PY
}

assert_postgres_client_matches_server() {
  local client_major server_major server_version
  client_major="$(pg_dump --version | sed -E 's/.* ([0-9]+)(\..*)?$/\1/')"
  server_version="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT current_setting('server_version_num');" | tr -d '[:space:]')"
  server_major="$((server_version / 10000))"
  [ "${client_major}" = "${server_major}" ] || fail \
    "pg_dump major ${client_major} does not match PostgreSQL server major ${server_major}."
}

quote_identifier() {
  local value="$1"
  printf '"%s"' "${value//\"/\"\"}"
}

aws_arguments() {
  AWS_ARGS=()
  if [ -n "${BACKUP_S3_ENDPOINT_URL:-}" ]; then
    AWS_ARGS+=(--endpoint-url "${BACKUP_S3_ENDPOINT_URL}")
  fi
  if [ -n "${BACKUP_AWS_REGION:-}" ]; then
    AWS_ARGS+=(--region "${BACKUP_AWS_REGION}")
  fi
}

parse_s3_uri() {
  local uri="$1"
  [[ "${uri}" == s3://* ]] || fail "BACKUP_STORAGE_URI must start with s3://"
  local without_scheme="${uri#s3://}"
  S3_BUCKET="${without_scheme%%/*}"
  if [[ "${without_scheme}" == */* ]]; then
    S3_PREFIX="${without_scheme#*/}"
  else
    S3_PREFIX=""
  fi
  S3_PREFIX="${S3_PREFIX%/}"
  [ -n "${S3_BUCKET}" ] || fail "S3 bucket is missing."
}

upload_file() {
  local source="$1"
  local key="$2"
  local extra=()
  aws_arguments
  if [ "${BACKUP_S3_SSE:-AES256}" = "aws:kms" ]; then
    require_env BACKUP_S3_KMS_KEY_ID
    extra+=(--sse aws:kms --sse-kms-key-id "${BACKUP_S3_KMS_KEY_ID}")
  else
    extra+=(--sse AES256)
  fi
  aws "${AWS_ARGS[@]}" s3 cp "${source}" "s3://${S3_BUCKET}/${key}" \
    --only-show-errors "${extra[@]}"
}
