# Redis Production Readiness Decision

Decision: FAIL

## Reason

The repository now contains Redis dependency inventory, failure policies, HA validation scripts, managed failover gates, operational runbooks, dashboard definitions, alert definitions, and CI guards.

Production Redis resilience cannot be marked as passed because the repository does not contain verified managed-provider evidence for:

- Redis HA service tier.
- at least one replica.
- automatic failover.
- provider SLA.
- monitoring and alert provisioning.
- real Staging primary-to-replica failover under active traffic.
- application reconnect without restart.
- post-failover OTP, refresh-token, rate-limiting, and SignalR verification.

## Required Evidence To Pass

Provide sanitized evidence through CI secrets or generated artifacts:

- `REDIS_HA_EVIDENCE_JSON`
- `REDIS_PROVIDER_FAILOVER_COMMAND`
- `artifacts/redis-failover/redis-failover-report.json`
- `artifacts/redis-failover/redis-failover-junit.xml`
- `artifacts/redis-failover/redis-failover-summary.md`

The failover report must prove:

- environment is Staging.
- one primary exists.
- at least one replica exists.
- automatic failover is enabled.
- replica promotion occurred.
- client recovery occurred.
- application restart was not required.
- request success rate met the configured threshold.
- output cache, rate limiting, authentication, and SignalR behavior matched the policy.
- security invariants were preserved.
- alerts fired and resolved.

## Production Gate Behavior

The production gate blocks release when Redis HA evidence or managed failover evidence is missing, failed, skipped, or incomplete.

This is intentional. A standalone Redis restart or readiness-only test is not sufficient Redis resilience evidence.
