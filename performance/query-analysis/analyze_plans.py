#!/usr/bin/env python3
import json
import pathlib
import sys


BLOCKING_NODES = {
    "Aggregate", "Gather Merge", "GroupAggregate", "Hash", "HashAggregate",
    "Incremental Sort", "Materialize", "Sort", "WindowAgg",
}


def walk(node, limited_early=False):
    yield node, limited_early
    child_limited_early = limited_early or node.get("Node Type") == "Limit"
    if node.get("Node Type") in BLOCKING_NODES:
        child_limited_early = False
    for child in node.get("Plans", []):
        yield from walk(child, child_limited_early)


def main():
    if len(sys.argv) != 4:
        raise SystemExit("usage: analyze_plans.py <plan-directory> <budgets.json> <output.json>")

    plan_dir = pathlib.Path(sys.argv[1])
    budgets = json.loads(pathlib.Path(sys.argv[2]).read_text(encoding="utf-8"))["queryPlans"]
    output_path = pathlib.Path(sys.argv[3])
    results = []
    failures = []

    for path in sorted(plan_dir.glob("*.json")):
        raw = json.loads(path.read_text(encoding="utf-8"))
        envelope = raw[0] if isinstance(raw, list) else raw
        root = envelope["Plan"]
        annotated_nodes = list(walk(root))
        nodes = [node for node, _ in annotated_nodes]
        node_types = sorted({node.get("Node Type", "") for node in nodes})
        indexes = sorted({node["Index Name"] for node in nodes if node.get("Index Name")})
        sequential_scans = [node for node in nodes if node.get("Node Type") == "Seq Scan"]
        large_sequential_scans = [
            node for node in sequential_scans
            if node.get("Actual Rows", 0) * node.get("Actual Loops", 1)
            >= budgets["maximumSequentialScanRows"]
        ]
        temp_blocks = sum(
            node.get("Temp Read Blocks", 0) + node.get("Temp Written Blocks", 0)
            for node in nodes)
        shared_reads = sum(node.get("Shared Read Blocks", 0) for node in nodes)
        shared_hits = sum(node.get("Shared Hit Blocks", 0) for node in nodes)
        sort_methods = sorted({node["Sort Method"] for node in nodes if node.get("Sort Method")})
        estimate_errors = []
        estimate_node_types = {
            "Bitmap Heap Scan", "Bitmap Index Scan", "Hash Join", "Index Only Scan",
            "Index Scan", "Merge Join", "Nested Loop", "Seq Scan",
        }
        for node, limited_early in annotated_nodes:
            # A scan below a non-blocking LIMIT is intentionally stopped after enough rows.
            # Comparing its full-table estimate with the partial actual row count is not a
            # cardinality error. Blocking nodes (sort/hash/aggregate) reset this exemption.
            if limited_early or node.get("Node Type") not in estimate_node_types:
                continue
            estimated = node.get("Plan Rows", 0)
            actual = node.get("Actual Rows", 0)
            if estimated > 0 and actual > 0:
                estimate_errors.append(max(actual / estimated, estimated / actual))
        maximum_estimate_error = max(estimate_errors, default=1)
        spatial_required = "geo" in path.stem
        spatial_index_used = any("GeoLocation" in index for index in indexes)

        issues = []
        if large_sequential_scans:
            issues.append("large-table-sequential-scan")
        if temp_blocks > budgets["maximumTemporaryBlocks"]:
            issues.append("temporary-block-usage")
        if maximum_estimate_error > budgets["maximumEstimateErrorRatio"]:
            issues.append("cardinality-estimate-error")
        if spatial_required and budgets["requireSpatialIndex"] and not spatial_index_used:
            issues.append("spatial-index-not-used")

        result = {
            "planFile": path.name,
            "planningTimeMilliseconds": envelope.get("Planning Time", 0),
            "executionTimeMilliseconds": envelope.get("Execution Time", 0),
            "actualRows": root.get("Actual Rows", 0),
            "nodeTypes": node_types,
            "indexes": indexes,
            "sequentialScanCount": len(sequential_scans),
            "largeSequentialScanCount": len(large_sequential_scans),
            "sharedBufferHits": shared_hits,
            "sharedBufferReads": shared_reads,
            "temporaryBlocks": temp_blocks,
            "sortMethods": sort_methods,
            "maximumEstimateErrorRatio": round(maximum_estimate_error, 3),
            "spatialIndexRequired": spatial_required,
            "spatialIndexUsed": spatial_index_used,
            "issues": issues,
            "passed": not issues,
        }
        results.append(result)
        failures.extend(f"{path.name}: {issue}" for issue in issues)

    if not results:
        failures.append("No query-plan JSON files were found.")

    output = {"plans": results, "failures": failures, "result": "passed" if not failures else "failed"}
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(output, indent=2), encoding="utf-8")
    print(json.dumps(output, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
