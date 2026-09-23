# Phone Password Authentication and Reverification

## Architecture

Phone authentication follows the existing API → MediatR → Application contract → Infrastructure adapter flow. ASP.NET Identity remains authoritative for password policy, hashing, lockout, password-reset tokens, roles, and security stamps. PostgreSQL stores both Identity data and `PhoneOtpChallenges`, so registration and OTP consumption use one database transaction.

Numbers are accepted only in canonical E.164 form and stored in `NormalizedPhoneNumber`. A filtered unique PostgreSQL index is the final concurrency guard. `PhoneNumber` remains protected by the existing data-protection converter; lookup diagnostics use no raw phone value.

## API

All paths are below `/api/auth/phone`.

| Method and path | Authorization | Rate policy | Purpose |
|---|---|---|---|
| `POST registration/send-otp` | Anonymous | `send-otp` | Generic registration challenge response |
| `POST registration/verify` | Anonymous | `verify-otp` | Consume challenge, create password account and session |
| `POST login` | Anonymous | `auth-login` | Phone and password login with Identity lockout |
| `POST password-reset/send-otp` | Anonymous | `auth-password-reset` | Generic reset challenge response |
| `POST password-reset/verify` | Anonymous | `verify-otp` | Exchange OTP for an Identity reset token |
| `POST password-reset/confirm` | Anonymous | `auth-password-reset` | Reset password and revoke sessions |
| `GET reverification/status` | Bearer | — | Current state and deadlines |
| `POST reverification/send-otp` | Bearer | `send-otp` | User-bound stored-phone challenge |
| `POST reverification/verify` | Bearer | `verify-otp` | Restore `Verified` state |
| `POST change/send-otp` | Bearer | `send-otp` | User-bound new-phone challenge |
| `POST change/verify` | Bearer + password | `verify-otp` | Change phone and revoke sessions |

Legacy `phone/send-otp` and `phone/verify` return HTTP 410. Email and social authentication are unchanged.

## OTP invariants

Challenges contain a random ID, normalized phone, purpose, optional authenticated user, HMAC/hash, five-minute expiry, attempt counter, consumption timestamp, and a two-minute reservation. Purpose, user, phone, expiry, attempt state, reservation, and consumption are checked before use. An atomic conditional update prevents concurrent reservation; final consumption is idempotent. Registration persists Identity, role, profile, refresh token, and consumption inside the same PostgreSQL transaction.

## Reverification state machine

```mermaid
stateDiagram-v2
    NotConfigured --> Verified: ownership established
    Verified --> DueSoon: 14 days before day 180
    DueSoon --> GracePeriod: day 180
    GracePeriod --> Restricted: day 183
    Restricted --> Verified: valid reverification OTP
```

The hourly batch worker processes 100 identities at a time. It creates persisted in-app reminders at 14, 7, and 1 days, grace start, one day before grace end, and restriction. A cycle/event key prevents duplicate user-facing notifications during retries. SMS is used for OTP delivery; the current notification subsystem has no general email or push reminder contract, so those channels are not invented here.

When `PhoneVerification:EnforcementEnabled` is true, restricted users may read data and access phone recovery, refresh, and logout endpoints, but other HTTP mutations receive `PHONE_REVERIFICATION_REQUIRED`.

## Sessions and security

Password reset and phone change update the Identity security stamp and revoke active refresh tokens. Existing refresh-token rotation, reuse detection, and JWT security-stamp validation remain authoritative. Unknown-phone login executes an Identity password-hasher dummy path and returns the same `PHONE_AUTH_FAILED` contract as a wrong password. Anonymous OTP send responses are generic.

Audit entries contain user ID when known, event name, outcome, IP through the existing audit redaction conventions, and no OTP, password, token, or raw phone.

## Migration and rollout

1. Deploy compatible code with enforcement and reminder processing disabled.
2. Run `scripts/report-phone-normalization-conflicts.sql` after application-level normalization backfill.
3. Resolve duplicates and invalid values manually; never merge accounts automatically.
4. Apply `AddPhonePasswordAuthenticationAndReverification`.
5. Enable reminder processing, observe one cycle, then enable enforcement per environment.

Existing users with no trustworthy phone-confirmation timestamp remain `NotConfigured` and are not restricted. Assign the documented 30-day transition during the operational backfill. Rollback first disables enforcement and jobs, then uses the migration `Down` operation. The migration drops only newly introduced schema on rollback.

## Error codes

`PHONE_ALREADY_REGISTERED`, `PHONE_AUTH_FAILED`, `PHONE_NUMBER_INVALID`, `PHONE_NUMBER_ALREADY_IN_USE`, `OTP_INVALID`, `PASSWORD_POLICY_FAILED`, `USER_CREATE_FAILED`, `ROLE_ASSIGNMENT_FAILED`, `PHONE_REVERIFICATION_REQUIRED`, `PHONE_REVERIFICATION_NOT_CONFIGURED`, `RECENT_AUTHENTICATION_REQUIRED`, and `PASSWORD_RESET_INVALID`.

## Operational limitations

Data Protection keys must be shared across instances for Identity reset tokens. The reminder worker is database-idempotent but runs in every API instance; the deduplication key prevents duplicate notifications. Apply the conflict report before the unique index in databases containing legacy phone data.

## Known gaps (audit 2026-09-23)

See [docs/audit/phone-login-verification-2026-09-23.md](audit/phone-login-verification-2026-09-23.md). The audit findings are fixed; what remains is documented residual risk:

- Anti-enumeration: ineligible numbers receive a stored decoy challenge (same responses at verify time), but an eligible request also sends an SMS, so response time can differ, and `SMS_FAILED` is returned only for eligible numbers.
- A ban takes effect on existing access tokens within 5 minutes (security-stamp snapshot cache) unless the ban workflow invalidates `IUserSecurityStampCacheInvalidator`; no ban workflow exists yet.
- `PhoneNumber` is encrypted, but `NormalizedPhoneNumber` and `UserName` hold the E.164 number in plaintext (lookup column).
- The shared consent checkbox (`consent-checkbox`) shows hardcoded Arabic text on the phone register page in every language; it is legal copy and needs review before translation (audit F-14).
