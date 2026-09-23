# Phone Login Verification Report — 2026-09-23

Scope: the phone + password login feature and its OTP-backed companions (registration, password
reset, reverification, phone change) across the API (`PropertyApi`) and the Angular/Capacitor app
(`HudhudNest`). Design reference: `docs/phone-password-authentication-and-reverification.md`.

The audit itself changed no production code. **Update (same day): F-1, F-3 and F-4 are fixed** in the follow-up change
(see "Fix status" below); F-2 and F-5 to F-12 remain open. No real SMS was sent and no Staging/Production system was touched.

## الملخص التنفيذي (Arabic summary)

**الحكم: جاهز بشروط — الميزة تعمل من البداية للنهاية. تحديث: أُصلحت F-1 (الحساب المحظور) وF-3 (فرق الزمن) وF-4 (رسالة كلمة المرور الضعيفة) — انظر «Fix status». المتبقي: F-2 وF-5 وما دونها.**

- **يعمل ومُثبَت بالتشغيل الفعلي:** التسجيل برقم الهاتف وOTP حقيقي، تسجيل الدخول، تجديد الجلسة، استعادة كلمة المرور، تغيير الرقم، إبطال الجلسات القديمة، منع إعادة استخدام الرمز، حد المحاولات، تزامن الطلبات، حدود المعدّل، كعكة الجلسة (HttpOnly/Secure/Partitioned)، والتدقيق دون تسريب الرقم أو الرمز.
- **ثغرة عالية:** الحساب **المحظور (IsBanned)** يستطيع الدخول برقم الهاتف، وتبقى جلسته صالحة ويستطيع تجديدها (تسجيل الدخول بالبريد يرفضه).
- **متوسطة:** تسريب وجود الرقم (هل هو مسجّل) عبر وجود `challengeId`؛ فرق زمن الاستجابة بين رقم غير معروف / كلمة مرور خاطئة / حساب مقفل؛ رسالة خاطئة للمستخدم عند كلمة مرور ضعيفة؛ لا يوجد أي مسار في الواجهة لصفحتَي إعادة التحقق وتغيير الرقم.
- **ما لا أستطيع التحقق منه هنا:** إرسال SMS حقيقي، Redis، بيئة Staging/Production، جهاز موبايل فعلي، Safari، وجلسة المتصفح عبر HTTPS (انظر «لم يُتحقق منه»).
- دليل تجربة خطوة بخطوة تجدونه في القسم 6.

## Fix status

| Finding | Status | Change |
|---|---|---|
| F-1 banned accounts | **Fixed** | Phone login refuses `IsBanned`/`IsDeleted` after the password verified (`ACCOUNT_UNAVAILABLE`, audited as `blocked`); `RefreshTokenCommand` and the per-request security-stamp check (`CachedSecurityStampValidator`, `SecurityStampSnapshot.IsBanned`) refuse banned identities. **Residual:** the snapshot is cached for 5 minutes, so a ban takes effect on existing access tokens within 5 minutes unless the ban workflow (none exists yet — nothing sets `IsBanned`) also calls `IUserSecurityStampCacheInvalidator`. |
| F-2 enumeration | **Fixed (response and verify behaviour)** | An ineligible number gets a stored *decoy* challenge that behaves exactly like a real one at verify time (`OTP_WRONG` ×3 then `OTP_RATE_LIMITED`, expiry) and can never be satisfied; no SMS. Past 3 challenges/hour every number is answered with its latest challenge. **Residual:** an eligible request also sends an SMS, so response *time* can still differ, and `SMS_FAILED` is returned only for eligible numbers (provider outage only). Closing that fully needs an asynchronous send queue. |
| F-3 timing | **Fixed** | Unknown numbers and locked accounts spend exactly one password-hasher operation (`E4` counts hasher calls). |
| F-4 password message | **Fixed (API + Angular)** | Every Identity password-rule failure returns `PASSWORD_POLICY_FAILED`; the field hint and the error text now state the full rule in ar/en/de (verified in the browser). |
| F-5 no UI path | **Fixed** | Profile page links to phone change and verification; a 403 `PHONE_REVERIFICATION_REQUIRED` explains itself and redirects to `/profile/phone-reverify` (`ApiErrorInterceptor`); reminder notifications link to the page. Profile link and page verified in the browser. |
| F-6 login error text | **Fixed** | Phone login distinguishes rate limit / network / server / blocked account from bad credentials and uses phone-specific wording. |
| F-7 lockout after reset | **Fixed** | A completed OTP password reset clears the failed-attempt count and the lockout. |
| F-8 change-phone password guessing | **Fixed** | The current-password check counts towards Identity lockout. |
| F-9 local number formats | **Fixed on the client** | `normalizePhoneInput`: `09xxxxxxxx`, `00963…`, spaces/dashes, Arabic-Indic and Persian digits are accepted and sent as strict E.164; the server contract is unchanged (verified in the browser with `0944 111 222`). |
| F-10 storage | Info — unchanged | Documented design. |
| F-11 http dev topology | Info — documented | Not a deployed-environment defect. |
| F-12 coverage | **Fixed** | Frontend specs for the API service, number normalization, error mapping, the re-verification redirect and login; the backend suite (52 tests) now runs in CI. |

