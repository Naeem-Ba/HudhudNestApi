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
