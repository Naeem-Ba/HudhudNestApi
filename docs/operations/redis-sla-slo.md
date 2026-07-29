# Redis SLA and SLO

Status: provider SLA unverified.

## Required Provider SLA Evidence

Before production readiness can pass, attach sanitized evidence for:

- managed Redis product and tier.
- published SLA.
- replica and failover capability on that tier.
- expected failover duration or provider guidance.
- maintenance behavior.
- incident notification mechanism.

Do not copy credentials or internal connection strings into this document or CI artifacts.

## PropertyApi Recovery Objectives

| Objective | Target | Current evidence |
| --- | ---: | --- |
| API process restart after Redis failover | 0 restarts | not verified against managed HA |
| Core liveness during Redis failover | `/health/live` remains 200 | local outage detection only |
| Output-cache degradation | uncached reads continue | policy defined; managed failover not verified |
| Auth/OTP rate limiting | never unlimited | policy defined; managed failover not verified |
| Client reconnect after primary promotion | bounded and automatic | not verified against managed HA |
| Full Redis feature recovery | measured in staging | not measured |

## Readiness Status

Production Redis resilience readiness is failed until the managed provider topology and active-traffic failover report are produced and validated by CI.
