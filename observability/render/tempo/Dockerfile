# Tempo + nginx for the real Staging deployment on Render. Render "Web Service" (Docker),
# Free plan. Tempo needs two distinct internal ports (OTLP ingest, query API) that can't be
# merged in its own config, and Render exposes only one public port per service -- nginx
# multiplexes by path. No Persistent Disk: /var/tempo is ephemeral, which is fine since this
# instance only needs to hold traces for the lifetime of scripts/verify-observability.sh's own
# run, not durable history across restarts.
FROM grafana/tempo:3.0.3 AS tempo-bin

FROM nginx:1.27-alpine

COPY --from=tempo-bin /tempo /usr/local/bin/tempo

COPY observability/render/tempo/tempo.yml /etc/tempo/tempo.yml
COPY observability/render/tempo/nginx.conf.template /etc/nginx/templates/nginx.conf.template
COPY observability/render/tempo/docker-entrypoint.sh /docker-entrypoint.sh

RUN mkdir -p /var/tempo/wal /var/tempo/blocks && \
    chmod +x /docker-entrypoint.sh /usr/local/bin/tempo

ENTRYPOINT ["/docker-entrypoint.sh"]
