#!/usr/bin/env bash

set -Eeuo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

require_command python3
require_command jq
MIGRATIONS_DIR="${MIGRATIONS_DIR:-${REPOSITORY_ROOT}/HudhudNestApi.Infrastructure/Migrations}"
BASELINE_FILE="${MIGRATION_RISK_BASELINE:-${REPOSITORY_ROOT}/ci/migration-risk-baseline.json}"
OUTPUT_FILE="${MIGRATION_RISK_OUTPUT:-${REPOSITORY_ROOT}/artifacts/database-recovery/migration-risk-report.json}"
mkdir -p "$(dirname "${OUTPUT_FILE}")"
python3 - "${MIGRATIONS_DIR}" "${BASELINE_FILE}" "${OUTPUT_FILE}" <<'PY'
import json
import pathlib
import re
import sys

directory, baseline_path, output_path = map(pathlib.Path, sys.argv[1:])
baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
approved = {item["migrationId"]: item for item in baseline["migrations"]}
results = []
unapproved = []
for path in sorted(directory.glob("*.cs")):
    if path.name.endswith(".Designer.cs") or path.name == "AppDbContextModelSnapshot.cs":
        continue
    text = path.read_text(encoding="utf-8")
    migration_id = path.stem
    up_match = re.search(r"protected override void Up\(MigrationBuilder migrationBuilder\)(.*?)(?=protected override void Down)", text, re.S)
    down_match = re.search(r"protected override void Down\(MigrationBuilder migrationBuilder\)(.*)", text, re.S)
    up = up_match.group(1) if up_match else ""
    down = down_match.group(1) if down_match else ""
    destructive = bool(re.search(r"migrationBuilder\.(DropTable|DropColumn|DeleteData)\s*\(", up))
    manual_sql = bool(re.search(r"migrationBuilder\.Sql\s*\(", up))
    shape_change = bool(re.search(r"migrationBuilder\.(AlterColumn|RenameColumn|RenameTable)\s*\(", up))
    risk = "DESTRUCTIVE" if destructive else "HIGH_RISK" if manual_sql or shape_change else "ADDITIVE"
    down_destructive = bool(re.search(r"migrationBuilder\.(DropTable|DropColumn|DeleteData)\s*\(", down))
    item = {
        "migrationId": migration_id,
        "risk": risk,
        "downDestructive": down_destructive,
        "approved": migration_id in approved,
    }
    results.append(item)
    if risk != "ADDITIVE" and migration_id not in approved:
        unapproved.append(item)
report = {"status": "PASS" if not unapproved else "FAIL", "migrations": results, "unapprovedRisk": unapproved}
output_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
if unapproved:
    print(json.dumps(unapproved, indent=2))
    raise SystemExit("Unapproved destructive/high-risk migration detected")
print(f"Migration risk gate passed for {len(results)} migrations")
PY
