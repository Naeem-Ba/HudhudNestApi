# Phase B Users ChangePassword Slice

## Scope

- `ChangePasswordCommandHandler` now depends on
  `IChangePasswordIdentityService` instead of `IPureIdentityService`.
- `IChangePasswordIdentityService` exposes only the identity capabilities used
  by the change-password workflow:
  - `FindByIdAsync`
  - `ChangePasswordAsync`
  - `RecordCredentialChangeAsync`
  - `UpdateSecurityStampAsync`
- `PureIdentityService` remains the Infrastructure adapter while the handler
  depends on the narrower capability contract.

## Behavior Preserved

- Missing or soft-deleted identities still return user-not-found.
- Failed password changes still return Identity errors.
- Successful password changes still record credential metadata, rotate the
  security stamp, and write the audit log.
- Audit logging remains in the Application workflow.

## Transaction Boundary

ChangePassword remains a best-effort workflow across Identity metadata and
audit logging. This slice does not introduce a transaction across those stores.
