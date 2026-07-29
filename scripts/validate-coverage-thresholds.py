#!/usr/bin/env python3
import argparse
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


AUTH_PATTERN = re.compile(r"(Auth|Identity|Security|Otp|Token|Login|Register|Password)", re.IGNORECASE)
BRANCH_COUNTS_PATTERN = re.compile(r"\((\d+)/(\d+)\)")


def percent(covered: int, valid: int) -> float:
    if valid <= 0:
        return 100.0
    return round((covered / valid) * 100.0, 2)


def normalize_path(path_value: str, repository_root: Path) -> str:
    if not path_value:
        return ""

    normalized = path_value.replace("\\", "/")
    path = Path(normalized)

    if path.is_absolute():
        try:
            return path.resolve().relative_to(repository_root).as_posix()
        except ValueError:
            return normalized

    root_marker = repository_root.as_posix().rstrip("/") + "/"
    if root_marker in normalized:
        return normalized.split(root_marker, 1)[1]

    return normalized.lstrip("./")


def is_generated_or_infra(file_name: str, class_name: str) -> bool:
    normalized = file_name.replace("\\", "/")
    return (
        "/Migrations/" in normalized
        or normalized.endswith(".Designer.cs")
        or normalized.endswith(".g.cs")
        or normalized.endswith(".AssemblyInfo.cs")
        or ".Migrations." in class_name
    )


def parse_branch_counts(line_element: ET.Element) -> tuple[int, int]:
    if line_element.attrib.get("branch", "false").lower() != "true":
        return 0, 0

    condition_coverage = line_element.attrib.get("condition-coverage", "")
    match = BRANCH_COUNTS_PATTERN.search(condition_coverage)
    if not match:
        return 0, 0

    return int(match.group(1)), int(match.group(2))


def parse_coverage_files(coverage_files: list[Path], repository_root: Path):
    totals = {
        "linesCovered": 0,
        "linesValid": 0,
        "branchesCovered": 0,
        "branchesValid": 0,
        "authLinesCovered": 0,
        "authLinesValid": 0,
        "authBranchesCovered": 0,
        "authBranchesValid": 0,
        "authGeneratedClassesExcluded": 0,
    }

    line_hits_by_file: dict[str, dict[int, int]] = defaultdict(dict)
    branch_counts_by_file: dict[str, dict[int, tuple[int, int]]] = defaultdict(dict)

    for coverage_file in coverage_files:
        document = ET.parse(coverage_file)
        root = document.getroot()

        totals["linesCovered"] += int(float(root.attrib.get("lines-covered", "0")))
        totals["linesValid"] += int(float(root.attrib.get("lines-valid", "0")))
        totals["branchesCovered"] += int(float(root.attrib.get("branches-covered", "0")))
        totals["branchesValid"] += int(float(root.attrib.get("branches-valid", "0")))

        for class_element in root.findall(".//class"):
            class_name = class_element.attrib.get("name", "")
            file_name = normalize_path(class_element.attrib.get("filename", ""), repository_root)
            is_auth_sensitive = bool(AUTH_PATTERN.search(class_name) or AUTH_PATTERN.search(file_name))

            if is_auth_sensitive and is_generated_or_infra(file_name, class_name):
                totals["authGeneratedClassesExcluded"] += 1
                continue

            for line_element in class_element.findall("./lines/line"):
                line_number = int(line_element.attrib["number"])
                hits = int(float(line_element.attrib.get("hits", "0")))
                covered_branches, valid_branches = parse_branch_counts(line_element)

                line_hits_by_file[file_name][line_number] = max(
                    hits,
                    line_hits_by_file[file_name].get(line_number, 0),
                )

                if valid_branches > 0:
                    existing_covered, existing_valid = branch_counts_by_file[file_name].get(line_number, (0, 0))
                    branch_counts_by_file[file_name][line_number] = (
                        max(existing_covered, covered_branches),
                        max(existing_valid, valid_branches),
                    )

                if is_auth_sensitive:
                    totals["authLinesValid"] += 1
                    if hits > 0:
                        totals["authLinesCovered"] += 1
                    totals["authBranchesCovered"] += covered_branches
                    totals["authBranchesValid"] += valid_branches

    return totals, line_hits_by_file, branch_counts_by_file


