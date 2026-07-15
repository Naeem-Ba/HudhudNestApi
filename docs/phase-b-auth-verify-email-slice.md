# Phase B Auth VerifyEmail Slice

## Scope

- `VerifyEmailCommandHandler` now depends on `IVerifyEmailIdentityService`
  instead of the broad `IPureIdentityService`.
- `IVerifyEmailIdentityService` exposes only the identity capabilities used by
  email verification:
  - `FindByIdAsync`
  - `ConfirmEmailAsync`
- `PureIdentityService` remains the Infrastructure adapter while Application
  code depends on a narrower capability contract.

## Behavior Preserved

- Missing or soft-deleted identities still return `INVALID_TOKEN`.
- Already confirmed email remains idempotent and returns success.
- Identities without an email still return `NO_EMAIL`.
- Invalid confirmation tokens still return `INVALID_TOKEN`.
- External API contracts are unchanged.

## Transaction Boundary

VerifyEmail remains a single Identity adapter workflow. This slice does not
change persistence behavior or introduce any new transaction boundary.
