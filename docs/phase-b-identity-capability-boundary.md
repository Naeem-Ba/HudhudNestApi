# Phase B Identity Capability Boundary

## Completed Boundary Change

- Application handlers no longer depend on the broad `IPureIdentityService`
  contract.
- The old `IPureIdentityService` interface was removed.
- Identity access from Application is now expressed through workflow-specific
  capability interfaces.
- Infrastructure composes those capabilities through
  `IIdentityCapabilityAdapter`, implemented by `PureIdentityService`.

## Capability Groups

- Auth: social login, register, login, refresh token, logout, phone OTP,
  add email, verify email, forgot password, reset password.
- Users: read profile identity, update user identity state, delete user,
  change password.

## Preserved Behavior

- External API contracts are unchanged.
- Token issuing, refresh-token rotation, OTP behavior, password recovery,
  email verification, and user profile workflows remain covered by the
  existing characterization tests.
- Infrastructure still owns framework-specific ASP.NET Core Identity adapter
  code.

## Remaining Architecture Work

- Composition root can now be reorganized by capability module.
- Repositories can be reviewed for leftover workflow orchestration.
- CI, performance, and observability phases can build on this boundary.
