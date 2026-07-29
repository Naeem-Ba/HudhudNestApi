#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env BACKUP_STORAGE_URI
require_command aws
require_command jq
parse_s3_uri "${BACKUP_STORAGE_URI}"
aws_arguments
OUTPUT_DIR="${BACKUP_OUTPUT_DIR:-${REPOSITORY_ROOT}/artifacts/database-backup-download}"
mkdir -p "${OUTPUT_DIR}"
LIST_JSON="$(aws "${AWS_ARGS[@]}" s3api list-objects-v2 --bucket "${S3_BUCKET}" --prefix "${S3_PREFIX:+${S3_PREFIX}/}backups/" --output json)"
MANIFEST_KEY="$(jq -r '[.Contents[]? | select(.Key | endswith(".manifest.json"))] | sort_by(.LastModified) | last | .Key // empty' <<<"${LIST_JSON}")"
[ -n "${MANIFEST_KEY}" ] || fail "No backup manifests were found in object storage."
aws "${AWS_ARGS[@]}" s3 cp "s3://${S3_BUCKET}/${MANIFEST_KEY}" "${OUTPUT_DIR}/manifest.json" --only-show-errors
ARCHIVE_KEY="$(jq -r '.archiveObjectKey' "${OUTPUT_DIR}/manifest.json")"
[ -n "${ARCHIVE_KEY}" ] && [ "${ARCHIVE_KEY}" != "null" ] || fail "Manifest does not contain archiveObjectKey."
aws "${AWS_ARGS[@]}" s3 cp "s3://${S3_BUCKET}/${ARCHIVE_KEY}" "${OUTPUT_DIR}/backup.dump.gpg" --only-show-errors
jq -n --arg status PASS --arg manifest "${OUTPUT_DIR}/manifest.json" --arg archive "${OUTPUT_DIR}/backup.dump.gpg" '{status:$status,manifest:$manifest,archive:$archive}' > "${OUTPUT_DIR}/download-result.json"
log "Downloaded latest encrypted backup and manifest."
