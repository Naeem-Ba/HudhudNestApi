# Redis Security Controls

Status: required controls defined; provider evidence pending.

## Required Controls

- TLS must be enabled for Production Redis.
- certificate validation must not be disabled.
- Redis credentials must be stored only in secret management.
- full Redis connection strings must not be logged.
- public access must be disabled where possible.
- network access must be private or tightly allow-listed.
- ACLs must use least privilege.
- application credentials must not be able to trigger destructive provider failover operations.
- failover automation must use separate privileged credentials.
- dangerous administrative commands must be restricted from application credentials where provider supports ACLs.

## Secret Names

Expected secret or variable names only:

- `REDIS_CONNECTION_STRING`
- `REDIS_URL`
- `REDIS_HA_EVIDENCE_JSON`
- `REDIS_FAILOVER_REPORT_JSON`
- `REDIS_PROVIDER_API_TOKEN`
- `REDIS_PROVIDER_RESOURCE_ID`
- `STAGING_BASE_URL`

Do not commit values.

## Sensitive Data

Redis keys and values may include rate-limit partitions, security-stamp cache keys, SignalR Pub/Sub payloads, and cached API responses. Logs and telemetry must avoid connection strings, tokens, OTPs, passwords, phone numbers, emails, raw cache values, and high-cardinality user identifiers.
