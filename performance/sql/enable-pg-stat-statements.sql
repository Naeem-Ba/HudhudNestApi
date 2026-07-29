\set ON_ERROR_STOP on

CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

DO $$
BEGIN
  IF position('pg_stat_statements' IN current_setting('shared_preload_libraries')) = 0 THEN
    RAISE EXCEPTION 'pg_stat_statements is not present in shared_preload_libraries';
  END IF;
END
$$;

SELECT extname, extversion
FROM pg_extension
WHERE extname IN ('postgis', 'pg_trgm', 'pg_stat_statements')
ORDER BY extname;

SELECT pg_stat_statements_reset();
