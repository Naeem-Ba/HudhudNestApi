# Authentication orchestration

## Architecture

Command handlers perform only command-to-orchestrator delegation. Provider/OTP
validation, account resolution, pure policy, mutation, transaction coordination
and session issuance are separate. Application policies do not reference EF Core
or ASP.NET Identity. Infrastructure implements Identity mutations.

The canonical `IAuthenticationSessionIssuer` creates access and refresh tokens,
persists the hashed refresh token through `IRefreshTokenRepository`, records the
successful login and writes a bounded audit payload containing method, outcome
and timestamp only.

## Sequences

### Existing linked social account

```mermaid
sequenceDiagram
  Handler->>SocialAuthenticationOrchestrator: Authenticate
  SocialAuthenticationOrchestrator->>SocialIdentityValidator: Verify provider token
  SocialAuthenticationOrchestrator->>SocialAccountResolver: Resolve provider key
  SocialAuthenticationOrchestrator->>AuthenticationSessionIssuer: Issue session
```

### First-time social account

```mermaid
sequenceDiagram
  SocialAuthenticationOrchestrator->>SocialAccountMutationCoordinator: NoAccountFound
  SocialAccountMutationCoordinator->>IUnitOfWork: Begin
  SocialAccountMutationCoordinator->>ISocialLoginIdentityService: Create + link + role
  SocialAccountMutationCoordinator->>IUserAccountRepository: Add profile
  SocialAccountMutationCoordinator->>AuthenticationSessionIssuer: Issue session
  SocialAccountMutationCoordinator->>IUnitOfWork: Commit
```

### Social conflict

```mermaid
sequenceDiagram
  SocialAccountResolver->>SocialAccountLinkingPolicy: Evaluate verified claims
  SocialAccountLinkingPolicy-->>SocialAccountResolver: Additional verification or forbidden
  SocialAccountResolver-->>SocialAuthenticationOrchestrator: Conflict
```

### Existing phone OTP login

```mermaid
sequenceDiagram
  PhoneOtpAuthenticationOrchestrator->>OtpConsumptionService: Validate
  PhoneOtpAuthenticationOrchestrator->>IUnitOfWork: Begin
  PhoneOtpAuthenticationOrchestrator->>OtpConsumptionService: Atomic consume
  PhoneOtpAuthenticationOrchestrator->>PhoneAccountMutationCoordinator: Resolve phone
  PhoneOtpAuthenticationOrchestrator->>AuthenticationSessionIssuer: Issue session
  PhoneOtpAuthenticationOrchestrator->>IUnitOfWork: Commit
```

### New phone account

```mermaid
sequenceDiagram
  PhoneAccountMutationCoordinator->>IPhoneOtpIdentityService: Create identity
  PhoneAccountMutationCoordinator->>IPhoneOtpIdentityService: Assign User role
  PhoneAccountMutationCoordinator->>IUserAccountRepository: Add profile
  PhoneAccountMutationCoordinator-->>PhoneOtpAuthenticationOrchestrator: Resolved new account
```

### OTP consumed then mutation failure

```mermaid
sequenceDiagram
  PhoneOtpAuthenticationOrchestrator->>OtpConsumptionService: Consume
  PhoneAccountMutationCoordinator-->>PhoneOtpAuthenticationOrchestrator: Mutation failed
  PhoneOtpAuthenticationOrchestrator->>IUnitOfWork: Rollback
  Note over IUnitOfWork: OTP returns to unused state
```

### Session issuance failure

```mermaid
sequenceDiagram
  SocialAccountMutationCoordinator->>AuthenticationSessionIssuer: Issue
  AuthenticationSessionIssuer-->>SocialAccountMutationCoordinator: Failed
  SocialAccountMutationCoordinator->>IUnitOfWork: Rollback link/create/profile/token
```

### Concurrent social login

```mermaid
sequenceDiagram
  participant A as Request A
  participant DB as PostgreSQL constraints
  participant B as Request B
  A->>DB: Create/link provider subject
  B->>DB: Create/link same provider subject
  DB-->>A: One transaction succeeds
  DB-->>B: Unique conflict; no second link/account
```

## Error and retry rules

Expected failures use bounded response codes/messages; internal exceptions are
not returned. Before commit, all mutations roll back. A session persistence
failure rolls back account/link/OTP changes. Provider token validation is an
external read and occurs before opening the database transaction. Retrying the
same valid provider identity resolves the unique persisted link. OTP replay is
prevented by the atomic conditional consume operation.

## Security invariants

No raw token, OTP, phone, email, password or claims are placed in operational
logs or metric labels. Linking never trusts an unverified email. The same issuer
is used by password, social and phone OTP login. Refresh rotation and hashing
remain owned by `IRefreshTokenRepository`.
