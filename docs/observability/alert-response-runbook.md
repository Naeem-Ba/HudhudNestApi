# Alert response runbook

## HudhudNestApiHigh5xxRate

Severity page. Check the 5xx panel, deployment version, error traces, database
and Redis health. Roll back or disable the failing dependency path if safe.
Escalate when sustained for ten minutes; recovery requires the ratio below 5%
for two evaluation periods and readiness healthy.

## HudhudNestApiHighP95Latency

Severity ticket. Check p95/p99 by route and dependency span metrics. Mitigate
pool saturation, slow queries, Redis/network failures, or regressions. Escalate
if the latency budget continues burning; confirm recovery below one second.

## HudhudNestApiAuthenticationFailures

Severity ticket. Separate bounded invalid-client failures from internal errors
and rate limiting. Never inspect credentials or OTP content. Escalate internal
errors immediately; recovery requires the bounded failure rate to normalize.

## HudhudNestApiDependencyFailures

Check child spans by `db.system.name` or client dependency, health checks, pool
metrics, and provider status. Apply circuit breaking/failover where supported.
Confirm child span error rate and readiness recover.

Every page requires timeline, impact, mitigation, evidence, and follow-up owner.

## HudhudNestSocialCredentialRejected

Severity page. A social platform (label `platform`) rejected the credential, so
the account was marked `Expired` and nothing more is sent to it. Open the admin
page (Accounts tab) to see which account, revoke/regenerate the token on the
platform (Telegram: @BotFather `/revoke`), then reconnect the account with the new
token and re-queue its dead letters. Details and the pause/kill-switch controls:
`docs/social-distribution/RUNBOOK-AR.md`. Never paste a token into chat or tickets.

## HudhudNestSocialPublishFailuresElevated

Severity ticket. Three or more publications failed on one platform within an
hour. Read the dead letters (admin page or `GET /api/social-distribution/dead-letters`)
for the bounded `failure_reason_category`: `RateLimited`/`NetworkError` are
transient, `InvalidContent`/`PermissionDenied` need a person (content rule, bot
removed from the channel). Pause the platform by deactivating its channel if the
failures embarrass the brand; see `docs/social-distribution/RUNBOOK-AR.md`.
