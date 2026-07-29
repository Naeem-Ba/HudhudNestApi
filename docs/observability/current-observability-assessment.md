# Current observability assessment

## Baseline

Before this change the API supplied correlation IDs, request logging scopes,
`PropertyApi.Api` and `PropertyApi.Application` activity sources, and custom
meters. No `TracerProvider`, `MeterProvider`, OTLP exporter, collector, trace
store, metric store, dashboard, executable SLO, or tested alert existed. An
activity source alone therefore produced no export evidence.

## Implemented in this change

- OpenTelemetry .NET SDK with OTLP trace and metric exporters.
- ASP.NET Core, HttpClient, Npgsql, Redis, runtime, process, and custom sources.
- Central fail-closed Production configuration validation.
- Application and collector redaction; SQL text and Redis verbose statements off.
- Collector, Tempo, Prometheus, Grafana dashboard, alert and SLO rules.
- Controlled Staging-only synthetic dependency check and machine-readable smoke test.
- Automated configuration, redaction, and cardinality tests.

## Remaining external verification

Repository implementation does not prove a real Production deployment. Until
the smoke test succeeds against the deployed release, Production readiness is
failed. Required external items are the OTLP endpoint and authentication secret,
Staging/Production service version and commit SHA, and notification routing.

## Risks reviewed

Raw SQL, query strings, authorization/cookie headers, Redis keys, tokens, OTPs,
phone numbers, email addresses, connection strings, and request/response bodies
must never be exported. High-cardinality identifiers are prohibited as metric
dimensions. `OpenTelemetry.Instrumentation.StackExchangeRedis` remains beta
because the upstream contrib package has not declared a stable release; it is
isolated behind the standard SDK and covered by the export/redaction smoke test.
