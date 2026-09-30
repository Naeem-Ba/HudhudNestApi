# Alert response runbook

## HudhudNestApiHigh5xxRate

Severity page. Check the 5xx panel, deployment version, error traces, database
and Redis health. Roll back or disable the failing dependency path if safe.
Escalate when sustained for ten minutes; recovery requires the ratio below 5%
for two evaluation periods and readiness healthy.

## HudhudNestApiHighP95Latency

Severity ticket. Check p95/p99 by route and dependency span metrics. Mitigate
pool saturation, slow queries, Redis/network failures, or regressions. Escalate
if the latency budget continues burning; confirm recovery below one second.

## HudhudNestApiAuthenticationFailures

Severity ticket. Separate bounded invalid-client failures from internal errors
and rate limiting. Never inspect credentials or OTP content. Escalate internal
errors immediately; recovery requires the bounded failure rate to normalize.

## HudhudNestApiDependencyFailures

Check child spans by `db.system.name` or client dependency, health checks, pool
metrics, and provider status. Apply circuit breaking/failover where supported.
Confirm child span error rate and readiness recover.

Every page requires timeline, impact, mitigation, evidence, and follow-up owner.
