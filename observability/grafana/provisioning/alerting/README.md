# Alert provisioning

Operational and SLO alerts are evaluated by Prometheus from the versioned files
under `observability/prometheus/rules`. Grafana reads the same Prometheus data
source and displays alert state. Notification routing is intentionally supplied
by the deployment secret store; no webhook or paging credential is committed.
