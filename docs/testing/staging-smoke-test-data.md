# Staging smoke test data

## Run identity

Each run uses:

```text
SMOKE_RUN_ID=<UTC timestamp>-<short commit>-<workflow run>-<attempt>
```

The exact marker is included in both user profiles, the property title, and message content. Parallel
runs use independent phones, users, properties, images, and cleanup scopes.

## Users and credentials

- Phone prefix: `STAGING_SMOKE_PHONE_PREFIX`, a provider-reserved Staging-only E.164 prefix.
- Two deterministic per-run suffixes create owner and visitor numbers.
- Last names are `<run-id>-OWNER` and `<run-id>-VISITOR`.
- Password comes from `STAGING_SMOKE_PASSWORD` and is never written to reports.
- The suite creates new users; it does not depend on a permanently shared application user.

## OTP strategy

`Staging:TestSupport:FixedOtp` is injected only when both conditions are true:

1. `ASPNETCORE_ENVIRONMENT=Staging`.
2. `Staging:TestSupport:Enabled=true`.

The silent SMS sink accepts only `Staging:TestSupport:PhonePrefix`, sends no external message, and
never logs the phone or code. Production always evaluates the policy as disabled. The fixed value is
stored only as a GitHub Environment secret.

## Property and image data

- Property title: `E2E-SMOKE-<run-id>`.
- Message: `E2E-SMOKE-MESSAGE-<run-id>`.
- The fixture is a deterministic 1x1 PNG embedded in the runner; no external URL is downloaded.
- Real Staging must use its own Cloudinary/storage account and marker. In-memory media is allowed only
  for the isolated local Docker gate through an explicit local-only switch.

## Cleanup and TTL

The suite calls the authenticated-by-secret Staging support cleanup endpoint with the exact run id,
user ids, and property ids. The endpoint validates that every profile and property contains the same
marker before deleting anything. It deletes media provider objects and dependent database rows in a
transaction. The endpoint returns 404 outside Staging or when disabled/unauthorized.

As defense in depth, operators should schedule a 24-hour TTL cleanup for records whose exact marker
starts with `E2E-SMOKE-`; it must never use a broad substring or affect unmarked records.

## Sensitive data handling

Reports exclude passwords, OTPs, tokens, full phone numbers, connection strings, and storage keys.
Cleanup and test support secrets must be rotated like other Staging credentials.
