# Observability architecture

`PropertyApi -> OTLP/gRPC -> OpenTelemetry Collector -> Tempo + Prometheus -> Grafana`

The application exports traces and metrics only. The collector performs a
second redaction pass, batches signals, sends traces to Tempo, converts spans to
bounded dependency metrics, and exposes metrics to Prometheus. Grafana uses
provisioned Tempo and Prometheus data sources.

The collector OTLP ports are internal Docker-network ports. Only Grafana,
Prometheus, Tempo query API, and the API test port are published by the local
stack. Production must use TLS to an authenticated or private-network collector;
credentials come from `Observability__Otlp__Headers` in the secret store.

Staging and Production use distinct `deployment.environment.name`, service
version, commit SHA, collectors, storage, credentials, and retention policies.
Local Tempo retains 24 hours. Production retention is a backend policy and must
match legal and incident-response requirements.

Exporter outages do not crash request handling; SDK queues/timeouts bound the
impact. Missing or insecure mandatory Production configuration does block
startup. Sampling is 100% in local/CI and configurable ratio sampling in
Production. Metric labels stay bounded to operations, outcomes, methods, status
classes, span kinds, and dependency systems.
