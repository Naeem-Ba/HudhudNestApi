# Redis Failure Policy

Status: repository-side policy defined. Production readiness remains failed until managed HA topology and active-traffic failover evidence are attached to the production gate.

| Feature | Redis purpose | Security criticality | Availability criticality | Allowed degradation | Mode | Fallback | Maximum degradation duration | Client response | Recovery verification |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Output cache | cache public read responses | low | medium | bypass Redis and execute handler | fail open | PostgreSQL-backed handler | managed failover window plus circuit cooldown | normal endpoint status if authoritative operation succeeds | cache write/read resumes and cache repopulates |
| General distributed cache | rebuildable app data | depends on key namespace | medium | bypass cache; no unbounded local fallback | fail open for rebuildable data | authoritative repository/service | managed failover window plus circuit cooldown | normal response if source succeeds | cache hit rate recovers |
| Security-stamp cache read | accelerate token stamp validation | high | high | ignore cache and read DB | fail secure via DB | `IUserSecurityStampReader` / PostgreSQL | bounded Redis outage | token valid/invalid based on DB stamp | cache miss/fallback metric returns to baseline |
| Security-stamp cache invalidation | remove stale stamp snapshot after mutation | high | medium | DB mutation remains authoritative; invalidation failure must be logged/alerted | fail secure via DB stamp update | short cache TTL plus DB stamp mismatch | cache TTL maximum is 5 minutes; alert if invalidation fails | mutation succeeds only if authoritative DB update succeeds | invalidation errors stop after recovery |
| Auth and OTP rate limiting | brute-force and abuse protection | high | high | no unlimited attempts | fail closed or bounded emergency limiter | restrictive per-instance limiter or gateway limiter | only during Redis failover/outage | `429` when emergency limit exceeded, `503` when protection cannot be guaranteed | Redis limiter counters resume; fallback deactivates |
| Authenticated normal reads | abuse control | medium | high | conservative local limiter acceptable | bounded fail open | per-instance limiter | managed failover window | `200`/normal response if handler succeeds; `429` if fallback limit exceeded | Redis limiter resumes |
| Public expensive search | protect DB from cache/rate-limit loss | medium | high | conservative local limiter and DB query limits | bounded fail open | per-instance limiter, max page size, query timeout | managed failover window | normal response or `429` | Redis limiter resumes, DB latency stable |
| Administrative/destructive operations | protect security or idempotency invariant | high | medium | block when invariant depends only on Redis | fail closed | durable DB invariant preferred | until Redis or durable fallback returns | `503` or domain-specific rejection | invariant checks pass after recovery |
| SignalR backplane | cross-instance realtime fanout | low to medium | medium | HTTP API stays up; same-instance behavior may continue; cross-instance delivery may degrade | degraded | client reconnect guidance; no durable message guarantee | managed failover window | websocket reconnect/degraded realtime | cross-instance message delivery resumes |
| Redis readiness | dependency signal | operational | medium | expose degraded dependency without restarting app | degraded | liveness remains independent | outage duration | `/health/live=200`; readiness/dependency state reflects Redis | readiness recovers without process restart |

## Observed behaviour during a Redis outage (load test 2026-11-04, finding D15)

Measured with the Redis store stopped (`OutputCache` fail-open already fixed in D12):

| Traffic | Rate-limit policy backend | Result during the outage | Matches the table above |
| --- | --- | --- | --- |
| Public reads (search, geo-search, public agency pages, lookups) | in process (`PublicReadPolicy`, launch audit A6-002) | normal responses, `429` only past the per-instance limit | yes (public expensive search, bounded fail open) |
| Login, OTP, password reset, register, refresh, logout | Redis | `503` ("rate limiter backend is unavailable") | yes (auth and OTP rate limiting: `503` when protection cannot be guaranteed) |
| Every other Redis-limited write (`user-write`, `visits`, `reviews`, `service-requests`, `contact`, ...) | Redis | `503` | **not stated in the table**: it is the same fail-closed behaviour as auth, applied to ordinary writes |
| `/health/live` | none | `200` | yes |
| `/health/ready` | Redis dependency check | `503` while Redis is down | partly: readiness reflects Redis and liveness does not, but the table calls the Redis readiness mode "degraded"; `503` on `/health/ready` takes the instance out of rotation, so confirm that is wanted |

The first, second and `/health/live` rows are the intended policy. Two points are **decisions still open**. Writes (third row): an ordinary
signed-in write (a favourite, a profile edit) is refused for the whole outage although a per-instance limiter could
bound it. Moving a policy to a per-instance fallback is a one-line change in `RedisRateLimitingDefaults` (`InProcess`
or a bounded fallback) but weakens the abuse limit during the outage, so it is not done without an explicit decision.
Options for writes: (a) keep fail closed for all writes (current, simplest, longest user-visible outage); (b) bounded per-instance
fallback for low-risk writes only (favourites, profile, notifications), auth/OTP/destructive stay fail closed; (c) per-instance
fallback for every write except auth/OTP.

## Non-Negotiable Security Rules

- Authentication and OTP endpoints must never become unlimited because Redis is down.
- Refresh-token state and OTP state are PostgreSQL-authoritative in this repository and must remain so.
- Redis must not be the only store for irreplaceable security state unless a separate durability and HA decision is approved.
- Redis credentials and full connection strings must never be logged.
