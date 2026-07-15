# Phase B Closeout Gate

Run this gate only after the Auth, composition, CI, performance, and observability slices are merged into the candidate branch.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase-b-closeout.ps1 `
  -StagingBaseUrl "https://staging.example.com" `
  -PerformanceBaselinePath "artifacts/performance/baseline-before.json" `
  -PerformanceCurrentPath "artifacts/performance/baseline-after.json"
```

The script produces:

- `artifacts/phase-b-closeout/phase-b-closeout-*.json`
- `artifacts/phase-b-closeout/phase-b-closeout-*.md`

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
