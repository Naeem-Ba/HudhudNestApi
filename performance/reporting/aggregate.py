#!/usr/bin/env python3
import csv
import json
import os
import pathlib
import shutil
import sys
from datetime import datetime, timezone
from xml.sax.saxutils import escape


def read_json(path, fallback=None):
    path = pathlib.Path(path)
    if not path.exists():
        return fallback
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/performance")
    budgets_path = pathlib.Path("performance/performance-budgets.json")
    budgets = read_json(budgets_path, {})
    scenarios = [read_json(path) for path in sorted((root / "k6").glob("*.json"))]
    scenarios = [item for item in scenarios if item]
    integrity = [read_json(path) for path in sorted((root / "concurrency").glob("*-integrity.json"))]
    integrity = [item for item in integrity if item]
    dataset = read_json(root / "dataset-manifest.json", {})
    environment = read_json(root / "environment.json", {})
    database = read_json(root / "database-results.json", {})
    query_analysis = read_json(root / "query-plan-analysis.json", {})
    pg_stats = read_json(root / "postgresql" / "pg-stat-statements.json", [])
    comparison = read_json(root / "baseline-comparison.json")

    failures = []
    expected_scenarios = int(os.environ.get("PERF_EXPECTED_SCENARIO_REPORTS", "17"))
    if len(scenarios) < expected_scenarios:
        failures.append(f"Expected at least {expected_scenarios} scenario reports; found {len(scenarios)}.")
    failures.extend(
        f'{scenario.get("scenario", "unknown")}: threshold failure'
        for scenario in scenarios if not scenario.get("thresholdsPassed", False))
    if not integrity:
        failures.append("No authoritative authentication-race integrity reports were captured.")
    failures.extend(
        f'{item.get("race_kind", "unknown")}/{item.get("concurrency", "unknown")}: database invariant failed'
        for item in integrity if not item.get("invariant_passed", False))
    if not query_analysis or query_analysis.get("result") != "passed":
        failures.append("Query-plan regression analysis did not pass.")
    if not pg_stats:
        failures.append("pg_stat_statements produced no workload evidence.")
    if int(environment.get("observedInstanceCount", 0)) < int(environment.get("expectedInstanceCount", 2)):
        failures.append("Traffic did not reach every required API instance.")
    if int(dataset.get("propertyCount", 0)) < int(budgets.get("environment", {}).get("minimumDatasetSize", 10000)):
        failures.append("The generated property dataset is below the minimum budget.")
    if os.environ.get("PERF_REQUIRE_APPROVED_BUDGETS", "false").lower() == "true" and not budgets.get("approved", False):
        failures.append("Performance budgets are not approved from measured comparable runs.")
    if os.environ.get("PERF_REQUIRE_BASELINE", "false").lower() == "true":
        if not comparison or comparison.get("result") != "passed":
            failures.append("Relative approved-baseline comparison did not pass.")

    started_values = [item.get("startedAtUtc") for item in scenarios if item.get("startedAtUtc")]
    summary = {
        "runId": os.environ.get("PERF_RUN_ID", environment.get("runId")),
        "commitSha": os.environ.get("PERF_COMMIT_SHA", environment.get("commitSha")),
        "environment": os.environ.get("PERF_ENVIRONMENT", environment.get("environment")),
        "apiInstanceCount": environment.get("observedInstanceCount", 0),
        "datasetSize": dataset.get("propertyCount", 0),
        "datasetManifestHash": dataset.get("manifestHash"),
        "startedAtUtc": min(started_values) if started_values else environment.get("startedAtUtc"),
        "completedAtUtc": datetime.now(timezone.utc).isoformat(),
        "scenarios": scenarios,
        "concurrencyInvariants": {
            "refreshRotationPassed": all(i.get("invariant_passed") for i in integrity if i.get("race_kind") == "refresh") and any(i.get("race_kind") == "refresh" for i in integrity),
            "otpSingleUsePassed": all(i.get("invariant_passed") for i in integrity if i.get("race_kind") in ("otp", "otp-invalid")) and any(i.get("race_kind") == "otp" for i in integrity),
            "registrationRacePassed": all(i.get("invariant_passed") for i in integrity if i.get("race_kind") == "registration") and any(i.get("race_kind") == "registration" for i in integrity),
            "rateLimitingPassed": all(
                i.get("thresholdsPassed") for i in scenarios
                if str(i.get("scenario", "")).startswith("rate-limit-"))
                and sum(1 for i in scenarios if str(i.get("scenario", "")).startswith("rate-limit-")) == 7
        },
        "database": {
            "queryPlansCaptured": bool(query_analysis.get("plans")),
            "pgStatStatementsCaptured": bool(pg_stats),
            "slowQueryCount": sum(1 for query in pg_stats if float(query.get("mean_execution_time_ms", 0)) > 100),
            "sequentialScanWarnings": sum(plan.get("largeSequentialScanCount", 0) for plan in query_analysis.get("plans", [])),
            "diagnostics": database
        },
        "queryPlans": query_analysis.get("plans", []),
        "budgetApproval": budgets.get("approval"),
        "baselineComparison": comparison,
        "failures": failures,
        "result": "passed" if not failures else "failed"
    }

    root.mkdir(parents=True, exist_ok=True)
    (root / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    shutil.copyfile(budgets_path, root / "thresholds.json")

    with (root / "endpoint-results.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(["scenario", "virtual_users", "duration", "requests", "rps", "p50_ms", "p95_ms", "p99_ms", "error_rate", "passed"])
        for item in scenarios:
            writer.writerow([item.get("scenario"), item.get("virtualUsers"), item.get("duration"), item.get("requestCount"), item.get("requestsPerSecond"), item.get("p50Milliseconds"), item.get("p95Milliseconds"), item.get("p99Milliseconds"), item.get("errorRate"), item.get("thresholdsPassed")])

    markdown = [
        "# Concurrent performance and query-analysis report", "",
        f'- Run: `{summary["runId"]}`',
        f'- Commit: `{summary["commitSha"]}`',
        f'- API instances: {summary["apiInstanceCount"]}',
        f'- Dataset: {summary["datasetSize"]} properties',
        f'- Result: **{summary["result"].title()}**', "",
        "| Scenario | VUs | Requests | RPS | p50 ms | p95 ms | p99 ms | Error rate | Result |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|"
    ]
    for item in scenarios:
        markdown.append(f'| {item.get("scenario")} | {item.get("virtualUsers")} | {item.get("requestCount")} | {float(item.get("requestsPerSecond", 0)):.2f} | {float(item.get("p50Milliseconds", 0)):.2f} | {float(item.get("p95Milliseconds", 0)):.2f} | {float(item.get("p99Milliseconds", 0)):.2f} | {float(item.get("errorRate", 0)):.4f} | {"Passed" if item.get("thresholdsPassed") else "Failed"} |')
    if failures:
        markdown.extend(["", "## Failures", ""] + [f"- {failure}" for failure in failures])
    (root / "summary.md").write_text("\n".join(markdown) + "\n", encoding="utf-8")

    testcases = []
    for item in scenarios:
        failure = "" if item.get("thresholdsPassed") else '<failure message="Performance threshold failed" />'
        testcases.append(f'<testcase classname="PropertyApi.Performance" name="{escape(str(item.get("scenario")))}">{failure}</testcase>')
    for item in integrity:
        name = f'{item.get("race_kind")}-{item.get("concurrency")}-database-integrity'
        failure = "" if item.get("invariant_passed") else '<failure message="Database concurrency invariant failed" />'
        testcases.append(f'<testcase classname="PropertyApi.Concurrency" name="{escape(name)}">{failure}</testcase>')
    junit = f'<?xml version="1.0" encoding="UTF-8"?>\n<testsuite name="PropertyApi.PerformanceGate" tests="{len(testcases)}" failures="{len(failures)}" skipped="0">' + "".join(testcases) + "</testsuite>\n"
    (root / "junit.xml").write_text(junit, encoding="utf-8")
    print(json.dumps({"result": summary["result"], "failureCount": len(failures)}, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
