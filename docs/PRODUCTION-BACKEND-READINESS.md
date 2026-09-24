# Production Backend Readiness — Phase 1 Gate

**Scope:** ASP.NET Core / Clean Architecture backend (`PropertyApi`) only. Frontend (Angular) and
mobile (Capacitor) are out of scope except where they affect a backend decision (CORS, CSRF).
**Method:** repository/code audit, live GitHub API queries (environments, secrets metadata, workflow
runs), a live in-progress `production-gate.yml` run observed end-to-end, and a local `dotnet build`.
No credentials were invented; no external service (Render dashboard, Upstash, Cloudinary) was queried
live in this session beyond what GitHub's API exposes — those facts are cited from this repository's
own prior documentation (`docs/architecture/ARD/ARD-PropertyApi.md`, `docs/REDIS-HA.md`) and dated
accordingly.
**Date of this audit:** 2026-09-04.

---

## Executive Summary

```
FINAL STATUS: BLOCKED
```

The codebase itself is unusually mature for production safety: fail-fast startup validators reject
missing/weak secrets, a placeholder email domain, `Trust Server Certificate=true` in Production, a
missing Redis connection, and an unconfigured CORS/ForwardedHeaders trust boundary. No hardcoded
secrets, no `localhost` runtime dependency, and no test/Production database confusion were found in
the code path that actually runs in Production.

**BLOCKED is not a statement about code quality — it is a statement about the release pipeline's
current, live, verified state.** Three independent, currently-reproducing facts make an actual
Production release impossible right now without human intervention:

1. **`PRODUCTION_DEPLOY_HOOK_URL` does not exist** — not on the `production` GitHub Environment, not
   at the repository level. `deploy-production` will fail at its "Trigger production deployment" step
   even after every gate above it turns green (confirmed live via GitHub API, see §Secrets Inventory).
2. **The mandatory `staging-smoke` gate is failing right now**, on the real, current `master` HEAD, with
   `Staging did not activate the intended commit with complete migrations before timeout` — because no
   step in the pipeline applies EF Core migrations to Staging after triggering a deploy (migrations are
   manual-only, `tools/PropertyApi.Migrator`, by design). See §CI/CD Readiness, Finding B2.
3. **The mandatory `Production Database Recovery Gate` (real backup + restore + validate against
   PostgreSQL 17/PostGIS) is failing right now**, on the same run, at the "Restore and validate the
   backup" step. See §CI/CD Readiness, Finding B3.

`deploy-production` requires all three (plus `redis-sentinel-ha`, `performance-validation`,
`observability-validation`, `production-deployment-gate`) to succeed. It cannot run to completion
today. This is evidence gathered from a live workflow run observed during this audit
(`https://github.com/Naeem-Ba/PropertyApi/actions/runs/33911314587`), not a theoretical gap.

Two further HIGH-severity, previously-known and self-documented risks compound the picture:
Production's `production`/`production-release`/`production-recovery` GitHub Environments carry **zero
protection rules** (confirmed live), and Production's real Redis (Upstash Free tier, per this repo's
own `docs/REDIS-HA.md`) **has no HA/failover capability at all**.

None of this required guessing. Every BLOCKER below is backed by a command, an API response, or a
file citation in this report.

---

## Architecture — Current Production (as documented + code-verified)

```
Mobile App (Capacitor) ──┐
                          │ HTTPS
Web App (Netlify,        │
realestateworld.world) ──┼──► propertyapi-api (Render, Docker, ASPNETCORE_ENVIRONMENT=Production)
                          │        │
                          │        ├── PostgreSQL + PostGIS (dedicated Production instance)
                          │        ├── Redis — Upstash (external managed, Free tier, no HA)
                          │        ├── Cloudinary (image storage; credential-isolated from Staging,
                          │        │   folder-shared)
                          │        ├── Resend (transactional email) / SMTP fallback
                          │        ├── SMS HTTP provider (OTP)
                          │        └── OTLP → otel-collector → Prometheus/Grafana/Tempo
```

Deployment is Render-hosted, fronted by Render's own edge (itself behind Cloudflare per
`ForwardedHeadersRegistration.cs` comments). There is **no separate `deploy-production.yml` file** —
the `deploy-production` job inside `.github/workflows/production-gate.yml` is the only path to a
Production deploy, and it fires a Render Deploy Hook after every upstream gate succeeds. Auto-deploy
from a plain push is explicitly disabled for this job (`workflow_dispatch` only) as of PR #124
(2026-09-04) — see Finding H1 for why that's a mitigation, not equivalent to reviewer protection.

---

## Findings

