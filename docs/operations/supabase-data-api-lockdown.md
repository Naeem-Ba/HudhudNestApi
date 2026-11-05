# Supabase Data API lockdown (rls_disabled_in_public, sensitive_columns_exposed)

Supabase publishes the `public` schema through PostgREST to anyone holding the project URL and the anon key.
Any table there without Row-Level Security is readable and writable by the world. The API never uses PostgREST:
it connects as the owning role, which bypasses RLS, so enabling RLS with no policies costs the app nothing.

## Why the advisory kept coming back

1. The fix (migration `20261102090000_EnableRowLevelSecurityOnPublicTables`) only reaches a database when the
   manual Migrator runs against it. Each Supabase project (production, `hudhudnest-staging`, the old `PropertyApi`
   project) needs that run on its own.
2. The migration's event trigger for future tables needs a privilege Supabase may withhold; it then logs a notice
   and carries on, so tables created later (for example `AgencyRatings`) stayed open.
3. Nothing in the pipeline noticed. Supabase's email was the detector.

## What now guarantees it

| Layer | What it does |
|---|---|
| `RowLevelSecurityEnforcer` | Idempotent: enables RLS on every non-extension public table and revokes `anon`/`authenticated` grants. |
| Migrator | Runs the enforcer on **every** execution (pending migrations or not) and exits 1 if a table is still open. |
| `row-level-security` readiness check | Degraded (and a logged error naming the tables) while any public table is open. Never Unhealthy: an exposure finding must not become an outage. |
| `RowLevelSecurityTests` | In CI, a fully migrated schema has no open table; the enforcer closes tables the event trigger missed. |

## Verified live (2026-11-05, Security Advisor and read-only SQL on both projects)

`hudhudnest-staging` and `PropertyApi` each show exactly one advisor error left, `public.spatial_ref_sys`, and no
`sensitive_columns_exposed`. Every application table (including `AgencyRatings`) already has RLS. The remaining
table belongs to PostGIS, which is installed in `public`:

- owner is `supabase_admin`; the `postgres` role the app and the Migrator use cannot `ALTER` it, so no migration can
  enable RLS on it (the enforcer and the readiness check skip extension members on purpose);
- `anon` and `authenticated` hold `SELECT` only, no write grant: it exposes the public EPSG reference catalogue, no
  user data.

It is an accepted, documented residual. It can only be cleared by Supabase support enabling RLS as `supabase_admin`,
or by recreating PostGIS in the `extensions` schema, which drops every geometry column and is not worth the risk.
Do not "fix" it by dropping the extension.

## Closing the hole right now

Run `scripts/database/close-supabase-data-api.sql` in the SQL editor of **each** affected project. It is idempotent
and ends with two verification queries that must return no rows. Alternatively run the Migrator against that project.
Then re-run the Supabase Security Advisor.
