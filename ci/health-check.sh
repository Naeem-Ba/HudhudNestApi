#!/usr/bin/env bash
set -Eeuo pipefail

: "${BASE_URL:?BASE_URL is required, for example https://api.example.com}"
BASE_URL="${BASE_URL%/}"

request() {
  local method="$1"
  local url="$2"
  local data="${3:-}"

  if [[ -n "$data" ]]; then
    curl -fsS --retry 3 --retry-delay 5 --max-time 20 \
      -X "$method" \
      -H 'Content-Type: application/json' \
      --data "$data" \
      "$url"
  else
    curl -fsS --retry 3 --retry-delay 5 --max-time 20 \
      -X "$method" \
      "$url"
  fi
}

echo "Running production health checks against: ${BASE_URL}"

echo "1) GET /health"
request GET "${BASE_URL}/health" >/tmp/propertyapi-prod-health-response.txt
cat /tmp/propertyapi-prod-health-response.txt

echo "2) Optional authenticated login health check"
if [[ -n "${HEALTH_EMAIL:-}" && -n "${HEALTH_PASSWORD:-}" ]]; then
  login_payload=$(jq -n \
    --arg email "$HEALTH_EMAIL" \
    --arg password "$HEALTH_PASSWORD" \
    '{Email: $email, Password: $password}')

  login_response=$(request POST "${BASE_URL}/api/Auth/login" "$login_payload")
  echo "$login_response" | jq -e '(.AccessToken // .accessToken // "") | length > 20' >/dev/null
  echo "Authenticated login health check passed."
else
  echo "HEALTH_EMAIL/HEALTH_PASSWORD are not configured. Skipping login check to avoid using fake credentials."
fi

echo "Production health checks passed."
