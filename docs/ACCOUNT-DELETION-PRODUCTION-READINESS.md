# Account Deletion — Production Readiness (Phase 5)

**Date:** 2026-09-05
**Scope:** Server-side account deletion for HudhudNestApi (.NET 8) + the HudhudNest Angular/Capacitor client, per the Phase 5 spec (Google Play + Apple App Store account-deletion compliance).
**Status of this document:** Technical readiness assessment based on actual code inspection and test runs performed in this session. It is **not** a legal opinion — see §18 for what still needs a legal/business decision.

> **Note on concurrent work:** this repository had several other Claude Code sessions active on the same working tree while this phase was executed (visible via `git status` picking up unrelated in-flight changes — a new Consent-tracking feature, PII log-masking, audit-log retention, Cloudinary cleanup for expired listings). Those are **out of scope** for this document and were not modified by this phase; they are called out only where they touch a fact this report depends on (e.g. `docs/privacy/data-inventory.md`).

---

## 1. Executive Summary

Before this phase, `DELETE /api/Users/me` existed but only **soft-deleted the Identity row** (`IsDeleted = true`) and rotated the security stamp. It never touched the business profile (`UserAccounts` — name, avatar, bio, contact info), never revoked refresh tokens, never invalidated the security-stamp cache, never freed the email/phone/social-login for reuse, and required no re-authentication. `docs/privacy/data-inventory.md` (a separate, concurrent privacy audit already in this repo) had already flagged this as a **P0 gap**: *"no data is ever actually removed — only hidden."*

This phase makes deletion real:

