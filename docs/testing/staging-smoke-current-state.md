# Staging smoke current state

## Previous behavior

`production-gate.yml` contained a `staging-smoke` job, but its configuration step wrote
`configured=false` and exited successfully when `STAGING_BASE_URL` was absent. Every deployment,
readiness, and smoke step then used an `if` expression based on that output. The mandatory gate
therefore appeared successful while all Staging work was skipped.

The previous `scripts/smoke-staging.sh` called only:

- `GET /health/live`
- `GET /health/ready`
- `GET /api/enums/PropertyStatus`

It did not authenticate, create data, verify persistence, upload media, exercise refresh-token
rotation, communicate between users, or clean up. A successful create followed by an unreadable
resource was invisible to the gate.

## Risks identified

- Missing Staging configuration authorized a false-positive release.
- An optional deploy hook allowed testing an old release.
- No deployed-commit verification existed.
- Migration/PostGIS state was not verified against the deployed service.
- Status-only checks did not validate JSON contracts or field values.
- No unique test-data scope, cleanup, or evidence report existed.
- OTP credentials existed in workflow inputs but the script never used them.
- The application had no sanitized build-information endpoint.

## API contracts derived from the repository

- Phone registration: `POST /api/auth/phone/registration/send-otp`, then
  `POST /api/auth/phone/registration/verify`.
- Phone login: `POST /api/auth/phone/login`.
- Refresh/logout: `POST /api/auth/refresh` and `POST /api/auth/logout`.
- Protected identity check: `GET /api/users/me`.
- Property create: `POST /api/properties` returns `201`, an id, and `Location`.
- A new property is intentionally a private Draft. Immediate read-after-write therefore uses
  `GET /api/properties/{id}/manage`; public `GET /api/properties/{id}` must remain `404` until publish.
- Images: multipart field `files` at `POST /api/properties/{id}/images`.
- Publish: `POST` or `PATCH /api/properties/{id}/publish`.
- Communication: authenticated `POST /api/messages`; the visitor omits `ReceiverId` so the owner is
  resolved from the property.

## Corrections implemented

- Missing/empty/unsafe Staging configuration now fails closed.
- Staging deployment hook is mandatory.
- The workflow polls sanitized build metadata until the expected Git SHA is active.
- Build metadata verifies environment, migrations, and PostGIS.
- Dedicated Staging isolation markers and external-notification suppression are startup requirements.
- A Staging-only fixed OTP and silent SMS sink are limited to an allowlisted phone prefix.
- Staging test support is impossible in Production by policy and automated tests.
- A complete .NET E2E runner validates contracts, negative security cases, cleanup, and reports.
- The same suite runs against an isolated Docker deployment in the build gate.
- Production deployment depends explicitly on successful Staging deployment and E2E smoke.

## Known external limitation

This repository cannot prove that a real provider-hosted Staging service, database, Redis, and media
account exist without their GitHub Environment configuration. Until the real workflow run passes,
production readiness remains failed even when the local container-equivalent suite passes.
