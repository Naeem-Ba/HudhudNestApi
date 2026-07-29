# Observability runbook

## Local startup

```bash
docker compose -f observability/docker-compose.observability.yml up -d --build
OBSERVABILITY_TEST_KEY=local-staging-cleanup-secret-at-least-32-characters \
  bash scripts/verify-observability.sh
```

Grafana: `http://localhost:13000`; Prometheus: `http://localhost:19090`;
Tempo API: `http://localhost:13200`. Local credentials are non-production.

## Required deployment settings

Set `Observability__Enabled`, service name/namespace/version/environment,
commit SHA, OTLP endpoint/protocol/headers, sampling ratio, timeout, and metric
interval. Store headers only in the deployment secret store.

Find a trace in Tempo by `correlation.id`, trace ID,
`service.name`, `service.version`, `git.commit.sha`, and environment. Use the
dashboard for rate, p95/p99, 5xx, auth, dependency, runtime, collector, and SLO
views. Confirm collector queue/export failure metrics before blaming the app.

Production collector endpoints require TLS and network restriction. Do not
enable request bodies, SQL statements, Redis verbose statements, or sensitive
headers. Validate retention and delete access quarterly.
