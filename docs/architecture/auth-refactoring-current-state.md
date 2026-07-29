# Authentication refactoring: current-state assessment

## Baseline

Before this refactoring, `SocialLoginCommandHandler` was 541 lines with six
constructor dependencies, `VerifyPhoneOtpCommand.cs` was 521 lines with seven
handler dependencies, and `PureIdentityService` was 660 lines. Both handlers
owned lookup, policy, mutation, transaction control and session issuance.
Session issuance was duplicated between social, phone OTP and password login.

## Responsibility map

| Responsibility | Previous location | Target | Security/transaction requirement |
|---|---|---|---|
| Provider selection and token verification | Social handler | `SocialIdentityValidator` | Raw token remains local and is never logged |
| Provider/email account lookup | Social handler | `SocialAccountResolver` | Read-only; verified email only |
| Automatic-link decision | Social handler branches | `SocialAccountLinkingPolicy` | Pure; private relay and unverified email forbidden |
| Creation decision | Social handler | `SocialAccountCreationPolicy` | Pure; claimed email must be verified |
| Social link/create mutation | Social handler | `SocialAccountMutationCoordinator` | One explicit database transaction |
| OTP lookup/hash verification/attempts | OTP handler | `OtpConsumptionService` | Wrong attempts persisted before auth transaction |
| Atomic OTP consumption | OTP handler | `OtpConsumptionService` | Conditional database update inside transaction |
| Phone ownership decision | OTP handler | `PhoneOwnershipPolicy` | Deleted identity forbidden |
| Phone identity/profile mutation | OTP handler | `PhoneAccountMutationCoordinator` | Same transaction as OTP consumption |
| JWT/refresh/audit/last-login | Three separate paths | `AuthenticationSessionIssuer` | Canonical issuer; no token or PII logging |
| Identity reads | `PureIdentityService` | `IdentityAccountReader` | No mutation |
| Identity creation | `PureIdentityService` | `IdentityAccountCreator` | Identity initialization only |
| External login, roles, login state | `PureIdentityService` | `IdentityAccessService` | Structured Identity errors |
| Credential/lifecycle mutations | `PureIdentityService` | `IdentityCredentialService` | Security stamp and verification changes |

## Existing invariants retained

- Provider identities are unique by provider and provider key.
- Automatic email linking requires verified provider email and confirmed local email.
- Apple private-relay addresses never trigger automatic linking.
- OTP consumption, identity/profile creation, role assignment and refresh-token
  persistence commit together.
- Refresh tokens are hashed by the repository and existing active tokens rotate.
- Deleted identities cannot receive a new session.
- Phone lookup uses the deterministic HMAC lookup field, not decrypted scanning.

## Baseline risks addressed

The transaction boundary and rollback existed but were hidden inside large
handlers. Session issuance paths differed, audit data contained email, and
Identity framework operations were concentrated in one adapter. These are now
explicit components with focused tests. The remaining facade is documented in
the migration plan.
