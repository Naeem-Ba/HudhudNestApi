# Phase B Auth Register Slice

Date: 2026-07-15

## Scope

This slice continues the Auth capability split by moving `RegisterCommandHandler`
off the broad `IPureIdentityService` dependency.

No external API contracts were changed.

## Changes

| Area | Change |
| --- | --- |
| Application | Added `IRegisterIdentityService` with only the identity operations needed by email registration. |
| Register handler | `RegisterCommandHandler` now depends on `IRegisterIdentityService` instead of `IPureIdentityService`. |
| Infrastructure | `PureIdentityService` implements `IRegisterIdentityService`; DI bridges it through the existing implementation. |
| Integration fault injection | Auth fault decorator implements the new register capability so PostgreSQL atomicity tests can still inject create/role failures. |
| Unit tests | Register command tests now mock `IRegisterIdentityService`. |

## Verification

| Command | Result |
| --- | --- |
| `dotnet test tests\HudhudNestApi.Auth.Tests\HudhudNestApi.Auth.Tests.csproj -c Release --no-restore --filter RegisterCommandHandlerTests` | Passed: 2/2. |
| `dotnet test tests\HudhudNestApi.Auth.Tests\HudhudNestApi.Auth.Tests.csproj -c Release --no-restore` | Passed: 158/158. |
| `dotnet test tests\HudhudNestApi.Architecture.Tests\HudhudNestApi.Architecture.Tests.csproj -c Release --no-restore` | Passed: 32/32. |
| `dotnet build .\HudhudNestApi.sln -c Release --no-restore` | Passed: 0 warnings, 0 errors. |
| `dotnet test tests\HudhudNestApi.Integration.Tests\HudhudNestApi.Integration.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName!~Postgres&FullyQualifiedName!~NotificationIntegrationTests"` | Passed: 20/20. |

## Remaining Boundary

`IPureIdentityService` is still used by other Auth/User workflows such as Login,
Logout, ForgotPassword, ResetPassword, AddEmail, VerifyEmail, and user profile
commands/queries. Those should be split in separate slices, not as one broad
rewrite.
