# Redis Current State Assessment

Status: repository-side assessment complete; managed-provider HA evidence is not present in the repository.

This assessment was produced from the current PropertyApi repository. It does not claim production-provider topology, replica count, provider SLA, failover duration, or monitoring evidence because those values are not available in source control.

## Redis Dependency Inventory

| Feature | Code location | Redis data structures | Criticality | Source of truth | Current failure behavior | Target behavior |
| --- | --- | --- | --- | --- | --- | --- |
| Distributed rate limiting | `PropertyApi/Security/RateLimiting/*` | fixed-window counters via `INCR` and `PEXPIRE` | security-sensitive | Redis counter per policy/window | returns `503` when Redis or multiplexer is unavailable | high-risk auth/OTP endpoints fail closed or use a bounded emergency limiter; no unlimited attempts |
| Output cache | `PropertyApi/Configuration/OutputCacheRegistration.cs` | ASP.NET output-cache entries | availability optimization | PostgreSQL/API handlers | Redis is required in Staging/Production and readiness depends on Redis | fail open by bypassing cache; underlying read endpoints continue when PostgreSQL succeeds |
| Distributed cache | `PropertyApi.Infrastructure/Caching/*` | serialized JSON values in `IDistributedCache` | mixed | PostgreSQL/application services | Redis required when `Redis:Required=true`; cache extension does not currently catch backend failures | rebuildable data falls back to authoritative source with bounded behavior |
| Security-stamp cache | `PropertyApi.Infrastructure/Identity/Services/CachedSecurityStampValidator.cs` | `security-stamp:{userId}` JSON snapshot | security-sensitive cache | ASP.NET Identity/PostgreSQL | cache read/write failures are caught; validator falls back to DB | keep DB-authoritative fallback; cache failure must not authenticate stale tokens |
| Security-stamp invalidation | `PropertyApi.Infrastructure/Identity/Services/DistributedSecurityStampCacheInvalidator.cs` | removal of security-stamp cache key | security-sensitive cache invalidation | PostgreSQL security stamp update | remove failures currently propagate from invalidator | mutation workflows must still update DB; cache invalidation failure must be observable and must not weaken token invalidation |
| SignalR backplane | `PropertyApi/Program.cs` | Redis Pub/Sub used by SignalR backplane | realtime availability | no durable message store | startup requires configured Redis when `SignalR:Provider=Redis` | HTTP API remains available; cross-instance realtime delivery may degrade and recover |
| Redis health/readiness | `PropertyApi.Infrastructure/Health/DistributedCacheHealthCheck.cs`, `PropertyApi/Health/HealthEndpointExtensions.cs` | temporary health key | operational dependency | n/a | `/health/ready` returns unhealthy when distributed cache fails | liveness remains healthy; readiness must expose degraded Redis state without causing avoidable total API outage |
| Data Protection | `PropertyApi.Infrastructure/DependencyInjection.cs`, PostgreSQL context | no Redis usage found | security-critical | PostgreSQL `DataProtectionKeys` | not Redis-dependent | keep Redis out of Data Protection key durability |
| Refresh tokens | `PropertyApi.Infrastructure/Auth/Repositories/RefreshTokenRepository.cs`, `PropertyApi.Infrastructure/Identity/Services/RefreshTokenStore.cs` | no Redis usage found | security-critical durable state | PostgreSQL | Redis outage does not remove authoritative refresh-token state | reuse detection remains PostgreSQL-authoritative |
| OTP codes/challenges | `PropertyApi.Infrastructure/Auth/Repositories/OtpCodeRepository.cs`, `PhoneOtpChallenge` EF config | no Redis usage found | security-critical durable state | PostgreSQL | Redis outage does not remove authoritative OTP state | OTP reuse and attempt state remain PostgreSQL-authoritative |

## Current Timeout and Reconnect Behavior

Rate limiting centralizes some StackExchange.Redis options in `RedisRateLimitingServiceCollectionExtensions`:

- `AbortOnConnectFail=false`
- `ConnectRetry=3`
- `ConnectTimeout=5000`
- `SyncTimeout=5000`

The generic `IDistributedCache`, output cache, and SignalR registrations currently pass string configuration directly to framework integrations. Their final timeout/reconnect behavior depends on the connection string and library defaults unless the connection string includes explicit options.

## Current Global Readiness Coupling

`DistributedCacheHealthCheck` is tagged with `ready`, so any distributed-cache Redis failure can make `/health/ready` fail. This proves dependency detection but does not prove continuity, failover, or feature-specific degradation.

## Key Risk

Redis is a shared failure point across rate limiting, output cache, distributed cache, security-stamp caching, and optional SignalR backplane. A standalone Redis outage or managed failover can affect unrelated capabilities at the same time unless runtime policies, topology, and the production gate explicitly isolate and validate behavior.
