#!/usr/bin/env bash
# Sends a dead-man heartbeat to an external monitor (Healthchecks.io protocol):
#   start   -> <url>/start   (the run began; lets the monitor see runs that never finish)
#   success -> <url>         (the backup was created, verified and uploaded)
#   fail    -> <url>/fail    (the backup failed)
# The URL comes from HEALTHCHECKS_PING_URL (a secret: it contains the check's UUID, never echo it).
# A monitoring outage must never fail or hide a backup, so this script always exits 0.
set -uo pipefail

kind="${1:-}"
case "${kind}" in
  start) suffix="/start" ;;
  success) suffix="" ;;
  fail) suffix="/fail" ;;
  *) echo "usage: ping-heartbeat.sh start|success|fail" >&2; exit 0 ;;
esac

if [ -z "${HEALTHCHECKS_PING_URL:-}" ]; then
  echo "::warning::HEALTHCHECKS_PING_URL is not configured: no external dead-man monitor is watching this backup (BKP-003)."
  exit 0
fi

base="${HEALTHCHECKS_PING_URL%/}"
case "${base}" in
  https://*) ;;
  *) echo "::warning::HEALTHCHECKS_PING_URL must start with https://; heartbeat skipped."; exit 0 ;;
esac

if curl --fail --silent --show-error --output /dev/null \
     --max-time 10 --retry 3 --retry-delay 2 --retry-connrefused \
     --request POST --data-raw "run=${GITHUB_RUN_ID:-local}" "${base}${suffix}"; then
  echo "Heartbeat '${kind}' sent."
else
  echo "::warning::Heartbeat '${kind}' could not be delivered (the backup result is unaffected)."
fi
exit 0
