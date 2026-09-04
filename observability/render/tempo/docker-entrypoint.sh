#!/bin/sh
# Starts Tempo (bound to localhost-only ports) in the background, then nginx in the
# foreground on Render's single public $PORT, multiplexing to Tempo's two internal ports.
# See nginx.conf.template for the routing and tempo.yml/Dockerfile for why this split exists.
set -eu

envsubst '${PORT}' < /etc/nginx/templates/nginx.conf.template > /etc/nginx/conf.d/default.conf

/usr/local/bin/tempo -config.file=/etc/tempo/tempo.yml &
tempo_pid=$!

# Give Tempo a moment to bind its ports before nginx starts proxying to them.
for i in $(seq 1 30); do
  wget -q -O /dev/null "http://127.0.0.1:3200/ready" 2>/dev/null && break
  kill -0 "${tempo_pid}" 2>/dev/null || { echo "tempo exited before becoming ready" >&2; exit 1; }
  sleep 1
done

exec nginx -g "daemon off;"
