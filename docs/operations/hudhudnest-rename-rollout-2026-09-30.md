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
| Production `Jwt__Issuer` / `Jwt__Audience` → `HudhudNest` / `HudhudNestClient` | Done 2026-09-30 (Render env save → manual deploy of `f9a3bc1`, 1m17s, Live) | `/health/ready` 200 throughout; new tokens carry the new values; earlier tokens stay valid through `Jwt:Additional*`. Not verified with a real production login (no test account on production) |
| `Observability__ServiceName` | Not set on Render (neither environment) → default `hudhudnest-api` applies | Env var list reviewed |
| Custom API domains `api.hudhudnest.com` (production) and `staging-api.hudhudnest.com` (Staging) | Done 2026-09-30: added in Render (the free plan allows 2 custom domains per workspace — both slots are now used); CNAMEs `api` → `wohnungen-api.onrender.com` and `staging-api` → `propertyapi-staging-api.onrender.com` in the Netlify DNS zone | Both answer `/health/ready` 200 over HTTPS (Google Trust Services certificates, valid to 2026-12-29); `POST /api/auth/login` returns 401; CORS preflight from `https://hudhudnest.com` (production) and `https://staging--hudhudnest.netlify.app` (Staging) returns 204 with the matching `Access-Control-Allow-Origin`. The old onrender.com hostnames still work |

## Open (in order)
1. **SECURITY — rotate production secrets.** While editing the env form, `Jwt__Key` and `OtpSettings__SecretKey` on the production service were seen to hold a test/placeholder-looking value, identical for both (value deliberately not recorded here). Replace each with a fresh random value (≥ 64 random characters). Rotating `Jwt__Key` invalidates every issued token (all users sign in again) and rotating `OtpSettings__SecretKey` invalidates in-flight OTPs — plan a quiet window. Values must be generated and pasted by the owner, never through chat.
2. **Switch the frontend to the new API hostnames** (`https://api.hudhudnest.com/api` for production, `https://staging-api.hudhudnest.com/api` for Staging): `API_URL` in Netlify, the `environment.prod.ts` / `environment.staging.ts` defaults, `DEFAULT_API_BASE_URL` in the Netlify edge functions, and specs. Blocked in practice until Netlify production deploys resume (credits exhausted). Keep the old `*.onrender.com` hostnames working — Render cannot rename those slugs, and native apps / older bundles still call them.
3. After ≥ 30 days (`Jwt:RefreshTokenDays`): remove `Jwt:AdditionalValidIssuers/Audiences` from `appsettings.json` and ship.
4. Databases: names `propertyapi_*` stay until the planned move off Render's expiring free Postgres (new Supabase project, clearly distinct from the live DB, migrated with `pg_dump`/restore + `tools/HudhudNestApi.Migrator`). Needs owner-created credentials — never pasted through chat.
5. Staging isolation markers (`Staging__EnvironmentId`, `…DatabaseNameMarker`, `…RedisIsolationMarker`, `…StorageIsolationMarker`) must be changed together with the database/Redis they describe.

## Lessons
- A pure rename still needs a full-pipeline run: #248's own PR checks were red (coverage gate; Observability gate only runs on the merge) and it was merged anyway.
- `Jwt` values must be read lazily (inside the `AddJwtBearer` callback); reading them at registration missed configuration layered after `Program.cs`.
- Pushing more commits to a PR branch that is merged meanwhile silently drops them (#250 merged at its first commit): check `git branch -r --contains <sha>` before assuming a change landed.
