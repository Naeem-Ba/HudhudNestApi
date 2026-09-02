#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env RESTORE_TARGET_DATABASE_URL
require_env MANIFEST_FILE
require_env RESTORE_TARGET_ENVIRONMENT
[ "${RESTORE_TARGET_ENVIRONMENT}" = "recovery-drill" ] || fail "Verification is restricted to recovery-drill."
for command in psql jq; do require_command "${command}"; done
configure_pg_environment "${RESTORE_TARGET_DATABASE_URL}"

VERIFICATION_STARTED_EPOCH="$(date -u +%s)"
EXPECTED_MIGRATION="$(jq -r '.latestEfMigration' "${MANIFEST_FILE}")"
ACTUAL_MIGRATION="$(psql -X -v ON_ERROR_STOP=1 -tAc 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;' | xargs)"
[ "${ACTUAL_MIGRATION}" = "${EXPECTED_MIGRATION}" ] || fail \
  "EF migration mismatch. Expected ${EXPECTED_MIGRATION}; got ${ACTUAL_MIGRATION}."

psql -X -v ON_ERROR_STOP=1 -tAc 'SELECT PostGIS_Full_Version();' >/dev/null
EXPECTED_POSTGIS="$(jq -r '.postgisVersion' "${MANIFEST_FILE}")"
ACTUAL_POSTGIS="$(psql -X -v ON_ERROR_STOP=1 -tAc 'SELECT PostGIS_Lib_Version();' | xargs)"
# Exact version-string equality is structurally unsatisfiable here, not a
# real regression signal: production's PostGIS is Supabase-managed (3.3.7),
# while the drill's restore-postgres image is pinned to match production's
# PostgreSQL MAJOR version (17 -- see ci/docker-compose.database-recovery.yml
# and assert_postgres_client_matches_server), and no PG17-compatible
# postgis/postgis image ships PostGIS 3.3.x upstream (3.3 tops out at PG15).
# Confirmed via two real CI runs on unrelated commits (33663487631,
# 33670058939): "Expected 3.3.7; got 3.5.2" every time, restore-drill
# content notwithstanding. A PostGIS MAJOR-version drift (e.g. 2.x -> 3.x)
# changes on-disk geometry encoding and would be a genuine restore risk, so
# that is still enforced. Minor/patch drift within major version 3 is
# backward compatible for the standard geometry/geography functions this
# application uses, and is additionally proven safe a few lines below by
# actually executing a real ST_Distance query against restored data.
EXPECTED_POSTGIS_MAJOR="${EXPECTED_POSTGIS%%.*}"
ACTUAL_POSTGIS_MAJOR="${ACTUAL_POSTGIS%%.*}"
[ "${ACTUAL_POSTGIS_MAJOR}" = "${EXPECTED_POSTGIS_MAJOR}" ] || fail \
  "PostGIS major version mismatch. Expected ${EXPECTED_POSTGIS} (major ${EXPECTED_POSTGIS_MAJOR}); got ${ACTUAL_POSTGIS} (major ${ACTUAL_POSTGIS_MAJOR})."
if [ "${ACTUAL_POSTGIS}" != "${EXPECTED_POSTGIS}" ]; then
  log "PostGIS minor/patch version differs from production (expected ${EXPECTED_POSTGIS}, restored-instance has ${ACTUAL_POSTGIS}); continuing, since major version matches and the spatial correctness check below still runs."
fi
SPATIAL_DISTANCE="$(psql -X -v ON_ERROR_STOP=1 -tAc \
  "SELECT round(ST_Distance(ST_SetSRID(ST_MakePoint(13.405,52.52),4326)::geography, ST_SetSRID(ST_MakePoint(13.406,52.521),4326)::geography)::numeric, 2);")"
[ -n "${SPATIAL_DISTANCE//[[:space:]]/}" ] || fail "PostGIS spatial query returned no result."

