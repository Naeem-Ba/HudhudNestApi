# Database host migration — Render Postgres → new Supabase project

Moves the live database off Render's expiring free Postgres to a **new, empty** Supabase project. The live database is never written to; the old one stays untouched as the rollback target until the owner decides otherwise.

Tooling: `scripts/database/copy-database-to-new-host.sh` (dump → restore → verify). Needs `pg_dump`/`pg_restore`/`psql` ≥ the source server's major version. Connection strings are secrets: set them in your own shell, never paste them into chat, tickets, or logs (the script prints no credentials).

## 0. Decide first
- **Name the new database distinctly**: Supabase project `hudhudnest-prod` (separate from anything `propertyapi_*`). Use a **separate** project for Staging (`hudhudnest-staging`) later — never share.
- Use the Supabase **direct** or **session pooler** connection (port 5432) for the copy and for the app (EF Core/Npgsql with prepared statements does not work on the transaction pooler, port 6543).
- Pick a low-traffic window; the write freeze in step 4 is typically minutes.

## 1. Create the target (owner)
1. Supabase → New project, region close to the Render region, PostgreSQL ≥ the source major version.
2. Database → Extensions → enable `postgis`.
3. Copy the connection URI (session pooler or direct, `sslmode=require`) into a local shell variable only.

## 2. Dry run (no cut-over, safe any time)
```bash
export SOURCE_DATABASE_URL='<RENDER_EXTERNAL_DATABASE_URL>'   # read-only use
export TARGET_DATABASE_URL='<SUPABASE_URI>'                   # must be empty
export CONFIRM_COPY_TO_NEW_HOST=true
bash scripts/database/copy-database-to-new-host.sh
```
PASS = every critical table has identical row counts and the same number of EF migrations. A failure leaves the source untouched: drop/recreate the target and rerun. The script refuses a non-empty target, the same host/database, or an older target server.

## 3. Validate the application against the copy
Point a **local** API (not Render) at the copy and run the checks without write side-effects:
```bash
export ConnectionStrings__DefaultConnection='<ADONET_CONNECTION_FOR_SUPABASE>'
dotnet run --project tools/HudhudNestApi.DatabaseRecoveryVerifier/HudhudNestApi.DatabaseRecoveryVerifier.csproj --configuration Release
```
Then `/health/ready` and a property list read. Confirm the Migrator reports "No pending migrations" (`dotnet run --project tools/HudhudNestApi.Migrator`).

## 4. Cut-over (owner-approved, production change)
1. Take a fresh backup (`database-backup.yml`) and note the Render deploy ID as the rollback point.
2. Freeze writes: suspend the Render service (or put the API in maintenance).
3. Drop the target's tables (recreate the empty project/database), rerun step 2 so the copy is final.
4. Render production → Environment: change `ConnectionStrings__DefaultConnection` to the Supabase connection string; save → deploy.
5. Verify `/health/ready`, a real login, a property list/read, and logs. Watch connections/latency for an hour.
6. **Rollback:** restore the old `ConnectionStrings__DefaultConnection` value and redeploy. The old database is unchanged, but writes made after cut-over exist only on Supabase.

## 5. Staging
Repeat with a separate `hudhudnest-staging` project. The Staging isolation guard requires `Staging__DatabaseNameMarker` to appear inside the database name of the connection string — change `Staging__EnvironmentId`, `…DatabaseNameMarker`, `…RedisIsolationMarker`, `…StorageIsolationMarker` **together** with the database/Redis they describe, and keep them different from production.

## 6. After
- Keep the old Render database read-only for ≥ 7 days, then delete it.
- Update `.github` secrets that hold database URLs (`database-backup.yml`, restore drill) and confirm the next scheduled backup succeeds against the new host.
- Record the date and evidence in `hudhudnest-rename-rollout-2026-09-30.md`.
