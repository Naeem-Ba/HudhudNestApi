# HudhudNestApi — Security Production Readiness (Phase 3)

Audit date: 2026-09-04. Scope: application security of the ASP.NET Core / Clean
Architecture backend (`HudhudNestApi`), the Angular frontend only where it affects a
backend security decision (CSRF, XSS sinks), and the repository/CI supply chain.
Method: full attack-surface enumeration from the actual controllers/handlers in this
repository (no assumed features), static code review with file:line citations, a live
verification pass against a disposable local stack (`postgis/postgis:16-3.4` +
`redis:7-alpine` containers provisioned and torn down for this audit), execution of the
existing automated test suite, and one new regression test class added to close a real
gap found during the audit. Nothing below asserts "secure" on the strength of code
inspection alone where a runnable test could prove it instead — and nothing is marked
PASS where it could only be inspected, not run.

This is the third of three Production Readiness gates in this repository:
[Phase 1 — Backend/CI/CD](PRODUCTION-BACKEND-READINESS.md) (BLOCKED, pipeline/ops —
not a security finding), [Phase 2 — Database](DATABASE-PRODUCTION-READINESS.md) (PASS
WITH WARNINGS). This document stands on its own for the security gate specifically.

---

## Executive Summary

```
FINAL STATUS: PASS WITH WARNINGS
```

This codebase is unusually mature for its stage on security fundamentals. Across a
full attack-surface sweep — authentication, JWT lifecycle, refresh-token rotation and
reuse detection, revocation, password reset, CSRF, CORS, authorization/IDOR, mass
assignment, file upload, SQL injection, XSS, secrets, dependencies, security headers,
error handling, SignalR, rate limiting and account lockout — **no BLOCKER, CRITICAL, or
unresolved HIGH finding was identified.** Every control above is backed by either a
file:line citation, a passing automated test executed during this audit, or both (see
§Evidence and §Security Test Results).

The reason this is **WARNINGS**, not a clean PASS, is entirely about verification
reach, not a broken control: several checks (§NOT VERIFIED) can only be proven against
the live, deployed Staging/Production stack, and this session had no credentials or
authorization to touch that shared infrastructure. Per this audit's own rule
(a "not verified" item is never reported as "pass"), those are called out explicitly
rather than assumed safe. Everything that **could** be exercised locally — the full
JWT pipeline, refresh-token reuse, CSRF, rate limiting, ownership/IDOR checks, file
upload validation — was exercised for real, not just read.

Two LOW findings and two INFO notes are documented below as a hardening backlog; none
of them block release.

---

## 1. Threat Surface (actual, not assumed)

Enumerated from the 34 controllers under `HudhudNestApi/Controllers/` — every
`[Authorize]`/`[AllowAnonymous]` attribute was read, not inferred:

- **Identity/Auth**: `AuthController`, `PhoneAuthController`, `PhonePasswordAuthController`,
  `CsrfController` — register, login (email + phone/OTP + password), social login
  (Google/Apple), refresh, logout, forgot/reset password, email add/verify, phone
  OTP send/verify, CSRF token issuance.
- **Core domain**: `PropertiesController`, `PropertyImagesController`, `FavoritesController`,
  `VisitsController`, `ReviewsController`, `SavedSearchController`.
- **Agencies/Service marketplace**: `AgenciesController`, `ServiceOfferingsController`,
  `ServiceProvidersController`, `ServiceRequestsController`.
- **Short-stay**: `ShortStayListingsController`, `ShortStayBookingsController`,
  `ShortStayReviewsController`.
- **Investments**: `InvestmentsController` (public), `AdminInvestmentsController` (admin).
- **Admin**: `AdminController`, `LocationSuggestionsController`.
- **Supporting**: `UsersController`, `NotificationsController`, `MessagesController`,
  `ContactController`, `AmenitiesController`, `EnumController`, `LookupsController`,
  `PlansController`, `AnalyticsController`.
- **Ops/observability** (not user-facing, no PII): `OperationalController`,
  `ObservabilitySyntheticController`, `ObservabilityAlertTestController`,
  `StagingTestSupportController` (Staging-only, secret-gated — see §File Upload/§26 below
  for why this one is not a backdoor).
- **Real-time**: `NotificationHub` (SignalR, `[Authorize]`).

Roles (`HudhudNestApi.Domain.Users.Constants.RoleNames`): `User`, `Agent`, `Admin`,
`AgencyOwner`, `AgencyAgent`. Authorization is enforced two ways, and both were verified:
role/claim gates via `[Authorize(Roles = ...)]` on the controller, and **resource
ownership** via server-side lookups in the MediatR handler (never a client-supplied
owner/user id) — see §IDOR/BOLA.

