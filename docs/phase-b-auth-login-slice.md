# Phase B Auth Login Slice

Date: 2026-07-15

## Scope

This slice continues the Auth capability split by moving `LoginCommandHandler`
off the broad `IPureIdentityService` dependency.

No external API contracts were changed.

## Changes

| Area | Change |
| --- | --- |
| Application | Added `ILoginIdentityService` with only the identity operations needed by email/password login. |
| Login handler | `LoginCommandHandler` now depends on `ILoginIdentityService` instead of `IPureIdentityService`. |
| Infrastructure | `PureIdentityService` implements `ILoginIdentityService`; DI bridges it through the existing implementation. |
| Integration fault injection | Auth fault decorator implements the new login capability so shared test infrastructure remains compatible. |
| Unit tests | Login command tests now mock `ILoginIdentityService`. |

## Verification

| Command | Result |
| --- | --- |
| `dotnet test tests\HudhudNestApi.Auth.Tests\HudhudNestApi.Auth.Tests.csproj -c Release --no-restore --filter LoginCommandHandlerTests` | Passed: 2/2. |
| `dotnet test tests\HudhudNestApi.Auth.Tests\HudhudNestApi.Auth.Tests.csproj -c Release --no-restore` | Passed: 158/158. |
| `dotnet test tests\HudhudNestApi.Architecture.Tests\HudhudNestApi.Architecture.Tests.csproj -c Release --no-restore` | Passed: 32/32. |
| `dotnet test tests\HudhudNestApi.Application.Tests\HudhudNestApi.Application.Tests.csproj -c Release --no-restore` | Passed: 122/122. |
| `dotnet test tests\HudhudNestApi.Integration.Tests\HudhudNestApi.Integration.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName!~Postgres&FullyQualifiedName!~NotificationIntegrationTests"` | Passed: 20/20. |
| `dotnet build .\HudhudNestApi.sln -c Release --no-restore` | Passed: 0 warnings, 0 errors. |

## Remaining Boundary

`IPureIdentityService` is still used by Logout, ForgotPassword, ResetPassword,
AddEmail, VerifyEmail, and user profile commands/queries. Split each workflow in
its own slice to avoid turning Phase B into a broad rewrite.
