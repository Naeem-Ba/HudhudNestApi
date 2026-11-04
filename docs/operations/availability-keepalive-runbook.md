# API availability and keepalive runbook

Incident: 2026-10-23 03:26–08:00 GMT, `https://api.hudhudnest.com/health/live` answered `503` for ~4.5 h.
cron-job.org then disabled the "HudhudNest API keepalive" job after 26 consecutive failures, so nothing
kept the instance warm afterwards.

`/health/live` checks nothing but the process (`Predicate = _ => false`), so a 503 there comes from
Render's edge: the instance was not listening (sleeping, restarting, deploying, or stuck in startup).

## What the code now guarantees

- **Startup never waits on the database.** Reference-data seeding (`DatabaseSeeder`, one transaction
  behind a Postgres advisory lock, no time bound) used to run inline before Kestrel listened. In
  Production/Staging it now runs in `ReferenceDataSeedHostedService` after the host is up, with a 5-minute
  per-attempt timeout and 5 retries (5 s → 2 min). Override with `Database__SeedReferenceData__Background=false`
  to get the old inline behaviour.
- Redis connect at startup is still bounded (`Redis:StartupConnectTimeoutSeconds`, default 15 s, max 60 s).
- `.github/workflows/uptime-keepalive.yml` pings `/health/live` every 5 min with a ~9 min retry window
  and reports `/health/ready` as a warning. It cannot be disabled by failing probes.

## Owner actions (not possible from the repo)

1. **Re-enable the cron-job.org job** (members area → HudhudNest API keepalive → enable). Set the failure
   threshold / notification so a transient 503 does not disable it again, and keep the request timeout at 30 s+.
2. **Render service settings:** Health Check Path = `/health/live` (not `/health/ready`, so a DB blip does not
   restart the instance); keep "Auto-Deploy" off (deploys come from the Deploy Hook after the Production Gate).
3. **Plan:** the free web-service tier sleeps after 15 min idle and has 512 MB RAM. If the pings are the only
   thing preventing sleep, any missed ping means a cold start (~1 min of 503). A paid instance removes sleep entirely
   and is the only way to guarantee availability.
4. When this recurs, read the Render **Events** and **Logs** around the first failed ping: "Instance failed health
   check", "Out of memory" and "Deploy started" each point to a different cause.
