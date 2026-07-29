# Load-testing architecture

## Framework decision

k6 is the primary load-test framework. Its concurrent executors, custom metrics, hard thresholds, setup/teardown,
and container image fit GitHub Actions without installing a global host tool. NBomber was considered, but it would
add another compiled orchestration project while the security fixtures can use the real HTTP contracts and
authoritative PostgreSQL checks.

The .NET `PropertyApi.PerformanceDataGenerator` is not a competing load framework. It exists to create domain-valid,
deterministic data efficiently with the current EF Core model.

## Isolated topology

```text
k6
  -> nginx load balancer :8080
       -> PropertyApi api-1 :8080
       -> PropertyApi api-2 :8080
            -> PostgreSQL/PostGIS + pg_stat_statements
            -> Redis distributed rate limiter
```

The host exposes nginx on port 58080, PostgreSQL on 55432, and Redis on 56379. Docker Compose creates a disposable
database volume and removes it during teardown. Two API processes have separate connection pools of 40, so the
maximum configured application pool budget is 80 database connections, excluding migration and diagnostic clients.

## Instance and query-count proof

When, and only when, the environment is not Production and explicit performance flags are true, responses include:

- `X-Instance-Id`: `api-1` or `api-2`.
- `X-Database-Command-Count`: commands completed during that request.
- `X-Database-Duration-Ms`: aggregate EF/database command duration.

Production forcibly disables these headers even if a flag is accidentally configured. k6 has hard thresholds
requiring traffic on both instances and checks endpoint-specific SQL-command budgets.

## Workloads

- Browse workload: first, middle, and deep property pages; combined filters; detail; and PostGIS radius search.
- Cold-start phase: Redis is cleared and the short cold scenario runs without prewarming.
- Warm phase: a separate warm-up executor precedes steady-state measurement.
- Authentication races: refresh and OTP at concurrency 2, 5, and 10; invalid OTP attempt race; registration race.
- Distributed rate-limit races: login, registration, OTP request, OTP verification, refresh, property list, and geo.
- Profiles: 10,000 properties for PR/CI, 25,000 for release, and 100,000 for scheduled Staging.

Stress, spike, and extended soak remain scheduled-environment profiles rather than PR gates. A five-minute run is
not described as a soak test.

## Reports and secrets

k6 writes sanitized JSON, Markdown, and JUnit reports. The aggregator creates the root summary, endpoint CSV,
threshold snapshot, concurrency integrity results, query-plan analysis, and database workload analysis. Passwords,
OTP values, tokens, connection strings, and full test phone numbers are never included. All secrets are generated
ephemerally by the runner or supplied through protected environment variables.
