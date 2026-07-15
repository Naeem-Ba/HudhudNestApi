# Phase B CI Quality Gates

## Purpose

This slice makes the CI result harder to misread. The pipeline now fails for
format drift, vulnerable NuGet packages, coverage regression, missing production
gate scripts, and missing report artifacts.

## Gates

| Gate | Enforcement |
| --- | --- |
| Format | `dotnet format PropertyApi.sln --verify-no-changes --no-restore` |
| Vulnerabilities | `ci/check-vulnerable-packages.ps1` parses `dotnet list package --vulnerable --include-transitive` and fails on advisories not present in `ci/vulnerability-baseline.json` |
| Coverage baseline | `ci/check-coverage-baseline.ps1` aggregates Cobertura reports and compares against `ci/coverage-baseline.json` |
| Coverage report | `dotnet-reportgenerator-globaltool` writes HTML, Cobertura, and GitHub markdown summaries |
| Artifacts | CI uploads test results, coverage reports, vulnerability reports, and production gate reports |
| Production static gate | `scripts/verify-production-gate.ps1` verifies key production hardening assumptions |
| Staging smoke | `scripts/smoke-staging.sh` checks liveness, readiness, and a public API endpoint |

## Baseline

### Vulnerabilities

`ci/vulnerability-baseline.json` records the currently known GHSA advisories.
The gate passes for those known advisories and fails when a new advisory appears.
Remove entries from the baseline as dependencies are upgraded.

### Coverage

`ci/coverage-baseline.json` starts with conservative thresholds:

- line coverage: 5%
- branch coverage: 10%
- auth-sensitive line coverage: 12%

The baseline is intentionally modest for the first gate. Raise it only after CI
has produced stable reports for several runs.

## Artifacts

CI uploads:

- `artifacts/test-results/**`
- `artifacts/coverage/**`
- `artifacts/vulnerability/**`
- `artifacts/production-gate/**`
- `production-gate-containers.log`
