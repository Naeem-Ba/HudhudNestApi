#!/usr/bin/env bash
set -Eeuo pipefail

: "${BASE_URL_A:?Direct URL for replica A is required}"
: "${BASE_URL_B:?Direct URL for replica B is required}"
CACHE_PATH="${CACHE_PATH:-/api/properties?page=1&pageSize=5}"

hash_response() {
  curl --fail --silent "$1$CACHE_PATH" | sha256sum | awk '{print $1}'
}

echo 'Warm cache on replica A'
hash_a1="$(hash_response "$BASE_URL_A")"

echo 'Read same key from replica B'
hash_b1="$(hash_response "$BASE_URL_B")"

if [[ "$hash_a1" != "$hash_b1" ]]; then
  echo "Initial responses differ: A=$hash_a1 B=$hash_b1" >&2
  exit 1
fi

echo 'Initial cross-replica cache consistency passed.'

if [[ -n "${MUTATION_COMMAND:-}" ]]; then
  echo 'Executing configured mutation command'
  bash -c "$MUTATION_COMMAND"
  sleep "${EVICTION_WAIT_SECONDS:-2}"

  hash_a2="$(hash_response "$BASE_URL_A")"
  hash_b2="$(hash_response "$BASE_URL_B")"
  if [[ "$hash_a2" != "$hash_b2" ]]; then
    echo "Post-mutation responses differ: A=$hash_a2 B=$hash_b2" >&2
    exit 1
  fi
  if [[ "$hash_a2" == "$hash_a1" && "${EXPECT_CONTENT_CHANGE:-true}" == "true" ]]; then
    echo 'Mutation did not change the cached response. Verify tag eviction and test data.' >&2
    exit 1
  fi
  echo 'Post-mutation eviction consistency passed.'
else
  echo 'MUTATION_COMMAND not configured; eviction phase skipped.'
fi
