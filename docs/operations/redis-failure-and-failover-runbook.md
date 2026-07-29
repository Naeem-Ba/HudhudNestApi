# Redis Failure and Failover Runbook

## Safety Rules

- Do not run Redis failover drills against Production without explicit approval, rollback, and change window.
- Run the initial full drill in isolated Staging with production-equivalent topology.
- Do not connect Staging tests to Production Redis.
- Do not record Redis credentials, full connection strings, OTP values, tokens, or PII in reports.

## Preflight

1. Confirm environment is `Staging`.
2. Confirm API has at least two healthy replicas.
3. Confirm Redis has one primary and at least one replica.
4. Confirm automatic failover is enabled.
5. Confirm TLS and network restrictions are enabled.
6. Confirm dashboards and alerts are active.
7. Confirm rollback path.
8. Generate isolated test users and data.

## Active Traffic

Run controlled traffic for:

- `/health/live`
- `/health/ready`
- public property reads.
- authenticated reads.
- output-cached endpoints.
- login.
- OTP request and verification with safe test mechanism.
- refresh token rotation.
- SignalR connection and cross-instance messaging when configured.

## Failover Action

Use only the provider-supported failover operation. Capture sanitized identifiers for:

- old primary.
- promoted replica.
- failover initiation timestamp.
- promotion timestamp.
- first successful Redis command after failover.
- full feature recovery timestamp.

Do not merely restart a standalone Redis container.

## Required Checks During Failover

- API process remains alive.
- `/health/live` remains healthy.
- core PostgreSQL-backed endpoints continue according to policy.
- output cache bypasses Redis safely.
- rate limiting does not become unrestricted.
- high-risk auth/OTP operations follow documented fail-closed or emergency-limiter policy.
- SignalR behavior matches the documented degradation model.
- no secrets appear in logs.

## Required Checks After Failover

- new primary is active.
- replicas are synchronized.
- app reconnects without restart.
- cache operations resume.
- rate limiting resumes.
- SignalR backplane resumes where configured.
- OTP cannot be reused.
- revoked/rotated refresh tokens remain invalid.
- alerts fire and resolve.

## Evidence

Generate:

- `artifacts/redis-failover/redis-failover-report.json`
- `artifacts/redis-failover/redis-failover-junit.xml`
- `artifacts/redis-failover/redis-failover-summary.md`