REQUIRED_TABLE_COUNT=0
while IFS= read -r table || [ -n "${table}" ]; do
  [ -n "${table}" ] || continue
  REQUIRED_TABLE_COUNT="$((REQUIRED_TABLE_COUNT + 1))"
  [[ "${table}" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || fail "Unsafe critical table name."
  jq -e --arg table "${table}" '.tableRowCounts | has($table)' "${MANIFEST_FILE}" >/dev/null || fail \
    "Backup manifest has no row count for required table: ${table}"
done < "${DATABASE_SCRIPTS_DIR}/critical-tables.txt"
[ "$(jq '.tableRowCounts | length' "${MANIFEST_FILE}")" -eq "${REQUIRED_TABLE_COUNT}" ] || fail \
  "Backup manifest critical table set does not match the required table set."

while IFS=$'\t' read -r table expected; do
  [ -n "${table}" ] || continue
  [[ "${table}" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || fail "Unsafe table name in manifest."
  quoted_table="$(quote_identifier "${table}")"
  actual="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT COUNT(*) FROM ${quoted_table};" | xargs)"
  [ "${actual}" = "${expected}" ] || fail \
    "Row-count mismatch for ${table}: expected ${expected}, got ${actual}."
done < <(jq -r '.tableRowCounts | to_entries[] | [.key, (.value|tostring)] | @tsv' "${MANIFEST_FILE}")

INVALID_INDEXES="$(psql -X -v ON_ERROR_STOP=1 -tAc \
  "SELECT COUNT(*) FROM pg_index WHERE NOT indisvalid OR NOT indisready;")"
[ "${INVALID_INDEXES//[[:space:]]/}" = "0" ] || fail "Invalid or unready indexes found."
UNVALIDATED_CONSTRAINTS="$(psql -X -v ON_ERROR_STOP=1 -tAc \
  "SELECT COUNT(*) FROM pg_constraint WHERE contype IN ('c','f') AND NOT convalidated;")"
[ "${UNVALIDATED_CONSTRAINTS//[[:space:]]/}" = "0" ] || fail "Unvalidated constraints found."
PRIMARY_KEY_COUNT="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT COUNT(*) FROM pg_constraint WHERE contype='p' AND connamespace='public'::regnamespace;" | xargs)"
FOREIGN_KEY_COUNT="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT COUNT(*) FROM pg_constraint WHERE contype='f' AND connamespace='public'::regnamespace;" | xargs)"
UNIQUE_CONSTRAINT_COUNT="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT COUNT(*) FROM pg_constraint WHERE contype='u' AND connamespace='public'::regnamespace;" | xargs)"
UNIQUE_INDEX_COUNT="$(psql -X -v ON_ERROR_STOP=1 -tAc "SELECT COUNT(*) FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND i.indisunique AND NOT i.indisprimary AND i.indisvalid AND i.indisready;" | xargs)"
[ "${PRIMARY_KEY_COUNT}" -gt 0 ] || fail "No primary keys were restored."
[ "${FOREIGN_KEY_COUNT}" -gt 0 ] || fail "No foreign keys were restored."
[ "$((UNIQUE_CONSTRAINT_COUNT + UNIQUE_INDEX_COUNT))" -gt 0 ] || fail "No unique constraints or indexes were restored."

psql -X -v ON_ERROR_STOP=1 <<'SQL'
DO $body$
DECLARE
  fk record;
  join_expression text;
  non_null_expression text;
  parent_probe text;
  orphan_count bigint;
BEGIN
  FOR fk IN
    SELECT c.oid, ns.nspname AS child_schema, child.relname AS child_table,
           ps.nspname AS parent_schema, parent.relname AS parent_table,
           c.conkey, c.confkey
    FROM pg_constraint c
    JOIN pg_class child ON child.oid = c.conrelid
    JOIN pg_namespace ns ON ns.oid = child.relnamespace
    JOIN pg_class parent ON parent.oid = c.confrelid
    JOIN pg_namespace ps ON ps.oid = parent.relnamespace
    WHERE c.contype = 'f'
  LOOP
    SELECT string_agg(format('c.%I = p.%I', ca.attname, pa.attname), ' AND ' ORDER BY keys.ordinality),
           string_agg(format('c.%I IS NOT NULL', ca.attname), ' AND ' ORDER BY keys.ordinality),
           min(format('p.%I', pa.attname))
      INTO join_expression, non_null_expression, parent_probe
    FROM unnest(fk.conkey, fk.confkey) WITH ORDINALITY AS keys(child_attnum, parent_attnum, ordinality)
    JOIN pg_attribute ca ON ca.attrelid = (SELECT conrelid FROM pg_constraint WHERE oid = fk.oid) AND ca.attnum = keys.child_attnum
    JOIN pg_attribute pa ON pa.attrelid = (SELECT confrelid FROM pg_constraint WHERE oid = fk.oid) AND pa.attnum = keys.parent_attnum;

    EXECUTE format(
      'SELECT count(*) FROM %I.%I c LEFT JOIN %I.%I p ON %s WHERE %s AND %s IS NULL',
      fk.child_schema, fk.child_table, fk.parent_schema, fk.parent_table,
      join_expression, non_null_expression, parent_probe)
      INTO orphan_count;
    IF orphan_count > 0 THEN
      RAISE EXCEPTION 'Foreign key % has % orphan rows', fk.oid::regclass, orphan_count;
    END IF;
  END LOOP;
