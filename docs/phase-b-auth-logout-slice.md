# Phase B Auth Logout Slice

## Scope

- `LogoutCommandHandler` now depends on `ILogoutIdentityService`
  instead of the broad `IPureIdentityService`.
- `ILogoutIdentityService` exposes only the identity capabilities used by
  logout:
  - `FindByIdAsync`
  - `UpdateSecurityStampAsync`
- `PureIdentityService` remains the Infrastructure adapter while the
  Application layer depends on the narrower capability contract.

## Behavior Preserved

- Refresh token revocation remains owned by `IRefreshTokenRepository`.
- Logout still attempts to rotate the Identity security stamp when the
  identity exists and is not soft-deleted.
- The security-stamp cache is invalidated even when the identity cannot be
  resolved or the stamp update fails.
- External API contracts are unchanged.

## Transaction Boundary

Logout remains a best-effort application workflow:

- refresh token revocation is repository-owned;
- identity stamp rotation is a separate Identity adapter operation;
- cache invalidation always runs after those operations.

This slice does not introduce a cross-store transaction between refresh tokens,
ASP.NET Identity, and the distributed security-stamp cache.