Every backend fix was proven fail-first: with the previous code 8 tests fail in each of the two rounds; with the fixes `PhoneLoginAuditTests` gives 52 passed, 0 skipped, and the full integration suite 391 passed, 0 failed.

## 1. Verdict

**Ready (all findings closed except the documented residuals below and the items in section 7).** Originally: ready with conditions. All five journeys work end to end against real PostgreSQL, the production
`OtpService`, Identity, JWT and refresh-token stack. One high-severity defect (banned accounts) and
four medium ones should be fixed before launch; the rest are low or informational.

## 2. Evidence

| Check | Result |
|---|---|
| New probe suite `PhoneLoginAuditTests` (real PostgreSQL 18, real OTP generator, SMS captured) | **47 tests: 38 pass, 9 fail — every failure is a confirmed defect below (B3×3→F-4, D2/D4/D5→F-1, E1/E2→F-2, F2→F-7)** |
| Existing phone integration tests (`PhoneAuthFlowIntegrationTests`, hash backfill) | 6/6 pass |
| `PropertyApi.Auth.Tests` | 261/261 pass |
| `PropertyApi.Application.Tests` | 1237/1237 pass |
| Frontend `npm run test:ci` / `typecheck` | 386/386 pass / clean |
| Real UI in the browser (Angular dev server → API in Development mode, OTP from the console SMS service) | Register (invalid format, weak password, success), phone login (wrong password, CSRF path) exercised; see 4.x |
| i18n keys used by the phone pages and `phone-auth-errors.ts` (ar/en/de) | all present |

The existing phone tests run on the EF in-memory provider with a fixed OTP, so they never execute the
production relational paths (`ExecuteUpdate` reservations, the unique index). The new suite does.

## 3. Journey results

| Journey | Result |
|---|---|
| Registration (send OTP → verify → account + session) | ✅ (UI + API) |
| Phone + password login, refresh, cookie flags | ✅ |
| Forgot password (send → verify → confirm) | ✅ old password, old refresh token and old access token all invalidated; token cannot be replayed or used for another number |
| Reverification (incl. restricted-user enforcement) | ✅ API; ❌ no UI path (F-5) |
| Phone change (incl. wrong password, number already owned) | ✅ API; ❌ no UI path (F-5) |

## 4. Findings

Severity: High / Medium / Low / Info. "Test" names refer to `tests/PropertyApi.Integration.Tests/Auth/PhoneLoginAuditTests.cs`.

### F-1 (High — fixed, see Fix status) Banned accounts can log in by phone, and bans do not stop existing sessions
- Email login rejects `IsBanned` (`AuthenticationSessionIssuer`). `PhoneAuthenticationWorkflow.LoginAsync`
  builds the session itself (`IssueSessionAsync`) and never checks `IsBanned`.
- Tests `D2` (login succeeds and returns a token), `D4` (a token issued before the ban keeps working),
  `D5` (the refresh cookie still refreshes). Soft-deleted accounts are correctly refused (`D3` passes).
- `D4`/`D5` are probably cross-cutting: a grep shows `IsBanned` is checked only at session issuance, not in
  refresh or bearer validation. That was **not** tested for email sessions.
- Fix: route the phone login through the same `AuthenticationSessionIssuer` (or check `IsBanned`/`IsDeleted`
  in `LoginAsync`, `RegisterAsync` re-use paths and `ConfirmPasswordReset`), and decide whether a ban must
  revoke refresh tokens and be checked in bearer/refresh validation.

### F-2 (Medium) OTP send endpoints reveal whether a number is registered
- The docs say the anonymous send response is generic. It is generic in text only: `challengeId` is present
  only when the number is eligible. `registration/send-otp` returns a `challengeId` only for **unregistered**
  numbers; `password-reset/send-otp` only for **registered** ones (tests `E1`, `E2`). The rate-limited case
  (4th request per hour) and `SMS_FAILED` also differ from the eligible case.
- The Angular pages rely on the missing `challengeId` (they show "send failed"), so the fix needs both sides.
- Fix: always return a `challengeId` (a decoy for ineligible numbers, rejected at verify with the generic
  error), keep the response and timing shape identical, and move the "already registered" hint to the verify step.