def discover_coverage_files(coverage_root: Path) -> list[Path]:
    files = []
    for coverage_file in coverage_root.rglob("coverage.cobertura.xml"):
        normalized = coverage_file.as_posix()
        if "/artifacts/coverage/report/" in normalized:
            continue
        files.append(coverage_file)
    return sorted(files)


def should_check_changed_code(event_name: str, policy: dict) -> bool:
    changed_policy = policy.get("changedCode", {})
    if not changed_policy.get("enabled", False):
        return False
    return changed_policy.get("enforcement", "pull_request") == "always" or event_name == "pull_request"


def diff_base_ref() -> str | None:
    explicit = os.environ.get("COVERAGE_BASE_REF")
    if explicit:
        return explicit

    base_ref = os.environ.get("GITHUB_BASE_REF")
    if base_ref:
        return f"origin/{base_ref}"

    return None


def parse_changed_lines(repository_root: Path, base_ref: str) -> dict[str, set[int]]:
    command = ["git", "diff", "--unified=0", f"{base_ref}...HEAD", "--", "*.cs"]
    completed = subprocess.run(
        command,
        cwd=repository_root,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )

    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or "git diff failed")

    changed_lines: dict[str, set[int]] = defaultdict(set)
    current_file = None
    current_new_line = None

    for line in completed.stdout.splitlines():
        if line.startswith("+++ b/"):
            current_file = line[len("+++ b/") :]
            continue

        if line.startswith("@@"):
            match = re.search(r"\+(\d+)(?:,(\d+))?", line)
            current_new_line = int(match.group(1)) if match else None
            continue

        if current_file is None or current_new_line is None:
            continue

        if line.startswith("+") and not line.startswith("+++"):
            changed_lines[current_file].add(current_new_line)
            current_new_line += 1
        elif line.startswith("-") and not line.startswith("---"):
            continue
        else:
            current_new_line += 1

    return changed_lines


def is_policy_production_file(path: str, changed_policy: dict) -> bool:
    normalized = path.replace("\\", "/")
    prefixes = changed_policy.get("productionPathPrefixes", [])
    exclusions = changed_policy.get("excludedPathPatterns", [])

    if not any(normalized.startswith(prefix) for prefix in prefixes):
        return False

    return not any(exclusion in normalized for exclusion in exclusions)


