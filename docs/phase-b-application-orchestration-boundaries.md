# Phase B Application Orchestration Boundaries

## Repository Audit Result

Repositories remain persistence adapters:

- they persist and query database state;
- they do not issue access tokens;
- they do not resolve Identity account state;
- they do not send emails or OTP messages;
- they do not dispatch MediatR requests;
- they do not coordinate cross-capability workflows.

The architecture test `RepositoryWorkflowBoundaryTests` guards this boundary by
failing when Infrastructure repositories depend on workflow services such as
Identity capabilities, token services, email services, OTP services, or MediatR.

## Auth Transaction Boundaries

### Register

Application owns the workflow:

- create Identity account;
- assign default role;
- create `UserAccount` profile projection;
- persist the unit of work.

The transaction is coordinated by the Application handler through
`IUnitOfWork`. Infrastructure remains an adapter.

### Login

Application validates Identity state, checks the password, records successful
login metadata, stores the refresh token, and issues tokens. This workflow does
not use a cross-store transaction.

### Refresh Token

Application owns refresh-token rotation and reuse handling. The refresh-token
repository provides a repository-local transaction helper for refresh-token row
consistency, while Identity state lookup, security-stamp updates, cache
invalidation, audit logging, and access-token generation remain in Application.

### Logout

Application revokes the submitted refresh token, attempts security-stamp
rotation, and always invalidates the security-stamp cache. This remains a
best-effort workflow across refresh-token storage, Identity, and cache state.

### Phone OTP

Application owns the atomic phone authentication workflow:

- OTP validation and consumption;
- Identity creation or phone confirmation;
- role assignment;
- profile projection creation;
- refresh-token persistence;
- session issuance.

The handler coordinates commit or rollback through `IUnitOfWork`.

### Social Login

Application owns token verification, account linking, account creation, role
assignment, profile projection, and session issuance. Linking or new-account
flows are transaction-bound in the handler; existing linked account sign-in does
not require a linking transaction.

### Email and Password Recovery

Application owns add-email, verify-email, forgot-password, and reset-password
workflows. Identity adapter calls and external email delivery remain separate
operations; no cross-store transaction is introduced.

## User Transaction Boundaries

### Change Password

Application changes the password, records credential metadata, rotates the
security stamp, and writes audit data. The workflow is best-effort across
Identity and audit logging.

### Update User

Application coordinates profile updates and optional phone-number changes. The
existing `IUnitOfWork` transaction covers the profile persistence path and the
Identity adapter operation as currently configured.

### Delete User

Application rotates the security stamp before soft-delete. Stamp rotation
failure is logged but does not prevent the soft-delete attempt, preserving
existing behavior.

## Closure Decision

Phase B Identity/Application orchestration is complete when:

- Application handlers depend only on capability interfaces;
- `IPureIdentityService` is absent;
- Infrastructure repositories remain persistence adapters;
- transaction boundaries are documented here;
- Auth, Application, Architecture, and local Integration test suites pass.
