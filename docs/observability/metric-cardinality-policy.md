# Metric cardinality policy

Approved dimensions: `operation`, `outcome`, bounded
`failure_reason_category`, `method`, status code/class, `route_group`,
`request_kind`, `span.kind`, `db.system.name`, and environment.

Prohibited dimensions: user/property/request/trace IDs, phone, email, IP, raw
path, query string, exception message, SQL, cache key, city/address, filename,
token IDs, and arbitrary user-controlled text.

New labels require an owner, finite documented values, dashboard/alert use case,
and a test. A dimension with more than 100 expected values per service instance
is rejected unless an explicit capacity review approves it.

## Social distribution (`hudhudnest.social.publication.attempts`)

Owner: social-distribution maintainers. Use case: the two alerts in
`observability/prometheus/rules/hudhudnest-social-alerts.yml`. Test:
`SocialPublicationMetricsTests`.

- `platform`: the `SocialPlatform` enum name (Facebook, Instagram, Telegram,
  TikTok, YouTube, LinkedIn) — 6 values.
- `outcome`: `published`, `retrying`, `failed` (`other` is a defensive fallback).
- `failure_reason_category`: the `SocialPublicationErrorCode` enum name, present
  only when the attempt did not succeed — a fixed enum of fewer than 20 values.

Never labelled with an account, property, publication or external post id.
