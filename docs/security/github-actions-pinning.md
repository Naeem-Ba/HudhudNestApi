# GitHub Actions Pinning

All external `uses:` references must be pinned to a full 40-character commit SHA. A human-readable version comment is required next to each pinned action.

## Pinned Actions

| Action | Previous Ref | Pinned SHA | Release Comment | Verification |
| --- | --- | --- | --- | --- |
| `actions/checkout` | `v7` | `3d3c42e5aac5ba805825da76410c181273ba90b1` | `# v7.0.1` | `git ls-remote https://github.com/actions/checkout.git refs/tags/v7.0.1` |
| `actions/setup-dotnet` | `v6` | `a98b56852c35b8e3190ac28c8c2271da59106c68` | `# v6.0.0` | `git ls-remote https://github.com/actions/setup-dotnet.git refs/tags/v6.0.0` |
| `actions/cache` | `v6` | `55cc8345863c7cc4c66a329aec7e433d2d1c52a9` | `# v6.1.0` | `git ls-remote https://github.com/actions/cache.git refs/tags/v6.1.0` |
| `actions/upload-artifact` | `v7` | `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` | `# v7.0.1` | `git ls-remote https://github.com/actions/upload-artifact.git refs/tags/v7.0.1` |
| `gitleaks/gitleaks-action` | `v3` | `e0c47f4f8be36e29cdc102c57e68cb5cbf0e8d1e` | `# v3.0.0` | `git ls-remote https://github.com/gitleaks/gitleaks-action.git refs/tags/v3.0.0` |
| `aquasecurity/trivy-action` | — | `ed142fd0673e97e23eac54620cfb913e5ce36c25` | `# v0.36.0` | `git ls-remote --tags https://github.com/aquasecurity/trivy-action.git` peeled tag `v0.36.0` |

_Table last verified against actual `.github/workflows/*.yml` pins on 2026-09-18 (`docs/audit/` review). Previous versions of this table listed older SHAs (checkout@v5, setup-dotnet@v5, cache@v4, upload-artifact@v6, gitleaks-action@v2) that no longer matched the workflows — verify with `git ls-remote` before trusting this table again after any future Dependabot bump._

## Enforcement

`scripts/validate-github-actions-pinning.py` scans `.github/workflows` and `.github/actions`.

It fails when:

- an external action is not pinned to a full SHA;
- a branch, mutable tag, `main`, `master`, or `latest` is used;
- a pinned action lacks a readable version comment.

Local actions such as `./.github/actions/example` are allowed without external SHA pinning.

## Update Procedure

1. Let Dependabot open the GitHub Actions update PR.
2. Resolve the new tag to the full commit SHA with `git ls-remote`.
3. Replace only the SHA and update the version comment.
4. Run `python scripts/validate-github-actions-pinning.py --repository-root .`.
5. Review the upstream action release notes before merge.
