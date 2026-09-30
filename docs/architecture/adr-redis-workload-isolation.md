# ADR: Redis Workload Isolation

Status: proposed; provider implementation pending.

## Context

HudhudNestApi uses Redis for unrelated workloads: rate limiting, output cache, distributed cache, security-stamp cache acceleration, and optional SignalR backplane. A shared Redis outage or saturation event can affect security controls and availability at the same time.

## Decision

Use separate failure policies and separate configuration identities for Redis workloads. For production HA, prefer separate managed Redis services or equivalent provider isolation for:

1. security-sensitive rate limiting.
2. security/cache acceleration and general distributed cache.
3. output cache.
4. SignalR backplane when enabled.

If cost requires initial consolidation, use separate key prefixes, explicit timeouts, separate metrics, and documented blast-radius acceptance. Logical Redis database numbers are not considered strong failure-domain isolation.

## Alternatives Considered

| Alternative | Result | Reason |
| --- | --- | --- |
| one shared Redis for all workloads | rejected for mature production | cache and Pub/Sub pressure can affect rate limiting and security cache behavior |
| logical Redis databases only | rejected as isolation claim | does not isolate CPU, memory, connection, failover, or network saturation |
| separate managed services by workload | preferred target | stronger failure isolation and clearer capacity planning |
| replace SignalR Redis with managed SignalR service | possible future option | reduces Redis Pub/Sub pressure but requires platform-specific implementation |

## Impact

- cost increases if separate managed services are used.
- operational clarity improves.
- security-sensitive rate limiting is less likely to be starved by cache or Pub/Sub traffic.
- migration can be incremental by introducing workload-specific connection variables.

## Migration Plan

1. keep current shared configuration for non-production and local runs.
2. add production validation requiring explicit HA evidence.
3. introduce workload-specific Redis connection variables.
4. move rate limiting to the highest-priority Redis workload.
5. move output cache and SignalR to separate workloads where provider/cost allows.
