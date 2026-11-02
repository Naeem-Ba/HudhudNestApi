using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <summary>
    /// Supabase security advisories rls_disabled_in_public and sensitive_columns_exposed: every
    /// table in the public schema is reachable by anyone holding the project URL and the anon key
    /// through PostgREST, because Row-Level Security was never enabled. That includes Users,
    /// RefreshTokens, OtpCodes and the Data Protection keys.
    ///
    /// The API never uses PostgREST. It connects as the table-owning postgres role, which bypasses
    /// RLS, so enabling RLS with no policies closes the Data API completely and changes nothing for
    /// the application. The grants held by the Supabase anon and authenticated roles are revoked as
    /// well so the tables are closed even if RLS is ever switched off by mistake. Both steps are
    /// no-ops on a database without those roles (CI containers, local PostgreSQL).
    ///
    /// An event trigger enables RLS on every table created in public afterwards, so a future
    /// migration cannot reopen the hole. It needs a role allowed to create event triggers; if the
    /// connecting role is not, the migration still succeeds and a notice says so.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261102090000_EnableRowLevelSecurityOnPublicTables")]
    public partial class EnableRowLevelSecurityOnPublicTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
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
                    LOOP
                        BEGIN
                            EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', target.relname);
                        EXCEPTION WHEN insufficient_privilege THEN
                            -- Extension-owned tables such as PostGIS spatial_ref_sys.
                            RAISE NOTICE 'Row-level security not enabled on public.%: insufficient privilege', target.relname;
                        END;
                    END LOOP;
                END
                $$;
                """);

            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.hudhud_enable_rls_on_new_tables()
                RETURNS event_trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    created record;
                BEGIN
                    FOR created IN
                        SELECT object_identity
                        FROM pg_event_trigger_ddl_commands()
                        WHERE command_tag IN ('CREATE TABLE', 'CREATE TABLE AS', 'SELECT INTO')
                          AND object_type IN ('table', 'partitioned table')
                          AND schema_name = 'public'
                    LOOP
                        BEGIN
                            EXECUTE format('ALTER TABLE IF EXISTS %s ENABLE ROW LEVEL SECURITY', created.object_identity);
                        EXCEPTION WHEN OTHERS THEN
                            RAISE LOG 'hudhud_enable_rls_on_new_tables: could not enable RLS on %: %', created.object_identity, SQLERRM;
                        END;
                    END LOOP;
                END
                $$;
                """);

            migrationBuilder.Sql("REVOKE ALL ON FUNCTION public.hudhud_enable_rls_on_new_tables() FROM PUBLIC;");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_event_trigger WHERE evtname = 'hudhud_enable_rls_on_new_tables') THEN
                        DROP EVENT TRIGGER hudhud_enable_rls_on_new_tables;
                    END IF;

                    CREATE EVENT TRIGGER hudhud_enable_rls_on_new_tables
                        ON ddl_command_end
                        WHEN TAG IN ('CREATE TABLE', 'CREATE TABLE AS', 'SELECT INTO')
                        EXECUTE FUNCTION public.hudhud_enable_rls_on_new_tables();
                EXCEPTION WHEN insufficient_privilege THEN
                    RAISE NOTICE 'Event trigger hudhud_enable_rls_on_new_tables not created: insufficient privilege. New tables need ENABLE ROW LEVEL SECURITY in their own migration.';
                END
                $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Row-level security and the revoked grants are deliberately kept: rolling this
            // migration back must not reopen the public Data API. Only the automation is removed.
            migrationBuilder.Sql("DROP EVENT TRIGGER IF EXISTS hudhud_enable_rls_on_new_tables;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.hudhud_enable_rls_on_new_tables();");
        }
    }
}
