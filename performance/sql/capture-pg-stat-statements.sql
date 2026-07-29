\set ON_ERROR_STOP on

SELECT COALESCE(
  jsonb_pretty(jsonb_agg(to_jsonb(statement) ORDER BY statement.total_execution_time_ms DESC)),
  '[]'::text)
FROM (
  SELECT
    queryid::text AS query_id,
    left(regexp_replace(query, E'\\s+', ' ', 'g'), 1000) AS normalized_query,
    calls,
    round(total_plan_time::numeric, 3) AS total_plan_time_ms,
    round(mean_plan_time::numeric, 3) AS mean_plan_time_ms,
    round(total_exec_time::numeric, 3) AS total_execution_time_ms,
    round(mean_exec_time::numeric, 3) AS mean_execution_time_ms,
    rows,
    shared_blks_hit,
    shared_blks_read,
    temp_blks_read,
    temp_blks_written,
    wal_records,
    wal_bytes::text AS wal_bytes
  FROM pg_stat_statements
  WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
    AND query NOT ILIKE '%pg_stat_statements%'
  ORDER BY total_exec_time DESC
  LIMIT 100
) AS statement;
