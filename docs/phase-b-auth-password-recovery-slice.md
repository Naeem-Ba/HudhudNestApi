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

## Reset Link Contract (frontend)

`PasswordResetUrlBuilder` emails `{Frontend:PasswordResetUrl}?email=..&token=..`.
The frontend page `/auth/reset-password` (HudhudNest repo, PR #142) reads both
values from the link and never shows the token: the "reset code" is this Identity
token, not a phone OTP (the phone flow is separate: `/auth/phone-password-reset`).
The user enters only the new password and its confirmation, and the page posts
`{ email, token, newPassword, confirmPassword }` to `POST /auth/reset-password`.
`ResetPasswordCommandValidator` and the handler both enforce
`newPassword == confirmPassword`, so no API change was needed. A link missing
`email` or `token` shows an "invalid link" message with a way to request a new one.
`Frontend:PasswordResetUrl` must therefore point at `https://<site>/auth/reset-password`
(no hash; the frontend no longer uses hash routing).
