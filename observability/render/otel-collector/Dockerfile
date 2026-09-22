# otel-collector for the real Staging deployment on Render. Renders as a Render "Web
# Service" (Docker), Free plan. Only one public port is needed (the OTLP-HTTP receiver);
# everything else this collector does is outbound.
#
# Build context is the repository root (matches HudhudNestApi/Dockerfile's convention and the
# other two observability/render/* Dockerfiles).
FROM otel/opentelemetry-collector-contrib:0.161.0

COPY observability/render/otel-collector/config.yaml /etc/otelcol-contrib/config.yaml

# ${env:...} expansion in config.yaml is resolved by the collector itself at startup, so no
# custom entrypoint/shell wrapper is needed here.
CMD ["--config=/etc/otelcol-contrib/config.yaml"]
