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
| F-13 wrong-password wording, DE register, profile links | **Fixed** | Found in the browser round; see F-13. |
| F-14 hardcoded Arabic consent text | Open (not phone login) | Needs legal wording review; see F-14. |
| F-15 SMS pumping (any country, per-IP limits only) | **Mitigated, needs config** | `SmsProvider:AllowedCountryCodes` refuses NEW numbers (registration, number change) outside the list with `PHONE_COUNTRY_NOT_SUPPORTED`; empty = unchanged behaviour. **The owner must set it.** |
| F-16 HTTP SMS adapter without timeout | **Fixed** | `SmsProvider:TimeoutSeconds` (default 10, clamped 1–60); a silent provider no longer holds the request for 100 s. |
| F-17 number change vs registration race → 500 + burnt code | **Fixed** | Transaction + unique-index handling like registration; loser gets `PHONE_NUMBER_ALREADY_IN_USE` and keeps a usable code. |
| F-18 one inconsistent row stops every reminder | **Fixed** | Worker derives and repairs missing due dates instead of throwing. |
| F-19 auth responses cacheable | **Fixed** | `Cache-Control: no-store` on the three auth controllers. |
| F-20 no OTP autofill, errors not announced | **Fixed (app)** | `autocomplete="one-time-code"`, `role="alert"` / `role="status"`. |
| F-21–F-24 | Info | See findings. |

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

### F-13 (Medium, fixed) Wrong current password on phone change said "a recent login is required"
- Found while verifying in the browser. `RECENT_AUTHENTICATION_REQUIRED` is returned only when the current
  password is wrong (or the account is locked) on the phone-change step, but the text told people to log in
  again. Now: "the current password is incorrect, or the account is temporarily locked…" (ar/en/de).
- Same session: two German strings used informal "du" among the formal "Sie" auth texts (aligned), and the two
  profile links ("change sign-in number", "verify my number") rendered as run-together plain text (now outlined
  buttons).

### F-14 (Low, open — not part of phone login) Consent text is hardcoded Arabic
- `src/app/shared/components/consent-checkbox` ("I agree to the Privacy Policy and Terms of Service") is
  hardcoded Arabic, so the phone register page (and the email one) shows Arabic consent text in German and
  English. It is legal copy, so the wording needs the owner's / legal review before it is translated. Not changed.

### F-15 (High, mitigated — needs owner configuration) SMS pumping / international revenue share
- Any well-formed E.164 number in the world was accepted for a registration OTP, and every limit keys on the client IP
  (3 sends per 15 min per IP, 3 challenges per hour per number). An attacker with many IPs can therefore make the API
  send unlimited paid SMS to premium-rate numbers they control. Nothing capped the total.
- Fix: `SmsProvider:AllowedCountryCodes` (for example `["+963"]`). A NEW registration or number change outside the list
  is refused with `PHONE_COUNTRY_NOT_SUPPORTED` before any SMS is sent. The answer depends only on the public prefix, so it
  reveals nothing about accounts, and existing accounts (login, reset, re-verification) are never blocked by it.
  The default is empty (no restriction) so nothing changes until the owner decides which countries to serve.
- Still open (product decision): a global send cap / alert on SMS spend. Counting challenge rows would also count decoys,
  which would let an attacker exhaust the cap, so it needs its own counter; see section 8.

### F-16 (Medium, fixed) The HTTP SMS adapter had no timeout of its own
- It used the HttpClient default of 100 seconds inside the send-OTP request, so a provider that stops answering held every
  send request (and the person waiting for a code) for that long. Now bounded by `SmsProvider:TimeoutSeconds`
  (default 10). Proven fail-first with a provider that never answers; the adapter also maps every non-2xx status and
  connection failure to `false` without throwing and never logs the OTP, the API key or the full number.

### F-17 (Medium, fixed) Phone change racing a registration for the same number returned 500
- `VerifyPhoneChangeAsync` checked the number with `AnyAsync` and then wrote; if a registration took the number in
  between, the unique index threw out of `UpdateSecurityStampAsync`/`UpdateAsync`, the caller saw a 500 and the OTP was
  already consumed. It now runs in a transaction like registration: on the conflict the code is released and the answer
  is `PHONE_NUMBER_ALREADY_IN_USE`. The race cannot be forced deterministically: one 500 was observed before the fix,
  five parallel scenarios ran clean three times after it.

### F-18 (Medium, fixed) One inconsistent user stopped everybody's reminders
- A verified user with no due dates (imported, edited by hand, older release) made the hourly tick throw
  `Nullable object must have a value`; since the tick is retried against the same row, no reminder or state change was
  ever processed again. The worker now derives the dates (verified + 180 days, grace +3 days) and repairs the row.
  Also proven: 250 users across three pages get exactly one reminder each, a second tick adds none, and two instances
  ticking at the same time never duplicate one.

