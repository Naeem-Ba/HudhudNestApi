# Mandatory Staging E2E smoke runbook

## Purpose and architecture

The release path is build/test/container gate → Staging deploy hook → deployed commit/migration
verification → full HTTP E2E journey → protected Production environment approval → Production deploy
hook. Any failed, cancelled, neutral, or skipped mandatory Staging result blocks Production.

The runner is `tests/HudhudNestApi.StagingSmokeTests`; `scripts/smoke-staging.sh` validates configuration,
runs it, checks its machine-readable result, and publishes the Markdown summary.

## Required GitHub `staging` environment configuration

Variables:

- `STAGING_BASE_URL`
- `PRODUCTION_BASE_URL`
- `STAGING_SMOKE_PHONE_PREFIX`
- `STAGING_READY_TIMEOUT_SECONDS` (optional)
- `SMOKE_HTTP_TIMEOUT_SECONDS` (optional)
- `SMOKE_MAX_RETRIES` (optional)
- `SMOKE_RETRY_DELAY_SECONDS` (optional)

Secrets:

- `STAGING_DEPLOY_HOOK_URL`
- `STAGING_SMOKE_PASSWORD`
- `STAGING_SMOKE_FIXED_OTP`
- `STAGING_SMOKE_CLEANUP_SECRET`
- `STAGING_DATABASE_URL` — external connection string of the Render Staging Postgres
  (`postgresql://user:pass@host/propertyapi_staging`; `sslmode=require` is appended if absent).
  The gate applies pending EF migrations to it with `tools/HudhudNestApi.Migrator` **before** the deploy
  hook, because the API does not migrate on startup and Render Free has no pre-deploy command. The step
  refuses any database whose name does not contain `staging`.

Evidence: every run uploads `staging-release-verification.json` (expected vs deployed commit SHA, branch,
migration state, readiness, timestamps) and, even when earlier steps fail, failure-status
`staging-smoke-*` and observability reports. `commitShaMatches=true` with pending migrations means the
Staging DB was not migrated; `false` means a stale deploy or the service tracking the wrong branch.

The protected `production` environment requires `PRODUCTION_DEPLOY_HOOK_URL` and should require manual
reviewers.

## Required Staging service settings

Use placeholders/secrets from the deployment platform; never commit values:

```text
ASPNETCORE_ENVIRONMENT=Staging
Deployment__CommitSha=<deployed git sha>
Deployment__Version=<release version>
Deployment__PublicBaseUrl=https://<staging-host>
Deployment__ProductionBaseUrl=https://<production-host>
Staging__EnvironmentId=<staging identifier>
Staging__DatabaseNameMarker=staging
Staging__RedisIsolationMarker=<dedicated redis identifier>
Staging__StorageIsolationMarker=<dedicated storage identifier>
Staging__ExternalNotificationsDisabled=true
Staging__TestSupport__Enabled=true
Staging__TestSupport__FixedOtp=<secret>
Staging__TestSupport__PhonePrefix=<reserved prefix>
Staging__TestSupport__CleanupSecret=<strong secret>
Staging__TestSupport__UseInMemoryMedia=false
```

The database name must contain the configured marker. Redis and storage must be separate resources or
explicitly isolated namespaces. Staging must not contain Production SMS, email, webhook, database,
Redis, or storage credentials.

## Local configuration validation

```bash
export STAGING_BASE_URL="https://staging.example.invalid"
export PRODUCTION_BASE_URL="https://api.example.invalid"
export EXPECTED_COMMIT_SHA="<40-char-sha>"
export STAGING_SMOKE_PHONE_PREFIX="+155500"
export STAGING_SMOKE_PASSWORD="<secret>"
export STAGING_SMOKE_FIXED_OTP="<six-digits>"
export STAGING_SMOKE_CLEANUP_SECRET="<at-least-32-characters>"
bash scripts/smoke-staging.sh --validate-only
```

## Manual real-Staging execution

Set the same values, then run:

```bash
bash scripts/smoke-staging.sh
```

The complete flow performs health/version/migration checks, two registrations, wrong/correct/reused
OTP checks, login, protected-resource checks, refresh rotation, property creation, immediate Draft
management read, image persistence, publish/public read, listing, visitor messaging, private-read
authorization, logout, revoked-refresh verification, and scoped cleanup.

## Expected status highlights

- Registration OTP request: 200 with a challenge for an unused test phone.
- Wrong/reused OTP: 400.
- Phone registration/login: 200 with usable access and refresh tokens.
- Property create: 201 with id and Location.
- Immediate owner management read: 200.
- Public Draft read: 404 by design; published read: 200.
- Image upload: 200; unsupported type: 400; non-owner: 403.
- Message create: 201; unauthorized inbox read: 401/403.
- Logout: 204; refresh after logout: 401.
- Cleanup: 200 with exact removed counts.

## Retries and rate limits

Retries apply only to health/readiness and transient 502/503/504 or connection failures. Contract,
validation, authentication, authorization, and read-after-write failures are never retried. Defaults:
20-second request timeout, five attempts, three-second delay, and 900-second deployment readiness.
The suite creates two users and stays below the three-OTP-per-hour application limit.

## Reports

```text
artifacts/staging-smoke/staging-smoke-report.json
artifacts/staging-smoke/staging-smoke-junit.xml
artifacts/staging-smoke/staging-smoke-summary.md
```

If any report is missing, any journey is skipped, cleanup fails, the commit differs, or migrations are
pending, the script exits non-zero.

## Troubleshooting

- Build info 404: deploy the new API version before running the gate.
- Commit mismatch: confirm `Deployment__CommitSha` is populated from the deployed release, not branch
  name or an old environment variable.
- Startup isolation failure: check dedicated database naming, distinct HTTPS hosts, Redis, storage,
  and notification-suppression settings.
- OTP challenge missing: confirm the phone prefix exactly matches the Staging allowlist and the number
  was not left by a failed prior run.
- Upload failure: verify Staging uses dedicated valid media credentials and not Production credentials.
- Cleanup failure: retain the sanitized report, rotate the cleanup secret if exposed, and remove only
  the exact reported run marker.

No failure may be bypassed with `continue-on-error`, optional conditions, or manual Production deploy.