def evaluate_changed_code(policy: dict, repository_root: Path, line_hits_by_file, branch_counts_by_file):
    event_name = os.environ.get("GITHUB_EVENT_NAME", "")
    changed_policy = policy.get("changedCode", {})

    result = {
        "enabled": changed_policy.get("enabled", False),
        "enforced": False,
        "status": "skipped",
        "reason": "Changed-code coverage is enforced for pull_request events only.",
        "changedProductionFiles": [],
        "unmappedChangedProductionFiles": [],
        "changedExecutableLines": 0,
        "changedCoveredLines": 0,
        "changedLineCoveragePercent": 100.0,
        "changedBranchesValid": 0,
        "changedBranchesCovered": 0,
        "changedBranchCoveragePercent": 100.0,
    }

    if not should_check_changed_code(event_name, policy):
        return result

    base_ref = diff_base_ref()
    if not base_ref:
        result["status"] = "failed"
        result["reason"] = "Pull request changed-code coverage requires GITHUB_BASE_REF or COVERAGE_BASE_REF."
        return result

    result["enforced"] = True
    result["reason"] = f"Compared changed production lines against {base_ref}."

    changed_lines = parse_changed_lines(repository_root, base_ref)
    failures = []

    for path, line_numbers in sorted(changed_lines.items()):
        if not is_policy_production_file(path, changed_policy):
            continue

        result["changedProductionFiles"].append(path)
        coverage_lines = line_hits_by_file.get(path)
        branch_lines = branch_counts_by_file.get(path, {})

        if coverage_lines is None:
            result["unmappedChangedProductionFiles"].append(path)
            failures.append(f"{path}: changed production file is missing from coverage data.")
            continue

        for line_number in sorted(line_numbers):
            if line_number in coverage_lines:
                result["changedExecutableLines"] += 1
                if coverage_lines[line_number] > 0:
                    result["changedCoveredLines"] += 1

            if line_number in branch_lines:
                covered, valid = branch_lines[line_number]
                result["changedBranchesCovered"] += covered
                result["changedBranchesValid"] += valid

    result["changedLineCoveragePercent"] = percent(
        result["changedCoveredLines"],
        result["changedExecutableLines"],
    )
    result["changedBranchCoveragePercent"] = percent(
        result["changedBranchesCovered"],
        result["changedBranchesValid"],
    )

    if result["changedExecutableLines"] == 0 and not result["unmappedChangedProductionFiles"]:
        result["status"] = "passed"
        result["reason"] = "No changed executable production lines were found."
        return result

    if result["changedLineCoveragePercent"] < float(changed_policy.get("minimumLineCoveragePercent", 0)):
        failures.append(
            f"Changed-code line coverage {result['changedLineCoveragePercent']}% is below "
            f"{changed_policy.get('minimumLineCoveragePercent')}%."
        )

    if result["changedBranchesValid"] > 0 and result["changedBranchCoveragePercent"] < float(changed_policy.get("minimumBranchCoveragePercent", 0)):
        failures.append(
            f"Changed-code branch coverage {result['changedBranchCoveragePercent']}% is below "
            f"{changed_policy.get('minimumBranchCoveragePercent')}%."
        )

    result["status"] = "failed" if failures else "passed"
    result["failures"] = failures
    return result


