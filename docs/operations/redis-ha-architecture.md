# Redis HA Architecture

Status: target architecture defined; production-provider evidence is not present.

## Required Production Topology

PropertyApi requires a managed Redis-compatible service with:

- one writable primary.
- at least one replica.
- automatic primary failover.
- provider-supported stable endpoint, proxy, or topology discovery.
- TLS in transit.
- authentication.
- private networking or strict network allow-listing.
- monitoring for latency, saturation, role changes, failover events, replication health, reconnects, and command errors.
- documented provider SLA and maintenance behavior.

A single Redis process with persistence is not High Availability. A replica without automatic promotion is not sufficient.

## Endpoint Strategy

Application configuration must use the provider-supported endpoint for automatic failover. If the provider requires a proxy endpoint, use the proxy endpoint. If it requires topology discovery, use the provider-supported StackExchange.Redis configuration.

Do not hard-code a node-specific primary endpoint unless the provider documents that endpoint as failover-aware.

## Provider Evidence Required

The production gate requires a sanitized JSON evidence file containing:

- provider name and service tier.
- primary count.
- replica count.
- whether automatic failover is enabled.
- whether TLS is required.
- whether network access is restricted.
- endpoint strategy.
- persistence mode.
- provider SLA reference.
- monitoring and alert identifiers.

No credentials or full connection strings are allowed in evidence artifacts.

## Current Repository State

The repository currently contains Docker-based standalone Redis for local production-gate dependency detection. That test verifies outage detection only. It is not HA and must not be treated as production failover evidence.