### F-19 (Low, fixed) Auth responses carried no cache directive
- Login, refresh, OTP and reset responses (tokens, challenge ids) had no `Cache-Control`. They are now `no-store`.

### F-20 (Low, fixed in the app) One-time-code autofill and screen-reader announcements
- The four OTP inputs lacked `autocomplete="one-time-code"`, so phones could not offer the SMS code, and the error /
  success paragraphs on the phone pages were not live regions, so a wrong code was never announced.

### F-21 (Info) Development logging exposes data on purpose
- `AddDbContext` enables `EnableSensitiveDataLogging` in Development only, so Debug logs contain phone numbers, code
  hashes and password hashes as SQL parameters, and `ConsoleSmsService` prints the OTP. Re-running every flow under the
  `Testing` environment with Debug logging produced no full number, password, JWT or refresh-token value.

### F-22 (Info, decision needed) No ban workflow exists
- Nothing in the code base sets `IsBanned`. The enforcement is proven (login, refresh, per-request stamp check) and
  `PhoneBanCacheTests` documents the one thing a future ban action must do: call
  `IUserSecurityStampCacheInvalidator.InvalidateAsync(userId)`, otherwise a token whose stamp is already cached keeps
  working until the 5-minute TTL ends. A complete ban feature also needs the admin endpoint, refresh-token revocation,
  audit, and a user-facing message; that scope is a product decision.

### F-23 (Info) The Twilio adapter is not covered
- `TwilioSmsService` calls a static `TwilioClient.Init` on every send and has no test seam or explicit timeout, so its
  behaviour against a slow or failing Twilio was not exercised (the HTTP adapter was).

