#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_env BACKUP_STORAGE_URI
require_command aws
require_command jq
require_command python3
parse_s3_uri "${BACKUP_STORAGE_URI}"
aws_arguments
WORK_DIR="$(create_private_temp_dir)"
cleanup() { rm -rf -- "${WORK_DIR}"; }
trap cleanup EXIT INT TERM
OBJECTS_JSON="${WORK_DIR}/objects.json"
DELETE_KEYS="${WORK_DIR}/delete-keys.txt"
if ! aws "${AWS_ARGS[@]}" s3api list-objects-v2 \
  --bucket "${S3_BUCKET}" \
  --prefix "${S3_PREFIX:+${S3_PREFIX}/}backups/" \
  --output json > "${OBJECTS_JSON}" 2> "${WORK_DIR}/list-error.log"; then
  if grep -q "NoSuchKey" "${WORK_DIR}/list-error.log"; then
    log "Storage backend returned NoSuchKey for an empty prefix listing (known non-standard S3-compatible behavior). Treating as no objects."
    echo '{"Contents": []}' > "${OBJECTS_JSON}"
  else
    cat "${WORK_DIR}/list-error.log" >&2
    fail "list-objects-v2 failed."
  fi
fi
python3 - "${OBJECTS_JSON}" "${DELETE_KEYS}" <<'PY'
import datetime as dt
import json
import pathlib
import sys

source, output = map(pathlib.Path, sys.argv[1:])
objects = json.loads(source.read_text(encoding="utf-8")).get("Contents", [])
manifests = [o for o in objects if o.get("Key", "").endswith(".manifest.json")]
now = dt.datetime.now(dt.timezone.utc)
records = []
for item in manifests:
    stamp = dt.datetime.fromisoformat(item["LastModified"].replace("Z", "+00:00"))
    records.append((stamp, item["Key"][:-len(".manifest.json")]))
records.sort(reverse=True)
keep = set()
buckets = set()
hourly_window = dt.timedelta(hours=48)
daily_window = hourly_window + dt.timedelta(days=30)
weekly_window = daily_window + dt.timedelta(weeks=12)
monthly_window = weekly_window + dt.timedelta(days=366)
for stamp, stem in records:
    age = now - stamp
    bucket = None
    if age <= hourly_window:
        keep.add(stem)
        continue
    if age <= daily_window:
        bucket = ("day", stamp.date().isoformat())
    elif age <= weekly_window:
        iso = stamp.isocalendar()
        bucket = ("week", iso.year, iso.week)
    elif age <= monthly_window:
        bucket = ("month", stamp.year, stamp.month)
    if bucket is not None and bucket not in buckets:
        buckets.add(bucket)
        keep.add(stem)
delete_stems = {stem for _, stem in records if stem not in keep}
keys = sorted(
    item["Key"] for item in objects
    if any(item["Key"] == stem + suffix for stem in delete_stems for suffix in (
        ".manifest.json", ".dump.gpg", ".globals.sql.gpg"
    ))
)
output.write_text("\n".join(keys), encoding="utf-8")
print(json.dumps({"backupSets": len(records), "kept": len(keep), "deleteObjects": len(keys)}))
PY

if [ ! -s "${DELETE_KEYS}" ]; then
  log "Retention policy has nothing to delete."
  exit 0
fi
if [ "${RETENTION_APPLY:-false}" != "true" ]; then
  log "Retention dry run only. Set RETENTION_APPLY=true to delete expired backup objects."
  sed 's/^/[would-delete] /' "${DELETE_KEYS}" >&2
  exit 0
fi
while IFS= read -r key || [ -n "${key}" ]; do
  [ -n "${key}" ] || continue
  aws "${AWS_ARGS[@]}" s3 rm "s3://${S3_BUCKET}/${key}" --only-show-errors
done < "${DELETE_KEYS}"
log "Expired backup objects were removed according to the 48h/30d/12w/12m policy."