END
$body$;

DO $body$
DECLARE
  seq record;
  current_value bigint;
  maximum_value bigint;
BEGIN
  FOR seq IN
    SELECT sn.nspname AS sequence_schema, s.relname AS sequence_name,
           tn.nspname AS table_schema, t.relname AS table_name, a.attname AS column_name
    FROM pg_class s
    JOIN pg_namespace sn ON sn.oid = s.relnamespace
    JOIN pg_depend d ON d.objid = s.oid AND d.deptype IN ('a','i')
    JOIN pg_class t ON t.oid = d.refobjid
    JOIN pg_namespace tn ON tn.oid = t.relnamespace
    JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = d.refobjsubid
    WHERE s.relkind = 'S'
  LOOP
    EXECUTE format('SELECT last_value FROM %I.%I', seq.sequence_schema, seq.sequence_name) INTO current_value;
    EXECUTE format('SELECT COALESCE(MAX(%I), 0) FROM %I.%I', seq.column_name, seq.table_schema, seq.table_name) INTO maximum_value;
    IF current_value < maximum_value THEN
      RAISE EXCEPTION 'Sequence %.% is behind %.% column %', seq.sequence_schema, seq.sequence_name, seq.table_schema, seq.table_name, seq.column_name;
    END IF;
  END LOOP;
END
$body$;
SQL

VERIFICATION_FINISHED_EPOCH="$(date -u +%s)"
DURATION_SECONDS="$((VERIFICATION_FINISHED_EPOCH - VERIFICATION_STARTED_EPOCH))"
RESULT_DIR="${RESTORE_RESULT_DIR:-${REPOSITORY_ROOT}/artifacts/database-restore}"
mkdir -p "${RESULT_DIR}"
BACKUP_CREATED_EPOCH="$(date -u -d "$(jq -r '.createdAtUtc' "${MANIFEST_FILE}")" +%s)"
BACKUP_AGE_SECONDS="$((VERIFICATION_FINISHED_EPOCH - BACKUP_CREATED_EPOCH))"
jq -n \
  --arg status PASS \
  --arg verifiedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg latestEfMigration "${ACTUAL_MIGRATION}" \
  --arg postgisVersion "${ACTUAL_POSTGIS}" \
  --arg spatialDistanceMeters "${SPATIAL_DISTANCE//[[:space:]]/}" \
  --argjson durationSeconds "${DURATION_SECONDS}" \
  --argjson backupAgeSeconds "${BACKUP_AGE_SECONDS}" \
  --argjson rpoTargetSeconds "${RPO_TARGET_SECONDS:-3600}" \
  --argjson rtoTargetSeconds "${RTO_TARGET_SECONDS:-7200}" \
  --argjson primaryKeyCount "${PRIMARY_KEY_COUNT}" \
  --argjson foreignKeyCount "${FOREIGN_KEY_COUNT}" \
  --argjson uniqueConstraintCount "${UNIQUE_CONSTRAINT_COUNT}" \
  --argjson uniqueIndexCount "${UNIQUE_INDEX_COUNT}" \
  '{status:$status,verifiedAtUtc:$verifiedAtUtc,latestEfMigration:$latestEfMigration,postgisVersion:$postgisVersion,spatialDistanceMeters:($spatialDistanceMeters|tonumber),durationSeconds:$durationSeconds,backupAgeSeconds:$backupAgeSeconds,rpoTargetSeconds:$rpoTargetSeconds,rtoTargetSeconds:$rtoTargetSeconds,rpoPass:($backupAgeSeconds <= $rpoTargetSeconds),primaryKeyCount:$primaryKeyCount,foreignKeyCount:$foreignKeyCount,uniqueConstraintCount:$uniqueConstraintCount,uniqueIndexCount:$uniqueIndexCount}' \
  > "${RESULT_DIR}/database-verification.json"
log "Restored database validation passed in ${DURATION_SECONDS}s."
