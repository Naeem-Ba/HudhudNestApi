#!/usr/bin/env bash
set -euo pipefail

base_url="${STAGING_BASE_URL:-${BASE_URL:-}}"

if [ -z "${base_url}" ]; then
  echo "STAGING_BASE_URL or BASE_URL is required."
  exit 1
fi

base_url="${base_url%/}"

curl \
  --fail \
  --silent \
  --show-error \
  --max-time 10 \
  "${base_url}/health/live" \
  > /dev/null

curl \
  --fail \
  --silent \
  --show-error \
  --max-time 10 \
  "${base_url}/health/ready" \
  > /dev/null

curl \
  --fail \
  --silent \
  --show-error \
  --max-time 10 \
  "${base_url}/api/enums/PropertyStatus" \
  > /dev/null

echo "Staging smoke checks passed."
