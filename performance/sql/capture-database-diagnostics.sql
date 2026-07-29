\set ON_ERROR_STOP on

SELECT jsonb_pretty(to_jsonb(result))
FROM (
  SELECT
    current_database() AS database_name,
    version() AS postgresql_version,
    PostGIS_Version() AS postgis_version,
    pg_database_size(current_database()) AS database_size_bytes,
    (SELECT count(*) FROM "Properties") AS property_count,
    (SELECT count(*) FROM "UserAccounts") AS user_count,
    (SELECT count(*) FROM "PropertyImages") AS image_count,
    (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()) AS active_connections,
    (SELECT count(*) FROM pg_locks WHERE NOT granted) AS waiting_locks,
    (SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()) AS deadlocks
) AS result;
