# Phase B Composition Root Auth Module

## Scope

- Auth, Identity, email, SMS, OTP, refresh-token, and security-stamp
  registrations were moved from the large `AddInfrastructure` method into
  `AuthInfrastructureRegistration`.
- Service lifetimes were preserved.
- Runtime behavior was not changed.

## Registered Capability Groups

- JWT options and token services.
- Identity capability adapter and workflow-specific capability interfaces.
- Social authentication verifiers.
- Security-stamp reader, validator, and cache invalidator.
- Refresh-token repository and auth utility services.
- Email sender bindings.
- SMS and OTP services.

## Guard Rails

- `CompositionRegistrationTests` verifies the Auth capability services are
  registered with scoped lifetimes.
- The same test verifies the old `IPureIdentityService` registration does not
  return.
- `RepositoryWorkflowBoundaryTests` verifies repositories do not start depending
  on workflow orchestration services.

## Remaining Composition Root Work

- Extract persistence/cache/media/notification modules.
- Add options validation for remaining option groups.
- Review middleware order with explicit tests.
- Add duplicate-registration checks where multiple registrations are not
  intentional.