def write_summary(output_directory: Path, report: dict):
    lines = [
        "# Coverage Threshold Gate",
        "",
        f"Status: {report['status']}",
        "",
        "| Metric | Actual | Minimum |",
        "| --- | ---: | ---: |",
        f"| Line coverage | {report['lineCoveragePercent']}% | {report['thresholds']['minimumLineCoveragePercent']}% |",
        f"| Branch coverage | {report['branchCoveragePercent']}% | {report['thresholds']['minimumBranchCoveragePercent']}% |",
        f"| Auth-sensitive line coverage | {report['authLineCoveragePercent']}% | {report['thresholds']['minimumAuthLineCoveragePercent']}% |",
        f"| Auth-sensitive branch coverage | {report['authBranchCoveragePercent']}% | {report['thresholds']['minimumAuthBranchCoveragePercent']}% |",
        f"| Changed-code line coverage | {report['changedCode']['changedLineCoveragePercent']}% | {report['thresholds']['minimumChangedCodeLineCoveragePercent']}% |",
        "",
        f"Changed-code status: {report['changedCode']['status']}",
    ]

    if report["failures"]:
        lines += ["", "Failures:"]
        lines += [f"- {failure}" for failure in report["failures"]]

    (output_directory / "coverage-threshold-summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate staged coverage thresholds from Cobertura reports.")
    parser.add_argument("--repository-root", default=".")
    parser.add_argument("--coverage-root", default="artifacts/test-results")
    parser.add_argument("--policy", default="ci/coverage-thresholds.json")
    parser.add_argument("--output-directory", default="artifacts/coverage")
    args = parser.parse_args()

    repository_root = Path(args.repository_root).resolve()
    coverage_root = (repository_root / args.coverage_root).resolve()
    policy_path = (repository_root / args.policy).resolve()
    output_directory = (repository_root / args.output_directory).resolve()
    output_directory.mkdir(parents=True, exist_ok=True)

    policy = json.loads(policy_path.read_text(encoding="utf-8"))
    coverage_files = discover_coverage_files(coverage_root)

    failures = []
    if not coverage_files:
        failures.append(f"No coverage.cobertura.xml files were found under {coverage_root}.")
        totals = {
            "linesCovered": 0,
            "linesValid": 0,
            "branchesCovered": 0,
            "branchesValid": 0,
            "authLinesCovered": 0,
            "authLinesValid": 0,
            "authBranchesCovered": 0,
            "authBranchesValid": 0,
            "authGeneratedClassesExcluded": 0,
        }
        line_hits_by_file = defaultdict(dict)
        branch_counts_by_file = defaultdict(dict)
    else:
        totals, line_hits_by_file, branch_counts_by_file = parse_coverage_files(coverage_files, repository_root)

    line_coverage = percent(totals["linesCovered"], totals["linesValid"])
    branch_coverage = percent(totals["branchesCovered"], totals["branchesValid"])
    auth_line_coverage = percent(totals["authLinesCovered"], totals["authLinesValid"])
    auth_branch_coverage = percent(totals["authBranchesCovered"], totals["authBranchesValid"])

    global_thresholds = policy["global"]
    auth_thresholds = policy["criticalModules"]["authSensitive"]

    if line_coverage < float(global_thresholds["minimumLineCoveragePercent"]):
        failures.append(f"Line coverage {line_coverage}% is below {global_thresholds['minimumLineCoveragePercent']}%.")

    if branch_coverage < float(global_thresholds["minimumBranchCoveragePercent"]):
        failures.append(f"Branch coverage {branch_coverage}% is below {global_thresholds['minimumBranchCoveragePercent']}%.")

    if auth_line_coverage < float(auth_thresholds["minimumLineCoveragePercent"]):
        failures.append(
            f"Auth-sensitive line coverage {auth_line_coverage}% is below {auth_thresholds['minimumLineCoveragePercent']}%."
        )

    if auth_branch_coverage < float(auth_thresholds["minimumBranchCoveragePercent"]):
        failures.append(
            f"Auth-sensitive branch coverage {auth_branch_coverage}% is below {auth_thresholds['minimumBranchCoveragePercent']}%."
        )

    try:
        changed_code = evaluate_changed_code(policy, repository_root, line_hits_by_file, branch_counts_by_file)
    except Exception as error:
        changed_code = {
            "enabled": policy.get("changedCode", {}).get("enabled", False),
            "enforced": True,
            "status": "failed",
            "reason": str(error),
            "changedProductionFiles": [],
            "unmappedChangedProductionFiles": [],
            "changedExecutableLines": 0,
            "changedCoveredLines": 0,
            "changedLineCoveragePercent": 0.0,
            "changedBranchesValid": 0,
            "changedBranchesCovered": 0,
            "changedBranchCoveragePercent": 0.0,
            "failures": [str(error)],
        }

    if changed_code.get("status") == "failed":
        failures.extend(changed_code.get("failures") or [changed_code.get("reason", "Changed-code coverage failed.")])

    report = {
        "schemaVersion": 1,
        "status": "passed" if not failures else "failed",
        "coverageFileCount": len(coverage_files),
        "coverageFiles": [coverage_file.relative_to(repository_root).as_posix() for coverage_file in coverage_files],
        "lineCoveragePercent": line_coverage,
        "branchCoveragePercent": branch_coverage,
        "authLineCoveragePercent": auth_line_coverage,
        "authBranchCoveragePercent": auth_branch_coverage,
        "totals": totals,
        "thresholds": {
            "minimumLineCoveragePercent": global_thresholds["minimumLineCoveragePercent"],
            "minimumBranchCoveragePercent": global_thresholds["minimumBranchCoveragePercent"],
            "minimumAuthLineCoveragePercent": auth_thresholds["minimumLineCoveragePercent"],
            "minimumAuthBranchCoveragePercent": auth_thresholds["minimumBranchCoveragePercent"],
            "minimumChangedCodeLineCoveragePercent": policy["changedCode"]["minimumLineCoveragePercent"],
            "minimumChangedCodeBranchCoveragePercent": policy["changedCode"]["minimumBranchCoveragePercent"],
        },
        "changedCode": changed_code,
        "failures": failures,
    }

    (output_directory / "coverage-threshold-summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    write_summary(output_directory, report)

    print(f"Coverage threshold report: {output_directory / 'coverage-threshold-summary.json'}")
    print(f"Status: {report['status']}")

    if failures:
        for failure in failures:
            print(f"::error::{failure}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
