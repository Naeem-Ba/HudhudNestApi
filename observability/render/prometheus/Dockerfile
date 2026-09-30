# Prometheus for the real Staging deployment on Render. Render "Web Service" (Docker), Free
# plan. No Persistent Disk is attached -- /prometheus is ephemeral container storage, which is
# fine here: this instance only needs to hold the last few minutes of data for
# scripts/verify-observability.sh's own queries, not durable history across restarts.
FROM prom/prometheus:v3.15.0

# Build context is the repository root (matches HudhudNestApi/Dockerfile's convention) so this
# can reuse the exact same rule files the local docker-compose stack uses -- one source of
# truth for alerting/SLO rules across both environments.
COPY observability/prometheus/rules /etc/prometheus/rules
COPY observability/render/prometheus/prometheus.yml /etc/prometheus/prometheus.yml
COPY observability/render/prometheus/docker-entrypoint.sh /docker-entrypoint.sh

USER root
RUN chmod +x /docker-entrypoint.sh
USER nobody

ENTRYPOINT ["/docker-entrypoint.sh"]