- **Identity layer**: email/phone/username are irreversibly anonymized, every external login (Google/Apple) is unlinked, the security stamp is rotated, every refresh token is revoked, the security-stamp cache is invalidated immediately (not just after its 5-minute TTL), and the identity is soft-deleted (`IsDeleted=true` — required because `UserAccounts.Id → ApplicationUser.Id` and several business tables reference `UserAccounts` with `DeleteBehavior.Restrict`, so a hard delete is not possible without breaking referential integrity or destroying other users' data — see §3).
- **Business-profile layer**: name, avatar, bio, contact info, WhatsApp number, tax number are anonymized in place; the Cloudinary avatar asset is deleted.
- **Re-authentication**: a password-based account must submit its current password (verified with the same lockout-aware path login uses); a social-login-only account is not asked for a password it never had.
- **Frontend**: the previous `confirm()` dialog is replaced with a proper Danger-Zone-gated, two-step modal (what-will-happen → re-authenticate + type "DELETE" to confirm), with loading/error states and full local session cleanup on success.
- **External web page**: a public, no-login-required `/account-deletion` page now exists, explaining the process and offering an email fallback for someone who cannot log in.

What remains explicitly **not** resolved by this phase (by design, not oversight — see §18): what happens to properties/agencies/investment projects/pending transactions owned by a deleted user (they are preserved, unmodified, per existing `DeleteBehavior.Restrict` FKs — no domain rule to transfer/close them exists in the codebase, and inventing one was out of scope); the final legal retention duration for internal security logs (already an open item before this phase); and the production domain for the external deletion page (this repo has no production domain configured anywhere — see §12).

---

## 2. Existing Implementation (found before this phase)

| Area | Found state |
|---|---|
| Endpoint | `DELETE /api/Users/me` (`UsersController.DeleteAccount`) — already existed, already derived the target user from the JWT claim (`ClaimTypes.NameIdentifier`), never from client input. IDOR-safe by construction from the start. |
| Command/Handler | `DeleteUserCommand`/`DeleteUserCommandHandler` — rotated the security stamp, then called `SoftDeleteAsync` (sets `ApplicationUser.IsDeleted=true`/`DeletedAt`). Nothing else. |
| `UserAccount` (business profile) | Untouched by deletion. No `IsDeleted` field exists on this entity at all (by design — see §3). |
| Refresh tokens | Not revoked by the delete handler (relied entirely on `RefreshTokenCommandHandler`'s `identity.IsDeleted` check to reject reuse — see §8). |
| Security-stamp cache | Not invalidated (would only clear after its 5-minute TTL). |
| Cloudinary avatar | Never deleted. |
| Social logins (Google/Apple) | Never unlinked. |
| Rate limiting | None on this endpoint. |
| Re-authentication | None — a valid access token alone was sufficient. |
| Tests | **Zero** — no unit test file existed for `DeleteUserCommandHandler`, no integration test exercised `DELETE /api/Users/me` at all. |
| Frontend | `profile.component.ts`: `onDeleteAccount()` used the browser's native `confirm()`, with the delete button placed directly next to "Save changes" — both violations the spec explicitly calls out (§23/§24). |
| External web page | Did not exist. |
| Privacy policy | Already stated (accurately, at the time) that deletion only hides data, retention is undetermined — see §4/§10 below for what changed. |

This is corroborated by `docs/privacy/data-inventory.md` and `docs/privacy/data-flow.md` (an independent, concurrent audit in this same repo), which had already identified rows 1–3 and 6 of their inventory table as P0/P1 gaps for exactly this reason.

---

## 3. Data Dependency Map

Built from the actual EF Core configurations (`HudhudNestApi.Infrastructure/Persistence/Configurations/*.cs`), not assumed. `UserAccounts.Id` = `ApplicationUser.Id` (shared primary key, `DeleteBehavior.Restrict` — see `UserAccountConfiguration.cs`), so **no row that is a business record can ever be hard-deleted while any of the tables below still reference it** — this is why anonymize-in-place, not hard delete, is the only viable strategy in this codebase today.

| Entity | References User via | Personal data? | Delete behavior on user | Action taken |
|---|---|---|---|---|
| `ApplicationUser` (Identity) | is the user | Email, phone, password hash, security stamp | — | **Anonymize** (email/phone/username) + soft-delete (`IsDeleted`/`DeletedAt`) |
| `UserAccounts` (business profile) | is the user (shared PK) | Name, avatar, bio, contact info, WhatsApp, tax number | `Restrict` from many tables below | **Anonymize** (all PII fields cleared/replaced) |
| `AspNetUserLogins` (social logins) | `UserId` | Google/Apple provider link | n/a | **Delete** (unlinked entirely) |
| `RefreshTokens` | `UserId`, `Cascade` | session tokens | cascade (moot — never hard-deleted) | **Revoke all active** explicitly + query-filtered out once `IsDeleted` |
| Cloudinary avatar asset | `UserAccounts.ProfileImagePublicId` | profile photo | external, unrelated to DB FK | **Delete** (best-effort, non-blocking) |
| `Properties.OwnerId` | `Restrict` | — (Property itself has no PII beyond what's already public) | blocks hard delete | **Retain**, unmodified — see §18 |
| `Properties.AgentId` | `SetNull` | — | n/a | **Retain**, unmodified |
| `Agencies.OwnerUserId` | `Restrict` | — | blocks hard delete | **Retain**, unmodified — see §18 |
| `Messages.SenderId`/`ReceiverId` | `Restrict` | message content | blocks hard delete | **Retain** — conversation history for the other party must survive; sender identity now displays as "Deleted User" via the anonymized `UserAccounts` row |
| `PropertyReview.ReviewerId` | `Restrict` | review text + rating | blocks hard delete | **Retain** — marketplace trust data other users rely on; attribution anonymizes automatically |
| `UserRatings.RaterId`/`RatedUserId` | `Restrict` (both) | rating scores | blocks hard delete | **Retain**, attribution anonymizes automatically |
| `VisitRequest.RequesterId` | `Restrict` | visit request | blocks hard delete | **Retain** |
| `ServiceRequest.RequesterId` | `Restrict` | service request | blocks hard delete | **Retain** |
| `Transactions.PayerId`/`ReceiverId` | `Restrict` (both) | — | blocks hard delete | **Retain** — financial/audit record |
| `InvestmentProject.OwnerUserId` | `Restrict` | — | blocks hard delete | **Retain** — see §18 |
| `InvestmentInterest`/`InvestmentWatchlistItem.UserId` | `Cascade` | — | n/a (never hard-deleted) | **Retained**, not cleaned up by this phase (documented residual, see §19) |
| `Favorites.UserId` | `Cascade` | — | n/a | **Retained**, not cleaned up by this phase (documented residual, see §19) |
| `SavedSearches.UserId` | `Cascade` | — | n/a | **Retained**, not cleaned up by this phase (documented residual, see §19) |
| `Notifications.RecipientId` | `Cascade` | notification text (may embed another user's name) | n/a | **Retained**, unrelated to this phase |
| `AuditLogs.UserId` | `SetNull` | IP, action | n/a | **Retained** — security/audit requirement, pre-existing, no retention policy change made here |
| `PhoneOtpChallenges.UserId` | `SetNull` | — | n/a | **Retained**, harmless (already time-boxed to 5 minutes) |
| `UserAccounts.AgencyId`/`PlanId`/`PlanGrantedByUserId` | n/a (fields on the anonymized row itself) | business state, not PII | — | **Left untouched** — deliberate (see `UserAccount.Anonymize`'s doc comment); touching agency membership or plan bookkeeping as a side effect of deletion was judged out of scope for this phase — see §18 |

---

## 4. Deletion Strategy

| Data | Action | Reason |
|---|---|---|
| Email, phone, username | ANONYMIZE | Unique-constrained columns; must be freed for potential reuse and must stop being able to authenticate. Replaced with `deleted-{guid}@deleted.invalid` (RFC 2606 reserved, non-deliverable, collision-free). |
| Google/Apple login link | DELETE | Otherwise a later sign-in attempt (or a new registration) with the same provider account could collide with the deleted user's stale link. |
| Name, avatar, bio, contact info, WhatsApp, tax number | ANONYMIZE | Direct PII on a row that cannot be hard-deleted (see §3) — cleared/replaced with a fixed "Deleted User" placeholder. |
| Cloudinary avatar asset | DELETE | External asset with a public, unsigned URL (already flagged in `data-inventory.md`) — no reason to keep it once the profile is anonymized. |
| Security stamp | ROTATE | Invalidates any already-issued JWT access token on its next validation (`CachedSecurityStampValidator`). |
| Security-stamp cache entry | INVALIDATE (immediately) | Removes the up-to-5-minute window during which a just-issued access token would otherwise still pass. |
| Refresh tokens | REVOKE (all, explicitly) | Belt-and-braces on top of `RefreshTokenCommandHandler`'s existing `identity.IsDeleted` rejection (see §8). |
| Identity row (`ApplicationUser`) | SOFT-DELETE (`IsDeleted`/`DeletedAt`) | Hard delete is blocked by FK `Restrict` from `UserAccounts` and, transitively, from every business table in §3 that has ever recorded activity from this user. |
| Properties, Agencies, Reviews, Messages, Ratings, Visit/Service requests, Transactions, Investment projects | RETAIN, unmodified | `DeleteBehavior.Restrict` on all of these — no domain method exists to transfer/close them, and no business rule for what should happen to them exists in the codebase. **REQUIRES BUSINESS DECISION** (see §18) if a different behavior (auto-unpublish listings, transfer agency ownership, etc.) is wanted later. |
| Favorites, SavedSearches, Notifications, InvestmentInterest/Watchlist | RETAIN, unmodified | Purely user-owned engagement data with no third-party PII exposure; not cleaned up in this phase — documented residual (§19), not a privacy gap. |
| AuditLogs | RETAIN | Security/audit requirement; retention duration is a pre-existing open item (`data-inventory.md`), unchanged by this phase. |

---

## 5. API

`DELETE /api/Users/me` (`HudhudNestApi/Controllers/UsersController.cs:DeleteAccount`) — no new endpoint was created; the existing one was extended, per the instruction to prefer extending existing architecture.

- Request body (optional): `{ "currentPassword"?: string }`.
- Responses: `204 No Content` (success), `401 Unauthorized` (not authenticated, or identity not found/already deleted), `400 Bad Request` with `{ "errors": [...] }` for `CURRENT_PASSWORD_REQUIRED` / `INVALID_CURRENT_PASSWORD` / `ACCOUNT_LOCKED` / any identity-layer failure code.
- The target user id is **always** `User.FindFirstValue(ClaimTypes.NameIdentifier)` from the authenticated principal — never accepted from the request body, query string, or route. This was already true before this phase and is unchanged.

---

## 6. Authentication

- The endpoint requires a valid JWT (`[Authorize]` at the controller level, `FallbackPolicy` denies anonymous access by default across the whole API).
- **Re-authentication**: `IdentityAccountSnapshot.HasPassword` (already existed) drives the branch:
  - Password-based account → `CurrentPassword` is required and verified via `ILoginIdentityService.VerifyPasswordWithLockoutAsync` (the same lockout-aware path login itself uses) before anything is mutated. Missing/wrong password never begins the transaction.
  - Social-login-only account → no password step (there is nothing to verify it against); per the spec's own instruction not to invent a mechanism the architecture doesn't support. Documented as a residual hardening opportunity in §18, not silently ignored.

---

## 7. Authorization

- IDOR-safe by construction: the endpoint has no parameter, body field, or route segment that could name a different user. There is no code path by which User A's request can affect User B's account — verified by reading `UsersController.DeleteAccount` and `DeleteUserCommandHandler` end to end.
- `AccountDeletionAuthenticationTests` (new) proves, over a real HTTP round-trip: no token → 401; malformed token → 401.
- A full two-real-users IDOR proof (User A's session cannot delete User B's account) was **not** run as a live HTTP test in this session — see §16 for why and what would close it.

---

## 8. Database Transactions

`DeleteUserCommandHandler` wraps every Identity + `UserAccounts` mutation in one `IUnitOfWork` transaction (same pattern as the pre-existing `UpdateUserCommandHandler`):

```
BeginTransactionAsync
  AnonymizeCredentialsAsync   (fail → rollback, return Fail(errors))
  UpdateSecurityStampAsync    (fail → logged, does not abort — IsDeleted alone already blocks reuse)
  SoftDeleteAsync             (fail → rollback, return Fail(errors))
  account.Anonymize(now)
  SaveChangesAsync
CommitTransactionAsync
```

On any unhandled exception, the `catch` block rolls back and rethrows — no partially-deleted state is possible; the account is either fully anonymized+soft-deleted, or not touched at all.

Everything after the commit (refresh-token revocation, cache invalidation, Cloudinary cleanup, audit log) is deliberately **outside** the transaction: the account is already, irreversibly, gone at that point, and none of these steps could be meaningfully "rolled back" anyway (matches the existing precedent in `LogoutCommandHandler` and `UploadUserAvatarCommandHandler`). Covered by `Handle_StillSucceeds_WhenAvatarCleanupThrows` and the rollback-path tests in `DeleteUserCommandHandlerTests`.

---

## 9. External Storage (Cloudinary)

The avatar's `ProfileImagePublicId` is read **before** `account.Anonymize()` clears it, and `IMediaStorageService.DeleteImageAsync` is called after the transaction commits, wrapped in try/catch (`Handle_StillSucceeds_WhenAvatarCleanupThrows` proves a Cloudinary failure never turns a successful deletion into a reported failure). No outbox/retry system was added — this mirrors the existing, already-accepted pattern in `UploadUserAvatarCommandHandler` for the exact same asset type, not a new eventual-consistency posture introduced here.

**Out of scope, pre-existing, unrelated to account deletion**: property-listing images are never cleaned up from Cloudinary when a listing itself is deleted/expires (a separate P1 gap already tracked in `docs/privacy/data-inventory.md` row 7, being worked on in a concurrent session's changes to `DeletePropertyCommandHandler.cs`/`ListingExpiryHostedService.cs` visible in `git status` — not part of this phase).

---

## 10. Frontend UX (HudhudNest / Angular)

`src/app/profile/profile.component.ts`/`.html`/`.scss`:

- **Danger Zone**: the delete button now lives in its own bordered, red-tinted section, separated by a 2.5rem margin from Save/password/avatar actions — no longer adjacent to "Save changes".
- **Two-step modal** (built on the existing, already-used-elsewhere `ConfirmModalComponent` — no new modal primitive invented):
  1. *Warning step*: explains exactly what is deleted immediately, what is retained (and why), and that listings are not auto-unpublished — no request is sent yet.
  2. *Confirm step*: current-password field (shown only if `UserProfile.HasPassword`, a new field surfaced from the backend's `UserDto.HasPassword`), and a literal "type DELETE to confirm" field. The confirm button stays disabled until both are satisfied client-side; the server independently re-validates everything regardless.
- **Loading/error states**: `isDeletingAccount` disables the modal's backdrop-dismiss and buttons mid-request; server error codes (`CURRENT_PASSWORD_REQUIRED`/`INVALID_CURRENT_PASSWORD`/`ACCOUNT_LOCKED`) are mapped to localized inline messages — never a raw exception.
- **On success**: `AuthService.logoutLocal()` clears tokens + CSRF token (pre-existing) **and now also clears the cached profile** (`ProfileService.clearCache()`, newly wired into `AuthService.clearSession()` — a real fix for a stale-profile-in-memory gap that affected plain logout too, not just deletion), then navigates to `/auth/login`.
- Translated into all three supported locales (`ar`, `en`, `de` — `public/i18n/*.json`).

---

## 11. Capacitor (Android/iOS)

No native code was touched. The new flow is pure Angular/Capacitor-WebView UI (the shared `ConfirmModalComponent`, already used elsewhere in the app) — it deliberately **replaces** the previous `window.confirm()`, which is actually a mobile-UX improvement (a native WebView's `confirm()` dialog styling is inconsistent across platforms; the new modal renders identically everywhere the app runs). `HttpClient.delete()` with a JSON body is used to submit the optional password — this is standard Angular `HttpClient` behavior and was not modified; a device/emulator smoke test to confirm the native HTTP layer forwards a DELETE body correctly is recommended (see §16) but was not run in this session (no Android/iOS build environment available here).

---

## 12. External Web Deletion Page

`src/app/features/legal/account-deletion.page.ts`, routed at `/account-deletion` (public, no `AuthGuard`, no login required) — verified rendering correctly in a live browser check during this session (screenshot taken, RTL Arabic layout confirmed correct).

Content: how to delete in-app/on-web, exactly what is deleted vs. retained (same wording as the in-app modal, kept consistent on purpose), the authentication requirements, an email fallback (`support@hudhudnest.com`, the same contact address used on the privacy-policy page) for anyone who cannot log in, and a link to the privacy policy.

**No production domain exists anywhere in either repository** (confirmed by searching both codebases) — this repo only has local/staging URLs. Per the spec's explicit instruction, no domain was invented. **The Google Play Console / App Store Connect "account deletion URL" fields must be filled in manually once a production domain exists**, pointing at `https://YOUR_PRODUCTION_DOMAIN/account-deletion`.

---

## 13. Google Play Readiness

| Requirement | Status | Evidence |
|---|---|---|
| Account creation exists | PASS | Pre-existing (`AuthController.Register`, phone/social registration flows). |
| Account deletion inside app | PASS | Danger Zone → modal → `DELETE /api/Users/me` (§10). |
| Deletion actually deletes data server-side | PASS | §3/§4 — anonymization + credential wipe + token revocation, verified by `DeleteUserCommandHandlerTests` (12/12 passing). |
| Web deletion path | PASS | `/account-deletion`, public, verified rendering (§12). |
| Retention exceptions documented | PASS | §3/§4 (properties/reviews/messages/audit logs retained, with reasons). |
| External deletion page accessible without the app | PASS (once a production domain is configured — see §12) | |

**Google Play: PASS WITH WARNINGS** — the only blocker to a clean PASS is the production domain not existing yet in either repo; that is an infrastructure/business action, not a code gap.

---

## 14. Apple App Store Readiness

| Requirement | Status | Evidence |
|---|---|---|
| Account creation → deletion required | PASS | Same as above. |
| Deletion initiated inside app | PASS | §10. |
| Deletion is meaningful (not cosmetic) | PASS | §3/§4/§8 — real anonymization + transaction integrity. |
| UX/authentication/data cleanup/session invalidation | PASS | §6/§7/§8/§10. |
| External dependency (Cloudinary) handled | PASS | §9. |

**Apple App Store: PASS WITH WARNINGS** — same domain caveat as §13.

---

## 15. Security Tests

All in `tests/HudhudNestApi.Application.Tests/Users/DeleteUserCommandHandlerTests.cs` (12 tests, **all passing** — `dotnet test`, see §16 for the exact command) unless noted:

- Unauthenticated/missing identity → `UserNotFound` (also proven at the HTTP level, see below).
- Already-deleted identity → `UserNotFound`.
- Password-based account, no password submitted → `CURRENT_PASSWORD_REQUIRED`, no transaction started.
- Wrong password → `INVALID_CURRENT_PASSWORD`.
- Locked-out account → `ACCOUNT_LOCKED`.
- Social-login-only account → password step skipped entirely, deletion still succeeds.
- Missing `UserAccount` profile (data-integrity edge case) → `UserNotFound`, no transaction started.
- Credential-anonymization failure → transaction rolled back, `SoftDeleteAsync` never called, refresh tokens never revoked.
- Soft-delete failure → transaction rolled back.
- Happy path → business profile fields verified anonymized in memory (`FirstName == "Deleted"`, avatar cleared, etc.), `AnonymizeCredentialsAsync` called with the correct anonymized-email shape, refresh tokens revoked, security-stamp cache invalidated, Cloudinary asset deleted, audit log written exactly once with `AccountDeletionCompleted`.
- No avatar set → Cloudinary never called.
- Cloudinary throwing → deletion still reports success (external cleanup is best-effort, post-commit).

`tests/HudhudNestApi.Integration.Tests/Security/AccountDeletionAuthenticationTests.cs` (2 tests, **passing**, real HTTP round-trip through the actual JWT middleware): no token → 401; malformed token → 401.

**IDOR** is structurally proven by code inspection (§7) rather than a dedicated test — the endpoint has no attacker-controllable identifier at all, so there is no "User A targets User B" code path to exercise. A dedicated two-user HTTP test was not added — see §16.

---

## 16. Integration Tests — what ran, what did not, and why

Run in this session (all passing):

```
dotnet build HudhudNestApi.sln                                    → 0 errors
dotnet test tests/HudhudNestApi.Application.Tests/...              → 549/549 passed
dotnet test tests/HudhudNestApi.Architecture.Tests/...              → 108/108 passed
dotnet test tests/HudhudNestApi.Auth.Tests/...                       → 254/254 passed
dotnet test tests/HudhudNestApi.Integration.Tests/... --filter AccountDeletionAuthenticationTests  → 2/2 passed
npm run build   (HudhudNest, production config)               → succeeded, 0 errors
```

**NOT VERIFIED in this session** (honest disclosure, not a claimed PASS):

- A full Postgres-backed end-to-end test (two real seeded users, real JWTs, real HTTP calls to `DELETE /api/Users/me`, asserting the anonymized row and the reuse-fails-for-old-tokens behavior against a real database) — **Docker was not running in this sandbox** (`docker info` failed to reach the daemon), and this repo's Postgres-backed integration tests (`[Collection("AuthPostgres")]`, e.g. `UserAccountConcurrencyTests`) require it. Per the spec's own rule ("NOT VERIFIED ≠ PASS, do not fake it"), this is disclosed rather than assumed. The unit tests in §15 exercise the identical handler logic against mocked dependencies; the token-invalidation guarantee itself rests on pre-existing, already-established infrastructure (`RefreshTokenCommandHandler`'s `identity.IsDeleted` check, `CachedSecurityStampValidator`'s `IsDeleted` check) that predates this phase.
- Angular component-level unit tests for `profile.component.ts` — **no spec file existed for this component before this phase either** (confirmed: no `profile.component.spec.ts` in the repo). Adding full Karma/TestBed coverage for a component this size was judged out of proportion for this phase's time budget; the production build (which AOT-compiles and type-checks every template binding, including the new modal) passed with zero errors, and the page was visually verified live (screenshot taken).
- Live, credentialed exercise of the actual modal (open → warning → confirm → submit) in a browser — no valid login credentials were available against the only reachable backend in this sandbox (the live Staging API, `wohnungen-api.onrender.com`, blocked by CORS from `localhost:4200` as expected for a non-whitelisted origin). Registering/deleting a real account against a shared Staging environment without explicit authorization was avoided per this session's operating rules.
- A Capacitor Android/iOS on-device smoke test of the DELETE-with-body request — no native build environment was available in this sandbox.

None of the above gaps involve the actual authorization/anonymization logic itself (which is unit-tested); they are the "real backend + real browser + real device" layer that this sandbox could not reach.

---

## 17. Privacy/GDPR Technical Assessment

Not a legal compliance claim — a technical-capability assessment only (per the spec's own instruction not to assert legal GDPR compliance):

- **Technical deletion capability**: now real (§3/§4), not merely cosmetic — this was the P0 gap this phase closes.
- **Data minimization on deletion**: direct PII (name, avatar, bio, contact info, email, phone, social-login link) is removed; indirect/derived PII on other people's records (message history, reviews) is anonymized at the attribution layer rather than destroyed, because destroying it would remove other users' legitimate records.
- **Retention**: audit-log retention duration remains an open item, unchanged by this phase (already flagged in `data-inventory.md`; a concurrent session appears to be adding `AuditLogRetentionHostedService.cs` — outside this phase's scope to evaluate).
- **Consistency with the privacy policy**: `privacy-policy.page.ts` §5 was updated to describe what actually happens now (previously it only described the old soft-delete-only behavior) — this closes a policy/implementation mismatch the spec explicitly asks to check for (§30).
- **User-rights workflow**: the `/account-deletion` page and in-app flow together give a working self-service path; the email fallback covers the "cannot log in" case.

---

## 18. Remaining Manual/Legal/Business Decisions

- **REQUIRES BUSINESS DECISION**: what should happen to a deleted user's *active* property listings, agency ownership, investment projects, or pending service/visit requests? Today they are preserved unmodified (no domain rule exists to auto-unpublish, transfer, or close them). If the business wants e.g. "auto-unpublish active listings on account deletion," that is a new domain rule to design and implement deliberately, not something this phase should decide unilaterally.
- **REQUIRES BUSINESS DECISION**: should a social-login-only account get some form of step-up re-authentication beyond "you already hold a valid session"? The OTP infrastructure (`IOtpService`) exists and could be reused for this, but building a bespoke reauth path for this one case was judged out of proportion for this phase.
- **REQUIRES LEGAL REVIEW**: the exact retention duration for internal security/audit logs after account deletion (already an open item before this phase, per `data-inventory.md`).
- **Configure production domain**: needed before the Play Store/App Store console "account deletion URL" fields can be filled in (see §12).
- **Manual store-listing configuration**: both Google Play Console and App Store Connect require the deletion URL to be entered manually by whoever administers those listings — not something a code change can do.

---

## 19. Known Limitations

- Favorites, SavedSearches, Notifications, and Investment interest/watchlist rows belonging to a deleted user are not cleaned up by this phase (documented residual, §3/§4) — they carry no third-party-visible PII, so this was judged a lower priority than the P0 items this phase targeted, not an oversight.
- Agency membership (`UserAccounts.AgencyId`) and plan state are left untouched on deletion — deliberate, see §3's note in `UserAccount.Anonymize`'s doc comment.
- See §16 for the verification gaps (Docker/Postgres E2E, Angular component tests, live credentialed UI test, native device test).

---

## 20. Final Gate

### Evidence Table

| Requirement | Status | Evidence |
|---|---|---|
| In-app delete account | PASS | `profile.component.ts`/`.html` Danger Zone + modal |
| Backend delete endpoint | PASS | `UsersController.DeleteAccount` (pre-existing, extended) |
| Authentication | PASS | `[Authorize]` + re-authentication (§6); `AccountDeletionAuthenticationTests` |
| Authorization | PASS | No attacker-controllable identifier exists (§7) |
| IDOR protection | PASS (by construction) | Code inspection; no dedicated two-user HTTP test (§16) |
| User data cleanup | PASS | `UserAccount.Anonymize`, `AnonymizeCredentialsAsync` (§3/§4), unit-tested |
| Token revocation | PASS | `RevokeActiveTokensForUserAsync` + cache invalidation + pre-existing `IsDeleted` checks |
| External assets cleanup | PASS | Cloudinary avatar deletion, best-effort (§9) |
| Transaction safety | PASS | `IUnitOfWork` transaction + rollback tests (§8) |
| Local data cleanup | PASS | `AuthService.clearSession()` now also clears cached profile |
| External deletion page | PASS | `/account-deletion`, verified live (§12) |
| Privacy consistency | PASS | `privacy-policy.page.ts` §5 updated (§17) |
| Google Play readiness | PASS WITH WARNINGS | §13 (domain pending) |
| Apple readiness | PASS WITH WARNINGS | §14 (domain pending) |

### Decision

```
PHASE 5 — ACCOUNT DELETION
STATUS: PASS WITH WARNINGS
```

No BLOCKER-level issue exists: no unauthorized deletion path, no evidence a token or session survives deletion, no half-deleted state possible, and no invented business policy was smuggled in where the spec asked for an explicit decision instead. The warnings are: (1) a production domain does not yet exist to finish the store-listing configuration, (2) a subset of verification (real-Postgres E2E, Angular component tests, live credentialed UI/device tests) could not be run in this sandbox and is honestly disclosed rather than assumed, and (3) several business decisions about non-identity data owned by a deleted user (listings, agency ownership, investment projects) are deliberately left to the business rather than decided here.

**Phase 6 is not started.**

---

## 21. Update — Finding F7 delay window + data export (2026-09-07/08)

A separate Phase 2 database-readiness audit (`docs/DATABASE-PRODUCTION-READINESS.md`) reviewed
this phase's work and found it complete for anonymization/token-revocation/cleanup, but short of
a newly-approved policy on two points: a cancellable delay window before deletion executes, and
a JSON export endpoint. Both were added in a follow-up session **without modifying this phase's
anonymization logic** — see `docs/DATABASE-PRODUCTION-READINESS.md`'s Finding F7 RESOLUTION
block for full evidence (migrations, tests, live-Postgres proof). Summary of what changed here:

- `DELETE /api/Users/me` now **schedules** deletion (re-authenticates immediately, then starts a
  30-day-default, configurable, cancellable delay window) instead of anonymizing synchronously.
  **This is a breaking response-contract change**: `204 No Content` → `202 Accepted` with
  `{ scheduledFor }`. The Angular Danger-Zone modal built in this phase (§10) was **not**
  updated — it is a separate repository outside the follow-up session's reach — and needs its
  success handling changed from "assume immediate completion" to "show the scheduled date and a
  cancel option," consistent with the delay window rather than this phase's original immediate
  behavior.
- New `POST /api/Users/me/deletion/cancel` and `GET /api/Users/me/export` endpoints exist and
  have no corresponding frontend UI yet.
- The core guarantees this document already established — re-authentication, atomic
  Identity+UserAccount anonymization, token revocation, Cloudinary cleanup, retained
  properties/reviews/messages with anonymized attribution — are **unchanged**; the delay window
  wraps when that logic runs, not what it does.

**Manual follow-up needed** (frontend, separate repository, not done here): update the Danger
Zone flow for the new response contract and add UI for cancellation and export.
