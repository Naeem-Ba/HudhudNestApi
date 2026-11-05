using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HudhudNestApi.Infrastructure.Persistence;

/// <summary>
/// Keeps every table in the public schema closed to the Supabase Data API (PostgREST).
///
/// Supabase exposes the public schema to anyone holding the project URL and the anon key. A table
/// without Row-Level Security there is readable and writable by the world, which is what the
/// advisories rls_disabled_in_public and sensitive_columns_exposed report. The API itself never uses
/// PostgREST: it connects as the table-owning role, which bypasses RLS, so enabling RLS with no
/// policies changes nothing for the application.
///
/// The migration 20261102090000_EnableRowLevelSecurityOnPublicTables fixed the tables that existed
/// on 2026-11-02 and tried to install an event trigger for later ones. Neither is enough on its own:
/// a migration protects a database only after the manual Migrator has run against it, and the event
/// trigger needs a privilege Supabase may withhold (the migration then skips it with a notice, so a
/// table created later, such as AgencyRatings, stayed open). This enforcer is the guarantee that
/// does not depend on either: the Migrator runs it on every execution, whether or not a migration
/// was pending, and the readiness check reports any table it would still have to fix.
///
/// Everything here is idempotent, and a no-op on a database without the anon/authenticated roles
/// (CI containers, local PostgreSQL).
/// </summary>
public static class RowLevelSecurityEnforcer
{
    /// <summary>
    /// Tables in public that are not extension members (PostGIS spatial_ref_sys is owned by the
    /// extension and cannot be altered by the application role) and have RLS switched off.
    /// </summary>
    private const string UnprotectedTablesSql = """
        SELECT c.relname AS "Value"
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public'
          AND c.relkind IN ('r', 'p')
          AND NOT c.relrowsecurity
          AND NOT EXISTS (
              SELECT 1
              FROM pg_depend d
              WHERE d.classid = 'pg_class'::regclass
                AND d.objid = c.oid
                AND d.deptype = 'e')
        ORDER BY c.relname
        """;

    private const string EnableRowLevelSecuritySql = """
        DO $$
        DECLARE
            target record;
        BEGIN
            FOR target IN
                SELECT c.relname
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relkind IN ('r', 'p')
                  AND NOT c.relrowsecurity
                  AND NOT EXISTS (
                      SELECT 1
                      FROM pg_depend d
                      WHERE d.classid = 'pg_class'::regclass
                        AND d.objid = c.oid
                        AND d.deptype = 'e')
            LOOP
                EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', target.relname);
            END LOOP;
        END
        $$;
        """;

    private const string RevokeDataApiGrantsSql = """
        DO $$
        DECLARE
            api_role text;
        BEGIN
            FOREACH api_role IN ARRAY ARRAY['anon', 'authenticated']
            LOOP
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = api_role) THEN
                    EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA public FROM %I', api_role);
                    EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM %I', api_role);
                    EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM %I', api_role);
                    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM %I', api_role);
                    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM %I', api_role);
                    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM %I', api_role);
                END IF;
            END LOOP;
        END
        $$;
        """;

    /// <summary>The public tables that currently have Row-Level Security switched off.</summary>
    public static async Task<IReadOnlyList<string>> FindUnprotectedTablesAsync(
        DatabaseFacade database,
        CancellationToken cancellationToken = default)
    {
        return await database
            .SqlQueryRaw<string>(UnprotectedTablesSql)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Enables RLS on every unprotected public table and revokes the Data API roles' grants.
    /// Returns the tables that are still unprotected afterwards (empty when everything is closed).
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnforceAsync(
        DatabaseFacade database,
        CancellationToken cancellationToken = default)
    {
        await database.ExecuteSqlRawAsync(EnableRowLevelSecuritySql, cancellationToken);
        await database.ExecuteSqlRawAsync(RevokeDataApiGrantsSql, cancellationToken);

        return await FindUnprotectedTablesAsync(database, cancellationToken);
    }
}
