-- diagnose-unvalidated-constraints.sql
--
-- Diagnoses the exact failure from the Production Database Recovery Gate:
--   "[database-recovery] ERROR: Unvalidated constraints found."
-- (scripts/database/verify-restored-database.sh checks
--  `SELECT COUNT(*) FROM pg_constraint WHERE contype IN ('c','f') AND NOT convalidated`
--  and fails the gate if it's nonzero.)
--
-- Read-only. Run this against the REAL production database yourself, e.g.:
--   psql "$PRODUCTION_DATABASE_URL" -f scripts/database/diagnose-unvalidated-constraints.sql
--
-- A constraint ends up with convalidated = false when it was added as
-- `ADD CONSTRAINT ... NOT VALID` (the standard safe pattern for adding a
-- CHECK/FK to a large live table without holding a long-running exclusive
-- scan lock during the ALTER itself) and the follow-up
-- `VALIDATE CONSTRAINT` was never run. Postgres already enforces such a
-- constraint for all NEW rows/updates from the moment it's added -- only
-- EXISTING rows written before it existed are unverified against it, and
-- pg_dump/pg_restore faithfully preserves the NOT VALID flag, which is
-- exactly why the recovery gate (restoring a fresh copy) can see it even if
-- nobody manually ran a stray ALTER on the live database.

-- ── 1. Every unvalidated CHECK ('c') or FOREIGN KEY ('f') constraint ────────
SELECT
    con.conname                                   AS constraint_name,
    con.contype                                   AS constraint_type,   -- 'c' = CHECK, 'f' = FOREIGN KEY
    nsp.nspname                                   AS schema_name,
    rel.relname                                   AS table_name,
    pg_get_constraintdef(con.oid)                 AS constraint_definition,
    con.convalidated                              AS is_validated
FROM pg_constraint con
JOIN pg_class     rel ON rel.oid = con.conrelid
JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
WHERE con.contype IN ('c', 'f')
  AND NOT con.convalidated
ORDER BY nsp.nspname, rel.relname, con.conname;

-- ── 2. Row count of each affected table (VALIDATE CONSTRAINT does a full   ──
--       table scan under a SHARE UPDATE EXCLUSIVE lock -- it does NOT block
--       concurrent reads/writes, only other DDL, but a big table means a
--       longer-running validation; use this to decide whether to schedule
--       it for a quiet period) ─────────────────────────────────────────────
SELECT
    nsp.nspname                                   AS schema_name,
    rel.relname                                   AS table_name,
    con.conname                                   AS constraint_name,
    (SELECT reltuples::bigint FROM pg_class WHERE oid = con.conrelid) AS approx_row_count
FROM pg_constraint con
JOIN pg_class     rel ON rel.oid = con.conrelid
JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
WHERE con.contype IN ('c', 'f')
  AND NOT con.convalidated
ORDER BY approx_row_count DESC;

-- ── 3. Ready-to-run fix statements, generated from what's actually found ───
-- Copy the output of this query and run each statement individually (during
-- a quiet period for any large table from query 2 above). VALIDATE CONSTRAINT
-- only scans and checks -- it never rewrites the table and never drops or
-- weakens the constraint, so this is the correct fix, not a workaround.
SELECT
    format(
        'ALTER TABLE %I.%I VALIDATE CONSTRAINT %I;',
        nsp.nspname, rel.relname, con.conname
    ) AS validate_statement
FROM pg_constraint con
JOIN pg_class     rel ON rel.oid = con.conrelid
JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
WHERE con.contype IN ('c', 'f')
  AND NOT con.convalidated
ORDER BY nsp.nspname, rel.relname, con.conname;

-- ── 4. If a VALIDATE CONSTRAINT above fails, it means some EXISTING rows
--       genuinely violate the constraint. Use this per offending
--       constraint (fill in the two placeholders from query 1's
--       constraint_definition column) to find and inspect those rows
--       BEFORE deciding how to fix the data:
--
--   SELECT *
--   FROM <schema>.<table>
--   WHERE NOT (<the boolean expression inside the CHECK's parentheses>)
--   LIMIT 50;
--
--       For an unvalidated FOREIGN KEY specifically, this finds the
--       orphaned rows directly (substitute the real column/parent names
--       from query 1's constraint_definition, e.g. "FOREIGN KEY (AgencyId)
--       REFERENCES "Agencies"(Id)"):
--
--   SELECT child.*
--   FROM <schema>.<child_table> child
--   LEFT JOIN <schema>.<parent_table> parent ON parent.<parent_key> = child.<child_fk_column>
--   WHERE child.<child_fk_column> IS NOT NULL
--     AND parent.<parent_key> IS NULL
--   LIMIT 50;
