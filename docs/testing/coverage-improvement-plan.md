# Coverage Improvement Plan

## Current Baseline

The local validation run after this change reported:

| Metric | Measured | Previous Minimum | New Current Minimum |
| --- | ---: | ---: | ---: |
| Line coverage | `6.59%` | `5%` | `6%` |
| Branch coverage | `12.5%` | `10%` | `12%` |
| Auth-sensitive line coverage | `27.28%` after generated migration correction | `12%` | `12%` |
| Auth-sensitive branch coverage | `16.91%` | not enforced | `15%` |

The Auth-sensitive denominator excludes EF generated migrations and designer files. The production gate must publish `artifacts/coverage/coverage-threshold-summary.json` before any release decision.

## Enforced Policy

`ci/coverage-thresholds.json` defines the staged gate.

Current stage:

- global line coverage: at least `6%`;
- global branch coverage: at least `12%`;
- Auth-sensitive line coverage: at least `12%`;
- Auth-sensitive branch coverage: at least `15%`;
- changed production code in pull requests: at least `70%` line coverage when executable changed lines are present.

## Next Stages

| Stage | Line | Branch | Auth Line | Entry Criteria |
| --- | ---: | ---: | ---: | --- |
| Stage 2 | `15%` | `15%` | `25%` | Add meaningful Auth, token rotation, OTP replay, authorization failure, and Properties workflow tests. |
| Stage 3 | `25%` | `20%` | `40%` | Add behavior tests for failure paths, transaction rollback, concurrency, and external dependency failures. |
| Stage 4 | `40%` | `30%` | `70%` | Critical authentication and authorization branches have dedicated coverage. |

## Rules

- Thresholds are monotonic.
- Lowering a threshold requires explicit approval, reason, expiry, and a tracking issue.
- Generated files, EF migrations, and designer files may be excluded from critical-module denominator.
- Handlers, token rotation, OTP, authorization, repositories, domain logic, security middleware, and error-handling logic must not be excluded to raise coverage.
- Tests must assert behavior, branches, and failure paths.
