# PHASE 2 — Error Handling, Validation & Consistent Error UX

Date: 2026-09-17
Scope: `HudhudNestApi` (this repo) + `HudhudNest` (Angular frontend, separate repo at
`C:\Users\naeem\OneDrive\Desktop\Notiz\HudhudNest\Wohnungsmieten`).
Covers spec items 8–12 only (Login messages, Password Validation, OTP/Phone Errors, Session
Errors, unified error display) — Search/Filters/Favorites/Sharing/Dashboard are explicitly out
of scope and were not touched.

## A. Documentation Reviewed

| Document | Relevant requirement | How it affected implementation |
|---|---|---|
| `docs/architecture/auth-refactoring-current-state.md`, `auth-refactoring-migration-plan.md`, `authentication-orchestration.md` | What Phase 1 (`fix/auth-phase1-backend-cleanup`, PR #148, already merged to `origin/master`) actually changed | Confirmed Phase 1 is complete before touching anything; the orchestrator/session-issuer split is the current architecture and was not modified. |
| `docs/phone-password-authentication-and-reverification.md` | Documents `PHONE_AUTH_FAILED` as intentionally generic for unknown-phone/wrong-password; lists the existing error-code contract | Confirmed in code (`PhoneAuthenticationWorkflow.cs`); used as the baseline for which new codes were safe to *add* vs. which existing ones must not be renamed. |
| `docs/password-policy.md` | `PasswordErrorCodes` contract, "the code is the contract", `errorCodes` dict is additive | Directly shaped the frontend fix (`login.component.ts`'s register form now reuses this contract) and the decision to add new OTP codes additively rather than rename existing ones. |
| `docs/architecture/email-confirmation-and-delivery.md` | **"Login is not gated on confirmation... a deliberate product choice."** | Directly prevented a mistake: the spec's §4 "Unverified Email" flow was **not** implemented, because the docs explicitly document the opposite as an intentional decision. Reversing it would have violated the mandatory rule against reversing a correct Phase 1 decision. |
| `AuthController.cs`'s own inline comments (`SECURITY FIX: every failure now answers with one identical 401...`) | Documents *why* email login collapses unknown-account/wrong-password/locked/banned into one generic response — a deliberate anti-enumeration fix | Caught and reverted a mistake of my own: an initial backend change gave phone login a distinct `ACCOUNT_LOCKED` code, which would have reintroduced the exact same enumeration oracle this comment describes, for phone numbers instead of emails. Reverted before completion (see Findings). |
| `PhoneAuthController.cs`'s own comments (`Removed: POST /api/auth/phone/send-otp` / `/verify`) | States the legacy OTP-as-credential flow was replaced by `PhonePasswordAuthController` and the two old routes now return `410 Gone` | Found that the frontend's `login.component.ts` still called these dead routes from a live, reachable tab — see Finding 1. |
| `docs/investment/PHASE-1-*.md`, `PHASE-2-IMPLEMENTATION-REPORT.md` | N/A — different feature (Investment module), same phase-numbering convention | Read first to rule out relevance; confirmed unrelated to auth/error-handling, not used further. |

Full repository documentation tree (`docs/**`, `README-otp-sms-forwardedheaders-tests.md`,
`SECURITY_HARDENING_REPORT_AR.md`, `AQARTECH-SERVICES-IMPLEMENTATION-REPORT.md`) was enumerated;
only the entries above were relevant to error handling/auth. Observability, performance, Redis,
backup/recovery, valuation, and privacy docs were confirmed out of scope by title and skipped.

## B. Initial Findings

### Finding 1 — Broken flow, not just a message problem (Critical)
**Problem:** The login page's embedded "register with phone via OTP" tab still called
`POST /auth/phone/send-otp` and `POST /auth/phone/verify`.
**Root cause:** Those two routes were deliberately retired to `410 Gone` when
`PhonePasswordAuthController` replaced them (see `PhoneAuthController.cs`'s own comment), but the
old inline form in `login.component.ts`/`.html` was never removed — only a "this moved" notice was
added *above* it, with the broken form still rendered and functional-looking underneath.
**Affected layer:** Frontend (`login.component.ts`, `.html`, `OtpApiService`, and two fully
orphaned standalone routes `/auth/send-otp` / `/auth/verify-otp`, unlinked from anywhere in the UI
but still reachable by direct URL, also hitting the same dead routes).
**Risk:** A user could ignore the notice, fill in the old form, and get an untranslated English
technical message (`"Use the phone registration or password login endpoints."`) with no
indication their input was ever wrong — the worst kind of misleading error, because the flow
itself can never succeed.

### Finding 2 — OTP verify collapses every failure reason into one code (High)
**Problem:** `PhoneAuthenticationWorkflow.ValidateAndReserveAsync` returned a bare `null` for
wrong code, expired code, already-used code, and 3-attempts-exceeded alike, so
`RegisterAsync`/`VerifyReverificationAsync`/`VerifyPhoneChangeAsync` all answered `OTP_INVALID`
and `VerifyPasswordResetAsync` answered `PASSWORD_RESET_INVALID` regardless of the real reason.
**Root cause:** The reservation/consumption guard clauses shared one early-return path.
**Affected layer:** Backend (`HudhudNestApi.Infrastructure/Auth/Services/PhoneAuthenticationWorkflow.cs`).
**Risk:** Directly violates spec §8's explicit requirement to distinguish wrong/expired/used/rate-
limited OTP; a user who mistyped a code and a user whose code expired 4 minutes ago see an
identical, unhelpful message.

### Finding 3 (considered, then reverted) — Phone-login lockout distinction reintroduces an enumeration oracle
**Problem considered:** Distinguish `ACCOUNT_LOCKED` from `PHONE_AUTH_FAILED` on
`PhoneAuthenticationWorkflow.LoginAsync`, matching spec §4's "don't show generic invalid-password
when the real cause is rate-limit/lockout".
**Why reverted:** `AuthController.cs`'s own comment documents that email login used to do exactly
this and was deliberately changed to a single generic 401, because a distinct lockout response
lets an attacker confirm an address/number has an account by brute-forcing it until the response
changes. Applying the "distinguish lockout" instruction literally to phone login would have
reintroduced that same class of bug for phone numbers. Session-error clarity (spec §10) must not
come at the cost of reintroducing an anti-enumeration fix Phase 1 already made (see the mandatory
rule against reversing a correct prior decision). **Reverted before landing; not present in the
final diff.**

### Finding 4 — Refresh-token failures have no error code, only free-text message (Medium)
**Problem:** `RefreshTokenResult` had `Success`/`Message` only. Expired, reuse-detected, and
rotation-conflict all reached the frontend as an indistinguishable `401` + English sentence.
**Affected layer:** Backend (`RefreshTokenCommand.cs`) and frontend
(`refresh-auth.interceptor.ts`).
**Risk:** Spec §10 explicitly requires "Refresh Failure handled correctly" and a distinct message
for a genuinely-ended session; with no code, the frontend could only pattern-match English text
(explicitly forbidden by spec §13) or treat every failure identically.

### Finding 5 — `notifySessionExpired()` fires into the void (High — direct spec §10 violation)
**Problem:** `AuthService.sessionExpiredEvent`/`sessionExpired$` existed, was correctly emitted
only for a genuine "was logged in, refresh failed, on a page that needs a session" case (not for
anonymous browsing, not for a stray 401 on a public page) — but **no code anywhere in the app
subscribed to it**. The user was silently redirected to `/auth/login` with zero explanation.
**Affected layer:** Frontend (`auth.service.ts`, and the missing subscriber).
**Risk:** This is precisely the "Refresh Token Expired → show correct message" scenario from spec
§10/§22 Scenario 4 — and it silently failed.

### Finding 6 — Toast/inline error duplication on auth flows (Medium — direct spec §17 violation)
**Problem:** `ApiErrorInterceptor` showed a global toast for essentially every failed `/api/*`
call with no opt-out, while `LoginComponent.startLockout()`, `phone-register.page.ts`,
`phone-password-reset.page.ts`, `phone-change.page.ts`, and `phone-reverify.page.ts` **each also**
called `toast.error()` alongside their own inline `error()`/`message` display. An account-lockout
response could show a global toast + a component toast + an inline banner simultaneously.
**Affected layer:** Frontend (`api-error.interceptor.ts` + 5 auth components).

### Finding 7 — Hardcoded Arabic fallback strings, bypassing i18n entirely (Medium — direct spec §14 violation)
**Problem:** `ApiErrorInterceptor.getFriendlyApiErrorMessage()` returned hardcoded Arabic strings
for every status-code fallback and never injected `TranslateService`, so an English- or German-
language user could see Arabic text for any API failure with no `message` field in its body. A
second, entirely unused `ApiErrorService` (217 lines, dead code, confirmed via repo-wide grep)
duplicated this same anti-pattern with its own contradictory string table.
**Affected layer:** Frontend.

### Finding 8 — Register-form password-breach message not translated in one of two register forms (Low)
**Problem:** `register.page.ts` correctly used `getPasswordRejectionMessages()` to translate
`PASSWORD_BREACHED` and friends; the second, older register form embedded inside
`login.component.ts` did not — it displayed the raw English backend text.
**Affected layer:** Frontend.

## C. Changes Made

### Backend (`HudhudNestApi`)

| File | Change | Reason |
|---|---|---|
| [`PhoneAuthenticationWorkflow.cs`](HudhudNestApi.Infrastructure/Auth/Services/PhoneAuthenticationWorkflow.cs) | `ValidateAndReserveAsync` now returns an `OtpValidationOutcome` (`Challenge` + typed `OtpFailureReason?`) instead of a bare nullable challenge. New codes: `OTP_EXPIRED`, `OTP_ALREADY_USED`, `OTP_WRONG`, `OTP_RATE_LIMITED` (registration/reverification/phone-change); `PASSWORD_RESET_EXPIRED`, `PASSWORD_RESET_ALREADY_USED`, `PASSWORD_RESET_RATE_LIMITED` (password reset, additive — `PASSWORD_RESET_INVALID` keeps its original meaning for wrong-code/not-found). `NotFound` (purpose/user mismatch, concurrent-reservation race) deliberately stays generic — not a legitimate user's error, and distinguishing it would help an attacker probing challenge IDs. | Finding 2 |
| [`RefreshTokenCommand.cs`](HudhudNestApi.Application/Auth/Commands/RefreshToken/RefreshTokenCommand.cs) | `RefreshTokenResult` gains `ErrorCode`; new `RefreshTokenErrorCodes` (`REFRESH_TOKEN_EXPIRED`, `REFRESH_TOKEN_REUSE_DETECTED`, `REFRESH_TOKEN_ROTATION_CONFLICT`) assigned at each of the four failure branches. | Finding 4 |
| [`AuthController.cs`](HudhudNestApi/Controllers/AuthController.cs) | `POST /auth/refresh`'s 401 body now includes `errorCode`. | Finding 4 |
| [`RefreshTokenAndLogoutCommandHandlerTests.cs`](tests/HudhudNestApi.Auth.Tests/Application/Commands/RefreshTokenAndLogoutCommandHandlerTests.cs) | Asserts `ErrorCode` on the reuse-detected and rotation-conflict cases; added a new test for the previously-uncovered "unknown token hash" (`Expired`) case. | Test coverage for Finding 4's fix |

**Not changed, deliberately:** `PhoneAuthenticationWorkflow.LoginAsync`'s lockout handling (see
Finding 3) — reverted to its original single-code behavior.

### Frontend (`HudhudNest`)

| File | Change | Reason |
|---|---|---|
| `login.component.ts` / `.html` | Removed the dead phone-OTP-registration Step 1/2 form (`onSendPhoneOtp`, `onVerifyPhoneOtp`, `onResendOtp`, `onChangePhoneNumber`, `getOtpErrorMessage`/`getOtpErrorCode`, all associated fields) and the `OtpApiService`/`ConsentApiService` constructor params it alone needed. The tab now shows only the pre-existing "moved" notice + link to `/auth/phone-register`. `getRegisterErrorMessage()` now calls `getPasswordRejectionMessages()` first. | Findings 1, 8 |
| `send-otp.page.ts`, `verify-otp.page.ts` | **Deleted** (`/auth/send-otp`, `/auth/verify-otp` routes removed from `app.routes.ts`) — unlinked from anywhere in the app, and their only backend endpoints are `410 Gone`. | Finding 1 |
| `otp-api.service.ts` | **Deleted** — its only three callers were the three files above. | Finding 1 |
| `auth.model.ts` | Removed `OtpPurpose`, `SendOtpRequest/Response`, `VerifyOtpRequest/Response`, `OTP_ERROR_CODES` — all dead after the above (confirmed via repo-wide grep before deletion). | Finding 1 cleanup |
| `phone-auth-errors.ts` | `ERROR_CODE_TO_KEY` gains the 7 new backend codes from Finding 2's fix, mapped to translated messages. | Finding 2 |
| `auth.service.ts` | `sessionExpiredEvent`/`notifySessionExpired()` now carry a `'expired' \| 'revoked'` reason instead of firing a bare event. | Finding 5 |
| `refresh-auth.interceptor.ts` | Reads the new `errorCode` from a failed `/auth/refresh` response; maps `REFRESH_TOKEN_REUSE_DETECTED` → `'revoked'`, everything else → `'expired'`. | Findings 4, 5 |
| `app.component.ts` | **New subscriber** to `authService.sessionExpired$` — shows a translated toast (`AUTH.ERRORS.SESSION_EXPIRED` or `SESSION_REVOKED`) before the login redirect. | Finding 5 |
| `api-error.interceptor.ts` | (a) `getFriendlyApiErrorMessage` now takes `TranslateService` and reads `API_ERRORS.*` i18n keys instead of hardcoded Arabic, including the toast title. (b) `shouldShowToast` now excludes any `/auth/` request — every auth-flow page already shows its own inline error (confirmed exhaustively across all 9 auth pages before making this change). | Findings 6, 7 |
| `login.component.ts` (`startLockout`), `phone-change.page.ts`, `phone-reverify.page.ts`, `phone-register.page.ts`, `phone-password-reset.page.ts` | Removed the redundant `toast.error(message)` call from each `setError`/lockout path, keeping the inline `error()`/`message` display as the single source of truth. | Finding 6 |
| `api-error.service.ts` | **Deleted** — confirmed zero consumers anywhere in the app (only its own file referenced its own class). | Finding 7 cleanup |
| `public/i18n/{en,de,ar}.json` | New keys: `API_ERRORS.*` (10 keys — generic status-code fallbacks + toast title), `AUTH.ERRORS.OTP_EXPIRED`, `OTP_TOO_MANY_ATTEMPTS`, `PASSWORD_RESET_EXPIRED`, `PASSWORD_RESET_ALREADY_USED`, `SESSION_EXPIRED`, `SESSION_REVOKED` — all three languages, verified with matching key counts (77 `AUTH.ERRORS` keys, 10 `API_ERRORS` keys in each file). | Findings 2, 5, 7 |
| `api-error.interceptor.spec.ts` | Added `TranslateModule.forRoot()` (required once the interceptor injects `TranslateService`). | Fixing a regression introduced by the Finding 7 fix, caught by the existing test suite |
| `refresh-auth.interceptor.redirect.spec.ts` | Two new tests: `REFRESH_TOKEN_REUSE_DETECTED` → `sessionExpired$` emits `'revoked'`; any other/no code → `'expired'`. | Test coverage for Finding 5's fix |
| `login.component.spec.ts` | Removed the now-inapplicable OTP-branching tests; fixed the constructor call (11 → 9 args); added a test proving `PASSWORD_BREACHED` now translates instead of showing raw server text. | Keeping the suite accurate after Findings 1/8 |

## D. Error Mapping (this phase's scope)

| Scenario | Backend Status | Error Code | Frontend Message Key | UI Location |
|---|---:|---|---|---|
| Email login — wrong password / unknown account / locked / banned | 401 | `INVALID_CREDENTIALS` (unchanged — deliberately generic, anti-enumeration) | `AUTH.ERRORS.INVALID_CREDENTIALS` | Inline (`auth-alert`) |
| Phone login — wrong password / unknown number / locked | 400 | `PHONE_AUTH_FAILED` (unchanged — same anti-enumeration reasoning) | `AUTH.ERRORS.INVALID_CREDENTIALS` (component ignores the code by design) | Inline |
| Phone OTP verify — wrong code | 400 | `OTP_WRONG` (new) | `AUTH.ERRORS.OTP_WRONG` | Inline |
| Phone OTP verify — expired | 400 | `OTP_EXPIRED` (new) | `AUTH.ERRORS.OTP_EXPIRED` (new) | Inline |
| Phone OTP verify — already used | 400 | `OTP_ALREADY_USED` (new) | `AUTH.ERRORS.OTP_ALREADY_USED` | Inline |
| Phone OTP verify — 3 wrong attempts | 400 | `OTP_RATE_LIMITED` (new) | `AUTH.ERRORS.OTP_TOO_MANY_ATTEMPTS` (new) | Inline |
| Password-reset OTP verify — expired/used/rate-limited | 400 | `PASSWORD_RESET_EXPIRED` / `_ALREADY_USED` / `_RATE_LIMITED` (new, additive) | matching new keys | Inline |
| Password breach on registration | 422 | `PASSWORD_BREACHED` (unchanged) | `AUTH.ERRORS.PASSWORD_BREACHED`, now also from `login.component.ts`'s embedded register form | Inline, field-adjacent |
| Access token expired, refresh succeeds | 401 → silent refresh | n/a | none shown | n/a (by design) |
| Refresh token expired | 401 | `REFRESH_TOKEN_EXPIRED` (new) | `AUTH.ERRORS.SESSION_EXPIRED` (new) | Toast, before redirect to login |
| Refresh token reuse detected | 401 | `REFRESH_TOKEN_REUSE_DETECTED` (new) | `AUTH.ERRORS.SESSION_REVOKED` (new) | Toast, before redirect to login |
| Anonymous browsing a public page | n/a | n/a | none shown | n/a (unchanged — already correct) |
| 403 Forbidden (authenticated, insufficient permission) | 403 | `ForbiddenException.Code` / framework default | `API_ERRORS.FORBIDDEN` (new, generic fallback) | Toast (non-auth endpoints only) |
| Generic API failure with no body message | varies | n/a | `API_ERRORS.*` (new, translated, was hardcoded Arabic) | Toast (non-`/auth/` endpoints only) |
| Legacy phone OTP registration flow | 410 Gone | `PHONE_OTP_FLOW_DEPRECATED` (backend, unchanged) | **Route removed from the frontend entirely** — no longer reachable | n/a |

## E. Tests

| Test | Result |
|---|---|
| Backend: `HudhudNestApi.Auth.Tests` (256 tests, +11 vs. pre-Phase-2 baseline of 245) | PASS |
| Backend: `HudhudNestApi.Application.Tests` (1074 tests) | PASS |
| Backend: `HudhudNestApi.Architecture.Tests` (164 tests) | PASS |
| Backend: `dotnet build HudhudNestApi.sln` | PASS — 0 warnings, 0 errors |
| Backend: `dotnet format --verify-no-changes` (changed files) | PASS |
| Frontend: `npm run typecheck` | PASS |
| Frontend: `npm run build` (production config) | PASS — 0 errors; confirmed no `send-otp-page`/`verify-otp-page` chunks remain |
| Frontend: `ng test --browsers=ChromeHeadless` (299 tests, +3 vs. pre-Phase-2 baseline of 296) | PASS |
| Frontend: manual browser verification (login page, phone-register redirect, wrong-credentials error display, toast-stack inspection) | PASS — see below |
| Login | PASS (unit + manual) |
| Wrong Password | PASS (unit) |
| Email Verification | Unaffected — out of scope per Finding, confirmed intentional by docs |
| OTP | PASS (backend build/tests green; new codes verified by code path, not by a live Postgres integration run — see Remaining Issues) |
| Phone Login | PASS (unit + manual for the live password-based flow; OTP-registration flow removed, not "fixed") |
| Refresh | PASS (unit — 2 new tests) |
| Anonymous Browsing | PASS (pre-existing, unaffected, confirmed still correct by reading the guard logic) |
| Authorization (403) | Unaffected — confirmed already correctly separate from 401 in existing code, no change needed |

**Manual browser verification performed** (dev server, `ng serve --configuration staging`,
against the real HudhudNest app — screenshots/DOM inspected):
1. Login page renders correctly (email + phone tabs).
2. Phone "Konto erstellen" tab shows only the "moved" notice + working link — no trace of the old
   broken Step 1/2 form.
3. Clicking the link navigates to the live `/auth/phone-register` page (`#/auth/phone-register`),
   confirmed via `window.location.href`.
4. Submitting the login form with a network-unreachable backend shows **exactly one** inline error
   message (`AUTH.ERRORS.NETWORK_ERROR`) and the toast stack (`app-toast-container`) is empty —
   confirms the duplication fix (Finding 6).
5. No new console errors; only pre-existing, unrelated CORS errors from the local dev
   environment's `staging` build config pointing at the wrong backend host (a separate, previously
   documented issue — see `frontend-repo-location` memory — not something this phase touches).

## F. Regression

| Existing test | Result | Regression found | Fix |
|---|---|---|---|
| `HudhudNestApi.Auth.Tests` full suite | PASS (256/256) | None | n/a |
| `HudhudNestApi.Application.Tests` full suite | PASS (1074/1074) | None | n/a |
| `HudhudNestApi.Architecture.Tests` full suite | PASS (164/164) | None | n/a |
| `api-error.interceptor.spec.ts` (pre-existing 5 tests) | Initially FAILED (all 5) after adding `inject(TranslateService)` to the interceptor without updating the spec's `TestBed` providers | Yes — `NG0201: No provider for TranslateService!` thrown synchronously inside the interceptor, so every HTTP call in the spec never reached `HttpTestingController` | Added `TranslateModule.forRoot()` to the spec's `imports`; re-ran, all 5 pass |
| `login.component.spec.ts` (pre-existing tests referencing the removed OTP flow) | Would have failed to compile (`TS2554`, `TS2339` on deleted members) | Yes — direct consequence of Finding 1's fix | Removed the two obsolete OTP tests, fixed the constructor argument count |
| Full frontend suite (`ng test`) | PASS (299/299) after the two fixes above | — | — |

No other regressions found. `HudhudNestApi.Concurrency.Tests`, `HudhudNestApi.Integration.Tests`,
`HudhudNestApi.StagingSmokeTests`, `HudhudNestApi.Performance.Tests` were not run this session (see
Remaining Issues — same limitation the Investment Phase 1/2 work in this repo already documented).

## G. Remaining Issues

| Issue | Why not fixed | Impact | Recommended Phase |
|---|---|---|---|
| No real Postgres-backed integration test exercises the new OTP failure-reason codes end-to-end | Docker Desktop's engine was not running in this sandboxed session, and the only reachable local Postgres is the developer's own native instance (never used for tests, per prior documented practice in this repo — see the Investment Phase 2 report's own isolation discipline) | Medium — the logic is straightforward (typed enum → string mapping, unit-testable in principle) and covered by the full backend test suite passing with no regressions, but the exact HTTP-level JSON shape for the 7 new codes was not verified against a live database | Next session with Docker available: add `PhoneAuthFlowIntegrationTests` cases for OTP_EXPIRED/ALREADY_USED/WRONG/RATE_LIMITED |
| No E2E browser-automation framework exists in the frontend repo (no Cypress/Playwright — confirmed via `package.json`) | Setting one up from scratch was judged out of scope for an error-handling phase — a large, separate infrastructure investment | Medium — spec §22's 6 E2E scenarios were instead covered by a combination of unit tests (interceptor/component level) and one manual browser session against the real dev server (see §E) | If E2E coverage is wanted going forward, a dedicated "Testing Infrastructure" phase should set up Playwright first |
| Four different JSON error-response shapes coexist in the backend (global exception middleware, `LoginResponseDto`, `PhoneWorkflowResult`, Redis rate-limiter's `problem+json`) | Spec §12 explicitly warns against a broad, unrequested API-wide response-shape migration; this is architectural and affects every controller, not just auth | Low-to-Medium — each shape is internally consistent and this phase's new codes fit cleanly into the existing shapes without needing to change them | Only worth doing as its own deliberate, tested migration — not appropriate to fold into an error-*messaging* phase |
| `LoginResponseDto.CreateAccountLocked`/`RateLimited`/`AccountDisabled`/`PasswordChangeRequired` factory methods are dead code on the live login path (`AuthController.cs` always returns `CreateInvalidCredentials()`) | Discovered during the audit; removing dead code that only test files reference is a valid cleanup but was judged separate from error-*behavior* work, and touching it risks an unrelated diff in a file this phase already modifies | Low — purely a code-cleanliness item, no behavioral effect | Optional cleanup, any future pass touching `AuthController.cs`/`LoginResponseDto.cs` |
| Breach-password screening (`PASSWORD_BREACHED`) fires only on Registration, not Change-Password or Reset-Password | Confirmed as existing behavior, not a regression from this phase; adding it elsewhere is a product/security decision beyond "fix the messaging for what already happens" | Low-Medium — a user could set a breached password via reset/change without warning | Recommend a security-focused follow-up phase, not bundled into error-UX work |
| RTL/mobile visual QA for the new toast/inline messages was not exhaustively screenshot-tested in Arabic across all 9 auth pages | Time-scoped: one manual verification pass was done (German locale, login page); the app's RTL mechanism itself was not modified by this phase, only which messages get shown and where | Low — no CSS/layout code was touched, only message content/routing logic | Recommend as a lighter spot-check in whichever phase next touches these pages' templates |

---

*No changes in this phase touch Search, Filters, Favorites, Sharing, Dashboard, or Favorite/Share
UI. No git commits were created — all changes are in the working tree of both repos, pending
review.*