### F-24 (Info) Validation errors name C# parameters
- A malformed body answers 400 with messages such as "The r field is required" (the record's constructor parameter).
  No stack trace or internal type is exposed; cosmetic only.

### F-25 (High, fixed 2026-09-24) CI ran the H1–H3 hosts with no rate limiter at all
- `H1`–`H3` passed locally and failed on master CI (run 35918522176, `[OK, OK, OK, OK, OK]`). ci.yml sets
  `RateLimiting__Redis__Enabled=true` for the process; Program.cs read that before the test host's own
  `RateLimiting:Redis:Enabled=false` applied and mounted the Redis middleware instead of the in-memory limiter;
  the middleware then read the final options (disabled) and passed every request.
- Production was not affected (one configuration source there), but any host whose final configuration differed
  from its pre-build configuration had no limiter. Fix: Program.cs chooses the limiter after `Build()` via
  `IsRedisRateLimitingActive` (`RedisRateLimitingActivationTests`); reproduced and re-run locally with CI's variables.
- So the "Redis-backed rate limiting — CI already runs Redis" line in §7 was not true for these tests before this fix.

### F-26 (High, fixed 2026-09-24) Staging smoke asserted the pre-#207 duplicate-registration contract
- The Production Gate failed on master (run 35918522603): the smoke journey still required *no* challenge id for an
  already registered number, while #207 deliberately answers with a decoy challenge (F-2). The journey now expects
  the challenge id and proves the decoy cannot be completed, not even with Staging's fixed OTP.

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

Prerequisites: .NET 8 SDK, `dotnet-ef` (`dotnet tool install -g dotnet-ef`), PostgreSQL with the `postgis` and `pg_trgm`
extensions, Node 22. Do not use real secrets. Every command below was run while preparing this report.

1. **Database** — create an empty database, then apply the schema (the API does **not** migrate on startup; there is no
   `ApplyMigrationsOnStartup` setting):
   ```bash
   export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=phone_try;Username=postgres;Password=<yours>"
   export ASPNETCORE_ENVIRONMENT=Development RateLimiting__Redis__Enabled=false
   dotnet ef database update --context AppDbContext --project PropertyApi.Infrastructure --startup-project PropertyApi
   ```
2. **API** (Development mode prints the OTP in the console, phone masked; it also logs SQL parameters, so never reuse this
   setup for anything but local trials):
   ```bash
   export ASPNETCORE_URLS=https://localhost:7136
   export Jwt__Key="$(openssl rand -base64 48)" OtpSettings__SecretKey="$(openssl rand -base64 48)"
   export Security__PhoneLookupHmacKey="$(openssl rand -base64 32)" Database__SeedOnStartup=true
   export SmsProvider__AllowedCountryCodes__0=+963      # optional: refuse numbers from other countries
   export PhoneVerification__EnforcementEnabled=true PhoneVerification__ReminderProcessingEnabled=true   # optional
   dotnet run --project PropertyApi
   ```
   Use HTTPS (see F-11). Look for `[DEV MODE] OTP for ***1234: 123456` in the console.
3. **Frontend**: `API_URL=https://localhost:7136/api npm start`, open `https://localhost:4200/#/auth/phone-register`
   (trust the dev certificate first: `dotnet dev-certs https --trust`). Over plain `http://` the browser drops the CSRF
   cookie (F-11); for a quick try you can add `CookieCsrf__Enabled=false`, which also skips what you are testing.
4. **Scenarios** (expected result in brackets):
   1. Register with `+963933000111` (OTP from the API console, password `SecurePass9`, tick the consent box) [signed in, lands on home].
   2. Register with `0933 000 222` [accepted: normalized to `+963933000222`, the OTP arrives].
   3. Register with password `abcdefgh1` [the full rule is shown: 8 characters, upper, lower, digit].
   4. Sign out, sign in on the phone tab with the right password [success]; with a wrong password [one phone-specific message];
      wrong password 5+ times, then the right one [still refused while locked, same message].
   5. Forgot password: `#/auth/phone-password-reset` → OTP → new password [old password refused, new works, a locked account is
      unlocked, other sessions signed out].
   6. Use a wrong OTP three times, then the right one [refused: too many attempts]; wait 5 minutes [expired].
   7. Request four OTPs for one number within an hour [the 4th is answered without sending, no console line].
   8. Profile → "Change sign-in phone number" and "Verify my phone number" [both pages open; a wrong current password says so and
      counts as a failed attempt].
   9. Ban check: set `"IsBanned" = true` for the user in the `Users` table, then log in on the phone tab [refused:
      "this account is unavailable"]. An access token that was already used keeps working for up to 5 minutes (F-22).
   10. With `SmsProvider__AllowedCountryCodes__0=+963`, register with `+4915112345678` [refused: "phone numbers from this country
       are not supported"; no OTP in the console].
   11. Re-verification: set the user's `"PhoneVerificationState" = 'Restricted'` with due dates in the past, then mark a notification as read
       [403 → message → redirected to `/profile/phone-reverify`]; verify with the OTP [state `Verified`, actions work again].
   Signs of a problem: any success where a refusal is listed, an OTP or your number in a network response, or a `refreshToken` in a JSON body.
5. **Automated suites** (need the migrated database above, as in CI):
   ```bash
   export TEST_POSTGRES_CONNECTION_STRING="Host=localhost;Port=5432;Database=phone_try;Username=postgres;Password=<yours>"
   dotnet test tests/PropertyApi.Integration.Tests --filter "FullyQualifiedName~Phone"
   dotnet test tests/PropertyApi.Infrastructure.Tests                                       # SMS adapter contract, hosted services
   cd <frontend repo> && npm run test:ci && npm run typecheck && npm run audit:frontend && npm run build:prod
   ```

## 7. Verification status

### Verified in a third round (full audit: HTTPS API, real PostgreSQL, integration tests, browser at 375 / 768 / desktop)
- **CSRF, cookies and sessions over HTTPS** (dev certificate, `CookieCsrf:Enabled=true`, curl with a cookie jar):
  refresh without the header → 403, with it → 200 and a rotated cookie; reusing the previous refresh token → 401 and
  the whole family is revoked (the newer token is dead too); logout revokes the session and the old access token stops at
  once; the refresh cookie is `HttpOnly; Secure; SameSite=None; Partitioned; path=/api/auth`; CORS allows the
  configured origin and answers a foreign origin with no allow-origin header; security headers present. Logout is
  deliberately exempt from CSRF (it requires the bearer token; documented in `AuthController`).
- **Rate limiting behind a proxy** (`ForwardedHeaders`): with the proxy trusted, limits partition by the forwarded
  client IP (4th request from one IP → 429, another IP → 200); with an untrusted hop, a spoofed `X-Forwarded-For`
  changing every request is ignored (4th → 429).
- **SMS provider contract** against a stub handler: 200/202 succeed; 400/401/429/500/503 and a connection failure return
  false without throwing; a provider that never answers is given up on after the timeout (F-16); caller cancellation
  returns false; the OTP, API key and full number never reach the log.
- **Concurrency on PostgreSQL:** 12 parallel verifies of one challenge → one winner, one account; three challenges for
  one number verified together → one account; 15 parallel send-OTP → bounded and no 500; one reset token used 8 times →
  once; number change vs registration for the same number → one owner (F-17).
- **Time, with a controllable clock:** a code works at 4:50 and is rejected as expired at 5:10; the hourly challenge
  window closes after 3, answers the 4th without sending, and reopens after 61 minutes.
- **Reminder worker at scale and with two instances** (F-18).
- **Empty database:** all 59 migrations apply; the API starts with hosted services enabled, logs "will retry" for the
  ticks that need tables and stays up.
- **Bans:** login, refresh and a fresh token refuse a banned user; a token with a cached stamp keeps working until the
  cache is invalidated, and invalidation makes the ban immediate (F-22).
- **Privacy:** Debug logs under `Testing` contain no full number, password, JWT or refresh value (F-21); malformed
  bodies return validation problems without stack traces (F-24).
- **App, real browser:** the country refusal shows in German; a triple-clicked submit sends one request; `one-time-code`
  is present; controls are labelled; no horizontal scroll at 375 px and 768 px in LTR and RTL; a hostile `returnUrl`
  cannot leave the app (falls back to `/home`); errors are now announced (F-20).

### Verified in the second round (earlier)
- **Login errors, on screen:** wrong password → the phone-specific message (Arabic and German); rate limit → "too many
  attempts"; PostgreSQL stopped → 500 → "server error"; API stopped → "cannot reach the server"; banned account
  (`IsBanned = true`) → "this account is unavailable". A local number (`0944 111 001`) is normalized and works.
- **403 `PHONE_REVERIFICATION_REQUIRED`:** with enforcement on, a restricted user's "mark as read" (PATCH) returns
  403; the app shows the message and lands on `/profile/phone-reverify`. The notification's "verify my phone number"
  link opens the same page.
- **Pages end to end (dev OTP from the log):** re-verification (state Restricted → Verified in the database, the next
  write succeeds), password reset (weak password shows the full rule; a locked account is unlocked and the new
  password logs in at once; the old one does not), phone change (wrong current password counted as a failed attempt;
  correct one changed the number; the old number no longer logs in, the new one does, and the profile shows it).
- **Reminder worker, run for real** (`PhoneVerification:ReminderProcessingEnabled=true`): users in the due-soon and
  restricted windows moved to `DueSoon` / `Restricted`, each got exactly one notification and one audit row, and
  no tick errors were logged. (A user whose grace period began more than a day earlier gets the state change but no
  "grace started" notice — the code only sends it within a day of the due date.)
- **Wording:** every new key was read on screen in Arabic and German and in the diff for English; fixes in F-13.
- **Email sessions and bans (F-1):** covered by the `RefreshTokenCommand` / security-stamp tests and the integration
  suite, no longer only inferred from the code.

### Still not verified — and why
| Item | Why it could not be verified here | Who can |
|---|---|---|
| Real SMS delivery, the provider's real error behaviour, real spend | needs a provider account and real phones; nothing was sent | owner, on Staging with a test number |
| Twilio adapter (F-23) | no test seam, needs the SDK talking to Twilio | owner / follow-up |
| Redis-backed rate limiting | no Redis server on this machine and the Docker daemon is not running; downloading one was not done without asking | CI already runs Redis; owner can run the same tests locally |
| Staging / Production configuration (`ForwardedHeaders:KnownProxies`, CORS origins, `AllowedCountryCodes`) | no access, by design | owner |
| CSRF handshake **in a browser** over HTTPS | the browser pane rejects the self-signed development certificate and trusting it would change the system trust store; the same handshake was proven with curl over HTTPS, and the browser round ran with `CookieCsrf:Enabled=false` | owner with a trusted dev certificate |
| Session restore after reload in a browser | same cause (HTTPS needed for the Secure cookie) | owner |
| Capacitor Android / iOS, real device, Safari | the only Android virtual device on this machine is broken (its `.ini` is missing) and a dev build needs `CAPACITOR_ALLOW_CLEARTEXT`; no iOS/Safari | owner |
| Frontend CI on the pull requests | GitHub Actions budget exhausted; the same checks were run locally (typecheck, audit, 415 unit tests, production build) | owner (billing) |
| Global SMS spend cap (F-15) | product decision | owner |
| Ban workflow (F-22) | product decision | owner |
| Legal wording of the consent text (F-14) | needs approved wording | owner / legal |

## 8. Suggested order of work

1. Set `SmsProvider:AllowedCountryCodes` in every deployed environment (F-15) and decide on a global send cap with its own counter and an alert.
2. Decide the ban workflow scope (F-22) before anything can set `IsBanned`.
3. Run the browser CSRF/session-restore checks once with a trusted HTTPS development certificate, and the Redis limiter tests locally or read them from CI.
4. Staging: real SMS to a test number, the `ForwardedHeaders` trust list, then `PhoneVerification:ReminderProcessingEnabled` and enforcement per the rollout in the feature doc.
5. Capacitor: repair or recreate the Android virtual device and run register / login / re-verification on it; Safari and a real iPhone.
