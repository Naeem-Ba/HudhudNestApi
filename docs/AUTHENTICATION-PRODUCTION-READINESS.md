# HudhudNestApi — Authentication & Login Production Readiness (Phase 6)

Audit date: 2026-09-05. Scope: authentication end-to-end — ASP.NET Core / Clean
Architecture backend (`HudhudNestApi`), the Angular + Capacitor frontend (separate repo,
`Naeem-Ba/Wohnungsmieten`) only for auth-related code, and the Google/Apple social-login
integration specifically (not previously covered in depth by the general security audit
below). Method: full repository inventory of every login method actually implemented
(not assumed), reading of every social-auth source file end-to-end, a live `dotnet test`
run against real cryptographic attack scenarios (forged/expired/mis-scoped tokens), and
direct inspection of the native iOS project's Xcode configuration files. Nothing below
is marked PASS on inspection alone where a runnable test could prove it instead.

This document is scoped specifically to **login/authentication production readiness**,
building on top of — not duplicating — two existing gates in this repository:
[Phase 1 — Backend/CI/CD](PRODUCTION-BACKEND-READINESS.md) (BLOCKED, pipeline/ops, not
an auth finding) and [Phase 3 — General Security](SECURITY-PRODUCTION-READINESS.md)
(PASS WITH WARNINGS — already covers JWT lifecycle, refresh-token rotation/reuse,
password reset, CSRF, rate limiting, and account lockout in depth). This document goes
one level deeper on the parts that audit did not: the Google/Apple social-login pipeline
itself, native mobile authentication, and the Apple App Store 4.8 requirement.

