#!/bin/sh
# Render sets $PORT at runtime and expects the service to bind there; Prometheus itself has
# no config-file env-var expansion, so the listen address is passed as a CLI flag here instead.
set -eu
exec /bin/prometheus \
  --config.file=/etc/prometheus/prometheus.yml \
  --storage.tsdb.path=/prometheus \
  --web.enable-lifecycle \
  --web.enable-remote-write-receiver \
  --web.listen-address="0.0.0.0:${PORT:-9090}"
