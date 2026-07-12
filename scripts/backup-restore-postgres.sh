#!/usr/bin/env bash
set -Eeuo pipefail

required=(SOURCE_DATABASE_URL RESTORE_ADMIN_URL RESTORE_DATABASE_NAME)
for name in "${required[@]}"; do
  [[ -n "${!name:-}" ]] || { echo "$name is required" >&2; exit 2; }
done

[[ "$RESTORE_DATABASE_NAME" =~ ^[A-Za-z0-9_]+$ ]] || {
  echo "RESTORE_DATABASE_NAME may contain only letters, digits, and underscore." >&2
  exit 2
}

backup_dir="${BACKUP_DIR:-./artifacts/backups}"
mkdir -p "$backup_dir"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_file="$backup_dir/propertyapi-$timestamp.dump"

start_epoch="$(date +%s)"
echo "Creating custom-format backup: $backup_file"
pg_dump "$SOURCE_DATABASE_URL" \
  --format=custom \
  --compress=9 \
  --no-owner \
  --no-acl \
  --file="$backup_file"
backup_end="$(date +%s)"

# RESTORE_ADMIN_URL must connect to an admin database, not to the restore target.
echo "Recreating isolated restore database: $RESTORE_DATABASE_NAME"
psql "$RESTORE_ADMIN_URL" --set=ON_ERROR_STOP=1 <<SQL
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname = '$RESTORE_DATABASE_NAME' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "$RESTORE_DATABASE_NAME";
CREATE DATABASE "$RESTORE_DATABASE_NAME";
SQL

restore_url="${RESTORE_DATABASE_URL:-${RESTORE_ADMIN_URL%/*}/$RESTORE_DATABASE_NAME}"
restore_start="$(date +%s)"
pg_restore "$backup_file" \
  --dbname="$restore_url" \
  --no-owner \
  --no-acl \
  --exit-on-error \
  --jobs="${RESTORE_JOBS:-2}"
restore_end="$(date +%s)"

echo 'Verifying restored database'
psql "$restore_url" --set=ON_ERROR_STOP=1 <<'SQL'
SELECT extname FROM pg_extension WHERE extname = 'postgis';
SELECT COUNT(*) AS applied_migrations FROM "__EFMigrationsHistory";
SELECT table_schema, COUNT(*) AS table_count
FROM information_schema.tables
WHERE table_schema = 'public'
GROUP BY table_schema;
SQL

sha256sum "$backup_file" > "$backup_file.sha256"
echo "Backup seconds: $((backup_end-start_epoch))"
echo "Restore seconds: $((restore_end-restore_start))"
echo "Backup file: $backup_file"
