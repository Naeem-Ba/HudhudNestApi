# GitHub Actions Pinning

All external `uses:` references must be pinned to a full 40-character commit SHA. A human-readable version comment is required next to each pinned action.

## Pinned Actions

| Action | Previous Ref | Pinned SHA | Release Comment | Verification |
| --- | --- | --- | --- | --- |
| `actions/checkout` | `v5` | `fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09` | `# v5` | `git ls-remote https://github.com/actions/checkout.git refs/tags/v5` |
| `actions/setup-dotnet` | `v5` | `26b0ec14cb23fa6904739307f278c14f94c95bf1` | `# v5` | `git ls-remote https://github.com/actions/setup-dotnet.git refs/tags/v5` |
| `actions/cache` | `v4` | `0057852bfaa89a56745cba8c7296529d2fc39830` | `# v4` | `git ls-remote https://github.com/actions/cache.git refs/tags/v4` |
| `actions/upload-artifact` | `v6` | `b7c566a772e6b6bfb58ed0dc250532a479d7789f` | `# v6` | `git ls-remote https://github.com/actions/upload-artifact.git refs/tags/v6` |
| `gitleaks/gitleaks-action` | `v2` | `dcedce43c6f43de0b836d1fe38946645c9c638dc` | `# v2` | `git ls-remote https://github.com/gitleaks/gitleaks-action.git refs/tags/v2` |
| `aquasecurity/trivy-action` | new | `ed142fd0673e97e23eac54620cfb913e5ce36c25` | `# v0.36.0` | `git ls-remote --tags https://github.com/aquasecurity/trivy-action.git` peeled tag `v0.36.0` |

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