### F-3 (Medium — fixed) Login response time distinguishes unknown / wrong password / locked
- Measured medians (7 samples each, local): unknown number **126 ms**, wrong password **69 ms**, locked
  account **4 ms** (test `E3`, informational). Unknown numbers hash twice (`HashPassword` + `VerifyHashedPassword`),
  locked accounts hash zero times. The email path already fixed this (`VerifyDummyPasswordAsync`); the phone
  path did not. The response bodies are identical (`D1` passes).
- Fix: compute a single dummy verification for the unknown and locked cases (and keep one real verification
  for known accounts), as `LoginUserCommand` does.

### F-4 (Medium — API fixed) Weak passwords produce a misleading error
- Backend maps only `PasswordTooShort` to `PASSWORD_POLICY_FAILED`; missing upper-case, lower-case or digit
  returns `USER_CREATE_FAILED` (tests `B3`, 3 of 4 cases). `phone-auth-errors.ts` maps `USER_CREATE_FAILED` to
  "phone login failed, try again" (seen in the browser with `abcdefgh1`), and the page only states
  "at least 8 characters". Users are told nothing about the real rule.
- Fix: map every Identity password error to `PASSWORD_POLICY_FAILED` and show the full rule (or reuse the
  existing `password-errors.ts` used by the email register page) in the phone forms.

### F-5 (Medium, dormant until enforcement is enabled) No UI path to reverification or phone change
- `/profile/phone-reverify` and `/profile/phone-change` exist as routes, but no link, redirect or interceptor
  references them, and `PHONE_REVERIFICATION_REQUIRED` (403) is not handled anywhere in the app. With
  `PhoneVerification:EnforcementEnabled=true`, a restricted user just sees failing actions.
- The API side works (`G4`). Fix before enabling enforcement: add a profile entry, an error-interceptor
  redirect on `PHONE_REVERIFICATION_REQUIRED`, and deep links in the reminder notifications.

### F-6 (Low/Medium) Phone login shows "email or password incorrect" for every failure
- `login.component.ts` `onPhoneLogin` maps any error (rate limit 429, server 500, offline, CSRF 403) to
  `INVALID_CREDENTIALS`, whose text mentions the email address (seen in the browser). Lockout must stay
  generic, but rate-limit, network and server errors should not.
- Fix: distinguish 429 / network / 5xx from a 400 `PHONE_AUTH_FAILED`, and use phone-specific copy.

### F-7 (Low) A password reset does not clear an account lockout
- `F2`: after 10 wrong attempts the account is locked; a completed OTP password reset leaves it locked until
  the window ends. Fix: `ResetAccessFailedCount` / clear the lockout in `ConfirmPasswordResetAsync`.

### F-8 (Low) Phone change: current-password guessing is limited only by rate limiting
- `VerifyPhoneChangeAsync` uses `CheckPasswordAsync`, which does not count towards Identity lockout. The
  5-per-15-minutes `verify-otp` limiter (per user) stops it (`G3`: attempts 6–8 are 429) but not a slow attacker
  holding a stolen access token. Fix: use `CheckPasswordSignInAsync(lockoutOnFailure: true)`.

### F-9 (Low) Local number formats are rejected
- Only strict `+<country><digits>` is accepted (server and client). `0933…`, `00963…`, spaces and Arabic-Indic
  digits are all refused (tests `B1` pass by design; the UI explains the format). Syrian users habitually type
  `09xxxxxxxx`. Consider normalizing local Syrian formats in the client, keeping the strict server contract.

### F-10 (Info) Phone number storage
- `PhoneNumber` is encrypted (Data Protection); `NormalizedPhoneNumber` and `UserName` hold the E.164 number in
  plaintext (test `I2`). This matches the documented design (lookup column) but `UserName` duplicates it
  unencrypted. `IPhoneNumberLookupHasher` exists but the workflow does not use it for lookups.

### F-11 (Info) Local development over plain HTTP breaks cookie-session flows
- Served over `http://`, the antiforgery cookie is `SameSite=None` without `Secure`, so Chrome drops it; then
  refresh after a reload and login with a refresh cookie present return 403 `CSRF_VALIDATION_FAILED`. The API
  behaves correctly with a cookie jar (curl: 200 with the header, 403 without). Use the project's HTTPS dev
  setup (`https://localhost:7136`), not http, for browser testing. Not a deployed-environment defect.

### F-12 (Info) Test coverage gaps
- No frontend specs exist for `PhoneAuthApiService` or the four phone pages. The backend suite had only two
  phone HTTP tests before this audit.

## 5. Verified working (highlights)

