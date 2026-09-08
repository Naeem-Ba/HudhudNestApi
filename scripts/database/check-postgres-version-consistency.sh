#!/usr/bin/env bash
#
# Fails if any workflow or compose file references a postgis/postgis image
# tag that does not match ci/postgres-version.env -- the single canonical
# version this project targets (see that file's comment for why: production,
# CI, the production gate, and performance testing must all agree, or a
# version-specific regression is only ever caught in production).
#
# No python3 dependency (unlike analyze-migrations.sh) -- this must run the
# same way locally and in CI, and python3 is not reliably present on every
# local dev machine (confirmed missing on at least one contributor's machine
# during Phase 2's audit).
set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

CANONICAL_FILE="${REPOSITORY_ROOT}/ci/postgres-version.env"

if [[ ! -f "${CANONICAL_FILE}" ]]; then
    echo "[postgres-version] ERROR: canonical file not found: ${CANONICAL_FILE}" >&2
    exit 1
fi

# shellcheck source=/dev/null
source "${CANONICAL_FILE}"

if [[ -z "${POSTGRES_IMAGE:-}" ]]; then
    echo "[postgres-version] ERROR: POSTGRES_IMAGE is not set in ${CANONICAL_FILE}" >&2
    exit 1
fi

echo "[postgres-version] Canonical image: ${POSTGRES_IMAGE}"

# Search every workflow and compose file this project has for any
# postgis/postgis reference, then flag every one that does not exactly equal
# the canonical value. Scoped to .github/workflows, ci/, and performance/ --
# the three locations Finding F1 named -- rather than the whole repository,
# so an unrelated doc mentioning an old version in prose (e.g. a dated audit
# report describing what was true when it was written) does not false-fail
# this check.
SEARCH_PATHS=(
    "${REPOSITORY_ROOT}/.github/workflows"
    "${REPOSITORY_ROOT}/ci"
    "${REPOSITORY_ROOT}/performance"
)

mismatches=0
checked=0

while IFS= read -r -d '' file; do
    # ci/postgres-version.env itself is the canonical source, not a place to
    # check against itself.
    if [[ "${file}" == "${CANONICAL_FILE}" ]]; then
        continue
    fi

    while IFS=: read -r line_number line_content; do
        checked=$((checked + 1))

        found_image=$(echo "${line_content}" | grep -oE 'postgis/postgis:[A-Za-z0-9.\-]+')

        if [[ "${found_image}" != "${POSTGRES_IMAGE}" ]]; then
            echo "[postgres-version] MISMATCH: ${file}:${line_number} references '${found_image}', expected '${POSTGRES_IMAGE}'" >&2
            mismatches=$((mismatches + 1))
        fi
    done < <(grep -n 'postgis/postgis:' "${file}" || true)
done < <(find "${SEARCH_PATHS[@]}" -type f \( -name "*.yml" -o -name "*.yaml" \) -print0 2>/dev/null)

if [[ "${checked}" -eq 0 ]]; then
    echo "[postgres-version] ERROR: found zero postgis/postgis references under ${SEARCH_PATHS[*]} -- the search itself is broken, or every reference was removed without updating this script." >&2
    exit 1
fi

if [[ "${mismatches}" -gt 0 ]]; then
    echo "[postgres-version] FAILED: ${mismatches} file(s) reference a Postgres/PostGIS image other than the canonical ${POSTGRES_IMAGE}." >&2
    exit 1
fi

echo "[postgres-version] PASSED: all ${checked} reference(s) match ${POSTGRES_IMAGE}."
