# Supply Chain Runbook

## Normal Validation

Run:

```bash
dotnet restore HudhudNestApi.sln --locked-mode
dotnet format HudhudNestApi.sln --verify-no-changes --no-restore
dotnet build HudhudNestApi.sln --configuration Release --no-restore
pwsh ./scripts/validate-package-governance.ps1 -RepositoryRoot . -SolutionPath HudhudNestApi.sln
python scripts/validate-github-actions-pinning.py --repository-root .
```

CI runs the same checks and uploads sanitized reports under `artifacts/supply-chain`.

## Package Updates

1. Edit `Directory.Packages.props`.
2. Regenerate lock files with `dotnet restore HudhudNestApi.sln --use-lock-file`.
3. Confirm locked restore with `dotnet restore HudhudNestApi.sln --locked-mode`.
4. Run tests and vulnerability scan.
5. Keep project-level `Version` attributes out of `.csproj` files.

## Vulnerabilities

NuGet vulnerabilities are checked by `ci/check-vulnerable-packages.ps1`.

Container vulnerabilities are checked by Trivy in the production gate:

- `CRITICAL`: blocks release;
- `HIGH`: blocks release unless explicitly risk-accepted;
- `MEDIUM`, `LOW`, `UNKNOWN`: reported and tracked.

Exceptions must be specific. Wildcard severity suppression is not allowed.

## GitHub Actions

All external actions are pinned to full commit SHAs with a readable version comment. Run:

```bash
python scripts/validate-github-actions-pinning.py --repository-root .
```

Do not replace a pinned SHA with a mutable tag during updates.

## Container Build And Promotion

The production gate builds the API image once with:

```text
hudhudnest-api:${GITHUB_SHA}
```

It captures the immutable `sha256:` image id and uses that same image id for:

- container SBOM;
- container vulnerability scan;
- release security manifest;
- local production-gate runtime checks.

A future registry deployment must promote the same digest that was scanned.

## SBOM

The production gate emits:

- `artifacts/supply-chain/hudhudnest-dotnet.cdx.json`;
- `artifacts/supply-chain/hudhudnest-container.spdx.json`;
- `artifacts/supply-chain/sbom-metadata.json`;
- `artifacts/supply-chain/sbom-vulnerability-report.json`.

Empty or invalid SBOM files fail manifest validation.

## Coverage

Coverage is enforced by:

- `ci/check-coverage-baseline.ps1`;
- `scripts/validate-coverage-thresholds.py`;
- `ci/coverage-thresholds.json`.

Changed-code coverage is enforced for pull requests when executable production lines are detected.

## Incident Response

For compromised dependencies or Actions:

1. Disable deployment from affected branches.
2. Rotate any exposed credentials if compromise scope is unknown.
3. Pin to a known-good package/action version.
4. Regenerate lock files and security artifacts.
5. Rebuild and rescan the image.
6. Publish a release manifest for the remediated commit.

## Rollback

Rollback must use a previously built image digest with known SBOM and scan artifacts. Do not rebuild an old commit and assume it is equivalent to the original release artifact.
