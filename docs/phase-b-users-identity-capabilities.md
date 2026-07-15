# Phase B Users Identity Capabilities

## Scope

- User profile read handlers now depend on `IUserIdentityReadService`.
- `UpdateUserCommandHandler` now depends on `IUpdateUserIdentityService`.
- `DeleteUserCommandHandler` now depends on `IDeleteUserIdentityService`.
- `ChangePasswordCommandHandler` now depends on
  `IChangePasswordIdentityService`.

## Behavior Preserved

- User reads still combine Identity snapshots with `UserAccount` profile data.
- UpdateUser still keeps the existing application transaction around profile
  updates and the phone-number Identity operation.
- DeleteUser still rotates the security stamp before soft-delete and continues
  the delete attempt when stamp rotation fails.
- External API contracts are unchanged.

## Transaction Boundary

User workflows still coordinate Application profile state and Infrastructure
Identity adapter calls from the Application layer. This slice documents the
existing boundaries and narrows dependencies; it does not introduce new
cross-store transactions.
