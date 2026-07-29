#!/usr/bin/env python3
import argparse
import json
import re
from pathlib import Path


USES_PATTERN = re.compile(r"^(?P<indent>\s*)uses:\s*(?P<target>[^#\s]+)(?:\s*#\s*(?P<comment>.+))?\s*$")
FULL_SHA_PATTERN = re.compile(r"^[0-9a-fA-F]{40}$")
VERSION_COMMENT_PATTERN = re.compile(r"\bv?\d+(?:\.\d+){0,3}\b")


def iter_workflow_files(root: Path):
    workflow_root = root / ".github" / "workflows"
    if workflow_root.exists():
        yield from sorted(workflow_root.glob("*.yml"))
        yield from sorted(workflow_root.glob("*.yaml"))

    actions_root = root / ".github" / "actions"
    if actions_root.exists():
        yield from sorted(actions_root.rglob("*.yml"))
        yield from sorted(actions_root.rglob("*.yaml"))


def is_local_action(target: str) -> bool:
    return target.startswith("./") or target.startswith("../")


def validate_file(path: Path, root: Path):
    entries = []
    failures = []

    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        match = USES_PATTERN.match(line)
        if not match:
            continue

        target = match.group("target").strip().strip("'\"")
        comment = (match.group("comment") or "").strip()
        relative_path = path.relative_to(root).as_posix()

        entry = {
            "file": relative_path,
            "line": line_number,
            "uses": target,
            "local": is_local_action(target),
            "pinned": False,
            "hasVersionComment": False,
            "result": "failed",
            "failures": []
        }

        if entry["local"]:
            entry["pinned"] = True
            entry["result"] = "passed"
            entries.append(entry)
            continue

        if "@" not in target:
            entry["failures"].append("External action is missing an @ref.")
        else:
            action_name, action_ref = target.rsplit("@", 1)
            entry["action"] = action_name
            entry["ref"] = action_ref

            if FULL_SHA_PATTERN.match(action_ref):
                entry["pinned"] = True
            else:
                entry["failures"].append("External action ref is not a full 40-character commit SHA.")

        if VERSION_COMMENT_PATTERN.search(comment):
            entry["hasVersionComment"] = True
            entry["releaseComment"] = comment
        else:
            entry["failures"].append("Pinned action must keep a human-readable release comment such as '# v5'.")

        if not entry["failures"]:
            entry["result"] = "passed"
        else:
            failures.extend(f"{relative_path}:{line_number}: {failure}" for failure in entry["failures"])

        entries.append(entry)

    return entries, failures


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate GitHub Actions use immutable commit SHAs.")
    parser.add_argument("--repository-root", default=".", help="Repository root path.")
    parser.add_argument("--output", default="artifacts/supply-chain/action-pinning-report.json")
    args = parser.parse_args()

    root = Path(args.repository_root).resolve()
    output_path = root / args.output
    output_path.parent.mkdir(parents=True, exist_ok=True)

    all_entries = []
    all_failures = []
    workflow_files = list(iter_workflow_files(root))

    for workflow_file in workflow_files:
        entries, failures = validate_file(workflow_file, root)
        all_entries.extend(entries)
        all_failures.extend(failures)

    report = {
        "schemaVersion": 1,
        "status": "passed" if not all_failures else "failed",
        "workflowFileCount": len(workflow_files),
        "externalActionCount": len([entry for entry in all_entries if not entry["local"]]),
        "localActionCount": len([entry for entry in all_entries if entry["local"]]),
        "failures": all_failures,
        "actions": all_entries
    }

    output_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"GitHub Actions pinning report: {output_path}")
    print(f"Status: {report['status']}")

    if all_failures:
        for failure in all_failures:
            print(f"::error::{failure}")
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
