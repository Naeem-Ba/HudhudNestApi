# Phase B Auth Password Recovery Slice

## Scope

- `ForgotPasswordCommandHandler` now depends on
  `IForgotPasswordIdentityService`.
- `ResetPasswordCommandHandler` now depends on
  `IResetPasswordIdentityService`.
- `PureIdentityService` remains the Infrastructure adapter while Application
  code depends on narrower capability contracts.

## Behavior Preserved

- ForgotPassword still returns a generic success response to avoid account
  enumeration.
- ForgotPassword still generates a reset token and sends email only for active
  identities.
- ResetPassword still rejects same-password changes.
- ResetPassword still resets the password, updates the security stamp, records
  credential change time, and revokes active refresh tokens.
- External API contracts are unchanged.

## Transaction Boundary

Password recovery remains an application workflow across Identity, email
delivery, and refresh-token revocation. This slice documents the existing
best-effort boundary and does not introduce a cross-store transaction.
