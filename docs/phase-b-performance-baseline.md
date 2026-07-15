# Phase B Performance Baseline

## Purpose

This phase adds repeatable performance measurements before new feature work.
The scripts use public API endpoints so they exercise the same controllers,
middleware, validation, authentication, and database paths as real clients.

## Scripts

| Script | Purpose |
| --- | --- |
| `scripts/perf-seed-api.ps1` | Registers/logs in a dedicated seed user and creates repeatable property listings through `POST /api/properties` |
| `scripts/perf-baseline.ps1` | Measures Auth and Properties scenarios and writes JSON/Markdown baseline reports |
| `ci/performance-scenarios.json` | Defines the measured scenarios |

## Seed Dataset

Run against a local or staging-like environment, never production:

```powershell
./scripts/perf-seed-api.ps1 `
  -BaseUrl "http://localhost:5000" `
  -Email "perf.seed@example.test" `
  -Password "PerfSeed123!" `
  -PropertyCount 250
```

The script writes `artifacts/performance/seed-manifest.json`.

## Baseline Run

For public Properties scenarios only:

```powershell
./scripts/perf-baseline.ps1 `
  -BaseUrl "http://localhost:5000" `
  -RequestsPerScenario 100
```

For Auth scenarios as well:

```powershell
$env:PERF_AUTH_EMAIL = "perf.seed@example.test"
$env:PERF_AUTH_PASSWORD = "PerfSeed123!"

./scripts/perf-baseline.ps1 `
  -BaseUrl "http://localhost:5000" `
  -RequestsPerScenario 100
```

Outputs:

- `artifacts/performance/baseline-<run>.json`
- `artifacts/performance/baseline-<run>.md`

## Metrics

Each scenario records:

- request count
- success/failure count
- throughput requests/second
- p50, p95, p99 latency
- min, average, max latency

## Guardrails

- Do not run these scripts against production.
- Keep the same seed size, environment, and database state when comparing runs.
- Compare p95/p99 and throughput between runs; do not compare numbers from
  different machines or database sizes.
- Fix only obvious bottlenecks after a baseline exists.
