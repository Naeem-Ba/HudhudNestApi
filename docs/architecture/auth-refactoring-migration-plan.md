# Authentication refactoring migration plan

## Completed stages

1. Characterized social, OTP and password behavior using the existing auth tests.
2. Extracted pure linking, creation and phone-ownership policies.
3. Extracted provider validation, account resolution, OTP consumption and account mutations.
4. Replaced both oversized handlers with one-dependency delegates.
5. Unified password, social and OTP initial-session issuance.
6. Split Identity framework access into reader, creator, access and credential services.
7. Kept `PureIdentityService` as an obsolete delegating compatibility facade.
8. Verified rollback and concurrency using the real PostgreSQL integration suite.

## Compatibility facade

Existing capability registrations still resolve through `IIdentityCapabilityAdapter`
to avoid migrating every unrelated auth flow in one change. The facade has no
`UserManager`, no policy and no persistence logic. New code must not reference it.

Removal procedure:

1. Introduce composition adapters per existing narrow Application interface.
2. Register those adapters directly and migrate one consumer group at a time.
3. Run auth and PostgreSQL atomicity tests after every registration change.
4. Remove `IIdentityCapabilityAdapter` and `PureIdentityService` when its consumer count is zero.

## Rollback

The refactoring can be reverted at the Application DI registration boundary and
the compatibility facade without database rollback. No schema or public API
contract was changed. Do not roll back migrations or delete authentication data.

## Remaining debt

- Registration does not yet use the canonical session issuer because its current
  public response does not issue a session.
- Refresh rotation remains a related, intentionally separate command.
- The compatibility facade and grouped infrastructure registration remain until
  all unrelated identity capability consumers are migrated.
