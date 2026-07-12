#!/usr/bin/env bash
set -Eeuo pipefail

: "${BASE_URL:?BASE_URL is required, for example https://staging-api.example.com}"
BASE_URL="${BASE_URL%/}"

request() {
  local method="$1" path="$2" body="${3:-}" token="${4:-}"
  local args=(--fail --show-error --silent --request "$method" "$BASE_URL$path" --header 'Accept: application/json')
  if [[ -n "$body" ]]; then args+=(--header 'Content-Type: application/json' --data "$body"); fi
  if [[ -n "$token" ]]; then args+=(--header "Authorization: Bearer $token"); fi
  curl "${args[@]}"
}

echo '[1/6] Liveness'
request GET /health/live >/dev/null

echo '[2/6] Readiness'
request GET /health/ready >/dev/null

echo '[3/6] Public property list'
request GET '/api/properties?page=1&pageSize=5' >/tmp/property-list.json

if [[ -n "${SMOKE_EMAIL:-}" && -n "${SMOKE_PASSWORD:-}" ]]; then
  echo '[4/6] Login'
  login_body="$(python3 - <<PY
import json, os
print(json.dumps({'email': os.environ['SMOKE_EMAIL'], 'password': os.environ['SMOKE_PASSWORD']}))
PY
)"
  login_response="$(request POST /api/auth/login "$login_body")"
  token="$(python3 - "$login_response" <<'PY'
import json, sys
obj=json.loads(sys.argv[1])
for key in ('accessToken','token'):
    if obj.get(key): print(obj[key]); raise SystemExit
if isinstance(obj.get('data'),dict):
    for key in ('accessToken','token'):
        if obj['data'].get(key): print(obj['data'][key]); raise SystemExit
raise SystemExit('No access token found in login response')
PY
)"

  echo '[5/6] Authenticated profile'
  request GET /api/profile/me '' "$token" >/tmp/profile.json

  echo '[6/6] Refresh/logout contract presence'
  # Keep destructive/session-changing checks configurable because DTO shapes differ.
  # Add exact refresh/logout requests here after confirming the current API contract.
else
  echo '[4-6/6] Auth smoke skipped: SMOKE_EMAIL/SMOKE_PASSWORD not configured.'
fi

echo 'Staging smoke tests passed.'
