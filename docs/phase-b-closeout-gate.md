# Phase B Closeout Gate

Run this gate only after the Auth, composition, CI, performance, and observability slices are merged into the candidate branch.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase-b-closeout.ps1 `
  -ReleaseCandidate `
  -StagingBaseUrl "https://staging.example.com" `
  -PerformanceBaselinePath "artifacts/performance/baseline-before.json" `
  -PerformanceCurrentPath "artifacts/performance/baseline-after.json"
```

For a local confidence run without staging, PostgreSQL, or performance artifacts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase-b-closeout.ps1 `
  -SkipIntegrationTests
```

Local runs with skipped release-only gates return `LOCAL-PASS`, not `GO`.

To prepare a local PostgreSQL test database when PostgreSQL is installed locally:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/setup-local-test-postgres.ps1 `
  -AdminPassword "<local-postgres-password>"
```

Then set the printed `TEST_POSTGRES_CONNECTION_STRING` value in the same PowerShell session before running the closeout gate without `-SkipIntegrationTests`.

The script produces:

- `artifacts/phase-b-closeout/phase-b-closeout-*.json`
- `artifacts/phase-b-closeout/phase-b-closeout-*.md`

By default, performance comparison fails on p95 regression greater than 20 percent or throughput drop greater than 20 percent. Local localhost runs may pass wider tolerances explicitly, but release-candidate runs should keep the default unless the team records a reason.

## GO Requirements

- restore, format, build, Application, Auth, Architecture, and Integration tests pass.
- vulnerability gate has no unapproved regression beyond the committed baseline.
- staging smoke checks pass against `/health/live`, `/health/ready`, and `/api/enums/PropertyStatus`.
- performance comparison has no p95 regression greater than 20 percent and no throughput drop greater than 20 percent.
- telemetry and security review artifacts exist.

## NO-GO Conditions

- any required automated gate fails.
- integration tests cannot reach the expected PostgreSQL test database.
- staging smoke is skipped for a release candidate.
- performance results are missing or were captured from different environments.
- logs, traces, or metrics contain tokens, OTPs, phone numbers, emails, user ids, IP addresses, or other PII.

## Safety Rules

- Do not run smoke or load tests against production.
- Keep performance comparisons within the same environment and dataset.
- Treat skipped staging smoke or skipped performance comparison as acceptable only for local development, never for release closure.
