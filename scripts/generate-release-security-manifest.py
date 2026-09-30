#!/usr/bin/env python3
import argparse
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


def read_json(path: Path):
    if not path.exists() or path.stat().st_size == 0:
        raise FileNotFoundError(f"Required JSON artifact is missing or empty: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def git_commit(repository_root: Path) -> str:
    completed = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=repository_root,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        return ""
    return completed.stdout.strip()


def count_cyclonedx_components(sbom: dict) -> int:
    return len(sbom.get("components") or [])


def count_spdx_components(sbom: dict) -> int:
    return len(sbom.get("packages") or [])


def summarize_trivy_findings(scan: dict, exceptions: dict):
    exception_ids = {
        str(item.get("id") or item.get("vulnerabilityId") or "").upper()
        for item in exceptions.get("container", [])
    }

    counts = {
        "CRITICAL": 0,
        "HIGH": 0,
        "MEDIUM": 0,
        "LOW": 0,
        "UNKNOWN": 0,
    }
    blocking = []

    for result in scan.get("Results") or []:
        target = result.get("Target")
        for vulnerability in result.get("Vulnerabilities") or []:
            severity = str(vulnerability.get("Severity") or "UNKNOWN").upper()
            vulnerability_id = str(vulnerability.get("VulnerabilityID") or "").upper()
            counts[severity] = counts.get(severity, 0) + 1

            if severity in {"CRITICAL", "HIGH"} and vulnerability_id not in exception_ids:
                blocking.append({
                    "id": vulnerability_id,
                    "package": vulnerability.get("PkgName"),
                    "installedVersion": vulnerability.get("InstalledVersion"),
                    "fixedVersion": vulnerability.get("FixedVersion"),
                    "severity": severity,
                    "target": target,
                    "policy": "blocked"
                })

    return counts, blocking


def require_status(report: dict, path: Path, name: str, failures: list[str]):
    status = report.get("status")
    if status != "passed":
        failures.append(f"{name} did not pass. status={status}; artifact={path}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate and validate release supply-chain evidence manifest.")
    parser.add_argument("--repository-root", default=".")
    parser.add_argument("--output", default="artifacts/supply-chain/release-security-manifest.json")
    parser.add_argument("--sbom-metadata-output", default="artifacts/supply-chain/sbom-metadata.json")
    parser.add_argument("--summary-output", default="artifacts/supply-chain/release-security-manifest.md")
    parser.add_argument("--container-scan-summary-output", default="artifacts/supply-chain/container-scan-summary.md")
    parser.add_argument("--correlation-output", default="artifacts/supply-chain/sbom-vulnerability-report.json")
    parser.add_argument("--package-governance-report", default="artifacts/supply-chain/package-governance-report.json")
    parser.add_argument("--action-pinning-report", default="artifacts/supply-chain/action-pinning-report.json")
    parser.add_argument("--coverage-report", default="artifacts/coverage/coverage-threshold-summary.json")
    parser.add_argument("--container-scan-json", default="artifacts/supply-chain/container-scan.json")
    parser.add_argument("--dotnet-sbom", default="artifacts/supply-chain/hudhudnest-dotnet.cdx.json")
    parser.add_argument("--container-sbom", default="artifacts/supply-chain/hudhudnest-container.spdx.json")
    parser.add_argument("--vulnerability-exceptions", default="ci/vulnerability-exceptions.json")
    args = parser.parse_args()

    repository_root = Path(args.repository_root).resolve()
    output_path = repository_root / args.output
    sbom_metadata_path = repository_root / args.sbom_metadata_output
    summary_path = repository_root / args.summary_output
    container_scan_summary_path = repository_root / args.container_scan_summary_output
    correlation_path = repository_root / args.correlation_output
    output_path.parent.mkdir(parents=True, exist_ok=True)

    failures = []
    commit_sha = os.environ.get("GITHUB_SHA") or git_commit(repository_root)
    workflow_run_id = os.environ.get("GITHUB_RUN_ID", "")
    image_ref = os.environ.get("HUDHUDNEST_IMAGE_REF", "")
    image_digest = os.environ.get("HUDHUDNEST_IMAGE_DIGEST", "")

    if not commit_sha:
        failures.append("Commit SHA could not be determined.")

    if not image_ref:
        failures.append("HUDHUDNEST_IMAGE_REF is required.")

    if not image_digest.startswith("sha256:"):
        failures.append("HUDHUDNEST_IMAGE_DIGEST must be an immutable sha256 digest or local image ID.")

    artifact_paths = {
        "packageGovernance": repository_root / args.package_governance_report,
        "actionPinning": repository_root / args.action_pinning_report,
        "coverage": repository_root / args.coverage_report,
        "containerScan": repository_root / args.container_scan_json,
        "dotnetSbom": repository_root / args.dotnet_sbom,
        "containerSbom": repository_root / args.container_sbom,
        "vulnerabilityExceptions": repository_root / args.vulnerability_exceptions,
    }

    try:
        package_report = read_json(artifact_paths["packageGovernance"])
        require_status(package_report, artifact_paths["packageGovernance"], "Package governance", failures)
    except Exception as error:
        package_report = {}
        failures.append(str(error))

    try:
        action_report = read_json(artifact_paths["actionPinning"])
        require_status(action_report, artifact_paths["actionPinning"], "GitHub Actions pinning", failures)
    except Exception as error:
        action_report = {}
        failures.append(str(error))

    try:
        coverage_report = read_json(artifact_paths["coverage"])
        require_status(coverage_report, artifact_paths["coverage"], "Coverage threshold gate", failures)
    except Exception as error:
        coverage_report = {}
        failures.append(str(error))

    try:
        container_scan = read_json(artifact_paths["containerScan"])
    except Exception as error:
        container_scan = {}
        failures.append(str(error))

    try:
        dotnet_sbom = read_json(artifact_paths["dotnetSbom"])
    except Exception as error:
        dotnet_sbom = {}
        failures.append(str(error))

    try:
        container_sbom = read_json(artifact_paths["containerSbom"])
    except Exception as error:
        container_sbom = {}
        failures.append(str(error))

    try:
        exceptions = read_json(artifact_paths["vulnerabilityExceptions"])
    except Exception as error:
        exceptions = {"container": []}
        failures.append(str(error))

    finding_counts, blocking_findings = summarize_trivy_findings(container_scan, exceptions)
    if blocking_findings:
        failures.append(f"Container scan has {len(blocking_findings)} unaccepted CRITICAL/HIGH findings.")

    dotnet_component_count = count_cyclonedx_components(dotnet_sbom)
    container_component_count = count_spdx_components(container_sbom)

    if dotnet_component_count <= 0:
        failures.append("Application SBOM has no components.")

    if container_component_count <= 0:
        failures.append("Container SBOM has no packages.")

    sbom_metadata = {
        "schemaVersion": 1,
        "commitSha": commit_sha,
        "imageRef": image_ref,
        "imageDigest": image_digest,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "dotnetSbom": {
            "path": args.dotnet_sbom,
            "format": "CycloneDX JSON",
            "componentCount": dotnet_component_count
        },
        "containerSbom": {
            "path": args.container_sbom,
            "format": "SPDX JSON",
            "componentCount": container_component_count
        }
    }

    correlations = []
    for result in container_scan.get("Results") or []:
        target = result.get("Target")
        result_type = result.get("Type")
        for vulnerability in result.get("Vulnerabilities") or []:
            severity = str(vulnerability.get("Severity") or "UNKNOWN").upper()
            vulnerability_id = str(vulnerability.get("VulnerabilityID") or "").upper()
            correlations.append({
                "vulnerability": vulnerability_id,
                "component": vulnerability.get("PkgName"),
                "installedVersion": vulnerability.get("InstalledVersion"),
                "fixedVersion": vulnerability.get("FixedVersion"),
                "directOrTransitive": "container-or-runtime-component",
                "containerLayerOrApplicationPackage": result_type,
                "target": target,
                "severity": severity,
                "releasePolicy": "blocked" if severity in {"CRITICAL", "HIGH"} else "reported"
            })

    correlation_report = {
        "schemaVersion": 1,
        "commitSha": commit_sha,
        "imageRef": image_ref,
        "imageDigest": image_digest,
        "correlationCount": len(correlations),
        "correlations": correlations
    }

    manifest = {
        "schemaVersion": 1,
        "commitSha": commit_sha,
        "applicationVersion": os.environ.get("HUDHUDNEST_APPLICATION_VERSION", commit_sha[:12] if commit_sha else ""),
        "workflowRunId": workflow_run_id,
        "buildTimestampUtc": datetime.now(timezone.utc).isoformat(),
        "image": {
            "repository": "hudhudnest-api",
            "tag": os.environ.get("HUDHUDNEST_IMAGE_TAG", commit_sha),
            "ref": image_ref,
            "digest": image_digest,
            "promotionStrategy": "Build once in the production gate, scan the captured immutable image id, then run and promote that same image."
        },
        "sbom": sbom_metadata,
        "scans": {
            "container": args.container_scan_json,
            "sbomVulnerabilityCorrelation": args.correlation_output,
            "containerFindingCounts": finding_counts,
            "blockingFindings": blocking_findings,
            "nuget": "artifacts/vulnerability/vulnerable-packages.txt"
        },
        "coverage": {
            "linePercent": coverage_report.get("lineCoveragePercent"),
            "branchPercent": coverage_report.get("branchCoveragePercent"),
            "thresholdPassed": coverage_report.get("status") == "passed",
            "report": args.coverage_report
        },
        "packageGovernance": {
            "report": args.package_governance_report,
            "status": package_report.get("status")
        },
        "githubActionsPinned": action_report.get("status") == "passed",
        "artifacts": {name: str(path.relative_to(repository_root)).replace("\\", "/") for name, path in artifact_paths.items()},
        "result": "passed" if not failures else "failed",
        "failures": failures
    }

    sbom_metadata_path.write_text(json.dumps(sbom_metadata, indent=2), encoding="utf-8")
    correlation_path.write_text(json.dumps(correlation_report, indent=2), encoding="utf-8")
    output_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    summary_lines = [
        "# Release Security Manifest",
        "",
        f"Result: {manifest['result']}",
        f"Commit: {commit_sha}",
        f"Image: {image_ref}",
        f"Digest: {image_digest}",
        f"Application SBOM components: {dotnet_component_count}",
        f"Container SBOM packages: {container_component_count}",
        f"Critical findings: {finding_counts.get('CRITICAL', 0)}",
        f"High findings: {finding_counts.get('HIGH', 0)}",
    ]

    if failures:
        summary_lines += ["", "Failures:"]
        summary_lines += [f"- {failure}" for failure in failures]

    summary_path.write_text("\n".join(summary_lines) + "\n", encoding="utf-8")

    scan_summary_lines = [
        "# Container Scan Summary",
        "",
        f"Image: {image_ref}",
        f"Digest: {image_digest}",
        "",
        "| Severity | Count |",
        "| --- | ---: |",
    ]
    scan_summary_lines += [f"| {severity} | {finding_counts.get(severity, 0)} |" for severity in ["CRITICAL", "HIGH", "MEDIUM", "LOW", "UNKNOWN"]]
    scan_summary_lines += ["", f"Blocking findings: {len(blocking_findings)}"]
    container_scan_summary_path.write_text("\n".join(scan_summary_lines) + "\n", encoding="utf-8")

    print(f"Release security manifest: {output_path}")
    print(f"Result: {manifest['result']}")

    if failures:
        for failure in failures:
            print(f"::error::{failure}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
