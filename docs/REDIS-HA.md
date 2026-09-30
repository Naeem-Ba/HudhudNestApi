# Redis High Availability (Sentinel) — Production Gate

## What this is

Production Gate proves Redis automatic-failover capability with a real, self-hosted
**Redis OSS + Replication + Redis Sentinel** topology that the `redis-sentinel-ha` CI job
brings up itself, in Docker, on every pull request and push — not just a config file, and not
a claim backed only by a managed provider's marketing page.

The drill actually stops the real primary container and proves, with direct commands against
the surviving nodes, that:

1. Sentinel detects the failure.
2. A quorum of Sentinels agrees.
3. The replica is promoted to primary.
4. Data written before the failure is still readable on the new primary.
5. The new primary accepts new writes.
6. The Sentinel cluster returns to a stable state.

If any of those steps doesn't happen for real, the job fails. There is no `|| true`, no
`continue-on-error`, and no "container exited so we'll call it HA" shortcut anywhere in this
path — see [Verification stages](#verification-stages) below for exactly what is checked, and
why a first implementation attempt that only checked "did Sentinel say the master changed" was
rejected in favor of also checking `ROLE` directly against the promoted node (Sentinel's
opinion and the node's own reported role must agree).

## Architecture

```text
                    Production Gate (redis-sentinel-ha job)
                                    │
                                    ▼
                         Redis Sentinel quorum (2 of 3)
                    /              |               \
                   ▼               ▼                ▼
           redis-sentinel-1  redis-sentinel-2  redis-sentinel-3
                   │               │                │
                   └───────────────┴────────────────┘
                          monitors "mymaster"
                                    │
                                    ▼
                             redis-primary
                              172.28.0.10
                                    │
                              replication
                                    ▼
                             redis-replica
                              172.28.0.11
```

### Components

| Service | Role | Notes |
|---|---|---|
| `redis-primary` | Redis OSS primary | `redis:7-alpine`, static IP `172.28.0.10` |
| `redis-replica` | Redis OSS replica | `--replicaof 172.28.0.10 6379`, static IP `172.28.0.11` |
| `redis-sentinel-1/2/3` | Sentinel quorum | `sentinel monitor mymaster 172.28.0.10 6379 2`, quorum 2 of 3 |

Defined in [`ci/docker-compose.production-gate.yml`](../ci/docker-compose.production-gate.yml)
on their own `redis-ha` Docker network (fixed subnet `172.28.0.0/24`), separate from the
`postgres`/`redis`/`api`/`migrator` services the `build-test-container` job uses for the
container smoke test. The Sentinel config template lives at
[`ci/redis/sentinel/sentinel.conf`](../ci/redis/sentinel/sentinel.conf) and is copied into each
Sentinel container's own writable path at startup (Sentinel rewrites its config file as it
discovers topology state, so three containers can't share one bind-mounted file).

**Why static IPs, not Docker DNS hostnames.** The first implementation used
`sentinel resolve-hostnames yes` and addressed the primary as `redis-primary`. A real local
failover drill showed this doesn't work: once `docker compose stop redis-primary` stops the
container, Docker's embedded DNS stops resolving the `redis-primary` hostname at all
(confirmed directly: `getent hosts redis-primary` returns exit code 2 while the container is
stopped). Sentinel then logs `Failed to resolve hostname 'redis-primary'` and re-enters `TILT`
mode on every monitoring cycle, which suppresses failover indefinitely — the replica is never
promoted. Switching the topology to fixed IPs on a dedicated subnet removes the DNS dependency
entirely; Sentinel learns the replica's address from the primary's own `INFO replication`
output, the same way it would against a real provider's IP-addressed Sentinel topology.

### Failover mechanism

* `sentinel down-after-milliseconds mymaster 5000` — a Sentinel must see the primary fail to
  respond for 5s before considering it subjectively down.
* `sentinel monitor mymaster 172.28.0.10 6379 2` — quorum of 2 (out of 3) Sentinels must agree
  before an objective-down / failover decision is made.
* `sentinel failover-timeout mymaster 10000` / `sentinel parallel-syncs mymaster 1` — CI-scaled
  failover pacing.

These values are deliberately CI-appropriate (fast enough that a full drill finishes in well
under a minute; not so fast that ordinary CI runner scheduling jitter produces a false-positive
failure detection) and are not a production SLA recommendation for any specific provider.

## CI verification

**Job:** `redis-sentinel-ha` in
[`.github/workflows/production-gate.yml`](../.github/workflows/production-gate.yml).
**Script:** [`scripts/verify-redis-sentinel-ha.sh`](../scripts/verify-redis-sentinel-ha.sh).

Unlike every other job in this pipeline that touches Redis HA, `redis-sentinel-ha` has no `if:`
restriction and needs no secret or live deployment — it runs on every `pull_request` and
`push`, not only on `main`/`master`, because it costs nothing and depends on nothing external.

### Verification stages

1. **Connectivity** — `PING` against `redis-primary`, `redis-replica`, and all three Sentinels.
2. **Replication** — `ROLE` confirms `master`/`slave`; polls `INFO replication` until
   `connected_slaves >= 1` on the primary and `master_link_status:up` on the replica (bounded
   polling with a timeout, not a fixed `sleep`).
3. **Sentinel quorum** — polls all three Sentinels' `SENTINEL masters` until each independently
   reports the correct master IP and `num-other-sentinels == 2` (true mutual discovery, not
   just "three containers are running").
4. **Sentinel discovery** — `SENTINEL get-master-addr-by-name mymaster` is logged and asserted
   to match the primary, before anything is killed.
5. **Sentinel replica discovery** — polls `SENTINEL replicas mymaster` on all three Sentinels
   until each independently reports `redis-replica`'s IP. Sentinel learns a master's replicas by
   periodically polling the *master's own* `INFO replication` output (default cadence: every
   10s), which is independent of, and can lag behind, the direct replication link checked in
   step 2. Killing the primary before every Sentinel's internal replica table is populated
   leaves `sentinelSelectSlave()` with zero promotion candidates, so every failover attempt
   aborts with `-failover-abort-no-good-slave` for the rest of the run — this stage exists to
   fail fast and unambiguously on that race instead of timing out at step 7 looking like a
   generic failover failure.
6. **Write test data** — `SET production_gate_redis_ha_test <run-unique value>`, confirmed
   readable, before the primary is touched.
7. **Kill the primary** — a real `docker compose stop redis-primary`. Not a mock, not a
   config edit, not a scaled-to-zero replica.
8. **Wait for failover** — polls `SENTINEL get-master-addr-by-name mymaster` (bounded timeout,
   default 90s) until it reports the replica's IP, then independently confirms at least 2 of
   the 3 Sentinels agree (a real quorum majority, not just the one Sentinel being polled).
9. **Verify promotion against Redis itself** — `ROLE` against the (former) replica must report
   `master`. Sentinel's own claim is checked separately in step 8 and is **not** treated as
   sufficient on its own.
10. **Data + writability + stability** — the pre-failover key is read back from the new primary,
    a brand-new key is written and read back, and all three Sentinels are re-polled to confirm
    no `s_down`/`o_down` flags remain.

On any failure, the script (and a matching `if: always()` step in the job) prints
`docker compose ps`, the last 100 log lines from all five containers, `SENTINEL masters` /
`replicas mymaster` / `sentinels mymaster`, and the replica's `INFO replication` — before
exiting non-zero. The stack is always torn down (`docker compose down -v --remove-orphans`) in
an `if: always()` step regardless of outcome.

A machine-readable report (`redis-sentinel-ha-report.json`) and a human summary
(`redis-sentinel-ha-summary.md`) are written to `artifacts/redis-sentinel-ha/` and uploaded as
a CI artifact on every run, pass or fail.

### Application-level Sentinel-aware failover vs. infrastructure HA verification

These are deliberately kept separate:

* **Infrastructure HA verification** (this document, the `redis-sentinel-ha` job): proves the
  Redis/Sentinel *topology itself* fails over correctly. It does **not** exercise the
  HudhudNestApi application at all — the `api` container in
  `ci/docker-compose.production-gate.yml` still points at the plain, single `redis:` service
  used for the ordinary build/test/container gate, not at this Sentinel topology.
* **Application Sentinel-awareness**: `RedisConnectionResolver` and every Redis consumer in
  this codebase (rate limiting, `IDistributedCache`, output cache, SignalR backplane) pass a
  non-`redis://` connection string straight through to StackExchange.Redis's own
  `ConfigurationOptions.Parse` instead of building `ConfigurationOptions` by hand. That means
  the application is already Sentinel-capable at the configuration layer: pointing
  `ConnectionStrings:Redis` at `host1:26379,host2:26379,host3:26379,serviceName=mymaster` is
  enough for `ConnectionMultiplexer` to discover the current primary through Sentinel and
  follow it across a failover, with zero application code changes. This is proven as a real
  runtime assertion (not a source grep) in
  `tests/HudhudNestApi.Integration.Tests/Redis/RedisSentinelConnectionStringTests.cs` against the
  exact StackExchange.Redis version this repository ships. Wiring an actual live
  Sentinel-fronted Redis into a deployed environment's `ConnectionStrings:Redis` is an
  infrastructure/ops decision, not something this Gate performs — see
  [Limitations](#limitations).

## Production Gate: what changed

**Before this change**, Production Gate treated a *specific managed provider's* self-reported
evidence as the only proof of Redis HA:

* `validate-redis-ha-topology` hard-required the `REDIS_HA_EVIDENCE_JSON` secret — a JSON blob
  asserting `provider`, `serviceTier`, `providerSlaReference`, `monitoringConfigured`, etc. —
  and failed the whole job if it was absent.
* `redis-staging-failover` hard-required `REDIS_PROVIDER_FAILOVER_COMMAND` (a provider-specific
  CLI trigger against a live Staging deployment) and failed if it was absent.

Since this repository's actual Upstash account is Free Tier with no HA/failover capability and
no payment method configured (see the Solution Report for the underlying cost investigation —
Upstash's HA "Prod Pack" tier is a recurring paid add-on), both of these secrets have never
been configured, so both jobs — and everything gated behind them
(`staging-smoke`, `redis-staging-failover`, `deploy-production`) — have been failing or
blocked on missing infrastructure rather than on a real defect.

**After this change:**

* `validate-redis-ha-topology` and `redis-staging-failover` no longer hard-fail when their
  provider secret is absent. They skip that specific check with a clear `::notice::` and a
  "skipped (no managed provider configured)" artifact, and exit successfully — absence of a
  managed provider is explicitly **not** treated as a failure (see task requirement: "Upstash
  is no longer a mandatory provider requirement"). If a real managed provider is ever
  configured for Staging, these secrets resume being validated exactly as strictly as before —
  nothing about their existing fail-closed behavior when evidence IS asserted has changed.
* The new `redis-sentinel-ha` job is the always-enforced, provider-independent gate. It is
  required (via `needs:` + an explicit `if:` check) by `staging-smoke` and by
  `deploy-production`, so production deployment is still blocked unless a real Redis HA
  failover was proven — just proven against a self-hosted topology this pipeline controls,
  instead of against a specific paid vendor.

### Provider-neutral by design

Production Gate does not require any specific Redis provider. Whatever the actual production
Redis turns out to be — Upstash, Redis Cloud, AWS ElastiCache, Azure Cache for Redis, a
self-hosted Sentinel deployment, or a Redis Cluster — the Gate's mandatory technical
requirement is "Redis HA capability verified," not "Upstash Multi-Zone HA configured." Nothing
in the pipeline treats `UPSTASH_REQUIRED=true` or the presence of an `UPSTASH_REDIS_URL` as a
success condition, and nothing treats their absence as a failure.

## Limitations

* **This proves automatic failover capability in a CI Docker topology. It does not prove
  multi-zone or multi-host production infrastructure resilience.** All five containers run on
  the same CI runner / Docker daemon. This rules out "the process crashed and Sentinel handles
  it" as a false positive, but it says nothing about surviving a host failure, an availability
  zone outage, or a network partition between physically separate nodes — that is the
  responsibility of whatever real infrastructure/provider Production actually runs on.
* **Redis Sentinel HA is not backup, not disaster recovery, not durability, and not a
  substitute for `database-restore-drill.yml`.** It proves *availability* across a primary
  failure, not that data is retained beyond what replication already held at the moment of
  failure, and not protection against data corruption, accidental deletion, or a
  region-wide outage.
* **The CI-native topology is infrastructure-only.** The HudhudNestApi `api` container is not
  connected to the Sentinel topology in this Gate (see
  [Application-level Sentinel-aware failover](#application-level-sentinel-aware-failover-vs-infrastructure-ha-verification)
  above) — that is a deliberate scope boundary, not an oversight.

## Production Redis HA decision

The Production Gate validates Redis HA *capability* independently from which Redis provider
production actually uses. The real production infrastructure may use a managed provider's HA
offering, a self-hosted Redis Sentinel deployment matching this topology, or another supported
HA design — this repository does not claim "production Redis is HA" on the strength of this CI
drill alone. That claim is only true once production's *actual* Redis deployment has been
verified to run some real HA topology; passing `redis-sentinel-ha` proves this codebase's
Redis integration layer and CI tooling can prove and consume Sentinel-style failover, not that
any particular deployed environment currently does.

## Security review

* The `redis-ha` network has no `ports:` mapping to the host — `redis-primary`, `redis-replica`,
  and all three Sentinels are reachable only from other containers on that internal Docker
  network, never from outside the CI runner.
* `protected-mode no` / `bind 0.0.0.0` are set inside that isolated, unpublished network only
  (see comments in `ci/redis/sentinel/sentinel.conf`) — this is a CI-only, test-scoped
  trade-off and must not be copied into any internet-reachable Redis deployment.
* No Redis `requirepass`/`masterauth` is configured for this topology, matching the existing
  plain `redis:` service already in `ci/docker-compose.production-gate.yml` (also unauthenticated,
  also never published to the host). Real production/Staging Redis connection strings continue
  to go through `RedisRateLimitingServiceCollectionExtensions.BuildRedisConfigurationOptionsFromUri`,
  which already requires and forwards provider credentials (and forces TLS for Upstash hosts) —
  nothing about that path changed.
* No secrets were added. `REDIS_HA_EVIDENCE_JSON` and `REDIS_PROVIDER_FAILOVER_COMMAND` remain
  as before, just no longer mandatory.

## BUG-30b

The `redis-sentinel-ha` job and its self-hosted topology are unrelated to BUG-30b
(`Concurrent Performance Validation` / `performance-validation.yml` CI-runner resource
contention, documented separately in the Bug/Solution reports). Any future flakiness in
`performance-validation.yml` must continue to be diagnosed and documented on its own terms, not
attributed to this change.