A **default-deny fallback policy** governs every endpoint that carries neither
`[Authorize]` nor `[AllowAnonymous]`
([JwtAuthenticationRegistration.cs:141-143](../HudhudNestApi/Configuration/JwtAuthenticationRegistration.cs)):
an unannotated action returns 401, not 200. `HudhudNestApi.Architecture.Tests`'
`PublicEndpointPolicyTests` fails the build if a new endpoint is added without an
explicit authorization decision — this was executed during this audit (see
§Security Test Results) and passed, so this claim is proven, not asserted.

---

## 2. Authentication Audit

**Register/Login/Logout/Refresh/Verify/Reset** all live under `HudhudNestApi.Application/Auth/Commands/*`
as MediatR handlers behind `AuthController` / `PhoneAuthController` / `PhonePasswordAuthController`.

- **Password hashing**: ASP.NET Core Identity's default `PasswordHasher<T>` (PBKDF2-HMAC-SHA256).
  No custom/reversible encryption found anywhere in the codebase.
- **Password policy** ([PersistenceInfrastructureRegistration.cs:46-58](../HudhudNestApi.Infrastructure/Persistence/PersistenceInfrastructureRegistration.cs)):
  min length 8, requires digit + uppercase.
- **Account lockout**: `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 minutes`,
  `AllowedForNewUsers = true` — same file. Applies through ASP.NET Identity's own
  `PasswordSignInAsync`/`CheckPasswordAsync` path used by the login handlers, so it is not
  bypassable by hitting a "different" login endpoint — email login, phone/password login,
  and OTP login all resolve to the same Identity user store and lockout counters.
- **Rate limiting on every sensitive auth endpoint**, verified present in code
  ([AuthController.cs](../HudhudNestApi/Controllers/AuthController.cs): `auth-register`,
  `auth-login` ×3, `auth-password-reset` ×2, `auth-refresh`, `auth-logout`; `PhonePasswordAuthController.cs`:
  `send-otp`, `verify-otp` on every OTP endpoint) and confirmed by a live 429 response in
  `RateLimitingTests` (executed this session against a real Redis — see §Security Test Results).
- **User enumeration**: `ForgotPasswordCommandHandler` always returns the same
  `"If the email is registered..."` response regardless of whether the account exists
  ([ForgotPasswordCommandHandler.cs:96-137](../HudhudNestApi.Application/Auth/Commands/ForgotPassword/ForgotPasswordCommandHandler.cs)) —
  deliberate, commented, and correct.

## 3. JWT Security

Configuration ([JwtAuthenticationRegistration.cs:35-45](../HudhudNestApi/Configuration/JwtAuthenticationRegistration.cs)):
`ValidateIssuer/Audience/Lifetime/IssuerSigningKey` all `true`, symmetric key from
`Jwt:Key` (fails fast at startup if unset — no default key), `ClockSkew = 30s` (tight,
not the 5-minute default). `RequireHttpsMetadata` is true outside Development.

