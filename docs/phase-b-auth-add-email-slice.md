# Phase B Auth AddEmail Slice

## Scope

- `AddEmailCommandHandler` now depends on `IAddEmailIdentityService`
  instead of the broad `IPureIdentityService`.
- `IAddEmailIdentityService` exposes only the identity capabilities used by
  the add-email workflow:
  - `FindByIdAsync`
  - `FindByEmailAsync`
  - `SetEmailAsync`
  - `GenerateEmailConfirmationTokenAsync`
- `PureIdentityService` remains the Infrastructure adapter while Application
  code depends on a narrower capability contract.

## Behavior Preserved

- The requested email is normalized before lookup and persistence.
- Missing or soft-deleted identities still return `USER_NOT_FOUND`.
- Emails owned by another identity still return `EMAIL_TAKEN`.
- Successful requests still persist the unconfirmed email, generate a
  confirmation token, and send the verification link.
- External API contracts are unchanged.

## Transaction Boundary

AddEmail remains an application workflow composed of Identity adapter calls and
email dispatch. This slice does not introduce a transaction across Identity and
email delivery.
