#!/usr/bin/env python3
import json
import pathlib
import sys


def main():
    if len(sys.argv) != 5:
        raise SystemExit("usage: compare.py <current-summary> <approved-baseline> <budgets> <output>")
    current = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
    baseline = json.loads(pathlib.Path(sys.argv[2]).read_text(encoding="utf-8"))
    limits = json.loads(pathlib.Path(sys.argv[3]).read_text(encoding="utf-8"))["relativeRegression"]
    failures, comparisons = [], []

    for key in ("datasetManifestHash", "apiInstanceCount"):
        if current.get(key) != baseline.get(key):
            failures.append(f"Non-comparable environment metadata: {key} differs.")

    baseline_scenarios = {item["scenario"]: item for item in baseline.get("scenarios", [])}
    for item in current.get("scenarios", []):
        previous = baseline_scenarios.get(item["scenario"])
        if not previous:
            failures.append(f'Missing approved baseline scenario: {item["scenario"]}')
            continue
        metrics = {}
        for metric, allowed, lower_is_better in (
            ("p95Milliseconds", limits["p95Percent"], True),
            ("p99Milliseconds", limits["p99Percent"], True),
            ("requestsPerSecond", limits["throughputReductionPercent"], False),
        ):
            old = float(previous.get(metric, 0))
            new = float(item.get(metric, 0))
            if old <= 0:
                failures.append(f'{item["scenario"]}: approved {metric} is not positive.')
                continue
            change = ((new - old) / old) * 100
            regression = change if lower_is_better else -change
            passed = regression <= allowed
            metrics[metric] = {"baseline": old, "current": new, "regressionPercent": round(regression, 3), "allowedPercent": allowed, "passed": passed}
            if not passed:
                failures.append(f'{item["scenario"]}: {metric} regression {regression:.2f}% exceeds {allowed}%.')
        comparisons.append({"scenario": item["scenario"], "metrics": metrics})

    baseline_plans = {item["planFile"]: item for item in baseline.get("queryPlans", [])}
    plan_comparisons = []
    for plan in current.get("queryPlans", []):
        previous = baseline_plans.get(plan["planFile"])
        if not previous:
            failures.append(f'Missing approved query-plan baseline: {plan["planFile"]}')
            continue
        old_time = float(previous.get("executionTimeMilliseconds", 0))
        new_time = float(plan.get("executionTimeMilliseconds", 0))
        old_reads = float(previous.get("sharedBufferReads", 0))
        new_reads = float(plan.get("sharedBufferReads", 0))
        time_regression = ((new_time - old_time) / old_time * 100) if old_time > 0 else 0
        read_regression = ((new_reads - old_reads) / old_reads * 100) if old_reads > 0 else (100 if new_reads > 0 else 0)
        new_node_types = sorted(set(plan.get("nodeTypes", [])) - set(previous.get("nodeTypes", [])))
        passed = (
            time_regression <= limits["databaseTimePercent"]
            and read_regression <= limits["bufferReadPercent"]
            and plan.get("largeSequentialScanCount", 0) <= previous.get("largeSequentialScanCount", 0)
            and plan.get("temporaryBlocks", 0) <= previous.get("temporaryBlocks", 0)
            and (not plan.get("spatialIndexRequired") or plan.get("spatialIndexUsed"))
        )
        plan_comparisons.append({
            "planFile": plan["planFile"],
            "executionTimeRegressionPercent": round(time_regression, 3),
            "bufferReadRegressionPercent": round(read_regression, 3),
            "newNodeTypes": new_node_types,
            "passed": passed,
        })
        if not passed:
            failures.append(f'{plan["planFile"]}: meaningful query-plan regression detected.')

    result = {"comparisons": comparisons, "queryPlanComparisons": plan_comparisons, "failures": failures, "result": "passed" if not failures else "failed"}
    pathlib.Path(sys.argv[4]).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
