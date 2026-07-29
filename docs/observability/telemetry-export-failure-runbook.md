# Telemetry export failure runbook

Check `otelcol_exporter_send_failed_*`, queue size, collector logs, DNS, TLS
certificate chain, endpoint, protocol, authentication secret presence, backend
limits, and clock skew. Never print OTLP headers.

Keep the application serving: restore the collector/backend, reduce sampling if
capacity is exhausted, and preserve bounded queues. Do not disable Production
validation or TLS. Recovery requires successful smoke evidence for the current
service version/commit and zero sustained export failures.