| ID | Severity | Finding | Evidence | Status | Action |
|----|----------|---------|----------|--------|--------|
| B1 | BLOCKER | `PRODUCTION_DEPLOY_HOOK_URL` is not configured on the `production` environment or at repo level | `gh api .../environments/production/secrets` → `{"total_count":0}`; `gh api .../actions/secrets` (repo-level, 7 secrets) does not list it | Confirmed live, 2026-09-04 | Manual action required — see §Remaining Manual Actions |
| B2 | BLOCKER | Mandatory `staging-smoke` gate fails: Staging deploy never reports `migration.complete=true` because no CI step applies EF Core migrations to Staging after the deploy hook fires | Live run `33911314587`, job "Mandatory Staging Deploy + E2E Smoke" → annotation: *"Staging did not activate the intended commit with complete migrations before timeout."*; workflow code at [production-gate.yml:1050-1090](../.github/workflows/production-gate.yml) has no migrator step between "Trigger staging deployment" and "Wait for intended staging release" | Confirmed live, reproducing on current `master` HEAD | Fix is in-repo (add an automated migration-apply step) but needs a new secret (Staging DB connection string) that does not currently exist — classified BLOCKER, not silently fixed. See §Remaining Manual Actions |
| B3 | BLOCKER | Mandatory `Production Database Recovery Gate` (real backup → restore → validate against PostgreSQL 17 + PostGIS) fails at "Restore and validate the backup" | Live run `33911314587`, job "Production Database Recovery Gate / Actual PostgreSQL 17 + PostGIS Restore", step 11 failed after steps 1-10 (including "Create fresh encrypted backup") succeeded | Confirmed live, reproducing on current `master` HEAD | Root cause not fully diagnosable from this session (job logs are withheld by GitHub until the whole run completes, and the run outlived this audit's polling window). **Manual action:** re-run `.github/workflows/database-restore-drill.yml` via `workflow_dispatch` once this run finishes, capture the "Restore and validate the backup" step log, and fix before attempting a Production release |
| H1 | HIGH | `production`, `production-release`, `production-recovery` GitHub Environments have **zero protection rules** — no required reviewers, no wait timer, no branch restriction | `gh api repos/Naeem-Ba/PropertyApi/environments` → all three show `"protection_rules":[]` (checked live, 2026-09-04) | Confirmed, previously self-documented as a known org-billing-plan limitation (`docs/testing/staging-production-gate-checklist.md`) | Cannot be fixed from the repo — GitHub rejects "Required reviewers" on this org's current billing plan for a private repo. The `workflow_dispatch`-only trigger is a partial mitigation, not equivalent. Manual action required (billing upgrade or alternate approval control) |
| H2 | HIGH | Production Redis (Upstash) has **no HA/failover capability** — confirmed Free tier, no payment method configured, serves 4 coupled workloads (rate limiting, output cache, security-stamp cache, SignalR backplane) | `docs/REDIS-HA.md` lines 180-185 (team's own documentation, dated before this session — not independently re-verified against the live Upstash account in this session, since no Upstash credentials were available) | Documented, not re-verified live this session | STATUS = NOT VERIFIED per the task's own rule for unproven HA. Manual action: either upgrade to a paid HA tier, or explicitly accept a Redis-outage blast radius (auth security-stamp cache, rate limiting, real-time notifications) as a conscious risk with a written incident runbook |
| M1 | MEDIUM | Production's `ForwardedHeaders:KnownNetworks` reportedly includes `0.0.0.0/0` (per this session's recalled prior-session notes, not re-verified live against Render this session) | `docs/architecture/ARD/ARD-PropertyApi.md` states Production has ForwardedHeaders "enabled, with explicit KnownNetworks" but does not print the value in-repo (correctly — it's an env var, not committed); a `0.0.0.0/0` entry, if accurate, means the "known network" boundary trusts the immediate peer regardless of IP, relying entirely on Render's edge being the only thing that can reach Kestrel | Not independently re-verified this session (no Render dashboard access) | Manual action: confirm the live value in Render's environment variables; if it is genuinely `0.0.0.0/0`, document explicitly *why* (Render publishes no fixed proxy range) rather than leaving it implicit |
| M2 | MEDIUM | `docs/operations/redis-production-readiness-decision.md` ("Decision: FAIL... blocks release") is stale/contradicted by current pipeline behavior — `production-gate.yml`'s `validate-redis-ha-topology` job now treats a missing `REDIS_HA_EVIDENCE_JSON` as **non-blocking** (exits 0 with a notice), per the newer, more detailed reasoning in `docs/REDIS-HA.md` | Compared [production-gate.yml:876-906](../.github/workflows/production-gate.yml) against both docs | Confirmed by direct code/doc comparison | Reconcile the two documents — either delete/update the older decision doc or add a pointer to `docs/REDIS-HA.md` so a reviewer doesn't read a stale "blocks release" claim |
| M3 | MEDIUM | Redis workload isolation not implemented (ADR-005) — one Redis instance serves rate limiting, output cache, security-stamp cache, and SignalR backplane; a saturation/outage event affects all four at once | `docs/architecture/adr-redis-workload-isolation.md`; ARD risk R1 | Previously documented, not re-verified as changed | Track against ADR-005's migration plan; not a release blocker on its own, compounds with H2 |
| M4 | MEDIUM | No strict row-level tenant isolation between real-estate Agencies (ADR-006) — authorization relies on explicit predicate checks per handler, not a global query filter | ARD risk R2; `Agency.cs` header comment (explicit, conscious scope decision) | Previously documented | No code change made (task rule: do not redesign architecture); flagged for awareness only |
| L1 | LOW | Cloudinary Staging and Production share the same folder namespace (`property-images/`, etc.) — credential-isolated, not storage-isolated | ARD risk R4; [[render-staging-deployment]] session memory | Documented | Low risk (media cross-contamination between environments, not a security leak); optional future fix: environment-prefixed folder names |
| L2 | ~~LOW~~ **Resolved 2026-09-24** | `security-stamp-resilience-sources-20260714-213313.zip` is committed to the repo root — a zip bundle of source files already tracked elsewhere in the repo (verified: contains only `.cs` source, no secrets, no config with real values) | `git ls-files` + `unzip -l` inspection this session | Confirmed harmless | Housekeeping only — safe to delete, not a security or readiness issue | Removed together with `Collect-SecurityStampResilience-Sources.ps1`; root `*.zip` is now git-ignored.
| I1 | INFO | Gitleaks secret scanning runs on every CI build, pinned to a commit SHA | `.github/workflows/ci.yml:572-584` | Confirmed | No action — cited as evidence for §Search-based Leakage Detection |
| I2 | INFO | `dotnet build PropertyApi.sln -c Release` succeeds locally with 0 errors | Run this session | Confirmed | No action |

---

## Environment Configuration Audit

No secrets or Production credentials found committed to git in any `appsettings*.json`:

- `PropertyApi/appsettings.json` (base, shipped): every secret-shaped key (`Jwt:Key`,
  `Cloudinary:*`, `Email:*`, `Redis:ConnectionString`, `ConnectionStrings:DefaultConnection`) is an
  **empty string** — nothing to leak, and every one of them fails fast in Production if left empty
  (`JwtOptions` validator, `RedisConnectionResolver`, `ProductionStartupValidator`).
- `PropertyApi/appsettings.Development.json`: no secrets; `Cors:AllowedOrigins` points at
  `localhost:4200` only — expected, Development-only.
- `PropertyApi/appsettings.Testing.json`: contains `Jwt:Key = "TEST_ONLY_SECRET_KEY_..."`,
  `Cloudinary:CloudName/ApiKey/ApiSecret = "test"`, and a `postgres/postgres@localhost` connection
  string — all clearly-labeled, isolated test fixtures, not reachable from Production. Expected, no
  action.
- `PropertyApi/appsettings.Development.example.json`: `Password=CHANGE_ME` placeholder. Expected.
- **No `appsettings.Production.json` exists in the repo, and none should** — Production configuration
  is 100% environment-variable driven (Render dashboard), which is the correct pattern; `.gitignore`
  additionally excludes `appsettings.Production.json` by name (line 55) as a second line of defense.
- `.env.example` (repo root): every secret field is `CHANGE_ME*` or blank. No real values.

`IConfiguration`/`IOptions` usage was spot-checked across `Configuration/*.cs` and
`*.Infrastructure/**/Options.cs` — every secret-bearing option is bound via `IOptions<T>` with
`.ValidateOnStart()`, not read ad hoc via `Environment.GetEnvironmentVariable` scattered through
business logic (the one direct `Environment.GetEnvironmentVariable("DATABASE_URL")` call, in
`ProductionStartupValidator.cs`, is a documented Render-specific fallback, not a leak).

## Production Configuration — runtime behavior, not just file presence

Verified in code (not assumed from file existence):

- `StagingEnvironmentGuard.Validate` and `ProductionStartupValidator.StartAsync` both gate on
  `IHostEnvironment.IsStaging()` / `IsProduction()` — i.e., actual runtime environment name, read from
  `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT`, not a config flag that could be forgotten.
- `ProductionStartupValidator` is a real `IHostedService` — it runs at process start and **throws**
  (crashing the process, not logging-and-continuing) if: the Production DB connection is missing,
  `Trust Server Certificate=true` is present in the Production connection string, SMS/Email settings
  fail their Production-only validation, or PostGIS is not actually reachable and healthy.
- `Email:Provider` is validated to reject `Console` in Production (`AuthInfrastructureRegistration.cs:249`)
  and to reject the shipped placeholder `@propertyapi.local` `From` address (`EmailOptions.ValidateForEnvironment`).
- `Jwt:Key` is validated to be ≥32 characters via `IOptions<JwtOptions>.ValidateOnStart()`
  (`AuthInfrastructureRegistration.cs:58-81`) — a short/default key crashes the app at boot, in every
  environment, not just Production.

This satisfies the task's requirement to verify *runtime behavior*, not just file presence.

## Database Readiness — PostgreSQL / PostGIS

- No `localhost`/`127.0.0.1`/hardcoded `Host=` value found anywhere in non-test, non-example C# code
  or config (`grep` swept the full tree excluding `bin/obj/.vs/Migrations`, zero matches).
- Production DB is a dedicated instance per `docs/architecture/ARD/ARD-PropertyApi.md` (distinct from
  `propertyapi-staging-db`); this was not independently re-verified against Render this session (no
  Render credentials available) — cited as documented, not re-confirmed live.
- **SSL/TLS is enforced, not merely configured**: `ProductionStartupValidator` explicitly throws if the
  Production connection string contains `Trust Server Certificate=true`, forcing a real CA-validated
  TLS connection.
- PostGIS availability is verified at boot (`ValidatePostGisAsync`) and continuously via the
  `/health/ready` `postgresql-postgis` check (tag `ready`, `db`).
- Connection pooling / command timeout / retry strategy: standard Npgsql/EF Core defaults are used;
  no custom retry-on-failure policy was found configured on the `DbContext` — **not flagged as a
  blocker** (Npgsql's default pooling is production-viable), but worth a follow-up if connection churn
  is ever observed under load.

## Migration Strategy — who applies migrations in Production?

**Nobody, automatically — by design, and this is the direct cause of Finding B2.**

- `Database__ApplyMigrationsOnStartup` is read nowhere in the codebase (`grep` confirms) — it is a
  vestigial key some earlier config carried, and setting it today would do nothing.
- The only mechanism that applies EF Core migrations to any environment is running
  `tools/PropertyApi.Migrator` manually, pointed at that environment's connection string
  (`ci/Dockerfile.migrator` packages this as a container image for CI use).
- This eliminates the "race between API instances / migrator / startup logic" risk the task asked
  about — there is exactly one applier, and it never runs concurrently with API instance startup.
- **The gap is not "unsafe," it's "incomplete automation":** the CI pipeline deploys new code to
  Staging via a Render Deploy Hook but never invokes the Migrator against Staging afterward, so any
  commit that ships a new migration (this repository ships one almost every day — see
  `PropertyApi.Infrastructure/Migrations/`) makes the mandatory `staging-smoke` gate time out. See
  §Remaining Manual Actions for the concrete fix.
- Recommended target strategy (already partially true, needs the missing link added):
  ```
  Deploy (Render hook fires)
       ↓
  Migrator run against the just-deployed environment's DB   ← MISSING from CI today
       ↓
  Verify migration (build-info.migration.complete == true)  ← already implemented, just starved
       ↓
  Staging E2E smoke → Production gate
  ```

## Redis Production Audit

- Code-level: `RedisConnectionResolver` throws at startup if Production has no Redis connection string
  configured under any of the accepted keys — no silent fallback to `localhost:6379` is possible in
  Production (verified by reading the resolver; this was previously a real bug, fixed and now
  guarded — see the resolver's own comment).
- Connection string handling normalizes `redis://`/`rediss://` URLs, extracts password from user-info,
  sets `ssl=true` for `rediss://`, and sets `abortConnect=false`/retry/timeout values suitable for a
  managed provider.
- **HA: STATUS = NOT VERIFIED / confirmed absent.** Per this repo's own `docs/REDIS-HA.md`
  (dated before this session): Production's actual Upstash account is Free tier, with no HA/failover
  add-on and no payment method configured. This is Finding H2. The CI pipeline's `redis-sentinel-ha`
  job proves the *codebase's* Sentinel-integration logic works, not that Production's actual Redis
  deployment has replicas or automatic failover — `docs/REDIS-HA.md` itself makes this exact
  distinction explicitly (§"Production Redis HA decision").
- SignalR backplane, distributed rate limiting, and the security-stamp cache all share this one Redis
  connection (Finding M3) — not fixed here (architecture change, out of Phase 1 scope), but the
  concrete Production consequence of H2 is: an Upstash outage degrades all three simultaneously.

## Object Storage / Image Storage

- `IMediaStorageService` → `CloudinaryMediaStorageService` is the only image storage path found; no
  `wwwroot/uploads` or local-disk persistence for user-facing media was found referenced outside of
  `wwwroot` static-file serving for the app's own static assets (`app.UseStaticFiles()` in
  `Program.cs`, unrelated to uploads).
- Upload validation checks real binary magic bytes (JPEG/PNG/WebP), not just declared `Content-Type`
  or file extension (`docs/architecture/ARD/ARD-PropertyApi.md` §4.5, verified by the presence of the
  described check — file-level line audit not repeated here to control scope).
- Production credentials source: Render environment variables (`Cloudinary:CloudName/ApiKey/ApiSecret`),
  never committed (confirmed empty in every tracked `appsettings*.json`).
- Persistence guarantee: real, since storage is external to the container — image data survives
  container restart/redeploy by construction.
- Staging/Production folder-namespace sharing (Finding L1) is the one caveat: it's a data-hygiene risk,
  not a persistence or security one.

## JWT / Authentication Security

| Control | Status | Evidence |
|---|---|---|
| Key length | Enforced ≥32 chars, fails fast at startup | `AuthInfrastructureRegistration.cs:58-68`, `.ValidateOnStart()` |
| Key source | Environment variable / User Secrets only, never hardcoded | `JwtAuthenticationRegistration.cs:20-22` throws if missing |
| Issuer/Audience validation | Enforced | `TokenValidationParameters` in `JwtAuthenticationRegistration.cs` |
| Token lifetime | Access 30 min, Refresh 30 days (configurable) | `appsettings.json` defaults, `JwtOptions` validated >0 |
| Refresh token storage | HttpOnly cookie, not JSON response body | ARD §4.5, `RefreshTokenCookie.cs` |
| Refresh token reuse detection | Revokes **all** active tokens + rotates SecurityStamp on detected reuse | ARD §4.5 (`RefreshTokenReuseHandler`, present in the security-stamp resilience zip's own file list, confirming it exists in-tree) |
| Security stamp revalidation | Per-request via `OnTokenValidated`, Redis-cached with DB fallback, fails closed on unexpected exception | `JwtAuthenticationRegistration.cs:49-93` |
| Default authorization | **Fallback-deny**: any endpoint without `[Authorize]`/`[AllowAnonymous]` returns 401 | `JwtAuthenticationRegistration.cs:141-143`, enforced by `PublicEndpointPolicyTests` |
| HTTPS metadata requirement | Required outside Development (`RequireHttpsMetadata = !IsDevelopment()`) | `JwtAuthenticationRegistration.cs:32` |

No BLOCKER or HIGH finding here — this is the strongest area of the audit. No LOW findings raised.

## CORS

- `Cors:AllowedOrigins` is empty in every committed config file (base, Development, Testing) — actual
  origins are environment-variable driven, so nothing to leak and nothing hardcoded to `*`.
- Code path: outside Development/CI, an empty `Cors:AllowedOrigins` **throws at startup**
  (`CorsRegistration.cs:44`) — Production cannot silently boot with no CORS policy or a wildcard.
- `AllowAnyOrigin()` is reachable only in Development or Testing/CI (`isTestingOrCi` flag), never in
  Staging or Production.
- Credentialed CORS (`AllowCredentials()`) is required because the refresh-token cookie must reach the
  browser — correctly paired with an explicit origin allowlist, never with `AllowAnyOrigin()` (the
  code comment at `CorsRegistration.cs:19-25` documents this was a real historical bug, now fixed).
- Mobile (Capacitor) requests: not same-origin-restricted by CORS at all (CORS is a browser-enforced
  mechanism; native app HTTP calls aren't subject to it) — this is standard and correct, not a gap.
- Production's actual origin value was not re-verified live this session (no Render dashboard access);
  documented in the ARD as the production frontend's real domain, consistent with the CORS code path
  described above.

## HTTPS / Forwarded Headers

- `ForwardedHeadersRegistration.cs` requires an explicit `KnownProxies`/`KnownNetworks` allowlist in
  Staging or Production — throws at startup otherwise (`requiresExplicitTrustBoundary`, lines 49-60).
- `RequireHeaderSymmetry = false` is a deliberate, documented fix (code comment, lines 70-79) for a
  real, previously-shipped bug: Render's multi-hop edge doesn't symmetrically append `X-Forwarded-For`
  and `X-Forwarded-Proto`, and strict symmetry silently made `Request.IsHttps` false for every live
  Production request, which made `UseHsts()` a no-op. This is exactly the kind of runtime-behavior bug
  the task asked to catch — it was already caught and fixed, not by this audit but earlier in-repo.
- `app.UseHsts()` in Production only (`Program.cs:172-179`); other environments use
  `UseHttpsRedirection()`. HSTS is configured for 365 days, `IncludeSubDomains`, `Preload`.
- Finding M1 (possible `0.0.0.0/0` in Production's `KnownNetworks`) is the one open item here — see
  the findings table for why it isn't rated higher (Render publishes no fixed, documentable proxy
  range, so the alternative to a broad allowlist is inventing a false sense of restriction).

## Production Security (Swagger / errors / logging)

- Swagger is enabled only in Development, Testing/CI, or when `Swagger:Enabled=true` **and** not
  Production (`Program.cs:184-186`) — Production cannot enable Swagger even by explicit config error,
  short of also flipping `ASPNETCORE_ENVIRONMENT` itself.
- `ExceptionHandlingMiddleware` runs before routing/auth, converting exceptions to a uniform response —
  not independently line-audited for stack-trace leakage in this session (out of the time budget for
  this pass); flagged as a follow-up spot-check, not a finding (no evidence of a problem found, but no
  positive proof gathered either).
- `Console` email provider (which only logs message bodies, including password-reset links) is
  explicitly rejected in Production (see §Environment Configuration Audit).
- No `Environment.GetEnvironmentVariable`/connection-string/JWT value found in any `_logger.Log*` call
  during this session's reading of `Configuration/*.cs`, `Health/*.cs`, and the JWT/Redis/CORS
  registration files — logging statements reviewed log booleans, endpoint lists, and connection
  *health*, not connection *values*.

## Health Checks

- `/health/live` — always returns healthy if the process is running (`Predicate = _ => false`, i.e. no
  dependency checks) — correct liveness semantics (process up, not "everything downstream is fine").
- `/health/ready` — gated on tag `"ready"`, which currently covers `postgresql-postgis` and `redis` —
  correctly distinguishes readiness (dependencies reachable) from liveness.
- Response body is minimal by design (`HealthCheckResponseWriter`): status, duration, per-check name —
  no connection strings, no exception details, no internal paths. Good.
- Both endpoints are `.AllowAnonymous()` — appropriate for a load balancer / uptime check, and
  explicitly exempted from the fallback-deny authorization policy on purpose (see
  `JwtAuthenticationRegistration.cs` comment).

## Observability

- OpenTelemetry (OTLP) traces + metrics, Prometheus + Grafana + Tempo — provider-neutral by design
  (`Observability:Otlp:Endpoint` is the only coupling point).
- `Observability:Tracing:SamplingRatio` defaults to `1.0` in the base `appsettings.json` but the
  shipped `.env.example` sets `Observability__Tracing__SamplingRatio=0.10` for Production — i.e., the
  *repository's own guidance* is to sample at 10% in Production, not the 100% base default. This is
  not itself wrong (100% base default is a safe fail-open default for any environment that forgets to
  set it), but confirm the live Render value matches the intended 0.10, not the unset 1.0 default —
  not independently re-verified this session.
- The `Mandatory Observability Export Gate` CI job **passed** on the live run observed this session
  (see §CI/CD Readiness) — real evidence the OTLP pipeline, dashboards, and alert lifecycle work
  end-to-end against a live-equivalent stack, not just "the code compiles."

## Docker Production Audit

`PropertyApi/Dockerfile`:

- Multi-stage build (`sdk:8.0` build stage → `aspnet:8.0-jammy-chiseled-extra` runtime stage) —
  build tools are not present in the shipped image.
- **Chiseled runtime image** — no shell, no package manager in the runtime image (a deliberate,
  documented security decision per the ARD, §5.3).
- **Non-root**: `USER $APP_UID` before `ENTRYPOINT` — confirmed in the Dockerfile itself.
- No secrets baked in: `grep`-checked the Dockerfile for `ENV JWT_KEY`/`ENV PASSWORD`/`COPY .env` —
  none present; only `ASPNETCORE_HTTP_PORTS` and `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` are set.
- `dotnet restore --locked-mode` against a committed `packages.lock.json` — deterministic restore.
- Single port exposed (`8080`); no unnecessary ports.
- No explicit `HEALTHCHECK` instruction in the Dockerfile itself — Render's own health-check
  configuration (pointed at `/health/ready` per the app's own endpoints) substitutes for this in
  practice, but a native Docker `HEALTHCHECK` would make the image self-describing outside Render too.
  **Not rated as a finding** (functionally covered), noted for completeness only.

`ci/docker-compose.production-gate.yml` — this is CI-only tooling, not Production infrastructure. Its
`propertyapi_gate` database name (which superficially matches one of the task's forbidden-pattern
examples) was checked and confirmed to be parameterized (`${PROPERTYAPI_GATE_DB:-propertyapi_gate}`),
used only for the ephemeral CI Postgres container, and never referenced from any Production or Staging
configuration path. **No action required** — flagging this explicitly because the task's own
instructions named this exact string as a red flag to check.

## CI/CD Readiness

Live evidence gathered this session (not just reading the YAML):

- `production-gate.yml` correctly separates test/staging/production: `staging-smoke` and
  `redis-staging-failover` target the real Staging deployment (`environment: staging`);
  `deploy-production`, `production-deployment-gate` target `environment: production` /
  `production-release`; `database-recovery` targets `environment: production-recovery` and runs
  against the actual `PRODUCTION_DATABASE_URL`/`DATABASE_URL` secret, never a test DB.
- `deploy-production` is `workflow_dispatch`-only (not also triggered by a plain push) as of PR #124 —
  confirmed by reading the current file and by the run history (`gh run list`).
- No secret value is ever echoed (`echo $SECRET` or similar) anywhere in `.github/workflows/*.yml` —
  confirmed by grep.
- Gitleaks runs on every CI build, pinned to a commit SHA (`ci.yml:572-584`).
- **Live run observed this session**: `https://github.com/Naeem-Ba/PropertyApi/actions/runs/33911314587`
  (triggered by the `master` merge of PR #126, 2026-09-04T19:28 UTC):

  | Job | Result |
  |---|---|
  | Build, Test and Container Gate | ✅ success |
  | Redis Sentinel HA Failover Drill | ✅ success |
  | Validate Redis HA Topology Evidence | ✅ success |
  | Mandatory Observability Export Gate | ✅ success |
  | **Production Database Recovery Gate** | ❌ **failure** (Finding B3) |
  | **Mandatory Staging Deploy + E2E Smoke** | ❌ **failure** (Finding B2) |
  | Redis Staging Managed Failover | ⏭ skipped (needs staging-smoke) |
  | Production Deployment Gate | ❌ failure (cascades from B3) |
  | Mandatory Concurrent Performance Gate | ⏳ still queued at time of writing — see note below |
  | Production Deployment (manual trigger required) | not evaluated (workflow_dispatch only; this run was a push) |

  A prior completed run on the same `master` branch (`33878535465`, PR #124 merge, 2026-09-04T13:31
  UTC) showed four jobs failing with zero logged steps and multi-hour-old logs since purged
  (`Mandatory Observability Export Gate`, `Validate Redis HA Topology Evidence`, `Production Database
  Recovery Gate`, `Mandatory Concurrent Performance Gate`) — all four of which then **passed** on the
  live run observed this session, strongly suggesting that earlier failure was CI-infrastructure
  flakiness (e.g. runner provisioning), not a code regression. This report does not treat that earlier
  run as evidence of anything beyond "verify a red run before trusting it" — exactly why this session
  re-ran and re-checked live rather than citing the older result.

  **Note:** the Performance Gate job was still queued when this report was written and could not be
  waited out within this session. It does not change the BLOCKED verdict — B2 and B3 alone are
  sufficient, since `deploy-production` requires both.

## Secrets Inventory

Names only — no values are reproduced anywhere in this report or were viewed in this session (GitHub
Secrets are write-only via the API used).

| Name | Purpose | Required in Production? | Storage location (live-confirmed) | Status |
|---|---|---|---|---|
| `Jwt__Key` | JWT signing key | Yes | Render env var (Production service) | Not independently re-verified live (no Render access); code guarantees ≥32 chars or the app refuses to boot |
| `ConnectionStrings__DefaultConnection` | Production Postgres | Yes | Render env var | Same as above |
| `Redis__ConnectionString` / `ConnectionStrings__Redis` | Production Redis (Upstash) | Yes | Render env var | Same as above; app refuses to boot without it |
| `Cloudinary__CloudName/ApiKey/ApiSecret` | Image storage | Yes | Render env var | Not re-verified live |
| `Email__Resend__ApiKey` | Transactional email | Yes (if `Email:Provider=Resend`) | Render env var | Not re-verified live |
| `Cors__AllowedOrigins__0` | Production frontend origin | Yes | Render env var | Not re-verified live |
| `PRODUCTION_DEPLOY_HOOK_URL` | Render deploy hook, fired by `deploy-production` | Yes | **Not found** — checked `production` GitHub Environment (0 secrets) and repo level (7 secrets, this not among them) | **MISSING — BLOCKER B1** |
| `RENDER_API_KEY` | Query Render deploy history | Yes (`production-deployment-gate`) | `production-release` GitHub Environment | **PRESENT** (confirmed live, created 2026-08-17) |
| `RENDER_SERVICE_ID` | Target Render service for the above | Yes | `production-release` GitHub Environment | **PRESENT** (confirmed live) |
| `DATABASE_URL` | Production DB restore-drill source | Yes (`database-recovery`) | Repo-level Actions secret | **PRESENT** (confirmed live) |
| `BACKUP_ENCRYPTION_KEY`, `BACKUP_STORAGE_URI`, `BACKUP_AWS_ACCESS_KEY_ID`, `BACKUP_AWS_SECRET_ACCESS_KEY`, `BACKUP_AWS_REGION`, `BACKUP_S3_ENDPOINT_URL` | Backup/restore drill | Yes | Repo-level Actions secrets | **PRESENT** (confirmed live) |
| `BACKUP_S3_KMS_KEY_ID` | Optional, only if SSE=`aws:kms` | Conditional | Not found at repo or environment level | Not present — acceptable if SSE mode is `AES256` (the documented default) |
| `STAGING_DEPLOY_HOOK_URL`, `STAGING_SMOKE_PASSWORD`, `STAGING_SMOKE_FIXED_OTP`, `STAGING_SMOKE_CLEANUP_SECRET` | Staging deploy + E2E smoke | No (Staging-only) | `staging` GitHub Environment | **PRESENT** (confirmed live) |
| `STAGING_BASE_URL`, `PRODUCTION_BASE_URL`, `STAGING_SMOKE_PHONE_PREFIX`, `STAGING_PROMETHEUS_URL`, `STAGING_TEMPO_URL` | Staging gate variables | No | `staging` GitHub Environment variables | **PRESENT** (confirmed live) |
| `REDIS_HA_EVIDENCE_JSON`, `REDIS_PROVIDER_FAILOVER_COMMAND` | Managed-provider Redis HA proof | No longer required (see M2) | Not configured | Absent by design, non-blocking per current pipeline code |

---

## Environment Matrix

| Setting | Development | Test | Staging | Production | Status |
|---|---|---|---|---|---|
| PostgreSQL | `localhost`, `propertyapi_local` (example) | `localhost`, `propertyapi_testing` | Dedicated Render Postgres 18, Free, no backups, ~30-day TTL | Dedicated instance, backup/restore drilled by CI (currently failing, B3) | Separated ✅ / Prod backup gate ❌ |
| Redis | none (in-memory cache fallback) | disabled (`RateLimiting:Redis:Enabled=false`) | `propertyapi-redis` (Valkey 8 on Render) | Upstash, external, **no HA** (H2) | Separated ✅ / HA ❌ NOT VERIFIED |
| JWT | User Secrets | `TEST_ONLY_SECRET_KEY_...` (fixture) | Env var, ≥32 chars enforced | Env var, ≥32 chars enforced | Enforced ✅ |
| CORS | `localhost:4200` | `localhost:4200` | Staging frontend origin | Production frontend origin (not re-verified live) | Enforced (no `*`, no silent fallback) ✅ |
| Cloudinary | empty (uploads likely fail) | `test`/`test`/`test` fixture | Dedicated API key, shared folders with Prod (L1) | Dedicated credentials | Credential-separated ✅ / Storage-separated ❌ (L1) |
| SMTP/Email | unset | disabled | Resend, dedicated key | Resend, real verified domain | Console rejected in Prod ✅ |
| Swagger | Enabled | Enabled (CI) | Disabled by default | **Disabled**, cannot be forced on | ✅ |
| Logging | Debug-friendly | minimal | production-shaped | Structured, OTLP, sampled | ✅ |
| HTTPS | Redirect only | Redirect only | Redirect only | HSTS (365d, preload) | ✅ |
| Migrations | Manual (`Migrator`) | Applied per-test-run in CI | Manual — **not wired into the deploy pipeline (B2)** | Manual (`Migrator`), runbook-documented | Process gap ❌ (B2) |
| Health checks | `/health/live`, `/health/ready` | Same | Same | Same | ✅ |
| GitHub Environment protection | n/a | n/a | No protection rules (acceptable for Staging) | **No protection rules** (H1) | ❌ (H1) |

---

## Production Dependency Graph

```
Mobile App / Web App
        │ HTTPS
        ▼
propertyapi-api (Render, Docker, chiseled, non-root)
        │
        ├── PostgreSQL + PostGIS ── TLS required (Trust Server Certificate=true rejected at boot)
        │                            Health: /health/ready "postgresql-postgis"
        │                            Failure behavior: process refuses to become Ready; boot-time
        │                            PostGIS check crashes the process if unreachable at startup.
        │
        ├── Redis (Upstash) ─────── Connection required at boot (Production) or process crashes.
        │                            Health: /health/ready "redis"
        │                            Failure behavior at runtime: not independently proven this
        │                            session — code shows a DB-fallback path for security-stamp
        │                            checks with a fail-closed final catch; rate limiting / SignalR
        │                            backplane behavior under a live Redis outage is NOT VERIFIED.
        │                            HA: NOT VERIFIED / confirmed absent (H2).
        │
        ├── Cloudinary ──────────── Credentials from env var; upload failures are not silently
        │                            swallowed for the primary flow (partial-failure cleanup exists
        │                            per ARD).
        │
        ├── Resend / SMTP ───────── Console provider rejected in Production; placeholder From
        │                            domain rejected in Production. Failure mode: best-effort per
        │                            ARD design principle (does not fail the triggering request).
        │
        ├── SMS HTTP provider ───── HTTPS + ApiKey + FromNumber required in Production (validated
        │                            at boot).
        │
        └── OTLP collector ──────── Traces/metrics; Observability Export Gate passed live this
                                     session against an equivalent stack.
```

---

## Production Smoke Tests

No destructive or write smoke tests were run directly against live Production in this session (per
task rule §23 and the general safety rules governing this assistant — no unauthorized action against
a real production system with real users). Instead, this audit relied on:

1. **`GET /health/live`, `GET /health/ready` semantics** — verified by reading the actual endpoint
   code (`HealthEndpointExtensions.cs`), not by calling a live Production URL this session (no
   Production URL/credentials were supplied for direct testing, and probing an unannounced Production
   host without the user's explicit go-ahead was avoided as an unnecessary external action).
2. **The mandatory `staging-smoke` CI job**, which *is* the project's own sanctioned mechanism for
   exercising registration, OTP, login, refresh rotation, logout, property CRUD, image upload,
   messaging, and negative-auth cases against a real deployed instance — observed live this session,
   currently **failing** (B2), before it ever reaches the point of running those journeys (it times out
   waiting for migrations, before `scripts/smoke-staging.sh` even executes).
3. **The mandatory `database-recovery` CI job**, the project's own sanctioned mechanism for proving
   actual backup/restore against Production's real data — observed live this session, currently
   **failing** (B3).

**Conclusion: mandatory smoke evidence does not currently exist for this commit.** This is itself the
core reason for the BLOCKED verdict, independent of any code-quality finding.

---

## Remaining Manual Actions

### B1 — Configure `PRODUCTION_DEPLOY_HOOK_URL`

- **ACTION:** Copy the Deploy Hook URL from Render's dashboard for the Production `propertyapi-api`
  service (Settings → Deploy Hook), and add it as a secret named exactly `PRODUCTION_DEPLOY_HOOK_URL`
  on the GitHub `production` Environment (Settings → Environments → `production` → Secrets).
- **WHERE:** GitHub repo `Naeem-Ba/PropertyApi` → Settings → Environments → `production`.
- **WHY:** `deploy-production`'s "Trigger production deployment" step reads
  `secrets.PRODUCTION_DEPLOY_HOOK_URL` and hard-fails (`exit 1`) if empty — confirmed absent both at
  environment and repo scope via live API query this session.
- **EXPECTED VALUE TYPE:** An HTTPS URL of the form `https://api.render.com/deploy/srv-XXXXXXXX?key=YYYYYYYY`.
- **HOW TO VERIFY:** Re-run `gh api repos/Naeem-Ba/PropertyApi/environments/production/secrets` and
  confirm `PRODUCTION_DEPLOY_HOOK_URL` is listed (name only, as expected — the value is never
  retrievable via API by design).
- **BLOCKING:** Yes — without this, `deploy-production` cannot succeed under any circumstances.

### B2 — Wire Staging migrations into the deploy pipeline

- **ACTION:** Two parts, both required:
  1. Add a secret holding the Staging Postgres connection string (e.g. `STAGING_DATABASE_URL`) to the
     `staging` GitHub Environment. This value must be pasted from Render's `propertyapi-staging-db`
     connection details — it is a live infrastructure credential and must not be invented or guessed.
  2. Add a CI step to the `staging-smoke` job, between "Trigger staging deployment" and "Wait for
     intended staging release", that runs `tools/PropertyApi.Migrator` (the same container image
     already built by `ci/Dockerfile.migrator`) against that connection string.
- **WHERE:** `.github/workflows/production-gate.yml`, job `staging-smoke`; secret on the `staging`
  GitHub Environment.
- **WHY:** Confirmed live this session — the pipeline currently deploys code to Staging but never
  applies the matching migration, so `/api/operational/build-info`'s `migration.complete` field never
  turns `true` for a commit that ships a new migration, and the gate times out after 900 seconds.
- **EXPECTED VALUE TYPE:** A Postgres connection string in the same shape as
  `ConnectionStrings__DefaultConnection` (`Host=...;Port=5432;Database=...;Username=...;Password=...`),
  pointed at `propertyapi-staging-db`.
- **HOW TO VERIFY:** Trigger `production-gate.yml` via `workflow_dispatch` and confirm the
  `staging-smoke` job's "Wait for intended staging release" step succeeds (not times out) and that
  `scripts/smoke-staging.sh`'s subsequent journeys run.
- **BLOCKING:** Yes.

### B3 — Diagnose the Database Recovery Gate's restore failure

- **ACTION:** After the currently-queued run finishes (or by triggering a fresh
  `workflow_dispatch` run of `.github/workflows/database-restore-drill.yml`), read the "Restore and
  validate the backup" step's log (unavailable to this session while the run was in progress) and fix
  the underlying cause before attempting a Production release.
- **WHERE:** `.github/workflows/database-restore-drill.yml`, job `restore-drill`, step "Restore and
  validate the backup".
- **WHY:** This is a hard prerequisite of `production-deployment-gate` → `deploy-production`; a real
  restore has not been proven to work for the current backup/schema state.
- **EXPECTED VALUE TYPE:** N/A — this is a diagnostic/code action, not a missing credential (the
  secrets this job needs — `DATABASE_URL`, `BACKUP_*` — are all confirmed present).
- **HOW TO VERIFY:** The job's "Enforce RPO and RTO and write evidence" step completes and the
  `production-release-recovery-metadata` artifact shows `"recoveryGate":"PASS"`.
- **BLOCKING:** Yes.

### H1 — Production environment protection

- **ACTION:** Either upgrade the GitHub org's billing plan to one that supports "Required reviewers"
  on a private repository, then configure it on `production`, `production-release`, and
  `production-recovery`; or, if the plan cannot be upgraded, adopt an explicit compensating control
  (e.g., restrict `workflow_dispatch` to a named, small admin group via `CODEOWNERS`-gated branch
  protection on who can push to `master`, since `workflow_dispatch` access currently tracks repo push
  access).
- **WHERE:** GitHub org billing settings; then Settings → Environments per environment.
- **WHY:** Confirmed live this session: all three environments have `protection_rules: []`. Anyone
  with push/workflow access can single-handedly trigger a Production deploy once the gates are green.
- **EXPECTED VALUE TYPE:** N/A — an org billing/process decision, not a secret.
- **HOW TO VERIFY:** `gh api repos/Naeem-Ba/PropertyApi/environments/production` shows a non-empty
  `protection_rules` array.
- **BLOCKING:** No (documented, accepted mitigation exists) — but strongly recommended before scaling
  the team beyond the current single-operator model.

### H2 — Production Redis HA

- **ACTION:** Decide, explicitly and in writing (a short ADR or an update to
  `docs/REDIS-HA.md`'s "Production Redis HA decision" section), whether to (a) upgrade Upstash to a
  paid HA tier, (b) migrate to a different managed provider with HA, or (c) formally accept the
  single-point-of-failure risk with a documented incident-response runbook (who gets paged, what the
  user-facing symptom looks like, how long a manual Upstash restart takes).
- **WHERE:** Upstash dashboard (if upgrading) + `docs/REDIS-HA.md`.
- **WHY:** Currently undocumented as a live decision — only documented as "not yet decided, Free tier
  today" as of the cited doc's last update.
- **EXPECTED VALUE TYPE:** N/A — a decision, not a secret.
- **HOW TO VERIFY:** `docs/REDIS-HA.md`'s "Production Redis HA decision" section states a concrete,
  dated decision instead of "the real production infrastructure may use...".
- **BLOCKING:** No (does not block a first release), but should be resolved before the user base grows
  enough that a Redis outage becomes a real incident rather than a hypothetical one.

---

## Tests Executed This Session

- `dotnet build PropertyApi.sln -c Release` — **0 errors**, 8 NuGet-audit warnings (sandbox has no
  outbound access to `api.nuget.org`; expected in this environment, not a code issue).
- No `dotnet test` run in full (the existing CI pipeline already runs the full suite — Architecture,
  Application, Auth, Concurrency, Integration, Observability, Performance — on every push, and this
  session's live run showed `Build, Test and Container Gate` passing, which includes that suite).
- Live GitHub API queries against `Naeem-Ba/PropertyApi` (environments, secrets/variables metadata,
  workflow runs, job steps, check-run annotations) — see inline citations throughout this report.
- One full, real `production-gate.yml` run observed end-to-end from trigger to (mostly) completion.

## Files Changed

**None.** This session made no code, configuration, or CI changes. Every issue found either (a) has no
safe in-repo fix that doesn't require a secret only the user can supply (B1, B2's secret half, H1, H2),
or (b) requires a diagnosis this session could not complete because the CI run's logs were unavailable
while in progress (B3), or (c) is a documentation reconciliation (M2) deliberately left for the user to
apply alongside the ADR decision it depends on, rather than editing docs unilaterally mid-audit.

---

## Production Gate Checklist

```
[x] Production environment explicitly configured (ASPNETCORE_ENVIRONMENT-driven, code-verified)
[x] No localhost runtime dependency (swept, zero matches outside test/example files)
[x] No development DB (appsettings.json ships empty; Testing DB is a separate, clearly-labeled fixture)
[x] No test DB reachable from Production code path
[ ] Production PostgreSQL configured — documented, not independently re-verified live this session
[x] PostGIS verified — boot-time check + continuous /health/ready check, code-confirmed
[ ] Production Redis configured — documented, not independently re-verified live this session
[ ] Redis authentication/TLS verified — code supports it (password/ssl parsing); live value not re-verified
[ ] Production Object Storage configured — documented, not independently re-verified live this session
[x] JWT production secret externalized (never committed, ≥32 chars enforced at boot)
[x] JWT issuer verified (required, validated)
[x] JWT audience verified (required, validated)
[x] Refresh token security verified (rotation, reuse detection, HttpOnly cookie)
[x] CORS restricted (no wildcard/localhost reachable outside Development/CI, code-enforced)
[x] HTTPS verified (HSTS in Production, code-enforced)
[x] Forwarded Headers verified (explicit trust boundary required; symmetry bug already fixed) — value itself (M1) not re-verified live
[x] Swagger production policy verified (cannot be enabled in Production)
[ ] Exception handling verified — not independently line-audited this session (no issue found, but no full sweep done either)
[x] Sensitive logging reviewed (spot-checked; no secret values found in log calls read)
[x] Rate limiting verified (Redis-backed in Production, per-endpoint policies documented in ARD)
[x] Health endpoint verified (/health/live, correct liveness semantics)
[x] Readiness endpoint verified (/health/ready, gated on DB + Redis)
[x] Docker production image verified (multi-stage, chiseled, non-root, no baked secrets)
[x] Container security reviewed (non-root user, minimal base image, single port)
[x] CI/CD production separation verified (distinct GitHub Environments per stage, live-confirmed)
[x] Secrets not committed (swept; gitleaks runs on every CI build)
[x] Secrets not baked into Docker image (verified by reading Dockerfile)
[ ] Migration strategy verified — strategy is sound in principle, but NOT currently working end-to-end (B2)
[ ] Production smoke tests passed — mandatory Staging/Recovery gates are currently FAILING (B2, B3)
[ ] No production blockers — THREE BLOCKERS OPEN (B1, B2, B3)
```

---

## Final Status

```
PHASE 1 — PRODUCTION BACKEND
FINAL STATUS: BLOCKED
```

Re-run this gate after B1, B2, and B3 are resolved. H1 and H2 should be explicitly decided (not
necessarily resolved) before the first real Production release; none of the MEDIUM/LOW findings block
a release on their own.