OTP: CSPRNG 6-digit code, HMAC-SHA256 stored, fixed-time compare, 5-minute expiry (`C3`), 3 attempts then
locked even for the right code (`C1`), single use (`C2`, a replay cannot change the password), purpose binding
(`C4`), 3 sends per phone per hour (`C5`), concurrent verify of one challenge → one account (`C6`), two
challenges for one number racing → one account (`C7`), SMS failure leaves no challenge (`C8`), code and number
never in responses (`A2`).
Login: same error and message for unknown / wrong / locked (`D1`); lockout works; rate limits 3 sends/15 min,
5 verifies/15 min, 10 logins/min per client (`H1–H3`).
Sessions: refresh token only in an HttpOnly, Secure, SameSite=None, Partitioned cookie scoped to `/api/auth`;
never in the JSON body; password reset and phone change revoke refresh tokens and invalidate old access tokens
(`F1`, `G1`); reset token for another number is refused (`F3`); another user's reverification challenge is refused (`G5`).
Audit: registration, OTP request, login success/failure rows are written and contain neither the number, the
password nor the OTP (`I1`).
Robustness: malformed numbers (local formats, injection strings, Arabic digits, too long) → `PHONE_NUMBER_INVALID`
without errors (`B1`).

## 6. Owner's guide — try it yourself

Prerequisites: .NET 8 SDK, PostgreSQL, Node 22. Do not use real secrets.

1. **Database** — create an empty database and set the connection string.
2. **API** (Development mode prints the OTP in the console, phone masked):
   ```bash
   export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=https://localhost:7136
   export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=phone_try;Username=postgres;Password=<yours>"
   export Jwt__Key="$(openssl rand -base64 48)" OtpSettings__SecretKey="$(openssl rand -base64 48)"
   export Security__PhoneLookupHmacKey="$(openssl rand -base64 32)"
   export Database__ApplyMigrationsOnStartup=true Database__SeedOnStartup=true RateLimiting__Redis__Enabled=false
   dotnet run --project PropertyApi
   ```
   Use HTTPS (see F-11). Look for `[DEV MODE] OTP for ***1234: 123456` in the console.
3. **Frontend**: `API_URL=https://localhost:7136/api npm start`, open `https://localhost:4200/#/auth/phone-register`
   (trust the dev certificate first).
4. **Scenarios** (expected result in brackets):
   1. Register with `+963933000111` (OTP from the API console, password `SecurePass9`, tick the consent box) [signed in, lands on home].
   2. Register again with `0933000111` [format error before any request].
   3. Register with password `abcdefgh1` [today: "phone login failed" — see F-4].
   4. Sign out, sign in on the phone tab with the right password [success]; then with a wrong password [generic error];
      wrong password 5+ times, then the right one [still refused while locked].
   5. Forgot password: `#/auth/phone-password-reset` → OTP → new password [old password refused, new works; other sessions signed out].
   6. Use a wrong OTP three times, then the right one [refused: too many attempts]; wait 5 minutes [expired].
   7. Request four OTPs for one number within an hour [4th is silently not delivered, no console line].
   8. Change/reverify: routes `#/profile/phone-change` and `#/profile/phone-reverify` (signed in) — reachable only by URL today (F-5).
   9. Ban check (F-1): set `IsBanned = true` for the user in the `Users` table, then log in on the phone tab [today: still succeeds — defect].
   Signs of a problem: any success where a refusal is listed, an OTP or your number in a network response, or a `refreshToken` in a JSON body.
5. **Automated suite**: `TEST_POSTGRES_CONNECTION_STRING=... dotnet test tests/PropertyApi.Integration.Tests --filter FullyQualifiedName~PhoneLoginAuditTests`
   (needs migrations applied first, as in CI). Failing tests are the open defects above.

## 7. Not verified

- Real SMS delivery (Twilio / HTTP provider), the provider's error handling, cost controls / SMS pumping across many IPs.
- Redis-backed rate limiting, `ForwardedHeaders` behind a proxy (rate limits key on the client IP), Staging and Production configuration.
- A real device (Android/iOS Capacitor), Safari, and browser session restore over HTTPS (see F-11).
- The reset, reverify and change pages in the browser (read only; i18n keys checked), and the EN/DE wording.
- The reverification background worker and reminders (hosted services were disabled in the probes).
- Whether email sessions honor a ban on refresh (F-1, only inferred from the code).

## 8. Suggested order of work

1. F-1 (ban), F-3 (timing), F-4 (password message) — small, isolated fixes with tests already written.
2. F-2 (enumeration) together with the Angular pages.
3. F-6, F-7, F-8, F-9 quick wins.
4. F-5 before switching `PhoneVerification:EnforcementEnabled` on.
5. Convert the fixed probes to permanent regression tests (fail-first, then pass) and add frontend specs (F-12).
