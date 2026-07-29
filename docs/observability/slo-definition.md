# PropertyApi SLOs

Rolling window: 30 days. Scheduled maintenance is excluded only when approved
and annotated before the window.

| SLO | SLI | Target | Error budget |
|---|---|---:|---:|
| Availability | non-5xx requests / all requests | 99.5% | 0.5% |
| Latency | requests completed within 500 ms / all requests | 95% | 5% |
| Authentication | successful auth operations / valid auth attempts | 99.0% | 1% |

Prometheus recording rules implement the SLIs. Availability has 14.4x fast-burn
and 2x slow-burn alerts. p95 and p99 use histogram quantiles, never averages.
Current compliance is not asserted until queries return data for the full window.
