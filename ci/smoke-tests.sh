#!/usr/bin/env bash
set -Eeuo pipefail

: "${BASE_URL:?BASE_URL is required, for example https://staging-api.example.com}"
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

echo "Running staging smoke tests against: ${BASE_URL}"

echo "1) GET /health"
request GET "${BASE_URL}/health" >/tmp/propertyapi-health-response.txt
cat /tmp/propertyapi-health-response.txt

echo "2) Optional POST /api/Auth/login"
if [[ -n "${SMOKE_EMAIL:-}" && -n "${SMOKE_PASSWORD:-}" ]]; then
  login_payload=$(jq -n \
    --arg email "$SMOKE_EMAIL" \
    --arg password "$SMOKE_PASSWORD" \
    '{Email: $email, Password: $password}')

  login_response=$(request POST "${BASE_URL}/api/Auth/login" "$login_payload")
  echo "$login_response" | jq -e '(.AccessToken // .accessToken // "") | length > 20' >/dev/null
  echo "Login smoke test passed."
else
  echo "SMOKE_EMAIL/SMOKE_PASSWORD are not configured. Skipping login smoke test to avoid using fake credentials."
fi

echo "Staging smoke tests passed."