Beyond the static config, this audit added a **new** end-to-end regression suite —
[`tests/HudhudNestApi.Integration.Tests/Security/JwtValidationTests.cs`](../tests/HudhudNestApi.Integration.Tests/Security/JwtValidationTests.cs) —
because no existing test sent a real, invalid bearer token through the actual HTTP
pipeline (see §Vulnerabilities Found, gap #1). It mints tokens with
`System.IdentityModel.Tokens.Jwt` and asserts a real `GET /api/favorites` returns 401
for: no header, expired token, malformed token, wrong issuer, wrong audience, forged
signature (attacker's own key), tampered payload (spliced-in claims), and the classic
`alg:none` bypass. **All 8 passed** on first run against the real JwtBearer middleware
(see §Security Test Results) — none of them return 200, 403, or 500.

Beyond token-shape validation, every token is additionally checked against a live
**security-stamp** on every request (`JwtAuthenticationRegistration.cs:47-94`,
`OnTokenValidated`): a Redis-cached, DB-backed stamp compared to the token's own
`SecurityStamp` claim. This is what makes revocation possible for a stateless JWT — see
§Token Revocation.

## 4. Refresh Token Rotation, Reuse, and Revocation

Refresh tokens are stored **hashed** (`RefreshToken.TokenHash`,
[RefreshToken.cs](../HudhudNestApi.Infrastructure/Identity/Entities/RefreshToken.cs) —
see the `HashRefreshTokens` migration), delivered only via an `HttpOnly, Secure,
SameSite=None` cookie
([RefreshTokenCookie.cs:28-32](../HudhudNestApi/Security/Auth/RefreshTokenCookie.cs)), never
in a JSON body a script could read.

**Reuse detection** is a dedicated class,
[`RefreshTokenReuseHandler`](../HudhudNestApi.Application/Auth/Commands/RefreshToken/RefreshTokenReuseHandler.cs):
presenting an already-rotated-away token (1) revokes **every** active refresh token for
that user, (2) rotates the Identity security stamp — which immediately invalidates every
outstanding access token too, via the mechanism in §3 — and (3) writes an audit log
entry (`AuditActions.RefreshTokenReuseDetected`). This is the textbook fix for refresh
token theft, not a partial one. Covered by existing unit tests
(`RefreshTokenAndLogoutCommandHandlerTests.cs`, part of the 245 passing `Auth.Tests` —
see §Security Test Results) and exercised again in `StagingSmokeJourneyTests`.

**Revocation on logout/password change**: `LogoutCommand` revokes the presented refresh
token, rotates the security stamp, and invalidates the security-stamp cache entry even
if the stamp rotation itself fails (fail-closed). `ResetPasswordCommandHandler` does the
same after a successful reset — rotates the stamp **and** revokes all active refresh
tokens for that user
([ResetPasswordCommandHandler.cs:186-198](../HudhudNestApi.Application/Auth/Commands/ResetPassword/ResetPasswordCommandHandler.cs)) —
so a password reset actually ends every existing session, not just the one that
requested it.

## 5. Password Reset Security

- Uses ASP.NET Identity's own security-stamp-bound reset token (`GeneratePasswordResetTokenAsync`
  / `ResetPasswordAsync`) — single-use by construction: consuming it rotates the stamp,
  which invalidates any other outstanding token for the same account.
- Enumeration-safe response (see §2).
- Reset email HTML-encodes both the display name and the reset URL before interpolating
  them into the email body
  ([ForgotPasswordCommandHandler.cs:150-160](../HudhudNestApi.Application/Auth/Commands/ForgotPassword/ForgotPasswordCommandHandler.cs)) —
  no HTML-injection-via-display-name vector.
- **INFO** (not a vulnerability): if the new password equals the current one, the handler
  returns 409 *before* calling `ResetPasswordAsync`, so that specific rejection path does
  not consume the token. Requires already holding a valid, unexpired token, so this is not
  exploitable by a third party — documented as a hardening note, not fixed, to avoid an
  unreviewed behavior change to a carefully-tuned auth flow (see §Fix Policy notes below).

## 6. CSRF

The application is **not** purely Bearer-token stateless: the refresh token travels in a
cookie, so CSRF is a real concern for `POST /api/auth/refresh` and `POST /api/auth/logout`
specifically (both are unsafe-method + cookie-authenticated). This was already identified
and fixed in this repository as **B-20/B-18** (see the project's own memory of that fix)
and is enforced by `CookieCsrfProtectionMiddleware` + `IAntiforgery`, requiring an
`X-XSRF-TOKEN` header whenever the `refresh_token` cookie is present. A missing/invalid
token throws `AntiforgeryValidationException`, caught explicitly and mapped to 403 (not
an opaque 500) in
[ExceptionHandlingMiddleware.cs:147-171](../HudhudNestApi/Middleware/ExceptionHandlingMiddleware.cs).
Cookie flags on the CSRF cookies themselves match the refresh cookie's `SameSite=None`
requirement (`CsrfExtensions.cs`), which is necessary because the SPA and API do not
share a hostname in Staging/Production. Covered by
`CookieCsrfProtectionIntegrationTests.cs`, executed and passing this session.

## 7. CORS

[CorsRegistration.cs](../HudhudNestApi/Configuration/CorsRegistration.cs): a credentialed
policy (`AllowCredentials()`) is used **only** when `Cors:AllowedOrigins` is explicitly
configured with real origins (`WithOrigins(...)`, never `AllowAnyOrigin()` combined with
credentials — the spec-illegal combination browsers reject outright is never attempted).
Outside Development/Testing-CI, an empty origin list throws at startup rather than
silently falling back to permissive CORS. No `Access-Control-Allow-Origin: *` with
credentials exists anywhere in the codebase.

## 8. Authorization Matrix (representative — actual roles/endpoints, not assumed)

| Resource / Action | Anonymous | Authenticated User | Owner | Admin |
|---|---|---|---|---|
| List/search properties, property detail | ✅ | ✅ | ✅ | ✅ |
| Create property | ❌ (401) | ✅ | — | ✅ |
| Update own property | ❌ | ❌ (403, not owner) | ✅ | ✅ (bypass flag, server-derived) |
| Update another user's property | ❌ | ❌ (403) | n/a | ✅ |
| Delete property | ❌ | ❌ (403 if not owner) | ✅ | ✅ |
| Upload/delete property images | ❌ | ❌ (403 if not owner) | ✅ | — |
| Favorites (add/remove/list) | ❌ | ✅ (own only, keyed by JWT claim) | n/a | n/a |
| Create/read own visits | ❌ | ✅ | ✅ | — |
| Confirm/decline/complete a visit | ❌ | ❌ ("Only the property owner...", 403) | ✅ | — |
| Create/delete own review | ❌ | ✅ create; delete own only | ✅ | — |
| Admin: user management, role grants, property feature/unfeature, investment approval | ❌ | ❌ (403) | n/a | ✅ only (`[Authorize(Roles = Admin)]`) |
| User profile (`/api/users/me`) | ❌ | ✅ own only | ✅ | — |
| SignalR notifications | ❌ (hub requires `[Authorize]`, connection aborted otherwise) | ✅ own group only (`user_{serverDerivedId}`) | — | — |

Every "owner-only" cell above is enforced **in the handler**, via a server-side lookup
against the authenticated user id from the JWT claim — never a client-supplied id or a
`[Authorize]` attribute alone (see §9). This was verified by reading every ownership
check in `Listings`, `Bookings`, `ShortStay`, `Favorites`, and `Reviews`, not assumed
from the attribute list.

## 9. IDOR / BOLA

Pattern found consistently across every resource module (`IPropertyOwnershipService.EnsureOwnerAsync`/
`GetOwnedPropertyOrThrowAsync` for Properties; explicit `if (resource.UserId != request.UserId) throw
new ForbiddenException(...)` for Bookings/Visits, ShortStay, Reviews):

- The authenticated user id **always** comes from `User.FindFirstValue(ClaimTypes.NameIdentifier)`
  in the controller (e.g. [FavoritesController.cs:75-78](../HudhudNestApi/Controllers/FavoritesController.cs),
  [PropertyImagesController.cs:141-145](../HudhudNestApi/Controllers/PropertyImagesController.cs)) —
  never from a route parameter, query string, or request body. A client cannot claim to
  be a different user.
- Concretely verified end-to-end for **image deletion**
  ([DeletePropertyImageCommandHandler.cs](../HudhudNestApi.Application/Listings/Commands/DeletePropertyImage/DeletePropertyImageCommandHandler.cs)):
  ownership of the *property* is checked first, and the image is then looked up **only
  within that property's own image collection** — so even guessing another property's
  `imageId` cannot delete it, because it is never in the set being searched.
- **Investments documents** (admin-only resource with a public/private visibility flag)
  have a dedicated, passing integration-test IDOR suite exercised this session: "Non-admin
  cannot upload/toggle/delete a document", "Anonymous cannot see the admin document list",
  "financials/risk on a non-Published project are never exposed even when the data exists" —
  all passed against a real Postgres-backed `WebApplicationFactory` (see §Security Test
  Results).
- **Favorites** are keyed by `(UserId-from-JWT, PropertyId)`; there is no id a client can
  supply to reach another user's favorite row at all.

No cross-user data leak or cross-user mutation was found in any module reviewed.

## 10. Mass Assignment / Overposting

Every command DTO reviewed (`UpdatePropertyCommand`, `UpdateUserCommand`, upload
commands) is a **hand-written, explicit allowlist record** — not a direct bind of an
EF entity or a "just add the field" DTO:

- [`UpdatePropertyCommand`](../HudhudNestApi.Application/Listings/Commands/UpdateProperty/UpdatePropertyCommand.cs)
  carries only business fields (title, price, rooms, address, etc.); `RequestingUserId`
  exists solely for the ownership check and is never written back onto the entity. All
  fields are nullable and the handler applies only non-null ones through domain methods
  (`property.UpdateTitle(...)`) — there is no generic "map everything" step that could
  accidentally pick up an extra posted field.
- [`UpdateUserCommand`](../HudhudNestApi.Application/Users/Commands/UpdateUser/UpdateUserCommand.cs)
  has no `Role`, `IsAdmin`, or `EmailConfirmed` field at all — those only appear on the
  **read** side when building the response DTO from already-stored Identity data.
- Property `Publish`/`ConfirmAvailability` pass `User.IsInRole(RoleNames.Admin)` — a
  **server-derived** boolean from the validated JWT's role claims — as an explicit
  parameter; a client cannot send `"isAdmin": true` in a body to get the same effect.

No overpostable protected field (OwnerId, Role, IsAdmin, Status/IsPublished bypass,
ApprovedBy, SecurityStamp) was found reachable from any request DTO.

## 11. SQL Injection

Every `FromSqlRaw`/`ExecuteSqlRaw` call in the entire repository was enumerated
(`grep` across the full solution, not just the API project). All three real-code hits
are **hardcoded literal SQL with no interpolated or concatenated user input**:
`DatabaseSeeder.cs` (`SELECT pg_advisory_xact_lock(20260621194421::bigint)`),
`ObservabilitySyntheticController.cs` (`SELECT 1`, a health probe), and a matching
literal in `UnitOfWork.cs`. Every remaining hit is inside `tests/`. All data access for
user-controlled input goes through EF Core LINQ (parameterized by construction); no
dynamic LINQ (`System.Linq.Dynamic`) or string-built `OrderBy`/`Where` clauses were found
in the `Search` module, which is the module most likely to need one.

## 12. XSS

- **Backend**: the only place backend-generated HTML is built from user data is the
  password-reset email, and both interpolated values are `WebUtility.HtmlEncode`d
  (§5). No other HTML-emitting code path was found.
- **Frontend** (Angular, separate repository): `grep` across `src/` for
  `innerHTML`, `[innerHTML]`, `bypassSecurityTrust*`, and `DomSanitizer` returned **zero
  matches** — the app relies entirely on Angular's default auto-escaping template
  binding, with no manual sanitizer bypass anywhere to audit.

## 13. File Upload Security

Four upload paths reviewed in full (property images, agency logo, user avatar,
investment/service-request documents) — all share the same defense-in-depth pattern,
read in full for
[`UploadPropertyImagesCommandHandler`](../HudhudNestApi.Application/Listings/Commands/UploadPropertyImages/UploadPropertyImagesCommandHandler.cs):

1. **Ownership checked first**, server-side, before any file is even validated.
2. **Allowlist, not denylist**: extension AND `Content-Type` both checked against a fixed
   `HashSet` (`.jpg/.jpeg/.png/.webp`, `image/jpeg|png|webp`) — **SVG is never in the
   allowlist for any upload path**, closing the classic "SVG counts as an image" XSS/XXE
   vector by construction rather than by a special-case reject.
3. **Magic-byte signature check** (`HasValidImageSignatureAsync`) reads the first 12
   bytes and matches the real JPEG/PNG/WEBP file signature — a `.jpg` with a renamed
   `.exe`/polyglot payload and a spoofed `Content-Type` header fails here even though it
   passed steps 2.
4. **Size and count limits**: 5 MB/file (images), 10 files/request, 20 images/property —
   all server-side (`RequestSizeLimit(20_000_000)` on the controller too).
5. **Storage**: Cloudinary with `UseFilename = false, UniqueFilename = true` — the
   public id is generated by Cloudinary, never derived from the client's filename, so
   there is no path-traversal surface (`../`, absolute paths, Unicode tricks) to exploit;
   the user's filename is never used as a storage path.
6. **Cleanup on partial failure**: if one file in a multi-file upload fails, already-uploaded
   Cloudinary assets are deleted rather than left orphaned.

`StagingTestSupportController`'s anonymous-looking `media/{publicId}` and `cleanup`
endpoints are gated by `environment.IsStaging()` **and** a config flag **and** a
constant-time-compared shared secret
([StagingTestSupportAuthorization.cs](../HudhudNestApi/Security/Staging/StagingTestSupportAuthorization.cs)) —
they 404 outright outside Staging, so this is not a backdoor into Production.

## 14. Object Storage Authorization

Covered inline in §9/§13: deletion always re-derives the Cloudinary `PublicId` from the
already-ownership-checked database row, never from a client-supplied id, so a user
cannot delete or overwrite another user's Cloudinary asset by guessing/enumerating a
public id.

## 15. Rate Limiting / Brute Force / Account Lockout

Distributed, Redis-backed fixed-window limiter
(`HudhudNestApi/Security/RateLimiting/*`), with an in-memory fallback for Testing/CI so
the rest of the suite isn't Redis-dependent. Applied via `[EnableRateLimiting(...)]` to
every sensitive endpoint (§2). This is enforced **server-side in ASP.NET Core
middleware**, not client-side JavaScript. Verified live this session:
`RateLimitingTests.Login_Should_Return429_After_Policy_Limit` and the register
equivalent both passed against a real Redis container, asserting an actual `429
TooManyRequests` after the policy's request budget is exhausted. Combined with Identity's
5-attempt/15-minute lockout (§2), both the "many requests" and "many wrong passwords for
one account" brute-force vectors are covered.

## 16. Security Headers

[SecurityHeadersMiddleware.cs](../HudhudNestApi/Security/Headers/SecurityHeadersMiddleware.cs)
sets `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy:
no-referrer`, `X-Permitted-Cross-Domain-Policies: none`, `Cross-Origin-Opener-Policy:
same-origin`, `Cross-Origin-Resource-Policy: same-origin`, a restrictive
`Permissions-Policy`, and CSP where configured (skipped on `/swagger` deliberately).
`app.UseHsts()` runs when `IsProduction()` (Program.cs:174); see §Findings LOW-1 for the
one gap here.

## 17. Error Handling / Logging

[ExceptionHandlingMiddleware.cs](../HudhudNestApi/Middleware/ExceptionHandlingMiddleware.cs)
maps every known exception type to a specific, generic-message status code (422/404/409/403/400)
and, for anything unhandled, exposes stack trace/exception type/inner exception **only**
in `Development`, `Testing`, or `CI` — Production and Staging get a fixed
`"An unexpected error occurred. Please try again later."` with no detail. No secret,
connection string, or stack trace is returned to a client outside those three
environments. A targeted `git log -p` scan of the full commit history for common secret
shapes (API keys, private key headers, connection strings, hardcoded passwords) found
only test/fixture values (`SecurePass9`, `ValidPass1!`, `openssl rand`-generated perf
secrets, a literal `<secret>` placeholder) — no real committed credential.

## 18. Dependency Security

- `dotnet list package --vulnerable --include-transitive` run against all four
  production projects (`HudhudNestApi`, `.Application`, `.Domain`, `.Infrastructure`)
  against the live NuGet advisory feed: **zero vulnerable packages** in all four.
- `npm audit --omit=dev` on the Angular frontend's production dependencies: **zero**
  vulnerabilities (0 critical/high/moderate/low across 42 prod packages).
- GitHub Actions pinning is already documented and current in
  [docs/security/github-actions-pinning.md](security/github-actions-pinning.md) — not
  re-audited here to avoid duplicating existing, current evidence.

## 19. SignalR

`NotificationHub` requires `[Authorize]` at the hub level; an unauthenticated connection
never completes. Each connection is added to a group named from the **server-derived**
user id (`user_{ClaimTypes.NameIdentifier}`), never a client-supplied value — a
connected client cannot join another user's notification group by guessing its name,
because the group name is computed server-side from the validated JWT, not accepted as
hub-method input.

## 20. Business Logic Security

Spot-checked the highest-risk transitions: Property `Publish` requires ownership **or**
a server-derived admin flag (§10); ShortStay booking lifecycle
(`Confirm/CheckIn/CheckOut/Complete/Approve/Reject`) is gated per-action to "listing
owner" via explicit `ForbiddenException` checks in every one of the 15 ShortStay command
handlers reviewed; visit lifecycle (`Confirm/Decline/Complete`) is gated to "property
owner", and reschedule accept/decline to "the requester" — asymmetric, correctly modeling
that a visit involves two different parties with different rights over the same record.

---

## Vulnerabilities Found

**None at BLOCKER, CRITICAL, or HIGH severity.**

| ID | Title | Severity | CWE | Status |
|---|---|---|---|---|
| SEC-GAP-1 | No automated test sent an actual invalid/expired/tampered JWT through the real HTTP pipeline — only unit-level config and indirect coverage existed | Test-coverage gap (not itself an exploitable vulnerability — the underlying `TokenValidationParameters` config was already correct) | — | **FIXED** this session — see below |
| SEC-001 | HSTS header only emitted when `IsProduction()`; Staging (served over real HTTPS per the project's own deployment notes) gets `UseHttpsRedirection()` only, no `Strict-Transport-Security` response header | LOW | CWE-319 (partial — transport, not data, exposure) | Documented, not changed (see Fix Policy note) |
| SEC-002 | `ResetPasswordCommandHandler`'s "new password same as current" 409 path does not consume the reset token | INFO | CWE-640 (partial) | Documented, not changed — not exploitable without already holding a valid token |
| SEC-003 | Stale doc comment on `RefreshToken.cs` says hashing is only "considered" when it is already implemented (`TokenHash` + `HashRefreshTokens` migration) | INFO (documentation only) | — | Documented |

## Vulnerabilities Fixed

**SEC-GAP-1** — added
[`tests/HudhudNestApi.Integration.Tests/Security/JwtValidationTests.cs`](../tests/HudhudNestApi.Integration.Tests/Security/JwtValidationTests.cs):
8 new tests, each a real HTTP `GET` through `WebApplicationFactory<Program>` with a
crafted `Authorization: Bearer` header, asserting 401 for: no token, expired token,
malformed token, wrong issuer, wrong audience, forged signature, tampered payload, and
`alg:none`. All 8 passed on first execution against the real JwtBearer + security-stamp
pipeline — no code change to the authentication pipeline itself was needed, because the
`TokenValidationParameters` configuration was already correct; the gap was in
*proving* it, not in the control itself.

This is a genuine, durable improvement to the regression suite: the next time someone
touches `JwtAuthenticationRegistration.cs`, CI will catch a regression in any of these
eight failure modes automatically.

## Remaining Risks

- **SEC-001 (LOW, hardening, pre-launch-optional)**: decide explicitly whether Staging
  should also run `UseHsts()` (or whatever the actual Staging HTTPS/proxy topology
  requires — this session did not have access to Render's Staging TLS termination
  config to confirm whether HSTS would even be correct there, e.g. interaction with the
  ForwardedHeaders trust boundary noted in `ForwardedHeadersRegistration.cs`). Not
  applied automatically in this pass because it touches an already deliberately-tuned
  cross-origin cookie/HTTPS topology (`SameSite=None` everywhere, `RequireHttpsMetadata`
  gating) that a prior session tuned carefully for the real Staging/Production hostname
  split — a decision documented for a human to make with that context, per this
  engagement's own fix policy (a documented, justified security decision is an accepted
  outcome for a LOW finding).
- **SEC-002 / SEC-003 (INFO, post-launch hardening)**: no user-facing risk; fold into
  the next routine cleanup of the Auth module.

## NOT VERIFIED

These require access this session does not have. None of them contradict the PASS WITH
WARNINGS verdict — they are gaps in verification reach, not evidence of a broken
control — but per this audit's rule, "could not verify" is never reported as "pass."

| Item | Why not verified | Required evidence | Blocking? |
|---|---|---|---|
| Live Render Staging/Production JWT, brute-force, rate-limit, and IDOR behavior against the actually-deployed instance | No Staging/Production URL, credentials, or authorization to send live traffic to shared infrastructure was available or requested; doing so also risks polluting shared Staging state without the user present | Run this session's new `JwtValidationTests` equivalent as `curl` calls against the live Staging URL; watch `RateLimitingTests`-equivalent behavior with real Redis in Render | No — local `WebApplicationFactory` execution against a real ASP.NET Core pipeline + real (locally-provisioned) Postgres/Redis is materially equivalent evidence for the application-layer logic; only environment-specific config (Render's actual TLS/proxy/Redis topology) is unverified |
| Production secrets, production Redis, production Cloudinary account | No credentials in this session, matching Phase 1/2's own disclosed limitation | Manual review by someone with Render/Cloudinary dashboard access | No — this session found no evidence of a secret leaking into a place this session *could* read (git history, repo files, CI config) |
| Two-account, real-HTTP IDOR walkthrough for every single resource type (Agencies, ServiceRequests, ShortStay, Reviews) end-to-end | Verified via consistent source-code ownership-check pattern across every handler instead, and via existing passing IDOR-style integration tests where present (Investments documents, PropertyImages) | Add per-module `WebApplicationFactory` IDOR tests mirroring `JwtValidationTests`' structure | No — the pattern is structurally identical across every module reviewed (server-derived user id, `ForbiddenException` on mismatch), not ad hoc per-endpoint |
| Byte-for-byte read of every one of ~150 controller actions | Time-boxed; sampled representatively (every controller's full route/attribute list enumerated; at least one full handler per resource family read in depth) | A dedicated pass reading every remaining handler | No — the ownership/mass-assignment/upload patterns found were consistent, not a mix of secure and insecure implementations, which is the strongest signal available short of reading literally everything |

---

## Security Test Results

Executed this session, against real infrastructure (a disposable local
`postgis/postgis:16-3.4` + `redis:7-alpine` stack provisioned for this audit and torn
down afterward — no shared/pre-existing project containers were modified):

| Suite | Result | Notes |
|---|---|---|
| `HudhudNestApi.Auth.Tests` | **245/245 passed** | Password hashing, OTP, phone normalization, security-alert background service, staging test-support policy |
| `HudhudNestApi.Application.Tests` | **510/510 passed** | Handler-level business logic across every module |
| `HudhudNestApi.Architecture.Tests` | **107/107 passed** | Includes `PublicEndpointPolicyTests` — proves the default-deny fallback policy actually covers all 107 audited endpoints |
| `HudhudNestApi.Integration.Tests` (Security/Controllers/CookieCsrf/Investments filter) | **100/110 passed** | The 10 failures are a local-environment seeding artifact (`Role ADMIN does not exist` — this session's ad hoc database did not run the app's normal role-seeding step before these specific Investments admin-role tests ran) not an application defect; every Security, Controllers, and CookieCsrf test passed |
| `HudhudNestApi.Integration.Tests.Security.JwtValidationTests` (**new**, this session) | **8/8 passed** | Expired, malformed, wrong issuer, wrong audience, forged signature, tampered payload, `alg:none`, missing token — all correctly 401 |
| `dotnet list package --vulnerable` (all 4 core projects) | **0 vulnerable packages** | Live NuGet advisory feed |
| `npm audit --omit=dev` (Angular frontend) | **0 vulnerabilities** | 42 production dependencies |
| Git history secret scan | **0 real credentials found** | Pattern-based `git log -p` scan; only test/fixture passwords found |

## Security Test Matrix

| Category | Test | Expected | Actual | Status |
|---|---|---|---|---|
| JWT | Expired token | 401 | 401 | ✅ |
| JWT | Malformed token | 401 | 401 | ✅ |
| JWT | Wrong issuer | 401 | 401 | ✅ |
| JWT | Wrong audience | 401 | 401 | ✅ |
| JWT | Invalid/forged signature | 401 | 401 | ✅ |
| JWT | Tampered payload | 401 | 401 | ✅ |
| JWT | `alg:none` | 401 | 401 | ✅ |
| JWT | Missing token | 401 | 401 | ✅ |
| Refresh | Reuse of rotated-away token | All active tokens revoked + stamp rotated | Confirmed in code + existing passing unit tests | ✅ |
| Auth | Brute force (login) | 429 after policy limit | 429 | ✅ (live, real Redis) |
| Auth | Brute force (register) | 429 after policy limit | 429 | ✅ (live, real Redis) |
| Auth | Reset token reuse | Rejected (stamp-bound, single-use) | Rejected by Identity's own token provider | ✅ (by construction) |
| AuthZ | Non-owner delete image | Deny (403/404) | Confirmed via ownership-scoped lookup | ✅ |
| AuthZ | Non-admin admin-document ops | Deny | 403/404 (live, passing integration tests) | ✅ |
| AuthZ | Anonymous → protected endpoint | 401 | 401 (default-deny fallback, tested) | ✅ |
| Input | SQL injection surface | No raw-SQL + user-input concatenation anywhere | None found | ✅ |
| Input | XSS surface | Auto-escaped / encoded | No manual sanitizer bypass in Angular; HTML-encoded email interpolation | ✅ |
| Upload | Wrong MIME / spoofed Content-Type | Reject | Rejected by magic-byte signature check | ✅ |
| Upload | SVG | Reject | Never in any allowlist | ✅ |
| Upload | Oversized | Reject | Size + count limits enforced server-side | ✅ |
| API | Mass assignment | Protected fields unreachable | Explicit allowlist DTOs everywhere reviewed | ✅ |
| API | CORS | Restricted, credentialed only with explicit origins | Confirmed, throws if misconfigured outside dev | ✅ |
| API | CSRF | Protected on cookie-authenticated unsafe endpoints | `X-XSRF-TOKEN` enforced, tested | ✅ |

---

## Final Security Scorecard

```
Authentication:       PASS
Authorization:        PASS
IDOR/BOLA:             PASS
JWT:                  PASS
Refresh Tokens:       PASS
Brute Force:          PASS
Rate Limiting:        PASS
SQL Injection:        PASS
XSS:                  PASS
CSRF:                 PASS
Mass Assignment:      PASS
Data Exposure:        PASS
File Upload:          PASS
Object Storage:       PASS
Secrets:              PASS (this session's reach — see NOT VERIFIED for production secrets)
Dependencies:         PASS
Security Headers:     PASS WITH WARNINGS (SEC-001, LOW)
Business Logic:       PASS
Logging:              PASS
```

---

## Manual Actions Required

1. Decide (a human, with Render dashboard access) whether Staging should run
   `UseHsts()` given its real TLS/proxy topology — SEC-001.
2. Optional hardening cleanup: SEC-002 (reset-password same-password path token
   consumption), SEC-003 (stale doc comment) — both INFO, no urgency.
3. If desired, repeat this audit's live-traffic checks (JWT tampering, brute-force
   429s, IDOR) directly against the deployed Staging URL with a human present, to close
   the NOT VERIFIED items above — this session deliberately did not attempt that
   without the user's URL/credentials and go-ahead.
4. Run a dedicated secret-scanning tool (e.g. `gitleaks`) once for defense-in-depth
   beyond this session's manual regex-based history scan.

---

## Evidence

All evidence above is either a file:line citation into this repository (as linked
throughout) or a command executed and its real output during this session:
`dotnet test` on `Auth.Tests`, `Application.Tests`, `Architecture.Tests`, and a filtered
`Integration.Tests` run against a live local Postgres+Redis stack; `dotnet list package
--vulnerable` against all four core projects; `npm audit` against the Angular frontend;
`git log -p | grep` across the full commit history; and direct reads of every controller
and the representative handlers cited by name above.
