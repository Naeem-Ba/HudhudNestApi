# Phase B Observability

## Implemented Surface

- Every request receives a correlation id through `X-Correlation-ID`.
- Logs are scoped with `TraceId`, `SpanId`, `CorrelationId`, `RouteGroup`, and `Operation`.
- API metrics use low-cardinality tags only: `route_group`, `operation`, `method`, `status_code`, and `outcome`.
- Auth metrics are grouped by `register`, `login`, `refresh`, `logout`, `send_otp`, `verify_otp`, `email_verification`, and `password_recovery`.
- Properties metrics are grouped by `list`, `get_by_id`, `geo_search`, `create`, `update`, and `delete`.
- MediatR requests create Application-layer spans and metrics grouped by `command`, `query`, or `request`.

## OTLP Configuration Contract

The application now owns a stable configuration section:

```json
{
  "Observability": {
    "ServiceName": "PropertyApi",
    "ServiceVersion": "",
    "CorrelationHeaderName": "X-Correlation-ID",
    "RequestLoggingEnabled": true,
    "JsonConsoleEnabled": false,
    "Otlp": {
      "Endpoint": "http://otel-collector:4317",
      "Protocol": "grpc",
      "Headers": ""
    }
  }
}
```

`Observability:Otlp:Endpoint` is fail-fast validated when configured. The app emits `ActivitySource` and `Meter` instruments without adding exporter packages in this slice; the deployment can attach an OpenTelemetry collector/exporter without changing request behavior.

## Redaction Review

The request middleware does not log request bodies, query strings, authorization headers, refresh tokens, OTP codes, phone numbers, emails, user ids, or IP addresses. Metrics intentionally avoid user-level dimensions.

## Dashboard Specification

- API request rate by `route_group`, `operation`, and `outcome`.
- API duration p50/p95/p99 by `route_group` and `operation`.
- Auth request rate and failure rate by `operation`.
- Properties request rate and p95 latency by `operation`.
- Application command/query rate and duration by `request_kind`.

## Alert Specification

- Auth failure ratio exceeds baseline for 10 minutes.
- Refresh operation p95 latency exceeds baseline threshold for 10 minutes.
- Properties geo search p95 latency exceeds baseline threshold for 10 minutes.
- HTTP 5xx ratio exceeds baseline for 5 minutes.
- Collector/exporter delivery failures are non-zero for 5 minutes once OTLP export is enabled in the hosting environment.
