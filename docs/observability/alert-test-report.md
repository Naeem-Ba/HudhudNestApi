# Alert test report

The controlled test is implemented in `scripts/verify-observability.sh`.
It provisions `PropertyApiControlledObservabilityTest` with `vector(1) > 0`, reloads
Prometheus, verifies `firing`, changes the expression to `vector(0) > 0`, reloads,
and verifies the alert is no longer firing. The temporary rule is removed.

No successful lifecycle is claimed in this document until the generated
`artifacts/observability/observability-verification-report.json` has both
`firingTestPassed` and `recoveryTestPassed` equal to true. Production alert
notification routing must be tested separately without secrets in annotations.

## Local equivalent run — 2026-07-28

`PropertyApiControlledObservabilityTest` was loaded by Prometheus, observed in
`firing` state with its runbook annotation, changed to a false expression, and
then confirmed absent from the firing set. The machine report records both
checks as true. This proves local rule evaluation and recovery, not Production
notification delivery.
