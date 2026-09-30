# HudhudNest rename — rollout state (2026-09-30)

Living status of the gradual rename of identifiers that touch live systems. Update this file as each step lands.

## Done
| Item | State | Evidence |
|---|---|---|
| Code, CI, docs, metric/alert names | Merged (#248, #249) | `Production Gate` green on `f9a3bc1` |
| Metric names `hudhudnest_*`, alert `service: hudhudnest`, dashboards, `verify-observability.sh` | Merged (#249) | Observability Export Gate green. #248 alone broke this gate (underscore metric names were missed) — fixed in #249 |
| JWT issuer/audience default → `HudhudNest` / `HudhudNestClient`, old values still accepted via `Jwt:AdditionalValidIssuers/Audiences` | Merged (#249) | `JwtIssuerTransitionTests`; Staging smoke green |
| Render **Dockerfile Path** `PropertyApi/Dockerfile` → `HudhudNestApi/Dockerfile` | Done by owner in Render (production + staging) | Render deploy `f9a3bc1` Live on both. Before this, every Render deploy since the folder rename failed with `lstat .../PropertyApi` |
| Staging runs the new JWT values | Yes — Staging sets only `Jwt__Key`, so it uses the code defaults | Staging smoke E2E passed |
| `Observability__ServiceName` | Not set on Render (neither environment) → default `hudhudnest-api` applies | Env var list reviewed |

## Open (in order)
1. **Production `Jwt__Issuer` / `Jwt__Audience`** are set explicitly on Render (old `PropertyApi` / `PropertyApiClient`). Change to `HudhudNest` / `HudhudNestClient` (non-secret). Safe: both old and new are accepted. Verify login + a refresh afterwards.
2. After ≥ 30 days (`Jwt:RefreshTokenDays`): remove `Jwt:AdditionalValidIssuers/Audiences` from `appsettings.json` and ship.
3. API hostname: Render cannot rename the `wohnungen-api.onrender.com` / `propertyapi-staging-api.onrender.com` slugs. Add custom domains (`api.hudhudnest.com`, `staging-api.hudhudnest.com`) in Render + DNS (Netlify DNS), then switch `API_URL` in the frontend and `Cors`/`Frontend__*` values as needed. Keep the old hostnames working until clients have moved.
4. Databases: names `propertyapi_*` stay until the planned move off Render's expiring free Postgres (new Supabase project, clearly distinct from the live DB, migrated with `pg_dump`/restore + `tools/HudhudNestApi.Migrator`). Needs owner-created credentials — never pasted through chat.
5. Staging isolation markers (`Staging__EnvironmentId`, `…DatabaseNameMarker`, `…RedisIsolationMarker`, `…StorageIsolationMarker`) must be changed together with the database/Redis they describe.

## Lessons
- A pure rename still needs a full-pipeline run: #248's own PR checks were red (coverage gate; Observability gate only runs on the merge) and it was merged anyway.
- `Jwt` values must be read lazily (inside the `AddJwtBearer` callback); reading them at registration missed configuration layered after `Program.cs`.
