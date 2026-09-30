#!/usr/bin/env bash
# One-way copy of the live PostgreSQL database to a NEW, EMPTY database on another host
# (e.g. a new Supabase project) for the planned move off Render's expiring free Postgres.
#
# Read-only against the source. Refuses to write anywhere but an empty target on a different
# host/database. Prints no connection strings. See docs/operations/database-host-migration-runbook.md.
#
#   SOURCE_DATABASE_URL   postgresql:// URI of the live database (used read-only by pg_dump)
#   TARGET_DATABASE_URL   postgresql:// URI of the new empty database
#   CONFIRM_COPY_TO_NEW_HOST=true
set -Eeuo pipefail
umask 077
# shellcheck source=common.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

for c in pg_dump pg_restore psql; do require_command "$c"; done
require_env SOURCE_DATABASE_URL
require_env TARGET_DATABASE_URL
require_boolean_true CONFIRM_COPY_TO_NEW_HOST

case "$SOURCE_DATABASE_URL" in postgres://*|postgresql://*) ;; *) fail "SOURCE_DATABASE_URL must be a postgresql:// URI." ;; esac
case "$TARGET_DATABASE_URL" in postgres://*|postgresql://*) ;; *) fail "TARGET_DATABASE_URL must be a postgresql:// URI." ;; esac

# host:port/dbname only -- never the credentials
identity() { printf '%s' "$1" | sed -E 's#^[a-z]+://[^@]*@##; s#\?.*$##'; }
[ "$(identity "$SOURCE_DATABASE_URL")" != "$(identity "$TARGET_DATABASE_URL")" ] \
  || fail "Source and target are the same host/database."

q() { psql "$1" -X -A -t -v ON_ERROR_STOP=1 -c "$2"; }

log "Checking source and target connectivity."
src_major="$(q "$SOURCE_DATABASE_URL" "show server_version_num" | cut -c1-2)"
tgt_major="$(q "$TARGET_DATABASE_URL" "show server_version_num" | cut -c1-2)"
[ "$tgt_major" -ge "$src_major" ] || fail "Target PostgreSQL ($tgt_major) is older than source ($src_major)."
[ "$(pg_dump --version | grep -oE '[0-9]+' | head -1)" -ge "$src_major" ] \
  || fail "Local pg_dump is older than the source server ($src_major); install a matching client."

user_tables="$(q "$TARGET_DATABASE_URL" "select count(*) from pg_tables where schemaname not in ('pg_catalog','information_schema') and tablename <> 'spatial_ref_sys'")"
[ "$user_tables" = "0" ] || fail "Target already has $user_tables user table(s); it must be empty."

log "Ensuring PostGIS exists on the target."
q "$TARGET_DATABASE_URL" "create extension if not exists postgis" >/dev/null

work="$(create_private_temp_dir)"
trap 'rm -rf "$work"' EXIT
dump="$work/source.dump"

log "Dumping source (read-only)."
pg_dump "$SOURCE_DATABASE_URL" --format=custom --no-owner --no-privileges \
  --exclude-extension=postgis --file="$dump"
[ -s "$dump" ] || fail "Dump is empty."
pg_restore --list "$dump" >/dev/null || fail "Dump TOC is unreadable."

log "Restoring into the target."
pg_restore --dbname="$TARGET_DATABASE_URL" --no-owner --no-privileges --exit-on-error "$dump"

log "Verifying row counts (critical tables) and migration history."
bad=0
while IFS= read -r t; do
  [ -z "$t" ] && continue
  a="$(q "$SOURCE_DATABASE_URL" "select count(*) from \"$t\"" 2>/dev/null || echo missing)"
  b="$(q "$TARGET_DATABASE_URL" "select count(*) from \"$t\"" 2>/dev/null || echo missing)"
  if [ "$a" = "$b" ] && [ "$a" != "missing" ]; then log "OK   $t = $a"; else log "FAIL $t source=$a target=$b"; bad=1; fi
done < "$REPOSITORY_ROOT/scripts/database/critical-tables.txt"

ma="$(q "$SOURCE_DATABASE_URL" 'select count(*) from "__EFMigrationsHistory"')"
mb="$(q "$TARGET_DATABASE_URL" 'select count(*) from "__EFMigrationsHistory"')"
[ "$ma" = "$mb" ] && log "OK   __EFMigrationsHistory = $ma" || { log "FAIL migrations source=$ma target=$mb"; bad=1; }

[ "$bad" = "0" ] || fail "Verification failed. Do NOT cut over; drop the target and retry."
log "PASS: target matches source. Cut-over is a separate, owner-approved step (see runbook)."