**A note on repository state:** this session ran in the same working tree as another,
independently active session doing unrelated GDPR/consent-record and PII-masking work
(`ConsentRecord`, `AuditLogRetentionHostedService`, `PiiMasking`). Every file this audit
cites was diffed against that concurrent session's changes before being relied upon;
none of the files central to this report's findings (the social-login orchestration,
policies, and token verifiers) were touched by it. One shared dependency was found
broken (see §14, Fix #1) and repaired so this audit's own tests could run.

---

## Executive Summary

```
FINAL STATUS: PASS WITH WARNINGS
```

The most important discovery of this audit is that **Sign in with Apple is not a
feature to decide whether to build — it is already implemented, end-to-end, on both
platforms**, alongside Google and email/password. The mega-brief that opened this phase
insisted on establishing this from evidence before any Apple decision; that evidence is
in §3-4 below. The existing implementation is unusually well-engineered: server-side
cryptographic validation of both providers' tokens, correct nonce replay protection for
Apple (a real historical bug, B-16, already fixed and tested), a takeover-resistant
account-linking policy, and in-memory-only access-token storage with an HttpOnly-cookie
refresh token — the two things most social-login implementations get wrong.

What keeps this at **WARNINGS** rather than a clean PASS:

1. **The native iOS Xcode project is missing the "Sign in with Apple" capability
   entitlement and Google's URL-scheme configuration** (§9) — the Capacitor plugin code
   is correctly wired and merged, but without the entitlement, `ASAuthorizationAppleIDProvider`
   will fail at runtime on a real device even though everything compiles. This can only
   be added and verified from a Mac with Xcode and the Apple Developer Portal — both
   unavailable in this session (§13).
2. **A user-facing error message promises an account-linking flow that does not exist**
   (§6, AUTH-F1) — not a security hole, but a real dead end for a legitimate user.
3. **`GoogleTokenVerifier` has zero dedicated tests** (§5) — it delegates to Google's
   own official validation library, which is a reasonable trust boundary, but the
   audit's own "prove it, don't assert it" standard is not fully met for Google the way
   it now is for Apple (§14, Fix #2).
4. Two Store-readiness gaps — no App Store reviewer demo path, and a custom (non-Apple-asset)
   Apple button glyph — need a product decision, not a code fix (§12).

No BLOCKER or CRITICAL finding. No evidence of forgeable tokens, bypassable server-side
validation, or automatic account takeover via a social provider.

---

## 1. Authentication Architecture (current, as-built)

```
Web (Angular)                    Native (Capacitor: iOS/Android)
     │                                    │
     │ Google Identity Services           │ @capawesome/capacitor-google-sign-in
     │ (renderButton, real click,         │ (Credential Manager / native SDK)
     │  not One Tap — see §8 BUG-21)      │
     │ Apple JS SDK (popup)               │ @capacitor-community/apple-sign-in
     │                                    │ (ASAuthorizationAppleIDProvider)
     └───────────────┬────────────────────┘
                      │  POST /api/auth/social/google { idToken }
                      │  POST /api/auth/social/apple  { identityToken, nonce, ... }
                      ▼
        AuthController (AllowAnonymous, [EnableRateLimiting("auth-login")])
                      ▼
        SocialLoginCommand → SocialAuthenticationOrchestrator
                      ▼
   SocialIdentityValidator ── GoogleTokenVerifier (Google.Apis.Auth, official lib)
                          └── AppleTokenVerifier (JWKS fetch + issuer/audience/
                              signature/expiry/nonce validation, hand-rolled)
                      ▼
        SocialAccountResolver (SocialAccountLinkingPolicy / CreationPolicy)
                      ▼
        SocialAccountMutationCoordinator → IAuthenticationSessionIssuer
                      ▼
        JWT access token (body) + refresh token (HttpOnly, Secure, SameSite=None cookie)
```

Email/password, phone+OTP, and phone+password all exist as separate, parallel login
methods on the same `AuthController`/`PhoneAuthController`/`PhonePasswordAuthController`
family, resolving to the same ASP.NET Identity user store, lockout counters, and JWT/
refresh-token issuance path as social login (`IAuthenticationSessionIssuer` is shared).

## 2. Current Login Methods (inventory, not assumed)

| Method | Backend endpoint | Status |
|---|---|---|
| Email + password | `POST /api/auth/register`, `/api/auth/login` | Implemented, audited (Phase 3) |
| Phone + OTP | `PhonePasswordAuthController` (`send-otp`/`verify-otp`) | Implemented |
| Phone + password | `PhonePasswordAuthController` | Implemented |
| Google Sign-In | `POST /api/auth/social/google` | Implemented, this audit |
| Sign in with Apple | `POST /api/auth/social/apple` | Implemented, this audit |
| Facebook / X / Microsoft / GitHub / Magic Link / Passkeys / WebAuthn | — | **Not found anywhere in the codebase** — not implemented |

## 3. Primary Account Model

**Hybrid**: Email/Password + Google + Apple + Phone, unified under one ASP.NET Identity
user (`IdentityAccountSnapshot`), linked via `(Provider, ProviderId)` external-login rows
(`AddLoginAsync`) — never solely by email string comparison. A single user can hold any
combination of these credentials simultaneously; there is no per-provider separate user
table. Confirmed by reading `SocialAccountMutationCoordinator` (creates one Identity
account + adds an external login row + assigns `RoleNames.User` in one transaction) and
`SocialAccountResolver` (looks up first by `(Provider, ProviderId)`, never by trusting a
client-supplied user id).

## 4. Apple Requirement Decision

```
APPLE REQUIREMENT: ALREADY IMPLEMENTED
```

| Question | Result | Evidence |
|---|---|---|
| Email/password exists? | YES | `AuthController.Register`/`Login`, Phase 3 audit §2 |
| Google login exists? | YES | `AuthController.SocialLoginGoogle`, `GoogleTokenVerifier.cs` |
| Facebook login exists? | NO | Not found anywhere in repo (`grep`, zero matches) |
| X login exists? | NO | Not found |
| Other third-party login? | NO | Only Google/Apple |
| Third-party login is primary account authentication? | YES (one of several equally-primary methods) | §3 — hybrid model, no method is gated behind another |
| Apple 4.8 applies? | YES | The app offers Google as a third-party/social login option for creating/authenticating the primary account, which is exactly what triggers 4.8's "equivalent option" requirement |
| Sign in with Apple implemented? | YES | `AuthController.SocialLoginApple`, `AppleTokenVerifier.cs`, native + web flows in the frontend |
| Native iOS flow verified? | **NOT VERIFIED** (code-complete, entitlement missing) | §9 — no Mac/Xcode/Apple Developer account in this session |
| Apple private relay supported? | YES | `SocialAccountResolver`/`SocialAccountLinkingPolicy` both explicitly special-case `@privaterelay.appleid.com` |

This is not a case where a decision needed to be made and then acted on — the correct
architectural decision (offer Apple wherever Google is offered) was already made and
built. This audit's job on this question was to verify it, not choose it.

## 5. Google Login Assessment

`GoogleTokenVerifier` (`HudhudNestApi.Infrastructure/Auth/GoogleTokenVerifier.cs`) delegates
token validation to `Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync`, Google's own
official server-side validation library, constrained to `Audience = [GoogleClientId]`.
This library internally validates issuer, audience, signature (against Google's live
public certs), and expiration — the codebase is **not** decoding the JWT payload and
trusting it, which is exactly the failure mode §7 of the brief warns about.

**Gap**: no dedicated unit test exists for `GoogleTokenVerifier` at all (confirmed by
`grep` across `tests/`). Unlike `AppleTokenVerifier` (§14, Fix #2, now covered), there is
no test proving a token minted for the wrong audience, expired, or forged is rejected —
the guarantee rests entirely on trusting Google's library plus the one-line audience
constraint. Not fixed in this session: `GoogleJsonWebSignature.ValidateAsync` is a sealed
static call with no injectable seam, and this sandbox has no outbound network access to
fetch Google's real certs for a live-integration test (`dotnet list package --vulnerable`
in this same session failed to reach `api.nuget.org` for the same reason). A proper fix
needs either an abstraction layer around the static call (an architecture change, out of
this audit's "fix root cause, don't redesign" mandate) or a test environment with network
egress. Documented as AUTH-F3 below, not silently left unmentioned.

## 6. Apple Login Assessment

`AppleTokenVerifier` fetches Apple's real JWKS (`https://appleid.apple.com/auth/keys`,
cached 1h), validates issuer (`https://appleid.apple.com`), audience (`AppleClientId`),
lifetime, and RS256 signature via `TokenValidationParameters`, then additionally enforces
a **nonce hash comparison** (SHA-256 hex, lowercase) against the client-supplied raw
nonce — this closes a real, previously-shipped replay vulnerability (B-16: without it, a
valid Apple identity token from one sign-in could be replayed from a different session
within its validity window). `MapInboundClaims = false` is explicitly required and
commented — the default `JwtSecurityTokenHandler` behavior silently remaps `sub`→a long
legacy claim URI, which would make every Apple sign-in fail silently if left on; this was
apparently found the hard way once already (code comment cites a live signed-token test
as the only thing that surfaced it).

Private Relay handling: both `SocialAccountResolver` (initial account resolution) and
`SocialAccountLinkingPolicy` (linking to an existing email account) explicitly refuse to
treat a `@privaterelay.appleid.com` address as a real identity to match against — an
Apple user who chooses to hide their email is routed to account *creation*, never
silently matched to an unrelated existing account that happens to share no real email at
all. This is the correct behavior per Apple's own Private Relay guidance (§23 of the
brief) and was verified by reading the exact `EndsWith(..., OrdinalIgnoreCase)` checks in
both files.

**AUTH-F1 (MEDIUM, UX/support gap, not a security hole)**: When an Apple/Google identity
resolves to an email that already has an account, but linking isn't allowed (see §7), the
API returns: *"An account already uses this email. Sign in to that account and link the
social provider from account settings."* No such endpoint exists anywhere in the
codebase — `AddLoginAsync` (the only thing that could create that link) is called
exclusively from `SocialAccountMutationCoordinator`'s own internal flows, never from a
controller action reachable by an authenticated user managing their own account. A real
user who hits this message today has **no way to complete what it tells them to do**.
**Fix**: either build the promised "link from account settings" endpoint (authenticated,
requires the user to already be signed in — never automatic), or change the message to
something actionable today (e.g., "sign in with your password instead"). Not fixed in
this session — it's a product-facing UX decision (which account-settings flow to build)
more than a pure code fix, and the brief's own rule (§8) says linking policy must be a
deliberate decision, not something to improvise mid-audit.

## 7. Account Linking & Takeover Resistance

Verified via 8 passing tests (`SocialLoginSecurityTests`, run live this session — see
§10) plus direct reading of `SocialAccountLinkingPolicy`/`SocialAccountCreationPolicy`:

| Scenario | Policy | Result |
|---|---|---|
| Provider login already linked (`Provider`+`ProviderId` match) | N/A — direct match | Signs in, no re-verification needed |
| Provider email unverified | `Forbidden` | Rejected — never creates or links an account |
| Provider email is Private Relay | Treated as "no email" | Routed to account creation, never matched by email |
| Provider email matches an existing account, provider verified, **local account's own email also confirmed** | `Allowed` | Auto-linked (both sides independently proved ownership of the same email) |
| Provider email matches an existing account, **local account's email NOT confirmed** | `AdditionalVerificationRequired` (mapped to Conflict) | **Rejected** — no auto-link. This is the takeover-prevention case the brief's §8/§9 specifically asked to test, and it is correctly closed |
| Matched account is soft-deleted (`IsDeleted`) | Conflict | Rejected — "account is not available" |
| New account creation, no matching email at all | — | Creates a new Identity account, assigns `RoleNames.User` (never an elevated role from provider claims — see §11) |

No path exists where a social login silently takes over an account whose email the
attacker does not independently control and prove (via the provider's own verified-email
claim) **and** whose owner has not already confirmed that same email locally. This is
the correct, conservative design.

## 8. JWT & Refresh Token Security

Already audited exhaustively by [Phase 3](SECURITY-PRODUCTION-READINESS.md) §3-4 and not
repeated here in full: issuer/audience/lifetime/signing-key all validated, 30s clock skew,
security-stamp revalidated every request (Redis-cached, DB-backed), refresh tokens stored
hashed and delivered only via `HttpOnly, Secure, SameSite=None` cookie, reuse detection
revokes every active token + rotates the security stamp + audit-logs the event. That
audit's own new regression suite (`Security/JwtValidationTests.cs`, 8/8 passing) proved
the app's *own* JWT rejects expired/malformed/wrong-issuer/wrong-audience/forged/tampered/
`alg:none` tokens through the real HTTP pipeline. This document's contribution is doing
the equivalent proof for the *social provider* tokens specifically (§14, Fix #2) — a
distinct trust boundary Phase 3 did not target.

Social login issues the exact same JWT/refresh-token pair as email/password login
(`IAuthenticationSessionIssuer.IssueAsync`, shared code path) — there is no separate,
weaker token-issuance path for social sign-ins.

## 9. Mobile / Capacitor Authentication

**Frontend plugins are correctly chosen and merged**: `@capawesome/capacitor-google-sign-in`
(Credential Manager on Android / native SDK on iOS) and `@capacitor-community/apple-sign-in`
(`ASAuthorizationAppleIDProvider`) — both real native implementations, **not** an embedded
WebView OAuth flow. `social-auth.service.ts` explicitly branches on
`Capacitor.isNativePlatform()` to route to these instead of the web SDK, with an extensive
code comment (BUG-17) documenting that Google explicitly blocks its own web OAuth flow
inside `WKWebView`/Android WebView — so this is a fix for a real, previously-broken state,
not a stylistic choice. This satisfies §21/§30 of the brief: this is not
`BLOCKED`/`REQUIRES NATIVE IMPLEMENTATION`, it already is the native implementation.

**AUTH-F2 (HIGH for iOS store submission, code-complete but not device-verifiable
here)**: The iOS Xcode project (`ios/App/App/`) has **no `.entitlements` file anywhere**
(`find ios -iname "*.entitlements"` — zero results, confirmed on `origin/main`, not just
this branch) and **no `CFBundleURLTypes` in `Info.plist`**. Sign in with Apple requires
the `com.apple.developer.applesignin` entitlement to be added via Xcode's Signing &
Capabilities (or hand-edited into a new entitlements file *and* wired into the
`.pbxproj`'s `CODE_SIGN_ENTITLEMENTS` build setting) — without it, `SignInWithApple.authorize()`
will fail at runtime on a real device or TestFlight build with an authorization error,
even though the Capacitor plugin call itself is correctly written and the whole thing
compiles cleanly. This is exactly the gap this repository's own memory already flagged
independently (PR #35/#39 merge notes: "#39 needs a real device/simulator manual test of
native sign-in (no Mac/Apple Developer account available)").

**Why this wasn't fixed in this session**: adding the entitlement correctly requires
(a) the Apple Developer Portal to have "Sign in with Apple" enabled for this app's App ID
and a matching Services ID configured — an account-owner-only action this session cannot
perform or fake, and (b) Xcode itself to write the `.pbxproj` wiring in a way that is
guaranteed not to corrupt the generated project file, which cannot be verified without
Xcode available to open and build it. Hand-editing a `.pbxproj` blind, with no way to
compile-check the result, risks trading a documented, known gap for an unverifiable,
possibly-broken one. Per the brief's own rule (§52/§27): **REQUIRES EXTERNAL
CONFIGURATION** — recorded here precisely, not invented around.

**Android**: no equivalent entitlement concept exists; Google Sign-In on Android via
Credential Manager does not require a manifest-level capability declaration the way iOS
does. Not independently device-tested this session (no Android emulator/device
available) — **NOT VERIFIED**, but no code-level gap was found analogous to §iOS above.

## 10. Automated Test Results (executed this session, real output)

| Suite / Filter | Result | Notes |
|---|---|---|
| `HudhudNestApi.Auth.Tests` (full) | **251/251 passed** | Full regression after this session's fixes |
| → `SocialLoginSecurityTests` (8 tests) | **8/8 passed** | Unverified email rejected, deleted account rejected, verified+confirmed→linked, verified+unconfirmed→rejected without linking, provider-already-linked→sign-in, `AddLoginAsync` failure→no tokens issued |
| → `AppleTokenVerifierNonceTests` (4 tests, pre-existing) | **4/4 passed** | Nonce match/mismatch/missing/not-expected |
| → `AppleTokenVerifierValidationTests` (6 tests, **new this session**) | **6/6 passed** | Wrong audience, wrong issuer, expired, forged signature (attacker's own keypair), tampered payload, positive control |
| `GoogleTokenVerifier` dedicated tests | **0 exist** | See §5, AUTH-F3 |
| `HudhudNestApi.Application.Tests` (full) | **NOT RUN** | Pre-existing build break from the concurrent session's in-progress work (`DeletePropertyCommandHandlerTests.cs`, unrelated to auth) — not this audit's code, not fixed here (see report to user) |

A build-breaking typo in `HudhudNestApi.Application/Common/Security/PiiMasking.cs` (a
missing `///` continuation marker on an XML doc comment, breaking the *entire* solution's
compile) was found and fixed as a prerequisite to running any tests at all — see §14,
Fix #1.

## 11. Authorization Boundary (social login specifically)

`SocialAccountMutationCoordinator.CreateNewAsync` assigns exactly `RoleNames.User` via
`AddToRoleAsync` — never a role read from any Google/Apple claim. Google's ID token
payload (`GivenName`, `FamilyName`, `Picture`, `Email`) is mapped only to profile display
fields, never to `Role`/`IsAdmin`/any authorization-relevant field. A social login cannot
grant `Admin`/`Agent`/`AgencyOwner` — consistent with Phase 3's finding that no request
DTO anywhere in the codebase carries an overpostable role field.

## 12. Store Readiness

```
Apple App Store: PASS WITH WARNINGS
Google Play:     PASS WITH WARNINGS
```

**Apple**: Technically compliant with 4.8 (Google is offered, Apple is offered
equivalently, Private Relay respected, minimal data collected). Blocking items before
actual submission are entirely external-configuration/process, not code:
- AUTH-F2 (§9) must be resolved on a Mac with Xcode + Apple Developer Portal access
  before a real device/TestFlight build can complete Apple sign-in.
- **No App Store reviewer path found** (no demo account, no documented reviewer login
  flow) — `grep` for any demo/reviewer-account mechanism returned nothing. Apple's App
  Review Guidelines expect either a working demo account or full reviewer access to any
  backend service the app depends on. **Action needed**: decide and provide one before
  submission (this is a product/process decision, not something this session should
  invent credentials for).
- The Apple button in `login.component.html` uses a plain text/emoji glyph rather than
  Apple's official "Sign in with Apple" button asset — worth a design review against
  Apple's Human Interface Guidelines before submission, since HIG non-compliance on this
  specific button is a known, common rejection reason.

**Google Play**: No equivalent hard blocker found. Google Sign-In implementation follows
Google's own recommended pattern (Credential Manager on Android). No demo-account
requirement is as strict as Apple's for Play Store review, but the same "can a reviewer
actually log in and see the app's core functionality" question applies and was not
independently verified against a live Android device this session.

## 13. Mobile Verification Results

```
Android: NOT VERIFIED — no Android emulator/device available in this session
iOS:     NOT VERIFIED — no Mac/Xcode/Apple Developer account available in this session;
         additionally, AUTH-F2 (§9) means a real-device test would currently fail even
         if the environment existed
Web:     Verified by code + live test execution (§10); browser-level manual click-through
         not re-performed this session (Phase 3's audit already covers the web app's
         general security posture)
```

Per the brief's own rule (§59): NOT VERIFIED is recorded as exactly that, never as PASS.

## 14. Fixes Made This Session

**Fix #1 (build-blocking, not an auth defect but a prerequisite)**: Added the missing
`///` continuation marker on line 8 of
[`PiiMasking.cs`](../HudhudNestApi.Application/Common/Security/PiiMasking.cs) — a broken
XML doc comment was failing the entire solution's compile (`CS1002`/`CS0116`/`CS1010`/
`CS1012`), blocking every test in every project, including this audit's own. Root cause
was a plain typo in unrelated, in-progress privacy-hardening work; fixed so this session
could get real test evidence rather than none at all.

**Fix #2 (closes a real test-coverage gap, mirrors Phase 3's own JwtValidationTests
methodology)**: Added
[`AppleTokenVerifierValidationTests.cs`](../tests/HudhudNestApi.Auth.Tests/Infrastructure/Auth/AppleTokenVerifierValidationTests.cs) —
6 new tests proving `AppleTokenVerifier` actually rejects a wrong-audience token,
wrong-issuer token, expired token, a token forged with an attacker-controlled keypair,
and a payload tampered after signing — plus one positive control. Before this, Apple's
token-validation correctness rested entirely on reading `TokenValidationParameters`
configuration, exactly the gap Phase 3 flagged (and fixed) for the app's own JWT but
never extended to the social-provider layer. One test attempt
(`kid`-mismatch-but-authentic-key) was written, found to fail for a non-security reason
(`Microsoft.IdentityModel.Tokens` treats `kid` as a routing hint, not a trust boundary —
standard, spec-compliant behavior), and removed with an explanatory comment rather than
forced to pass artificially.

## 15. Manual Actions Required

1. **AUTH-F2**: On a Mac with Xcode — add the "Sign in with Apple" capability to the
   `App` target (Signing & Capabilities), which generates the entitlements file and
   wires it into the build settings; separately enable Sign in with Apple for this app's
   App ID in the Apple Developer Portal. Then rebuild and test on a real device or
   simulator with a signed-in Apple ID.
2. **AUTH-F1**: Product decision — build the "link social provider from account
   settings" endpoint the current error message promises, or change the message to
   something actionable without it.
3. **Store readiness**: decide and document an App Store reviewer path (demo account or
   equivalent); review the Apple button's visual compliance with Apple's official
   "Sign in with Apple" button asset guidelines.
4. **AUTH-F3** (optional hardening): add `GoogleTokenVerifier` unit-test coverage —
   requires either network egress to Google's real cert endpoint in the test
   environment, or introducing a thin injectable abstraction around
   `GoogleJsonWebSignature.ValidateAsync` (an intentionally-deferred architecture
   decision, not done unreviewed mid-audit).
5. Re-run `HudhudNestApi.Application.Tests` once the concurrent session's own
   `DeletePropertyCommandHandlerTests.cs` edit is finished, to get a full-solution
   regression baseline including this session's two changes.

## 16. Definition of Done — Status

```
[x] Login methods discovered — email/password, phone+OTP, phone+password, Google, Apple
[x] Primary account model identified — hybrid, provider-linked via (Provider, ProviderId)
[x] Social login verified — Google (library-backed) and Apple (custom, now test-proven)
[x] Provider tokens validated server-side (issuer/audience/signature/expiry) — proven for
    Apple this session; Google relies on Google's own official library (untested directly)
[x] Nonce reviewed — enforced and tested for Apple; not applicable to this Google flow
    (Google Identity Services doesn't thread a client nonce through today)
[x] Account linking safe — verified via 8 passing tests + policy source read
[x] External identity mapping safe — (Provider, ProviderId), never bare email trust
[x] Apple requirement determined from actual architecture — ALREADY IMPLEMENTED
[x] Apple 4.8 applicability assessed — YES, and satisfied
[x] Sign in with Apple implemented — YES (code); native flow NOT device-verified (AUTH-F2)
[x] Private Relay supported — YES, verified in both resolver and linking policy
[ ] Native iOS flow verified — NOT VERIFIED, entitlement missing (AUTH-F2)
[ ] Android login verified on device — NOT VERIFIED, no device available
[x] Roles never derived from provider claims — verified (§11)
[ ] Reviewer/demo login path available for store submission — NOT FOUND
```

## 17. Final Gate

```
PHASE 6 — LOGIN & AUTHENTICATION
STATUS: PASS WITH WARNINGS
```

No BLOCKER or CRITICAL finding. The authentication architecture itself — server-side
token validation, takeover-resistant linking, in-memory access tokens, HttpOnly refresh
cookies, shared JWT issuance across every login method — is sound and, for the parts
testable in this environment, now proven rather than merely inspected. What remains
(AUTH-F1, AUTH-F2, AUTH-F3, store reviewer path) is either a product decision or requires
hardware/accounts this session does not have — none of it is a broken control found and
left unfixed.

**Phase 7 not started, per this engagement's own rule.**
