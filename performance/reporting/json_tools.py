#!/usr/bin/env python3
import json
import pathlib
import sys


def load(path):
    return json.loads(pathlib.Path(path).read_text(encoding="utf-8-sig"))


def main():
    command, input_path = sys.argv[1], sys.argv[2]
    data = load(input_path)
    if command == "validate-array":
        return 0 if isinstance(data, list) else 1
    if command == "validate-object":
        return 0 if isinstance(data, dict) else 1
    if command == "assert-invariant":
        return 0 if isinstance(data, dict) and data.get("invariant_passed") is True else 1
    if command == "slow-markdown":
        output_path = pathlib.Path(sys.argv[3])
        lines = [
            "# PostgreSQL slow-query summary", "",
            "| Query ID | Calls | Total ms | Mean ms | Reads | Temp blocks |",
            "|---|---:|---:|---:|---:|---:|",
        ]
        for query in data[:20]:
            temp = (query.get("temp_blks_read") or 0) + (query.get("temp_blks_written") or 0)
            lines.append(
                f'| {query.get("query_id")} | {query.get("calls")} | '
                f'{query.get("total_execution_time_ms")} | {query.get("mean_execution_time_ms")} | '
                f'{query.get("shared_blks_read")} | {temp} |')
        output_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        return 0
    raise SystemExit(f"Unknown command: {command}")


if __name__ == "__main__":
    raise SystemExit(main())
